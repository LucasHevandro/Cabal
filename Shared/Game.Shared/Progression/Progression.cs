using System;

namespace Game.Shared.Progression
{
    /// <summary>
    /// Curva de XP do MVP (níveis 1 a 30). Valores provisórios até a planilha de design assumir.
    /// <code>XP para passar do nível N = 80 × N^1,6 + 20 × N</code>
    /// </summary>
    public static class LevelCurve
    {
        /// <summary>Nível máximo do MVP.</summary>
        public const int MaxLevel = 30;

        /// <summary>XP necessária para ir do nível <paramref name="level"/> ao seguinte.</summary>
        public static int XpToNext(int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
            if (level >= MaxLevel) return 0;
            return (int)Math.Round(80 * Math.Pow(level, 1.6) + 20 * level);
        }
    }

    /// <summary>Atributos base do personagem por nível (antes de equipamento).</summary>
    public readonly struct CharacterStats
    {
        public int MaxHp { get; }
        public int MaxMp { get; }
        public int AttackPower { get; }
        public int Defense { get; }

        public CharacterStats(int maxHp, int maxMp, int attackPower, int defense)
        {
            MaxHp = maxHp;
            MaxMp = maxMp;
            AttackPower = attackPower;
            Defense = defense;
        }

        /// <summary>Atributos no nível informado.</summary>
        public static CharacterStats ForLevel(int level)
        {
            var steps = Math.Max(0, Math.Min(level, LevelCurve.MaxLevel) - 1);
            return new CharacterStats(
                maxHp: 2000 + 120 * steps,
                maxMp: 500 + 20 * steps,
                attackPower: 60 + 8 * steps,
                defense: 50 + 6 * steps);
        }
    }

    /// <summary>Nível e XP de um personagem.</summary>
    public sealed class CharacterProgress
    {
        /// <summary>Nível atual (1 a <see cref="LevelCurve.MaxLevel"/>).</summary>
        public int Level { get; private set; }

        /// <summary>XP acumulada dentro do nível atual.</summary>
        public int Xp { get; private set; }

        /// <summary>XP que falta acumular no nível atual (0 no nível máximo).</summary>
        public int XpToNext => LevelCurve.XpToNext(Level);

        /// <summary>Cria no nível e XP informados.</summary>
        public CharacterProgress(int level = 1, int xp = 0)
        {
            if (level < 1 || level > LevelCurve.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));
            if (xp < 0) throw new ArgumentOutOfRangeException(nameof(xp));
            Level = level;
            Xp = level == LevelCurve.MaxLevel ? 0 : Math.Min(xp, LevelCurve.XpToNext(level) - 1);
        }

        /// <summary>Soma XP, sobe quantos níveis couberem e retorna quantos níveis subiu.</summary>
        public int AddXp(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (Level >= LevelCurve.MaxLevel) return 0;

            var gained = 0;
            Xp += amount;
            while (Level < LevelCurve.MaxLevel && Xp >= LevelCurve.XpToNext(Level))
            {
                Xp -= LevelCurve.XpToNext(Level);
                Level++;
                gained++;
            }
            if (Level >= LevelCurve.MaxLevel) Xp = 0;
            return gained;
        }
    }

    /// <summary>Regras de XP por monstro.</summary>
    public static class XpRules
    {
        /// <summary>
        /// XP ajustada pela diferença de nível: +10% por nível que o monstro está acima (até +50%),
        /// −20% por nível abaixo (mínimo 10%). Evita farmar monstros fracos demais.
        /// </summary>
        public static int Adjusted(int baseXp, int playerLevel, int monsterLevel)
        {
            var diff = monsterLevel - playerLevel;
            var factor = diff >= 0 ? 1 + 0.1 * Math.Min(diff, 5) : Math.Max(0.1, 1 + 0.2 * diff);
            return Math.Max(1, (int)Math.Round(baseXp * factor));
        }
    }
}
