using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Game.Server.World;
using Game.Shared.Net;

namespace Game.Server.Net;

/// <summary>Opções do servidor (linha de comando).</summary>
public sealed record ServerOptions
{
    public int Port { get; init; } = Protocol.DefaultPort;

    /// <summary>Atraso artificial em cada sentido, em ms (ida e volta = o dobro). Só para testes.</summary>
    public int SimulatedLatencyMs { get; init; }

    /// <summary>Variação aleatória extra em cada sentido, em ms (de 0 até este valor).</summary>
    public int SimulatedJitterMs { get; init; }

    /// <summary>De quantos em quantos segundos registrar as métricas (0 = nunca).</summary>
    public double StatsIntervalSeconds { get; init; }
}

/// <summary>
/// Liga a rede ao mundo: aceita conexões TCP, junta as mensagens recebidas numa fila e roda o loop do jogo
/// numa thread só, a <see cref="GameWorld.TickRate"/> ticks por segundo. Opcionalmente atrasa as mensagens
/// (simulador de latência) e registra métricas de desempenho.
/// </summary>
public sealed class ServerHost : IOutbox, IDisposable
{
    private readonly ServerOptions _options;
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<(ClientConnection Connection, Action<double> Work)> _inbox = new();
    private readonly Dictionary<int, ClientConnection> _byPlayer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _log;
    private readonly Random _jitter = new();
    private int _nextConnectionId;
    private Thread? _loopThread;

    // Simulador de latência: trabalho e envios esperando a hora certa (só usados pela thread do loop).
    private readonly List<(double At, Action<double> Work)> _delayedIn = new();
    private readonly List<(double At, ClientConnection Connection, byte[] Frame)> _delayedOut = new();
    private readonly Dictionary<int, double> _lastInAt = new();
    private readonly Dictionary<int, double> _lastOutAt = new();

    public GameWorld World { get; }
    public ServerMetrics Metrics { get; } = new();

    /// <summary>Porta em uso (útil quando criado com porta 0 nos testes).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Segundos desde que o servidor iniciou: o relógio do jogo.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    public ServerHost(int port, Action<string>? log = null) : this(new ServerOptions { Port = port }, log) { }

    public ServerHost(ServerOptions options, Action<string>? log = null)
    {
        _options = options;
        _log = log ?? (_ => { });
        _listener = new TcpListener(IPAddress.Any, options.Port);
        World = new GameWorld(this);
    }

    private bool SimulatingLatency => _options.SimulatedLatencyMs > 0 || _options.SimulatedJitterMs > 0;

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _loopThread = new Thread(GameLoop) { IsBackground = true, Name = "GameLoop" };
        _loopThread.Start();
        _log($"Servidor ouvindo na porta {Port} ({GameWorld.TickRate} ticks/s, protocolo v{Protocol.Version})");
        if (SimulatingLatency)
            _log($"ATENÇÃO: latência simulada de {_options.SimulatedLatencyMs} ms (+0 a {_options.SimulatedJitterMs} ms) em cada sentido");
    }

    public void Stop()
    {
        if (_stop.IsCancellationRequested) return;
        _stop.Cancel();
        _listener.Stop();
        _loopThread?.Join(TimeSpan.FromSeconds(2));
        foreach (var connection in _byPlayer.Values.ToList()) connection.Close();
    }

    public void Dispose() => Stop();

    void IOutbox.Send(int playerId, INetMessage message)
    {
        if (!_byPlayer.TryGetValue(playerId, out var connection)) return;
        var frame = NetCodec.Encode(message);
        Metrics.CountSent(frame.Length);
        if (!SimulatingLatency)
        {
            connection.Send(frame);
            return;
        }
        var at = DelayedTime(_lastOutAt, connection.ConnectionId);
        _delayedOut.Add((at, connection, frame));
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            var connection = new ClientConnection(Interlocked.Increment(ref _nextConnectionId), client);
            _log($"Conexão #{connection.ConnectionId} de {connection.RemoteEndPoint}");
            connection.Start(OnMessage, OnClosed);
        }
    }

    // Chamados na thread de rede: só enfileiram trabalho para o loop do jogo.
    private void OnMessage(ClientConnection connection, INetMessage message)
    {
        Metrics.CountReceived();
        _inbox.Enqueue((connection, now => Dispatch(connection, message, now)));
    }

    private void OnClosed(ClientConnection connection, string reason) =>
        _inbox.Enqueue((connection, _ =>
        {
            _log($"Conexão #{connection.ConnectionId} encerrada ({reason})");
            _lastInAt.Remove(connection.ConnectionId);
            _lastOutAt.Remove(connection.ConnectionId);
            if (connection.PlayerId == 0) return;
            _byPlayer.Remove(connection.PlayerId);
            World.RemovePlayer(connection.PlayerId);
        }));

    private void Dispatch(ClientConnection connection, INetMessage message, double now)
    {
        if (connection.IsClosed) return;

        if (connection.PlayerId == 0)
        {
            if (message is not Hello hello)
            {
                Refuse(connection, "Envie Hello primeiro.");
                return;
            }
            if (hello.ProtocolVersion != Protocol.Version)
            {
                Refuse(connection, $"Versão do jogo incompatível (cliente v{hello.ProtocolVersion}, servidor v{Protocol.Version}). Atualize o jogo.");
                return;
            }

            // A conexão é registrada assim que o id existe, antes das boas-vindas saírem.
            var player = World.AddPlayer(hello.Name, now, id =>
            {
                connection.PlayerId = id;
                _byPlayer[id] = connection;
            });
            _log($"Conexão #{connection.ConnectionId} entrou como {player.Name} (id {player.Id})");
            return;
        }

        World.Handle(connection.PlayerId, message, now);
    }

    private void Refuse(ClientConnection connection, string reason)
    {
        connection.Send(NetCodec.Encode(new Reject { Reason = reason }));
        _log($"Conexão #{connection.ConnectionId} recusada: {reason}");
        // Dá tempo para o Reject sair antes de fechar.
        _ = Task.Delay(200).ContinueWith(_ => connection.Close());
    }

    private void GameLoop()
    {
        var tickSeconds = 1.0 / GameWorld.TickRate;
        var nextTick = Now;
        var nextStatsAt = Now + _options.StatsIntervalSeconds;
        while (!_stop.IsCancellationRequested)
        {
            var now = Now;
            while (_inbox.TryDequeue(out var item))
            {
                if (!SimulatingLatency) item.Work(now);
                else _delayedIn.Add((DelayedTime(_lastInAt, item.Connection.ConnectionId), item.Work));
            }
            if (SimulatingLatency) RunDelayed(now);

            if (now >= nextTick)
            {
                var started = Stopwatch.GetTimestamp();
                World.Tick(now);
                Metrics.CountTick(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                nextTick += tickSeconds;
                if (now - nextTick > 1) nextTick = now; // atrasou demais: não tenta compensar todos os ticks
            }

            if (_options.StatsIntervalSeconds > 0 && now >= nextStatsAt)
            {
                nextStatsAt = now + _options.StatsIntervalSeconds;
                _log(Metrics.Report(World.Players.Count, World.Monsters.Count));
            }

            var wait = nextTick - Now;
            if (wait > 0.001) Thread.Sleep(TimeSpan.FromSeconds(Math.Min(wait, SimulatingLatency ? 0.002 : 0.005)));
        }
    }

    /// <summary>Executa o que já chegou na hora (entrada) e envia o que já pode sair (saída).</summary>
    private void RunDelayed(double now)
    {
        for (var i = 0; i < _delayedIn.Count;)
        {
            if (_delayedIn[i].At > now)
            {
                i++;
                continue;
            }
            var work = _delayedIn[i].Work;
            _delayedIn.RemoveAt(i);
            work(now);
        }
        for (var i = 0; i < _delayedOut.Count;)
        {
            if (_delayedOut[i].At > now)
            {
                i++;
                continue;
            }
            var (_, connection, frame) = _delayedOut[i];
            _delayedOut.RemoveAt(i);
            connection.Send(frame);
        }
    }

    /// <summary>Horário em que a mensagem "chega", sem passar à frente de uma anterior da mesma conexão (como no TCP).</summary>
    private double DelayedTime(Dictionary<int, double> lastAt, int connectionId)
    {
        var delay = (_options.SimulatedLatencyMs + _jitter.NextDouble() * _options.SimulatedJitterMs) / 1000.0;
        var at = Now + delay;
        if (lastAt.TryGetValue(connectionId, out var previous) && at < previous) at = previous;
        lastAt[connectionId] = at;
        return at;
    }
}

/// <summary>Contadores de desempenho do servidor, zerados a cada relatório.</summary>
public sealed class ServerMetrics
{
    private readonly Stopwatch _window = Stopwatch.StartNew();
    private TimeSpan _cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
    private long _bytesSent;
    private long _messagesSent;
    private long _messagesReceived;
    private int _ticks;
    private double _tickTotalMs;
    private double _tickMaxMs;

    public void CountSent(int bytes)
    {
        Interlocked.Add(ref _bytesSent, bytes);
        Interlocked.Increment(ref _messagesSent);
    }

    public void CountReceived() => Interlocked.Increment(ref _messagesReceived);

    public void CountTick(double milliseconds)
    {
        _ticks++;
        _tickTotalMs += milliseconds;
        if (milliseconds > _tickMaxMs) _tickMaxMs = milliseconds;
    }

    /// <summary>Resumo desde o último relatório (e zera os contadores).</summary>
    public string Report(int players, int monsters)
    {
        var seconds = Math.Max(0.001, _window.Elapsed.TotalSeconds);
        var cpu = Process.GetCurrentProcess().TotalProcessorTime;
        var cpuSeconds = (cpu - _cpuAtStart).TotalSeconds;
        var coreUsage = cpuSeconds / seconds * 100;
        var machineUsage = coreUsage / Environment.ProcessorCount;
        var kbPerSecond = Interlocked.Exchange(ref _bytesSent, 0) / 1024.0 / seconds;
        var outRate = Interlocked.Exchange(ref _messagesSent, 0) / seconds;
        var inRate = Interlocked.Exchange(ref _messagesReceived, 0) / seconds;
        var avgTick = _ticks == 0 ? 0 : _tickTotalMs / _ticks;
        var ticksPerSecond = _ticks / seconds;

        var report =
            $"[métricas] {players} jogadores, {monsters} monstros | ticks {ticksPerSecond:0.0}/s, " +
            $"médio {avgTick:0.00} ms, máx {_tickMaxMs:0.00} ms | CPU {coreUsage:0.0}% de 1 núcleo " +
            $"({machineUsage:0.0}% da máquina) | saída {kbPerSecond:0.0} KB/s" +
            (players > 0 ? $" ({kbPerSecond / players:0.0} KB/s por jogador)" : "") +
            $" | mensagens {outRate:0}/s saindo, {inRate:0}/s entrando";

        _window.Restart();
        _cpuAtStart = cpu;
        _ticks = 0;
        _tickTotalMs = 0;
        _tickMaxMs = 0;
        return report;
    }
}
