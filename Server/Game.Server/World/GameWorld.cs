using Game.Shared.Net;

namespace Game.Server.World;

/// <summary>Para onde o mundo manda mensagens. A rede implementa; os testes usam uma lista.</summary>
public interface IOutbox
{
    void Send(int playerId, INetMessage message);
}

/// <summary>Jogador conectado, do ponto de vista do servidor.</summary>
public sealed class ServerPlayer
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public required MovementValidator Movement { get; init; }
    public float Yaw { get; set; }
    public uint LastMoveSequence { get; set; }
    public int Level { get; set; } = 1;
    public int Hp { get; set; } = 2000;
    public int MaxHp { get; set; } = 2000;
}

/// <summary>
/// O mundo do jogo no servidor, sem rede: recebe mensagens já decodificadas, avança no tempo
/// e responde pelo <see cref="IOutbox"/>. Tudo roda numa única thread (o loop de ticks).
/// </summary>
public sealed class GameWorld
{
    public const byte TickRate = 20;
    public const float SpawnX = 0f;
    public const float SpawnZ = 0f;

    private readonly IOutbox _outbox;
    private readonly Dictionary<int, ServerPlayer> _players = new();
    private int _nextEntityId = 1;
    private uint _tick;

    public GameWorld(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public IReadOnlyCollection<ServerPlayer> Players => _players.Values;
    public uint CurrentTick => _tick;

    /// <summary>
    /// Entra um jogador: recebe as boas-vindas e a lista de quem já está aqui; os outros são avisados.
    /// <paramref name="onIdAssigned"/> roda antes de qualquer mensagem sair, para a rede saber para onde mandar.
    /// </summary>
    public ServerPlayer AddPlayer(string name, double now, Action<int>? onIdAssigned = null)
    {
        var cleanName = string.IsNullOrWhiteSpace(name) ? "Aventureiro" : name.Trim();
        var player = new ServerPlayer
        {
            Id = _nextEntityId++,
            Name = cleanName.Length > 20 ? cleanName[..20] : cleanName,
            Movement = new MovementValidator(SpawnX, SpawnZ, now),
        };
        onIdAssigned?.Invoke(player.Id);

        _outbox.Send(player.Id, new Welcome
        {
            PlayerId = player.Id, ServerTime = now, TickRate = TickRate, SpawnX = SpawnX, SpawnZ = SpawnZ,
        });
        foreach (var other in _players.Values)
        {
            _outbox.Send(player.Id, SpawnMessage(other));
            _outbox.Send(other.Id, SpawnMessage(player));
        }
        _players.Add(player.Id, player);
        return player;
    }

    public void RemovePlayer(int playerId)
    {
        if (!_players.Remove(playerId)) return;
        foreach (var other in _players.Values) _outbox.Send(other.Id, new EntityDespawned { Id = playerId });
    }

    /// <summary>Trata uma mensagem do cliente.</summary>
    public void Handle(int playerId, INetMessage message, double now)
    {
        if (!_players.TryGetValue(playerId, out var player)) return;

        switch (message)
        {
            case MoveInput move:
                HandleMove(player, move, now);
                break;
            case Ping ping:
                _outbox.Send(playerId, new Pong { ClientTime = ping.ClientTime, ServerTime = now });
                break;
        }
    }

    /// <summary>Um passo da simulação: manda a cada jogador o estado de quem ele vê.</summary>
    public void Tick(double now)
    {
        _tick++;
        foreach (var player in _players.Values)
        {
            var snapshot = new Snapshot { Tick = _tick, ServerTime = now };
            foreach (var other in _players.Values)
            {
                snapshot.Entities.Add(new EntityState
                {
                    Id = other.Id, X = other.Movement.X, Z = other.Movement.Z, Yaw = other.Yaw, Hp = other.Hp,
                });
            }
            _outbox.Send(player.Id, snapshot);
        }
    }

    private void HandleMove(ServerPlayer player, MoveInput move, double now)
    {
        // Mensagens fora de ordem são ignoradas (TCP mantém a ordem, mas o cliente pode reenviar).
        if (move.Sequence <= player.LastMoveSequence && player.LastMoveSequence != 0) return;
        player.LastMoveSequence = move.Sequence;

        if (player.Movement.TryMove(move.X, move.Z, now))
        {
            player.Yaw = move.Yaw;
            return;
        }
        _outbox.Send(player.Id, new PositionCorrection { X = player.Movement.X, Z = player.Movement.Z });
    }

    private static EntitySpawned SpawnMessage(ServerPlayer player) => new()
    {
        Id = player.Id, Kind = EntityKind.Player, Name = player.Name, Level = player.Level, MaxHp = player.MaxHp,
        X = player.Movement.X, Z = player.Movement.Z,
    };
}
