using Game.Shared.Combat;
using Game.Shared.Loot;

namespace Game.Shared.Tests.Loot;

public class DropTableTests
{
    private sealed class SequenceRandom : IRandomSource
    {
        private readonly double[] _values;
        private int _index;

        public SequenceRandom(params double[] values) => _values = values;

        public double NextDouble() => _values[_index++ % _values.Length];
    }

    private static DropTable Table() => new(10, 20,
        new DropEntry("pele_lobo", ItemRarity.Common, chance: 0.5, minQuantity: 1, maxQuantity: 3),
        new DropEntry("presa_lobo", ItemRarity.Rare, chance: 0.05));

    [Fact]
    public void Sorteio_abaixo_da_chance_faz_o_item_cair()
    {
        // pele: chance 0,1 < 0,5 cai, quantidade 0,99 → 3; presa: 0,9 ≥ 0,05 não cai; Alz 0,0 → 10.
        var loot = Table().Roll(new SequenceRandom(0.1, 0.99, 0.9, 0.0));

        var item = Assert.Single(loot.Items);
        Assert.Equal("pele_lobo", item.ItemId);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(10, loot.Alz);
    }

    [Fact]
    public void Sorteio_acima_da_chance_nao_derruba_nada()
    {
        var loot = Table().Roll(new SequenceRandom(0.9, 0.9, 0.999));

        Assert.Empty(loot.Items);
        Assert.Equal(20, loot.Alz);
    }

    [Fact]
    public void Taxa_de_drop_bate_com_a_chance_em_muitas_rodadas()
    {
        var table = Table();
        var random = new SystemRandomSource(7);
        var pele = 0;
        var presa = 0;
        const int rounds = 20_000;

        for (var i = 0; i < rounds; i++)
        {
            foreach (var item in table.Roll(random).Items)
            {
                if (item.ItemId == "pele_lobo") pele++;
                else presa++;
            }
        }

        Assert.InRange(pele / (double)rounds, 0.48, 0.52);
        Assert.InRange(presa / (double)rounds, 0.04, 0.06);
    }

    [Fact]
    public void Tabela_invalida_eh_rejeitada()
    {
        Assert.Throws<ArgumentException>(() => new DropTable(10, 5).Validate());
        Assert.Throws<ArgumentException>(() => new DropTable(0, 0, new DropEntry("x", ItemRarity.Common, 1.5)).Validate());
        Assert.Throws<ArgumentException>(() => new DropTable(0, 0, new DropEntry("", ItemRarity.Common, 0.5)).Validate());
        Table().Validate();
    }
}
