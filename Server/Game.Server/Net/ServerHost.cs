using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Game.Server.World;
using Game.Shared.Net;

namespace Game.Server.Net;

/// <summary>
/// Liga a rede ao mundo: aceita conexões TCP, junta as mensagens recebidas numa fila e roda o loop do jogo
/// numa thread só, a <see cref="GameWorld.TickRate"/> ticks por segundo.
/// </summary>
public sealed class ServerHost : IOutbox, IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentQueue<Action<double>> _inbox = new();
    private readonly Dictionary<int, ClientConnection> _byPlayer = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _stop = new();
    private readonly Action<string> _log;
    private int _nextConnectionId;
    private Thread? _loopThread;

    public GameWorld World { get; }

    /// <summary>Porta em uso (útil quando criado com porta 0 nos testes).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Segundos desde que o servidor iniciou: o relógio do jogo.</summary>
    public double Now => _clock.Elapsed.TotalSeconds;

    public ServerHost(int port, Action<string>? log = null)
    {
        _log = log ?? (_ => { });
        _listener = new TcpListener(IPAddress.Any, port);
        World = new GameWorld(this);
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoop);
        _loopThread = new Thread(GameLoop) { IsBackground = true, Name = "GameLoop" };
        _loopThread.Start();
        _log($"Servidor ouvindo na porta {Port} ({GameWorld.TickRate} ticks/s, protocolo v{Protocol.Version})");
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
        if (_byPlayer.TryGetValue(playerId, out var connection)) connection.Send(NetCodec.Encode(message));
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
    private void OnMessage(ClientConnection connection, INetMessage message) =>
        _inbox.Enqueue(now => Dispatch(connection, message, now));

    private void OnClosed(ClientConnection connection, string reason) =>
        _inbox.Enqueue(_ =>
        {
            _log($"Conexão #{connection.ConnectionId} encerrada ({reason})");
            if (connection.PlayerId == 0) return;
            _byPlayer.Remove(connection.PlayerId);
            World.RemovePlayer(connection.PlayerId);
        });

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
        while (!_stop.IsCancellationRequested)
        {
            var now = Now;
            while (_inbox.TryDequeue(out var work)) work(now);

            if (now >= nextTick)
            {
                World.Tick(now);
                nextTick += tickSeconds;
                if (now - nextTick > 1) nextTick = now; // atrasou demais: não tenta compensar todos os ticks
            }

            var wait = nextTick - Now;
            if (wait > 0.001) Thread.Sleep(TimeSpan.FromSeconds(Math.Min(wait, 0.005)));
        }
    }
}
