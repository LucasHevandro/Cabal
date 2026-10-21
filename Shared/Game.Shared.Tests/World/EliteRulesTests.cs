using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Shared.Tests.World;

public class EliteRulesTests
{
    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public double NextDouble() => _value;
    }

    [Fact]
    public void So_monstro_comum_do_mundo_aberto_pode_ser_elite()
    {
        Assert.True(EliteRules.CanBeElite(new SpawnDefinition(), 0));
        Assert.False(EliteRules.CanBeElite(new SpawnDefinition(), 3));
        Assert.False(EliteRules.CanBeElite(new SpawnDefinition { IsBoss = true }, 0));
        Assert.False(EliteRules.CanBeElite(new SpawnDefinition { Scheduled = true }, 0));
    }

    [Fact]
    public void Sorteio_respeita_a_chance()
    {
        var spawn = new SpawnDefinition();

        Assert.True(EliteRules.Roll(spawn, 0, new FixedRandom(0.04)));
        Assert.False(EliteRules.Roll(spawn, 0, new FixedRandom(0.05)));
        Assert.False(EliteRules.Roll(new SpawnDefinition { IsBoss = true }, 0, new FixedRandom(0.0)));
    }

    [Fact]
    public void Multiplicadores_de_vida_dano_e_xp()
    {
        Assert.Equal(400, EliteRules.MaxHp(100));
        Assert.Equal(75, EliteRules.Damage(50));
        Assert.Equal(120, EliteRules.Xp(30));
    }

    [Fact]
    public void Drop_do_elite_rola_duas_vezes_e_triplica_o_alz()
    {
        var table = new DropTable(10, 10, new DropEntry("pele_lobo", ItemRarity.Common, chance: 0.5));

        var loot = EliteRules.RollLoot(table, new FixedRandom(0.1));

        Assert.Equal(2, loot.Items.Count);
        Assert.Equal(30, loot.Alz);
    }

    [Fact]
    public void Nome_do_elite_vem_do_texto_traduzido()
    {
        Assert.Equal("Lobo Cinzento (Elite)", Loc.T("monster.elite_name", Loc.T("monster.lobo_cinzento")));
    }
}
