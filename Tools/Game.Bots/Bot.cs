using System.Collections.Concurrent;
using System.Net.Sockets;
using Game.Shared.Combat;
using Game.Shared.Net;
using ComboStateMessage = Game.Shared.Net.ComboState;

namespace Game.Bots;

/// <summary>Conexão TCP de um bot: recebe numa tarefa de fundo, envia direto.</summary>
public sealed class BotConnection : IDisposable
{
    private readonly TcpClient _tcp = new() { NoDelay = true };
    private readonly ConcurrentQueue<INetMessage> _inbox = new();
    private readonly object _sendLock = new();
    private NetworkStream? _stream;
    private long _bytesReceived;

    public bool Connected { get; private set; }
    public string Error { get; private set; } = "";
    public long BytesReceived => Interlocked.Read(ref _bytesReceived);

    public async Task ConnectAsync(string host, int port)
    {
        await _tcp.ConnectAsync(host, port);
        _stream = _tcp.GetStream();
        Connected = true;
        _ = Task.Run(ReceiveLoop);
    }

    public bool TryReceive(out INetMessage message) => _inbox.TryDequeue(out message!);

    public void Send(INetMessage message)
    {
        if (!Connected || _stream == null) return;
        var frame = NetCodec.Encode(message);
        try
        {
            lock (_sendLock) _stream.Write(frame, 0, frame.Length);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            Fail("conexão perdida ao enviar");
        }
    }

    public void Dispose()
    {
        Connected = false;
        _tcp.Dispose();
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[16384];
        var frames = new FrameBuffer();
        try
        {
            while (Connected)
            {
                var read = await _stream!.ReadAsync(buffer);
                if (read == 0)
                {
                    Fail("servidor fechou a conexão");
                    return;
                }
                Interlocked.Add(ref _bytesReceived, read);
                frames.Append(buffer, 0, read);
                while (frames.TryRead(out var type, out var payload)) _inbox.Enqueue(NetCodec.Decode(type, payload));
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or ProtocolException)
        {
            Fail(ex.Message);
        }
    }

    private void Fail(string reason)
    {
        if (!Connected) return;
        Error = reason;
        Connected = false;
    }
}

/// <summary>Métricas de um bot.</summary>
public sealed class BotStats
{
    public readonly List<double> RttMs = new();
    public int HitsConfirmed;
    public long DamageDealt;
    public readonly int[] Grades = new int[3]; // Miss, Good, Excellent
    public int CombosStarted;
    public int CombosBrokenByServer;
    public int HitsTaken;
    public int Dodges;
    public int Deaths;
    public int Kills;
    public int Corrections;

    /// <summary>Detalhes dos apertos que o servidor julgou como Miss (para investigar).</summary>
    public readonly List<string> MissDetails = new();

    /// <summary>Avisos do servidor por texto (sem números), para achar o que está recusando as ações.</summary>
    public readonly Dictionary<string, int> Notices = new();
}

/// <summary>
/// Um jogador falso: anda até o monstro vivo mais perto, ataca com a skill 1 e, quando tem SP, faz combos
/// apertando no centro da janela (com um erro humano de até ±30 ms), tudo pelo protocolo real.
/// </summary>
public sealed class Bot
{
    private const float RunSpeed = 6.5f;
    private const float AttackDistance = 1.9f;

    private readonly string _name;
    private readonly Random _random;
    private readonly HashSet<string> _prey;
    private readonly float _huntX;
    private readonly float _huntZ;
    private readonly ServerClock _clock = new();
    private readonly Dictionary<int, (EntityKind Kind, string TypeId, float X, float Z, bool Dead)> _entities = new();
    private uint _sequence;
    private double _nextMoveAt;
    private double _nextPingAt;
    private double _nextSkillAt;
    private double _pressedCycle = double.NaN;
    private double _pressError;
    private bool _comboRequested;
    private double _comboRequestedAt;
    private string _lastPress = "";
    private ComboStateMessage? _combo;
    private int _xp;
    private int _level = 1;
    private int _sp;
    private bool _dead;

    public BotConnection Connection { get; } = new();
    public BotStats Stats { get; } = new();
    public int PlayerId { get; private set; }
    public float X { get; private set; }
    public float Z { get; private set; }
    public string Name => _name;

    /// <summary>
    /// <paramref name="prey"/> = tipos de monstro que este bot caça (espalha os bots pelo mapa: 20 bots num campo
    /// com 9 monstros passam mais tempo procurando alvo do que lutando).
    /// </summary>
    public Bot(string name, int seed, IEnumerable<string> prey, float huntX, float huntZ)
    {
        _name = name;
        _random = new Random(seed);
        _prey = new HashSet<string>(prey);
        _huntX = huntX;
        _huntZ = huntZ;
    }

    public async Task StartAsync(string host, int port)
    {
        await Connection.ConnectAsync(host, port);
        Connection.Send(new Hello { Name = _name });
    }

    /// <summary>Um passo do bot (chamado a ~50 Hz).</summary>
    public void Update(double localNow, float dt)
    {
        while (Connection.TryReceive(out var message)) Handle(message, localNow);
        if (PlayerId == 0 || !Connection.Connected) return;

        if (localNow >= _nextPingAt)
        {
            _nextPingAt = localNow + 1;
            Connection.Send(new Ping { ClientTime = localNow });
        }
        if (_dead) return;

        var serverNow = _clock.ServerNow(localNow);
        var target = NearestMonster();
        if (target == null)
        {
            // Nenhuma presa à vista (o servidor só manda o que está a até 60 m): anda para a zona de caça.
            Walk(_huntX, _huntZ, 2f, dt);
        }
        else if (target is { } t)
        {
            var dx = t.X - X;
            var dz = t.Z - Z;
            var distance = MathF.Sqrt(dx * dx + dz * dz);
            if (distance > AttackDistance)
            {
                var step = MathF.Min(distance - AttackDistance + 0.1f, RunSpeed * dt);
                X += dx / distance * step;
                Z += dz / distance * step;
            }
            else
            {
                Fight(t.Id, serverNow, localNow);
            }
        }

        if (localNow >= _nextMoveAt)
        {
            _nextMoveAt = localNow + 0.05;
            Connection.Send(new MoveInput { Sequence = ++_sequence, X = X, Z = Z });
        }
    }

    private void Walk(float x, float z, float stopAt, float dt)
    {
        var dx = x - X;
        var dz = z - Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (distance <= stopAt) return;
        var step = MathF.Min(distance - stopAt, RunSpeed * dt);
        X += dx / distance * step;
        Z += dz / distance * step;
    }

    private void Fight(int targetId, double serverNow, double localNow)
    {
        var comboActive = _combo is { Active: true };

        if (!comboActive && !_comboRequested && _sp >= ComboConfig.Default.ActivationSpCost && _random.NextDouble() < 0.05)
        {
            _comboRequested = true;
            _comboRequestedAt = localNow;
            Stats.CombosStarted++;
            Connection.Send(new StartCombo { TargetId = targetId, ClientTime = serverNow });
            return;
        }

        if (comboActive)
        {
            var combo = _combo!;
            if (combo.CycleStartServerTime.Equals(_pressedCycle)) return;
            if (double.IsNaN(_pressError) || _pressError == 0) _pressError = (_random.NextDouble() * 2 - 1) * 0.03;
            var pressAt = combo.CycleStartServerTime + combo.WindowCenter * combo.BarDurationSeconds + _pressError;
            if (serverNow < pressAt || localNow < _nextSkillAt) return;
            _pressedCycle = combo.CycleStartServerTime;
            var cursor = (serverNow - combo.CycleStartServerTime) / combo.BarDurationSeconds;
            var half = combo.WindowWidth / 2;
            _lastPress = $"acerto {combo.HitCount + 1}: cursor {cursor:0.000} (janela {combo.WindowCenter - half:0.000}-{combo.WindowCenter + half:0.000}, " +
                         $"barra {combo.BarDurationSeconds:0.000}s), erro sorteado {_pressError * 1000:+0;-0} ms, " +
                         $"atraso do quadro {(serverNow - pressAt) * 1000:0} ms";
            _pressError = 0;
            _nextSkillAt = localNow + 0.52;
            Connection.Send(new UseSkill { Slot = 0, TargetId = targetId, ClientTime = serverNow });
            return;
        }

        // Pediu o combo e o servidor ainda não confirmou: não ataca, senão o golpe chega depois do início do combo
        // e conta como um aperto no começo da barra (Miss). O cliente real não tem isso: ele já começa o combo na hora.
        // Se o servidor recusar (alvo morreu), não vem ComboState: desiste do pedido depois de 1 s.
        if (_comboRequested && localNow - _comboRequestedAt > 1.0) _comboRequested = false;
        if (_comboRequested) return;

        if (localNow < _nextSkillAt) return;
        _nextSkillAt = localNow + 0.55;
        Connection.Send(new UseSkill { Slot = 0, TargetId = targetId, ClientTime = serverNow });
    }

    private (int Id, float X, float Z)? NearestMonster()
    {
        (int, float, float)? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (id, e) in _entities)
        {
            if (e.Kind != EntityKind.Monster || e.Dead) continue;
            if (!_prey.Contains(e.TypeId)) continue;
            var d = (e.X - X) * (e.X - X) + (e.Z - Z) * (e.Z - Z);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = (id, e.X, e.Z);
            }
        }
        return best;
    }

    private void Handle(INetMessage message, double localNow)
    {
        switch (message)
        {
            case Welcome welcome:
                PlayerId = welcome.PlayerId;
                X = welcome.SpawnX;
                Z = welcome.SpawnZ;
                _clock.Seed(welcome.ServerTime, localNow);
                break;
            case Pong pong:
                _clock.AddSample(pong.ClientTime, pong.ServerTime, localNow);
                Stats.RttMs.Add(_clock.LastRtt * 1000);
                break;
            case EntitySpawned spawned:
                _entities[spawned.Id] = (spawned.Kind, spawned.TypeId, spawned.X, spawned.Z, false);
                break;
            case EntityDespawned despawned:
                _entities.Remove(despawned.Id);
                break;
            case Snapshot snapshot:
                foreach (var state in snapshot.Entities)
                {
                    if (state.Id == PlayerId) continue;
                    if (_entities.TryGetValue(state.Id, out var e))
                        _entities[state.Id] = (e.Kind, e.TypeId, state.X, state.Z, (state.Flags & EntityState.FlagDead) != 0);
                }
                break;
            case PositionCorrection correction:
                X = correction.X;
                Z = correction.Z;
                Stats.Corrections++;
                break;
            case PlayerStats stats:
                if (stats.Level > _level || stats.Xp > _xp) Stats.Kills++;
                _level = stats.Level;
                _xp = stats.Xp;
                _sp = stats.Sp;
                var dead = stats.Hp == 0;
                if (dead && !_dead) Stats.Deaths++;
                _dead = dead;
                break;
            case ComboStateMessage combo:
                if (_combo is { Active: true } && !combo.Active && combo.LastEndReason == (byte)ComboEndReason.Missed)
                {
                    Stats.CombosBrokenByServer++;
                    Stats.MissDetails.Add(_lastPress);
                }
                _combo = combo;
                _comboRequested = false; // o servidor respondeu: começou (Active) ou recusou
                break;
            case Notice notice:
                var key = new string(notice.Text.Where(c => !char.IsDigit(c)).ToArray());
                Stats.Notices[key] = Stats.Notices.GetValueOrDefault(key) + 1;
                break;
            case CombatEvent hit:
                if (hit.AttackerId == PlayerId)
                {
                    Stats.HitsConfirmed++;
                    Stats.DamageDealt += hit.Damage;
                    if (hit.Grade < 3) Stats.Grades[hit.Grade]++;
                }
                else if (hit.TargetId == PlayerId)
                {
                    if (hit.Dodged) Stats.Dodges++;
                    else Stats.HitsTaken++;
                }
                break;
        }
    }
}
