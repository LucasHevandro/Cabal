using System;

namespace Game.Shared.Combat
{
    /// <summary>Fonte de números aleatórios em [0, 1). Permite resultados repetíveis nos testes.</summary>
    public interface IRandomSource
    {
        /// <summary>Próximo número em [0, 1).</summary>
        double NextDouble();
    }

    /// <summary>Fonte aleatória padrão, baseada em <see cref="Random"/>.</summary>
    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _random;

        /// <summary>Cria com semente aleatória ou fixa.</summary>
        public SystemRandomSource(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        /// <inheritdoc />
        public double NextDouble() => _random.NextDouble();
    }

    /// <summary>Dados de um golpe para o cálculo de dano.</summary>
    public readonly struct DamageInput
    {
        /// <summary>Dano base da skill.</summary>
        public int SkillBaseDamage { get; }

        /// <summary>Poder de ataque do atacante, somado ao dano base.</summary>
        public int AttackPower { get; }

        /// <summary>Multiplicador de combo, rota, finalizador etc. (1 = nenhum).</summary>
        public double Multiplier { get; }

        /// <summary>Chance de crítico em [0, 1].</summary>
        public double CritChance { get; }

        /// <summary>Multiplicador do crítico (1,5 = +50%).</summary>
        public double CritMultiplier { get; }

        /// <summary>Defesa do alvo.</summary>
        public int TargetDefense { get; }

        /// <summary>Cria os dados do golpe.</summary>
        public DamageInput(int skillBaseDamage, int attackPower, double multiplier, double critChance,
            double critMultiplier, int targetDefense)
        {
            SkillBaseDamage = skillBaseDamage;
            AttackPower = attackPower;
            Multiplier = multiplier;
            CritChance = critChance;
            CritMultiplier = critMultiplier;
            TargetDefense = targetDefense;
        }
    }

    /// <summary>Resultado de um golpe.</summary>
    public readonly struct DamageResult
    {
        /// <summary>Dano final (no mínimo 1).</summary>
        public int Amount { get; }

        /// <summary>Verdadeiro se foi crítico.</summary>
        public bool Critical { get; }

        /// <summary>Cria o resultado.</summary>
        public DamageResult(int amount, bool critical)
        {
            Amount = amount;
            Critical = critical;
        }
    }

    /// <summary>
    /// Fórmula de dano usada pelo cliente (previsão) e pelo servidor (decisão):
    /// <code>
    /// bruto    = (dano da skill + ataque) × multiplicador
    /// redução  = defesa / (defesa + EscalaDeDefesa)
    /// final    = bruto × (1 − redução) × variação [0,92 a 1,08] × (crítico ? multiplicador do crítico : 1)
    /// </code>
    /// A redução nunca chega a 100%: defesa alta diminui o dano sem anulá-lo.
    /// </summary>
    public static class DamageFormula
    {
        /// <summary>Defesa que reduz o dano pela metade.</summary>
        public const double DefenseScale = 500;

        /// <summary>Menor variação aleatória do dano.</summary>
        public const double MinVariation = 0.92;

        /// <summary>Maior variação aleatória do dano.</summary>
        public const double MaxVariation = 1.08;

        /// <summary>Fração do dano que a defesa remove, em [0, 1).</summary>
        public static double Mitigation(int defense) => defense <= 0 ? 0 : defense / (defense + DefenseScale);

        /// <summary>Calcula o dano de um golpe.</summary>
        public static DamageResult Calculate(in DamageInput input, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));

            var raw = Math.Max(0, input.SkillBaseDamage + input.AttackPower) * Math.Max(0, input.Multiplier);
            var afterDefense = raw * (1 - Mitigation(input.TargetDefense));
            var variation = MinVariation + random.NextDouble() * (MaxVariation - MinVariation);
            var critical = random.NextDouble() < input.CritChance;
            var value = afterDefense * variation * (critical ? input.CritMultiplier : 1);
            return new DamageResult(Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero)), critical);
        }
    }
}
