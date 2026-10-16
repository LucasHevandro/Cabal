using System.Net.Sockets;
using Game.Server.Net;
using Game.Shared.Net;

namespace Game.Server.Tests;

/// <summary>Testes com TCP de verdade, em porta livre escolhida pelo sistema.</summary>
public class ServerHostTests
{
    private sealed class TestClient : IDisposable
    {
        private readonly TcpClient _tcp = new();
        private readonly FrameBuffer _frames = new();
        private readonly byte[] _buffer = new byte[8192];
        private NetworkStream _stream = null!;

        public async Task ConnectAsync(int port)
        {
            await _tcp.ConnectAsync("127.0.0.1", port);
            _stream = _tcp.GetStream();
        }

        public Task SendAsync(INetMessage message) => _stream.WriteAsync(NetCodec.Encode(message)).AsTask();

        /// <summary>Lê mensagens até chegar uma do tipo pedido que satisfaça o filtro (ou estourar o tempo).</summary>
        public async Task<T> ReceiveAsync<T>(Func<T, bool>? filter = null, TimeSpan? timeout = null) where T : INetMessage
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
            while (true)
            {
                while (_frames.TryRead(out var type, out var payload))
                {
                    if (NetCodec.Decode(type, payload) is T match && (filter == null || filter(match))) return match;
                }
                var read = await _stream.ReadAsync(_buffer, cts.Token);
                if (read == 0) throw new IOException("Servidor fechou a conexão.");
                _frames.Append(_buffer, 0, read);
            }
        }

        public void Dispose() => _tcp.Dispose();
    }

    [Fact]
    public async Task Cliente_conecta_recebe_boas_vindas_e_snapshots()
    {
        using var host = new ServerHost(port: 0);
        host.Start();
        using var client = new TestClient();
        await client.ConnectAsync(host.Port);

        await client.SendAsync(new Hello { Name = "Teste" });
        var welcome = await client.ReceiveAsync<Welcome>();
        var snapshot = await client.ReceiveAsync<Snapshot>();

        Assert.True(welcome.PlayerId > 0);
        Assert.Contains(snapshot.Entities, e => e.Id == welcome.PlayerId);
        // Os coelhos dos Campos do Vento ficam a menos de 60 m da vila: aparecem já no primeiro snapshot.
        Assert.True(snapshot.Entities.Count > 1);
    }

    [Fact]
    public async Task Dois_clientes_se_enxergam()
    {
        using var host = new ServerHost(port: 0);
        host.Start();
        using var ana = new TestClient();
        using var bia = new TestClient();
        await ana.ConnectAsync(host.Port);
        await ana.SendAsync(new Hello { Name = "Ana" });
        var anaId = (await ana.ReceiveAsync<Welcome>()).PlayerId;

        await bia.ConnectAsync(host.Port);
        await bia.SendAsync(new Hello { Name = "Bia" });

        Assert.Equal(anaId, (await bia.ReceiveAsync<EntitySpawned>(e => e.Name == "Ana")).Id);
        Assert.Equal(EntityKind.Player, (await ana.ReceiveAsync<EntitySpawned>(e => e.Name == "Bia")).Kind);
        var snapshot = await bia.ReceiveAsync<Snapshot>(s => s.Entities.Any(e => e.Id == anaId));
        Assert.Contains(snapshot.Entities, e => e.Id == anaId);
    }

    [Fact]
    public async Task Versao_errada_eh_recusada_com_motivo()
    {
        using var host = new ServerHost(port: 0);
        host.Start();
        using var client = new TestClient();
        await client.ConnectAsync(host.Port);

        await client.SendAsync(new Hello { ProtocolVersion = 999, Name = "Antigo" });

        var reject = await client.ReceiveAsync<Reject>();
        Assert.Contains("Versão", reject.Reason);
    }

    [Fact]
    public async Task Latencia_simulada_atrasa_ida_e_volta()
    {
        using var host = new ServerHost(new ServerOptions { Port = 0, SimulatedLatencyMs = 100, AllowGuests = true });
        host.Start();
        using var client = new TestClient();
        await client.ConnectAsync(host.Port);
        await client.SendAsync(new Hello { Name = "Longe" });
        await client.ReceiveAsync<Welcome>();

        var sent = System.Diagnostics.Stopwatch.StartNew();
        await client.SendAsync(new Ping { ClientTime = 1 });
        await client.ReceiveAsync<Pong>();

        Assert.InRange(sent.Elapsed.TotalMilliseconds, 195, 600);
    }

    [Fact]
    public async Task Metricas_contam_ticks_e_envios()
    {
        using var host = new ServerHost(port: 0);
        host.Start();
        using var client = new TestClient();
        await client.ConnectAsync(host.Port);
        await client.SendAsync(new Hello { Name = "Teste" });
        await client.ReceiveAsync<Snapshot>();
        await Task.Delay(300);

        var report = host.Metrics.Report(1, 0);

        Assert.Contains("ticks", report);
        Assert.Contains("KB/s", report);
    }

    [Fact]
    public async Task Ping_mede_ida_e_volta()
    {
        using var host = new ServerHost(port: 0);
        host.Start();
        using var client = new TestClient();
        await client.ConnectAsync(host.Port);
        await client.SendAsync(new Hello { Name = "Teste" });
        await client.ReceiveAsync<Welcome>();

        await client.SendAsync(new Ping { ClientTime = 42 });

        var pong = await client.ReceiveAsync<Pong>();
        Assert.Equal(42, pong.ClientTime);
        Assert.True(pong.ServerTime > 0);
    }
}
