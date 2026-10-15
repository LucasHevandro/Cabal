using Game.Shared.Core;

namespace Game.Shared.Tests.Core;

public class ResourcePoolTests
{
    [Fact]
    public void Comeca_cheio_por_padrao()
    {
        var pool = new ResourcePool(1000);

        Assert.Equal(1000, pool.Current);
    }

    [Fact]
    public void Gasta_quando_ha_o_suficiente()
    {
        var pool = new ResourcePool(1000, 600);

        Assert.True(pool.TrySpend(500));
        Assert.Equal(100, pool.Current);
    }

    [Fact]
    public void Nao_gasta_quando_falta()
    {
        var pool = new ResourcePool(1000, 400);

        Assert.False(pool.TrySpend(500));
        Assert.Equal(400, pool.Current);
    }

    [Fact]
    public void Add_respeita_os_limites_e_informa_quanto_mudou()
    {
        var pool = new ResourcePool(100, 90);

        Assert.Equal(10, pool.Add(50));
        Assert.Equal(100, pool.Current);
        Assert.Equal(-100, pool.Add(-500));
        Assert.Equal(0, pool.Current);
    }

    [Fact]
    public void Reduzir_o_maximo_corta_o_valor_atual()
    {
        var pool = new ResourcePool(100);

        pool.SetMax(60);

        Assert.Equal(60, pool.Current);
    }

    [Fact]
    public void Valores_negativos_sao_rejeitados()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourcePool(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourcePool(10).TrySpend(-1));
    }
}
