using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Game.Shared.Net;

namespace Game.Server.Net;

/// <summary>
/// Uma conexão TCP de cliente: uma tarefa lê e decodifica mensagens, outra envia o que estiver na fila.
/// Mensagens recebidas vão para a fila do loop do jogo; nada aqui mexe no mundo.
/// </summary>
public sealed class ClientConnection
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _closed = new();

    public int ConnectionId { get; }
    public string RemoteEndPoint { get; }

    /// <summary>Id do jogador depois do Hello aceito (0 antes disso).</summary>
    public int PlayerId { get; set; }

    /// <summary>Hello com conta recebido; esperando o personagem chegar do banco.</summary>
    public bool LoginPending { get; set; }

    public bool IsClosed => _closed.IsCancellationRequested;

    /// <summary>Maior mensagem que um cliente pode mandar (as dele são pequenas: chat de 120 letras, ofertas de troca).</summary>
    public const int MaxClientPayload = 16 * 1024;

    private static readonly double TicksToSeconds = 1.0 / Stopwatch.Frequency;
    private readonly long _createdAt = Stopwatch.GetTimestamp();
    private long _lastMessageAt = Stopwatch.GetTimestamp();

    /// <summary>Anti flood desta conexão (só a thread de leitura mexe nele).</summary>
    public InboundLimiter Limiter { get; init; } = new();

    /// <summary>Endereço IP do cliente (para o limite de conexões por IP).</summary>
    public IPAddress? Address { get; }

    /// <summary>Segundos desde que a conexão abriu.</summary>
    public double AgeSeconds => (Stopwatch.GetTimestamp() - _createdAt) * TicksToSeconds;

    /// <summary>Segundos desde a última mensagem recebida.</summary>
    public double IdleSeconds => (Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastMessageAt)) * TicksToSeconds;

    public ClientConnection(int connectionId, TcpClient client)
    {
        ConnectionId = connectionId;
        _client = client;
        _client.NoDelay = true; // sem Nagle: mensagens pequenas saem na hora
        _stream = client.GetStream();
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
        Address = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
    }

    /// <summary>Começa a ler e enviar. <paramref name="onMessage"/> e <paramref name="onClosed"/> rodam na thread de rede.</summary>
    public void Start(Action<ClientConnection, INetMessage> onMessage, Action<ClientConnection, string> onClosed)
    {
        _ = Task.Run(() => ReceiveLoop(onMessage, onClosed));
        _ = Task.Run(SendLoop);
    }

    /// <summary>Põe uma mensagem já codificada na fila de envio (seguro de qualquer thread).</summary>
    public void Send(byte[] frame)
    {
        if (!IsClosed) _outgoing.Writer.TryWrite(frame);
    }

    public void Close()
    {
        if (_closed.IsCancellationRequested) return;
        _closed.Cancel();
        _outgoing.Writer.TryComplete();
        try { _client.Close(); } catch { /* já fechado */ }
    }

    private async Task ReceiveLoop(Action<ClientConnection, INetMessage> onMessage, Action<ClientConnection, string> onClosed)
    {
        var buffer = new byte[8192];
        var frames = new FrameBuffer();
        var reason = "desconectou";
        try
        {
            while (!IsClosed)
            {
                var read = await _stream.ReadAsync(buffer, _closed.Token);
                if (read == 0) break;
                frames.Append(buffer, 0, read);
                while (frames.TryRead(out var type, out var payload))
                {
                    if (payload.Length > MaxClientPayload)
                    {
                        reason = $"mensagem grande demais ({payload.Length} bytes)";
                        return;
                    }
                    var now = Stopwatch.GetTimestamp();
                    Interlocked.Exchange(ref _lastMessageAt, now);
                    // Acima do limite a mensagem é ignorada; insistindo muito, a conexão cai.
                    if (!Limiter.Allow(now * TicksToSeconds))
                    {
                        if (!Limiter.ShouldKick) continue;
                        reason = $"excesso de mensagens ({Limiter.Dropped} descartadas)";
                        return;
                    }
                    onMessage(this, NetCodec.Decode(type, payload));
                }
            }
        }
        catch (ProtocolException ex)
        {
            reason = "erro de protocolo: " + ex.Message;
        }
        catch (OperationCanceledException)
        {
            reason = "fechada pelo servidor";
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            reason = "conexão perdida";
        }
        finally
        {
            Close();
            onClosed(this, reason);
        }
    }

    private async Task SendLoop()
    {
        try
        {
            await foreach (var frame in _outgoing.Reader.ReadAllAsync(_closed.Token))
                await _stream.WriteAsync(frame, _closed.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
        {
            Close();
        }
    }
}
