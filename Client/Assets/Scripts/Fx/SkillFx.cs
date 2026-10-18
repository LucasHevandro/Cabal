using System;
using System.Collections.Generic;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Como o projétil de uma skill mágica sai.</summary>
    public enum ProjectileStyle
    {
        /// <summary>Esfera saindo da mão.</summary>
        Orb,

        /// <summary>Lança: menor, mais rápida, com rastro mais longo.</summary>
        Lance,

        /// <summary>Dois projéteis seguidos.</summary>
        Twin,

        /// <summary>Cai do céu em cima do alvo.</summary>
        Meteor,
    }

    /// <summary>
    /// Sensação de cada skill (game feel): cor, como sai, como acerta e quanto pesa. Antes todas as físicas soltavam as
    /// mesmas faíscas e todas as mágicas a mesma esfera; agora cada uma tem identidade. Só visual e som: dano, alcance e
    /// área continuam vindo do Shared (e do servidor no online).
    /// </summary>
    public static class SkillFx
    {
        private sealed class Feel
        {
            public Color Color = Color.white;
            public ProjectileStyle Projectile = ProjectileStyle.Orb;
            public float ProjectileSize = 1f;

            /// <summary>Golpe que acerta duas vezes (a segunda faísca e o segundo som vêm logo depois).</summary>
            public bool Double;

            /// <summary>Área em volta do personagem (giro), e não do alvo.</summary>
            public bool AroundCaster;

            /// <summary>Peso do impacto: soma no hit stop e multiplica a tremida da câmera.</summary>
            public float Weight;

            public int Sparks = 14;
        }

        private static readonly Color Steel = new Color(0.85f, 0.9f, 1f);
        private static readonly Color Rune = new Color(0.45f, 0.75f, 1f);
        private static readonly Color Fire = new Color(1f, 0.5f, 0.15f);
        private static readonly Color Earth = new Color(0.85f, 0.65f, 0.35f);
        private static readonly Color Arcane = new Color(0.7f, 0.45f, 1f);
        private static readonly Color Ice = new Color(0.55f, 0.95f, 1f);
        private static readonly Color Spark = new Color(1f, 0.95f, 0.5f);
        private static readonly Color Blood = new Color(1f, 0.25f, 0.2f);

        private static readonly Dictionary<string, Feel> Feels = new Dictionary<string, Feel>
        {
            // ----- Lâmina (corpo a corpo) -----
            ["corte"] = new Feel { Color = Steel, Sparks = 12 },
            ["giro_de_lamina"] = new Feel { Color = Steel, AroundCaster = true, Weight = 0.3f, Sparks = 10 },
            ["golpe_runico"] = new Feel { Color = Rune, Weight = 0.3f, Sparks = 18 },
            ["estocada_dupla"] = new Feel { Color = Steel, Double = true, Sparks = 12 },
            ["fenda_sismica"] = new Feel { Color = Earth, Weight = 1f, Sparks = 20 },
            ["lamina_flamejante"] = new Feel { Color = Fire, Weight = 0.5f, Sparks = 24 },
            ["tempestade_de_aco"] = new Feel { Color = Steel, AroundCaster = false, Weight = 0.8f, Double = true, Sparks = 18 },
            ["execucao"] = new Feel { Color = Blood, Weight = 1.6f, Sparks = 36 },
            // ----- Arcanista (à distância) -----
            ["faisca"] = new Feel { Color = Spark, Projectile = ProjectileStyle.Lance, ProjectileSize = 0.6f, Sparks = 10 },
            ["lanca_de_forca"] = new Feel { Color = Rune, Projectile = ProjectileStyle.Lance, ProjectileSize = 0.9f, Weight = 0.2f, Sparks = 16 },
            ["golpe_de_cajado"] = new Feel { Color = Arcane, Sparks = 10 },
            ["explosao_arcana"] = new Feel { Color = Arcane, ProjectileSize = 1.3f, Weight = 0.6f, Sparks = 22 },
            ["corrente_gelida"] = new Feel { Color = Ice, Projectile = ProjectileStyle.Lance, ProjectileSize = 1f, Weight = 0.4f, Sparks = 20 },
            ["meteoro"] = new Feel { Color = Fire, Projectile = ProjectileStyle.Meteor, ProjectileSize = 2f, Weight = 1.2f, Sparks = 30 },
            ["raio_duplo"] = new Feel { Color = Spark, Projectile = ProjectileStyle.Twin, ProjectileSize = 0.8f, Double = true, Weight = 0.4f, Sparks = 16 },
            ["cataclismo"] = new Feel { Color = Arcane, Projectile = ProjectileStyle.Meteor, ProjectileSize = 2.6f, Weight = 1.8f, Sparks = 40 },
        };

        private static readonly Feel Default = new Feel();
        private static readonly Dictionary<string, float> LastAreaAt = new Dictionary<string, float>();

        private static Feel For(SkillData skill) => skill != null && Feels.TryGetValue(skill.Id, out var feel) ? feel : Default;

        /// <summary>Cor da skill misturada com a nota do combo (Excellent puxa para o dourado).</summary>
        public static Color ColorFor(SkillData skill, ComboGrade? grade, bool critical)
        {
            var color = For(skill).Color;
            if (critical) return Color.Lerp(color, new Color(1f, 0.55f, 0.15f), 0.6f);
            if (grade == ComboGrade.Excellent) return Color.Lerp(color, new Color(1f, 0.88f, 0.25f), 0.55f);
            return color;
        }

        /// <summary>Ao apertar: corte no ar na frente do personagem (físicas) ou anel em volta dele (giro).</summary>
        public static void OnCast(SkillData skill, Transform caster)
        {
            var fx = CombatFx.Instance;
            if (fx == null || skill == null || caster == null) return;
            var feel = For(skill);
            if (feel.AroundCaster)
            {
                Ring(caster.position + Vector3.up * 0.9f, Mathf.Max(1f, skill.AreaRadius), feel.Color, 36);
                return;
            }
            if (skill.Kind != SkillKind.Physical) return;
            // Arco de faíscas da direita para a esquerda, na altura do peito.
            var center = caster.position + Vector3.up * 1.2f;
            for (var i = 0; i < 14; i++)
            {
                var angle = Mathf.Lerp(-70f, 70f, i / 13f) * Mathf.Deg2Rad;
                var direction = caster.rotation * new Vector3(Mathf.Sin(angle), 0.1f, Mathf.Cos(angle));
                fx.Emit(center + direction * 1.1f, direction * 1.5f + Vector3.up * 0.4f, feel.Color, 0.14f, 0.22f);
            }
        }

        /// <summary>Lança o projétil da skill mágica do jeito dela; <paramref name="onArrive"/> roda na chegada do primeiro.</summary>
        public static void Launch(SkillData skill, Transform caster, Transform target, Vector3 targetOffset, ComboGrade? grade, Action onArrive)
        {
            var fx = CombatFx.Instance;
            if (fx == null || caster == null || target == null)
            {
                onArrive?.Invoke();
                return;
            }
            var feel = For(skill);
            var color = ColorFor(skill, grade, false);
            var hand = caster.position + Vector3.up * 1.4f + caster.forward * 0.6f;
            switch (feel.Projectile)
            {
                case ProjectileStyle.Meteor:
                    var sky = target.position + Vector3.up * 11f - caster.forward * 3f;
                    fx.LaunchProjectile(sky, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.4f);
                    break;
                case ProjectileStyle.Twin:
                    fx.LaunchProjectile(hand + caster.right * 0.3f, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.2f);
                    fx.LaunchProjectile(hand - caster.right * 0.3f, target, targetOffset, color, null, feel.ProjectileSize, 1.05f);
                    break;
                case ProjectileStyle.Lance:
                    fx.LaunchProjectile(hand, target, targetOffset, color, onArrive, feel.ProjectileSize, 1.6f);
                    break;
                default:
                    fx.LaunchProjectile(hand, target, targetOffset, color, onArrive, feel.ProjectileSize, 1f);
                    break;
            }
        }

        /// <summary>
        /// Impacto num alvo: faíscas da cor da skill; anel do tamanho da área (uma vez por uso, mesmo acertando vários);
        /// segunda batida nos golpes duplos. Retorna o peso para quem chama aplicar no hit stop, na câmera e no som.
        /// </summary>
        public static float OnImpact(SkillData skill, Vector3 center, ComboGrade? grade, bool critical)
        {
            var fx = CombatFx.Instance;
            var feel = For(skill);
            if (fx == null || skill == null) return feel.Weight;
            var color = ColorFor(skill, grade, critical);
            var heavy = grade == ComboGrade.Excellent || critical;
            fx.Sparks(center, color, heavy ? feel.Sparks * 2 : feel.Sparks);

            var now = Time.unscaledTime;
            var firstOfCast = !LastAreaAt.TryGetValue(skill.Id, out var last) || now - last > 0.25f;
            LastAreaAt[skill.Id] = now;
            if (firstOfCast && skill.AreaRadius > 0 && !feel.AroundCaster)
                Ring(new Vector3(center.x, center.y - 0.6f, center.z), skill.AreaRadius, color, 28 + (int)(skill.AreaRadius * 8));
            if (firstOfCast && feel.Double) fx.StartCoroutine(SecondHit(center, color, feel.Sparks));
            if (firstOfCast && feel.Weight >= 1f) fx.Sparks(center + Vector3.up * 0.3f, Color.white, 12);
            return feel.Weight;
        }

        private static System.Collections.IEnumerator SecondHit(Vector3 center, Color color, int sparks)
        {
            yield return new WaitForSeconds(0.11f);
            if (CombatFx.Instance == null) yield break;
            CombatFx.Instance.Sparks(center + Vector3.up * 0.15f, color, sparks);
            SoundPlayer.Play(SoundId.Hit, 0.6f, 0.08f);
        }

        /// <summary>Anel de partículas saindo para fora no chão (mostra o tamanho da área).</summary>
        private static void Ring(Vector3 center, float radius, Color color, int count)
        {
            var fx = CombatFx.Instance;
            if (fx == null) return;
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float)count * Mathf.PI * 2f;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                fx.Emit(center + direction * radius * 0.35f, direction * radius * 2.2f + Vector3.up * 1.2f, color, 0.16f, 0.32f);
            }
        }
    }
}
