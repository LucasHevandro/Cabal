using Game.Client.Combat;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// HUD do protótipo em IMGUI: barra de combo, SP, skills, alvo e mensagens.
    /// Desenhado numa tela virtual de 1920x1080 escalada para a resolução real.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;

        [SerializeField] private PlayerCombat combat;

        private GUIStyle _label;
        private GUIStyle _center;
        private GUIStyle _big;
        private GUIStyle _small;

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BarBack = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color SpColor = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color WindowColor = new Color(0.25f, 0.75f, 0.35f, 0.85f);
        private static readonly Color ExcellentColor = new Color(1f, 0.9f, 0.3f, 0.95f);
        private static readonly Color PhysicalColor = new Color(0.95f, 0.55f, 0.25f);
        private static readonly Color MagicalColor = new Color(0.45f, 0.6f, 1f);
        private static readonly Color RouteColor = new Color(0.85f, 0.65f, 1f);

        public void SetCombat(PlayerCombat playerCombat) => combat = playerCombat;

        private void Awake()
        {
            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
        }

        private void OnGUI()
        {
            if (combat == null || combat.Combo == null) return;
            EnsureStyles();

            var scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            GUI.matrix = Matrix4x4.TRS(
                new Vector3((Screen.width - VirtualWidth * scale) / 2, (Screen.height - VirtualHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));

            DrawHelp();
            DrawTargetFrame();
            DrawComboBar();
            DrawSpBar();
            DrawSkillBar();
            DrawGradePopup();
            DrawMessage();

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        private void DrawHelp()
        {
            Box(new Rect(20, 20, 420, 270), Panel);
            GUI.Label(new Rect(36, 30, 400, 260),
                "<b>Controles</b>\n" +
                "WASD andar · Shift correr · Espaço esquivar\n" +
                "Botão direito girar câmera · Roda zoom\n" +
                "Tab / clique: selecionar alvo · Esc limpar\n" +
                "1–4 skills\n" +
                "Q iniciar combo (gasta SP)\n" +
                "F finalizador (depois de 3 acertos)\n\n" +
                "<b>Rotas de combo</b> (F = físico, M = mágico)\n" +
                "F→M ou M→F: Alternância +10%\n" +
                "F F M: Tempestade +25% · M M F: Eco Arcano +25%\n\n" +
                $"Melhor corrente: <b>{combat.BestChain}</b>", _small);
        }

        private void DrawTargetFrame()
        {
            var target = combat.Target;
            if (target == null) return;

            var rect = new Rect(VirtualWidth / 2 - 220, 24, 440, 74);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x, rect.y + 6, rect.width, 24), target.DisplayName, _center);
            var hp = target.Hp;
            Bar(new Rect(rect.x + 16, rect.y + 34, rect.width - 32, 16), (float)hp.Current / Mathf.Max(1, hp.Max), HpColor);
            GUI.Label(new Rect(rect.x, rect.y + 32, rect.width, 20), $"{hp.Current} / {hp.Max}", _small.WithCenter());

            var dummy = target.GetComponent<TrainingDummy>();
            if (dummy != null)
                GUI.Label(new Rect(rect.x, rect.y + 52, rect.width, 20), $"DPS {dummy.Dps:0} · total {dummy.TotalDamage}", _small.WithCenter());
        }

        private void DrawComboBar()
        {
            var combo = combat.Combo;
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 300, 660, 34);

            if (combo.State != ComboState.Active)
            {
                var cost = combo.Config.ActivationSpCost;
                var ready = combat.Sp.Has(cost) && combat.Target != null;
                GUI.color = ready ? Color.white : new Color(1, 1, 1, 0.5f);
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height), $"Q: iniciar combo ({cost} SP)", _center);
                GUI.color = Color.white;
                return;
            }

            var now = Time.time;
            Box(new Rect(rect.x - 6, rect.y - 6, rect.width + 12, rect.height + 12), Panel);
            Box(rect, BarBack);

            // Janela-alvo e zona Excellent.
            var start = (float)combo.WindowStart;
            var end = (float)combo.WindowEnd;
            Box(new Rect(rect.x + rect.width * start, rect.y, rect.width * (end - start), rect.height), WindowColor);
            var half = (end - start) / 2f * (float)combo.Config.ExcellentZoneRatio;
            var center = (float)combo.Config.WindowCenter;
            Box(new Rect(rect.x + rect.width * (center - half), rect.y, rect.width * half * 2, rect.height), ExcellentColor);

            // Marcador.
            var cursor = Mathf.Clamp01((float)combo.GetCursor(now));
            Box(new Rect(rect.x + rect.width * cursor - 3, rect.y - 8, 6, rect.height + 16), Color.white);

            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), $"<b>{combo.HitCount}</b> acertos", _big.WithAlignment(TextAnchor.MiddleLeft));
            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), $"bônus +{combo.ChainBonus * 100:0}%", _label.WithAlignment(TextAnchor.MiddleRight));
            if (combo.CanFinish)
                GUI.Label(new Rect(rect.x, rect.y + rect.height + 6, rect.width, 24), "F: finalizar", _center);

            // Últimos tipos acertados na corrente, para o jogador planejar a próxima rota.
            var kinds = combo.ChainKinds;
            var shown = Mathf.Min(6, kinds.Count);
            for (var i = 0; i < shown; i++)
            {
                var kind = kinds[kinds.Count - shown + i];
                var chip = new Rect(rect.x + i * 30, rect.y + rect.height + 8, 26, 22);
                Box(chip, kind == SkillKind.Physical ? PhysicalColor : MagicalColor);
                GUI.Label(chip, kind == SkillKind.Physical ? "<b>F</b>" : "<b>M</b>", _small.WithCenter());
            }
        }

        private void DrawSpBar()
        {
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 210, 660, 18);
            Bar(rect, (float)combat.Sp.Current / Mathf.Max(1, combat.Sp.Max), SpColor);
            GUI.Label(rect, $"SP {combat.Sp.Current} / {combat.Sp.Max}", _small.WithCenter());
        }

        private void DrawSkillBar()
        {
            const float size = 150f;
            const float gap = 20f;
            var skills = combat.Skills;
            var total = skills.Count * size + (skills.Count - 1) * gap;
            var x = VirtualWidth / 2 - total / 2;
            var y = VirtualHeight - 180;

            for (var i = 0; i < skills.Count; i++)
            {
                var rect = new Rect(x + i * (size + gap), y, size, 120);
                var skill = skills[i];
                Box(rect, Panel);
                GUI.Label(new Rect(rect.x + 10, rect.y + 6, 40, 30), $"<b>{i + 1}</b>", _big);
                GUI.Label(new Rect(rect.x + 8, rect.y + 44, rect.width - 16, 44), skill.name, _small.WithCenter());
                GUI.Label(new Rect(rect.x + 8, rect.y + 86, rect.width - 16, 24),
                    skill.kind == SkillKind.Physical ? "físico" : "mágico", _small.WithCenter());

                var remaining = combat.CooldownRemaining(i);
                if (remaining > 0)
                {
                    var fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, skill.cooldown));
                    Box(new Rect(rect.x, rect.y + rect.height * (1 - fraction), rect.width, rect.height * fraction), new Color(0, 0, 0, 0.6f));
                    GUI.Label(rect, $"<b>{remaining:0.0}</b>", _big.WithCenter());
                }
            }
        }

        private void DrawGradePopup()
        {
            var age = Time.unscaledTime - combat.LastGradeAt;
            if (age > 0.6f) return;

            string text;
            Color color;
            switch (combat.LastGrade)
            {
                case ComboGrade.Excellent: text = "EXCELLENT"; color = ExcellentColor; break;
                case ComboGrade.Good: text = "GOOD"; color = new Color(0.6f, 0.9f, 1f); break;
                default: text = "MISS"; color = new Color(1f, 0.35f, 0.3f); break;
            }

            var pop = 1f + Mathf.Max(0, 0.15f - age) * 3f;
            _big.fontSize = Mathf.RoundToInt(56 * pop);
            ShadowLabel(new Rect(0, VirtualHeight - 490, VirtualWidth, 80), $"<b>{text}</b>", _big.WithCenter(),
                color, 1f - Mathf.Clamp01((age - 0.35f) / 0.25f));
            _big.fontSize = 30;

            var routeAge = Time.unscaledTime - combat.LastRouteAt;
            if (combat.LastRoute != null && routeAge <= 0.9f)
            {
                ShadowLabel(new Rect(0, VirtualHeight - 412, VirtualWidth, 40),
                    $"<b>{combat.LastRoute.Name.ToUpperInvariant()} +{combat.LastRoute.DamageBonus * 100:0}%</b>", _big.WithCenter(),
                    RouteColor, 1f - Mathf.Clamp01((routeAge - 0.6f) / 0.3f));
            }
        }

        /// <summary>Texto com sombra escura, legível sobre qualquer fundo.</summary>
        private static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            GUI.color = new Color(0, 0, 0, alpha * 0.85f);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private void DrawMessage()
        {
            var age = Time.unscaledTime - combat.MessageAt;
            if (age > 2.5f || string.IsNullOrEmpty(combat.Message)) return;
            GUI.color = new Color(1, 1, 1, 1f - Mathf.Clamp01((age - 2f) / 0.5f));
            GUI.Label(new Rect(0, VirtualHeight - 250, VirtualWidth, 30), combat.Message, _center);
            GUI.color = Color.white;
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Bar(Rect rect, float fraction, Color color)
        {
            Box(rect, BarBack);
            Box(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            _label.normal.textColor = Color.white;
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_label) { fontSize = 30 };
            _small = new GUIStyle(_label) { fontSize = 16 };
        }
    }

    internal static class GuiStyleExtensions
    {
        public static GUIStyle WithCenter(this GUIStyle style) => style.WithAlignment(TextAnchor.MiddleCenter);

        public static GUIStyle WithAlignment(this GUIStyle style, TextAnchor alignment) =>
            new GUIStyle(style) { alignment = alignment };
    }
}
