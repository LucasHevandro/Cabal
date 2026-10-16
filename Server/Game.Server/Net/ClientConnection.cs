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

    public ClientConnection(int connectionId, TcpClient client)
    {
        ConnectionId = connectionId;
        _client = client;
        _client.NoDelay = true; // sem Nagle: mensagens pequenas saem na hora
        _stream = client.GetStream();
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "?";
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
                    onMessage(this, NetCodec.Decode(type, payload));
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
