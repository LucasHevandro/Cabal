using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Progression;

namespace Game.Client.Fx
{
    /// <summary>
    /// Qual animação de golpe cada skill usa (parâmetro <c>AttackKind</c> do Animator). O Animator Controller dos
    /// personagens (KayKitBuilder) tem um estado para cada valor; skill nova sem entrada cai no corte (físico) ou no
    /// disparo mágico (magia).
    /// </summary>
    public static class AttackStyle
    {
        public const int Slice = 0;
        public const int Chop = 1;
        public const int Stab = 2;
        public const int Spin = 3;
        public const int JumpChop = 4;
        public const int Diagonal = 5;
        public const int MagicShoot = 6;
        public const int MagicCast = 7;
        public const int MagicRaise = 8;
        public const int Finisher = 9;
        public const int RangedShoot = 10;

        public const int Count = 11;

        public static int For(SkillData skill)
        {
            if (skill == null) return Slice;
            // Cada skill tem o próprio golpe (SkillMoves); os estilos abaixo ficam para skills novas e monstros.
            var move = SkillMoves.IndexOf(skill.Id);
            if (move >= 0) return SkillMoves.FirstKind + move;
            switch (skill.Id)
            {
                case "corte": return Slice;
                case "golpe_runico":
                case "golpe_de_cajado": return Chop;
                case "estocada_dupla": return Stab;
                case "giro_de_lamina":
                case "tempestade_de_aco": return Spin;
                case "fenda_sismica":
                case "execucao": return JumpChop;
                case "lamina_flamejante": return Diagonal;
                case "explosao_arcana": return MagicCast;
                case "meteoro":
                case "cataclismo": return MagicRaise;
                default: return skill.Kind == SkillKind.Magical ? MagicShoot : Slice;
            }
        }

        /// <summary>Golpe de monstro (esqueletos têm Animator; os bichos em partes só dão o bote).</summary>
        public static int ForMonster(string monsterId)
        {
            switch (VisualCatalog.BaseOf(monsterId))
            {
                case "esqueleto_arqueiro": return RangedShoot;
                case "espectro":
                case "senhor_da_cripta": return MagicShoot;
                case "cavaleiro_ossudo":
                case "guardiao_das_ruinas": return Finisher;
                case "cavaleiro_caido": return Chop;
                default: return Slice;
            }
        }

        /// <summary>Golpe de outro jogador: a classe vem no TypeId e a skill pela posição (Source) do evento.</summary>
        public static int ForRemote(string classTypeId, byte source)
        {
            if (source == Game.Shared.Net.CombatSource.Finisher) return Finisher;
            var characterClass = classTypeId == "arcanist" ? CharacterClass.Arcanist : CharacterClass.Blade;
            var skills = ClassCatalog.SkillsFor(characterClass);
            return source < skills.Count ? For(skills[source].Skill) : Slice;
        }
    }
}
