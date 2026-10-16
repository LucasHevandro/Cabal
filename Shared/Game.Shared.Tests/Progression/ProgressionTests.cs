using Game.Shared.Progression;

namespace Game.Shared.Tests.Progression;

public class ProgressionTests
{
    [Fact]
    public void Curva_cresce_a_cada_nivel_e_zera_no_maximo()
    {
        for (var level = 1; level < LevelCurve.MaxLevel - 1; level++)
            Assert.True(LevelCurve.XpToNext(level + 1) > LevelCurve.XpToNext(level));

        Assert.Equal(100, LevelCurve.XpToNext(1));
        Assert.Equal(0, LevelCurve.XpToNext(LevelCurve.MaxLevel));
    }

    [Fact]
    public void XP_suficiente_sobe_de_nivel_e_guarda_a_sobra()
    {
        var progress = new CharacterProgress();

        var gained = progress.AddXp(130);

        Assert.Equal(1, gained);
        Assert.Equal(2, progress.Level);
        Assert.Equal(30, progress.Xp);
    }

    [Fact]
    public void Muita_XP_sobe_varios_niveis_de_uma_vez()
    {
        var progress = new CharacterProgress();
        var total = LevelCurve.XpToNext(1) + LevelCurve.XpToNext(2) + LevelCurve.XpToNext(3) + 5;

        var gained = progress.AddXp(total);

        Assert.Equal(3, gained);
        Assert.Equal(4, progress.Level);
        Assert.Equal(5, progress.Xp);
    }

    [Fact]
    public void Nivel_maximo_nao_acumula_XP()
    {
        var progress = new CharacterProgress(LevelCurve.MaxLevel - 1);

        progress.AddXp(10_000_000);

        Assert.Equal(LevelCurve.MaxLevel, progress.Level);
        Assert.Equal(0, progress.Xp);
        Assert.Equal(0, progress.AddXp(500));
    }

    [Fact]
    public void Atributos_crescem_com_o_nivel()
    {
        var level1 = CharacterStats.ForLevel(1);
        var level10 = CharacterStats.ForLevel(10);

        Assert.Equal(2000, level1.MaxHp);
        Assert.Equal(60, level1.AttackPower);
        Assert.True(level10.MaxHp > level1.MaxHp);
        Assert.True(level10.AttackPower > level1.AttackPower);
        Assert.True(level10.Defense > level1.Defense);
    }

    [Theory]
    [InlineData(5, 5, 100)]   // mesmo nível
    [InlineData(5, 7, 120)]   // 2 acima: +20%
    [InlineData(5, 20, 150)]  // muito acima: teto de +50%
    [InlineData(5, 3, 60)]    // 2 abaixo: −40%
    [InlineData(20, 5, 10)]   // muito abaixo: piso de 10%
    public void XP_ajustada_pela_diferenca_de_nivel(int player, int monster, int expected)
    {
        Assert.Equal(expected, XpRules.Adjusted(100, player, monster));
    }
}
