using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Game.Shared.Net;

namespace Game.Client.Net
{
    /// <summary>
    /// Conexão TCP com o servidor. Uma thread de fundo recebe e decodifica; o jogo lê as mensagens na thread
    /// principal com <see cref="TryReceive"/>. Envio é feito direto (mensagens pequenas, poucas por quadro).
    /// Não usa nada do Unity: pode ser testado e reaproveitado em bots.
    /// </summary>
    public sealed class NetClient : IDisposable
    {
        public enum Status
        {
            Idle,
            Connecting,
            Connected,
            Disconnected,
        }

        private readonly ConcurrentQueue<INetMessage> _inbox = new ConcurrentQueue<INetMessage>();
        private readonly object _sendLock = new object();
        private TcpClient _tcp;
        private NetworkStream _stream;
        private Thread _receiveThread;
        private volatile Status _status = Status.Idle;
        private volatile string _error = "";

        public Status State => _status;

        /// <summary>Motivo da desconexão ou falha (vazio se nada deu errado).</summary>
        public string Error => _error;

        /// <summary>Bytes enviados e recebidos (para o HUD de depuração).</summary>
        public long BytesSent { get; private set; }
        public long BytesReceived => Interlocked.Read(ref _bytesReceived);
        private long _bytesReceived;

        /// <summary>Começa a conectar em segundo plano. Acompanhe <see cref="State"/>.</summary>
        public void Connect(string host, int port, int timeoutMs = 5000)
        {
            if (_status == Status.Connecting || _status == Status.Connected) return;
            _status = Status.Connecting;
            _error = "";
            _receiveThread = new Thread(() => Run(host, port, timeoutMs)) { IsBackground = true, Name = "NetClient" };
            _receiveThread.Start();
        }

        public bool TryReceive(out INetMessage message) => _inbox.TryDequeue(out message);

        public void Send(INetMessage message)
        {
            if (_status != Status.Connected) return;
            var frame = NetCodec.Encode(message);
            try
            {
                lock (_sendLock)
                {
                    _stream.Write(frame, 0, frame.Length);
                    BytesSent += frame.Length;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SocketException || ex is ObjectDisposedException)
            {
                Fail("conexão perdida ao enviar");
            }
        }

        public void Disconnect()
        {
            if (_status == Status.Disconnected || _status == Status.Idle) return;
            Fail("");
        }

        public void Dispose() => Disconnect();

        private void Run(string host, int port, int timeoutMs)
        {
            try
            {
                _tcp = new TcpClient { NoDelay = true };
                var connect = _tcp.ConnectAsync(host, port);
                if (!connect.Wait(timeoutMs))
                {
                    Fail($"tempo esgotado conectando em {host}:{port}");
                    return;
                }
                _stream = _tcp.GetStream();
                _status = Status.Connected;
                ReceiveLoop();
            }
            catch (AggregateException ex) when (ex.InnerException is SocketException socket)
            {
                Fail($"não foi possível conectar em {host}:{port} ({socket.SocketErrorCode})");
            }
            catch (Exception ex) when (ex is SocketException || ex is IOException || ex is ObjectDisposedException)
            {
                Fail(_status == Status.Connected ? "conexão perdida" : $"não foi possível conectar em {host}:{port}");
            }
            catch (ProtocolException ex)
            {
                Fail("erro de protocolo: " + ex.Message);
            }
        }

        private void ReceiveLoop()
        {
            var buffer = new byte[16384];
            var frames = new FrameBuffer();
            while (_status == Status.Connected)
            {
                var read = _stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    Fail("o servidor encerrou a conexão");
                    return;
                }
                Interlocked.Add(ref _bytesReceived, read);
                frames.Append(buffer, 0, read);
                while (frames.TryRead(out var type, out var payload)) _inbox.Enqueue(NetCodec.Decode(type, payload));
            }
        }

        private void Fail(string reason)
        {
            if (_status == Status.Disconnected) return;
            if (!string.IsNullOrEmpty(reason)) _error = reason;
            _status = Status.Disconnected;
            try { _tcp?.Close(); } catch { /* já fechado */ }
        }
    }
}
