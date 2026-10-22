using System.Net.Sockets;
using Game.Server.Net;
using Game.Shared.Net;

namespace Game.Server.Tests;

/// <summary>Anti flood e prazos de conexão (Fase 8: segurança).</summary>
public class FloodTests
{
    [Fact]
    public void Limite_deixa_passar_o_ritmo_normal_e_corta_a_rajada()
    {
        var limiter = new InboundLimiter(perSecond: 40, burst: 80, kickAfterDrops: 100);

        // Cliente normal: 21 mensagens por segundo durante 30 s, nada é descartado.
        var now = 0.0;
        for (var i = 0; i < 30 * 21; i++)
        {
            Assert.True(limiter.Allow(now));
            now += 1.0 / 21;
        }
        Assert.Equal(0, limiter.Dropped);

        // Rajada de 200 no mesmo instante: passa a folga (80 menos o que já foi gasto) e o resto cai.
        var passed = 0;
        for (var i = 0; i < 200; i++) if (limiter.Allow(now)) passed++;
        Assert.InRange(passed, 1, 80);
        Assert.True(limiter.Dropped > 100);
        Assert.True(limiter.ShouldKick);
    }

    [Fact]
    public void Descartes_espalhados_nao_derrubam()
    {
        var limiter = new InboundLimiter(perSecond: 40, burst: 80, kickAfterDrops: 100);
        var now = 0.0;
        // 45 por segundo durante 60 s: um pouco acima do limite, perde algumas, mas nunca 100 em 5 s.
        for (var i = 0; i < 60 * 45; i++)
        {
            limiter.Allow(now);
            now += 1.0 / 45;
        }
        Assert.True(limiter.Dropped > 0);
        Assert.False(limiter.ShouldKick);
    }

    private static async Task<bool> ClosedWithin(NetworkStream stream, TimeSpan timeout)
    {
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cts.Token);
                if (read == 0) return true;
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    [Fact]
    public async Task Quem_inunda_o_servidor_de_mensagens_cai()
    {
        using var host = new ServerHost(new ServerOptions { Port = 0, AllowGuests = true });
        host.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", host.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(NetCodec.Encode(new Hello { Name = "Inundador" }));

        // 2.000 pings de uma vez: muito acima de 40 por segundo.
        var ping = NetCodec.Encode(new Ping { ClientTime = 1 });
        var burst = new byte[ping.Length * 2000];
        for (var i = 0; i < 2000; i++) Buffer.BlockCopy(ping, 0, burst, i * ping.Length, ping.Length);
        try
        {
            await stream.WriteAsync(burst);
        }
        catch (IOException)
        {
            // O servidor pode fechar no meio da escrita.
        }
        Assert.True(await ClosedWithin(stream, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Fila_de_envio_cheia_derruba_a_conexao()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        using var tcp = new TcpClient();
        var accept = listener.AcceptTcpClientAsync();
        await tcp.ConnectAsync("127.0.0.1", ((System.Net.IPEndPoint)listener.LocalEndpoint).Port);
        using var server = await accept;
        // Sem Start: nada sai da fila, como um cliente que não lê o que recebe.
        var connection = new ClientConnection(1, server) { MaxPendingBytes = 64 * 1024 };

        var frame = new byte[16 * 1024];
        for (var i = 0; i < 4; i++) connection.Send(frame);
        Assert.False(connection.IsClosed); // 64 KB na fila: ainda dentro do limite

        connection.Send(frame);
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task Conexao_que_nao_manda_hello_cai_com_aviso()
    {
        using var host = new ServerHost(new ServerOptions { Port = 0, AllowGuests = true, HelloTimeoutSeconds = 1 });
        host.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", host.Port);
        var stream = tcp.GetStream();

        var frames = new FrameBuffer();
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Reject? reject = null;
        while (reject == null)
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            if (read == 0) break;
            frames.Append(buffer, 0, read);
            while (frames.TryRead(out var type, out var payload))
                if (NetCodec.Decode(type, payload) is Reject r) reject = r;
        }
        Assert.NotNull(reject);
        Assert.Equal("net.hello_timeout", reject!.Reason.Key);
    }

    [Fact]
    public async Task Cliente_parado_cai_depois_do_prazo()
    {
        using var host = new ServerHost(new ServerOptions { Port = 0, AllowGuests = true, IdleTimeoutSeconds = 2 });
        host.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", host.Port);
        var stream = tcp.GetStream();
        await stream.WriteAsync(NetCodec.Encode(new Hello { Name = "Parado" }));
        // Entra e não manda mais nada (nem ping): o servidor fecha em ~2 s.
        Assert.True(await ClosedWithin(stream, TimeSpan.FromSeconds(6)));
    }
}
