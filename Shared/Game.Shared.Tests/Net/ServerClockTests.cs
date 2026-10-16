using Game.Shared.Net;

namespace Game.Shared.Tests.Net;

public class ServerClockTests
{
    [Fact]
    public void Estima_a_diferenca_supondo_o_carimbo_no_meio_da_ida_e_volta()
    {
        var clock = new ServerClock();

        // Servidor está 100 s à frente; ida e volta de 0,2 s.
        clock.AddSample(localSent: 10.0, serverTime: 110.1, localReceived: 10.2);

        Assert.True(clock.IsSynced);
        Assert.Equal(100.0, clock.Offset, 6);
        Assert.Equal(0.2, clock.LastRtt, 6);
        Assert.Equal(115.0, clock.ServerNow(15.0), 6);
        Assert.Equal(15.0, clock.ToLocal(115.0), 6);
    }

    [Fact]
    public void Usa_a_amostra_de_menor_latencia()
    {
        var clock = new ServerClock();
        clock.AddSample(10.0, 110.05, 10.1);   // rtt 0,1 → offset 100 (a melhor amostra)
        clock.AddSample(20.0, 120.5, 21.0);    // rtt 1,0 → offset 100, mas lenta
        clock.AddSample(30.0, 130.9, 30.4);    // rtt 0,4 com fila na volta → offset 100,7 (errado)

        Assert.Equal(100.0, clock.Offset, 6);
        Assert.Equal(0.1, clock.BestRtt, 6);
        Assert.Equal(0.4, clock.LastRtt, 6);
    }

    [Fact]
    public void Semente_vale_ate_a_primeira_amostra()
    {
        var clock = new ServerClock();
        clock.Seed(serverTime: 50, localNow: 5);
        Assert.Equal(45, clock.Offset, 6);
        Assert.False(clock.IsSynced);

        clock.AddSample(10.0, 60.0, 10.0);
        clock.Seed(serverTime: 999, localNow: 0);
        Assert.Equal(50, clock.Offset, 6);
    }
}
