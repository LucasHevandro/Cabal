using Game.Shared.Combat;

namespace Game.Shared.Tests.Combat;

public class DamageFormulaTests
{
    /// <summary>Devolve sempre os mesmos números, em ordem: variação e depois o sorteio do crítico.</summary>
    private sealed class FixedRandom : IRandomSource
    {
        private readonly double[] _values;
        private int _index;

        public FixedRandom(params double[] values) => _values = values;

        public double NextDouble() => _values[_index++ % _values.Length];
    }

    // Variação 0,5 → fator 1,00 exato; sorteio de crítico 0,99 → sem crítico.
    private static readonly IRandomSource NoVariationNoCrit = new FixedRandom(0.5, 0.99);

    private static DamageInput Input(int skill = 100, int attack = 0, double multiplier = 1, double critChance = 0,
        double critMultiplier = 1.5, int defense = 0) =>
        new(skill, attack, multiplier, critChance, critMultiplier, defense);

    [Fact]
    public void Sem_defesa_e_sem_variacao_o_dano_eh_skill_mais_ataque()
    {
        var result = DamageFormula.Calculate(Input(skill: 120, attack: 80), new FixedRandom(0.5, 0.99));

        Assert.Equal(200, result.Amount);
        Assert.False(result.Critical);
    }

    [Fact]
    public void Multiplicador_de_combo_escala_o_dano()
    {
        var result = DamageFormula.Calculate(Input(skill: 100, multiplier: 1.2), new FixedRandom(0.5, 0.99));

        Assert.Equal(120, result.Amount);
    }

    [Fact]
    public void Defesa_igual_a_escala_corta_o_dano_pela_metade()
    {
        var result = DamageFormula.Calculate(Input(skill: 1000, defense: (int)DamageFormula.DefenseScale),
            new FixedRandom(0.5, 0.99));

        Assert.Equal(500, result.Amount);
    }

    [Fact]
    public void Defesa_alta_reduz_mas_nunca_anula()
    {
        Assert.True(DamageFormula.Mitigation(1_000_000) < 1);
        var result = DamageFormula.Calculate(Input(skill: 10, defense: 1_000_000), new FixedRandom(0.5, 0.99));

        Assert.Equal(1, result.Amount);
    }

    [Fact]
    public void Critico_aplica_o_multiplicador()
    {
        var result = DamageFormula.Calculate(Input(skill: 100, critChance: 0.5, critMultiplier: 1.5), new FixedRandom(0.5, 0.1));

        Assert.True(result.Critical);
        Assert.Equal(150, result.Amount);
    }

    [Theory]
    [InlineData(0.0, 92)]
    [InlineData(0.999999, 108)]
    public void Variacao_fica_entre_92_e_108_por_cento(double roll, int expected)
    {
        var result = DamageFormula.Calculate(Input(skill: 100), new FixedRandom(roll, 0.99));

        Assert.Equal(expected, result.Amount);
    }

    [Fact]
    public void Mesma_semente_da_o_mesmo_resultado()
    {
        var input = Input(skill: 150, attack: 50, critChance: 0.3, defense: 120);
        var a = new SystemRandomSource(42);
        var b = new SystemRandomSource(42);

        for (var i = 0; i < 20; i++)
        {
            var ra = DamageFormula.Calculate(input, a);
            var rb = DamageFormula.Calculate(input, b);
            Assert.Equal(ra.Amount, rb.Amount);
            Assert.Equal(ra.Critical, rb.Critical);
        }
    }

    [Fact]
    public void Dano_minimo_eh_1()
    {
        var result = DamageFormula.Calculate(Input(skill: 0, multiplier: 0), NoVariationNoCrit);

        Assert.Equal(1, result.Amount);
    }
}
