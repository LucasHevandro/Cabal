using System.Collections.Generic;
using Game.Shared.Items;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Arte da interface desenhada em código, sem arquivos de fora:
    /// <list type="bullet">
    /// <item>fonte serifada dos títulos, a primeira que o Windows tiver (Palatino Linotype, Constantia, Georgia);</item>
    /// <item>ornamento dourado nos cantos dos painéis;</item>
    /// <item>silhuetas das casas de equipamento vazias (espada, couraça, luva, bota, anel).</item>
    /// </list>
    /// </summary>
    public static class UiArt
    {
        public static readonly Color TitleColor = new Color(0.97f, 0.87f, 0.62f);

        private static Font _titleFont;
        private static bool _fontTried;
        private static Texture2D _corner;
        private static readonly Dictionary<EquipSlot, Texture2D> Silhouettes = new Dictionary<EquipSlot, Texture2D>();
        private static readonly Dictionary<int, GUIStyle> TitleStyles = new Dictionary<int, GUIStyle>();

        /// <summary>Fonte dos títulos (nula se o sistema não tiver nenhuma das serifadas: aí fica a fonte comum).</summary>
        public static Font TitleFont
        {
            get
            {
                if (_fontTried) return _titleFont;
                _fontTried = true;
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var name in new[] { "Palatino Linotype", "Constantia", "Book Antiqua", "Georgia" })
                {
                    if (!installed.Contains(name)) continue;
                    _titleFont = Font.CreateDynamicFontFromOSFont(name, 24);
                    break;
                }
                return _titleFont;
            }
        }

        /// <summary>Estilo de título: serifado, dourado claro, com rich text.</summary>
        public static GUIStyle Title(int size, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var key = size * 16 + (int)alignment;
            if (TitleStyles.TryGetValue(key, out var style)) return style;
            style = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, alignment = alignment, wordWrap = false, clipping = TextClipping.Overflow };
            if (TitleFont != null) style.font = TitleFont;
            style.normal.textColor = TitleColor;
            TitleStyles[key] = style;
            return style;
        }

        /// <summary>Ornamento nos quatro cantos do retângulo (painéis grandes o bastante).</summary>
        public static void Corners(Rect rect, float alpha)
        {
            if (Event.current.type != EventType.Repaint || rect.width < 120 || rect.height < 70) return;
            if (_corner == null) _corner = MakeCorner();
            const float size = 40f;
            var color = new Color(1f, 1f, 1f, alpha);
            var previous = GUI.color;
            GUI.color = color;
            // A mesma textura, espelhada para cada canto (coordenadas de textura invertidas).
            const float outset = 5f; // o ornamento passa um pouco para fora da moldura
            GUI.DrawTextureWithTexCoords(new Rect(rect.x - outset, rect.y - outset, size, size), _corner, new Rect(0, 1, 1, -1));
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - size + outset, rect.y - outset, size, size), _corner, new Rect(1, 1, -1, -1));
            GUI.DrawTextureWithTexCoords(new Rect(rect.x - outset, rect.yMax - size + outset, size, size), _corner, new Rect(0, 0, 1, 1));
            GUI.DrawTextureWithTexCoords(new Rect(rect.xMax - size + outset, rect.yMax - size + outset, size, size), _corner, new Rect(1, 0, -1, 1));
            GUI.color = previous;
        }

        /// <summary>Silhueta clara da casa de equipamento vazia (o tipo de peça que vai ali).</summary>
        public static Texture2D Silhouette(EquipSlot slot)
        {
            if (Silhouettes.TryGetValue(slot, out var texture)) return texture;
            texture = Paint(64, (x, y) => slot switch
            {
                EquipSlot.Weapon => Sword(x, y),
                EquipSlot.Armor => Armor(x, y),
                EquipSlot.Gloves => Glove(x, y),
                EquipSlot.Boots => Boot(x, y),
                _ => Ring(x, y),
            });
            Silhouettes[slot] = texture;
            return texture;
        }

        // ===== Desenho (distâncias com sinal: negativo dentro da forma) =====

        private delegate float Shape(float x, float y);

        /// <summary>Textura branca com a forma (borda suavizada em 1 px), em coordenadas de 0 a 1 com y para cima.</summary>
        private static Texture2D Paint(int size, Shape shape)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            for (var py = 0; py < size; py++)
            for (var px = 0; px < size; px++)
            {
                var d = shape((px + 0.5f) / size, (py + 0.5f) / size) * size;
                var a = Mathf.Clamp01(0.5f - d);
                pixels[py * size + px] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        private static float Box(float x, float y, float cx, float cy, float hw, float hh)
        {
            var dx = Mathf.Abs(x - cx) - hw;
            var dy = Mathf.Abs(y - cy) - hh;
            return Mathf.Max(dx, dy);
        }

        private static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        private static float Segment(float x, float y, float ax, float ay, float bx, float by, float r)
        {
            var pax = x - ax;
            var pay = y - ay;
            var bax = bx - ax;
            var bay = by - ay;
            var h = Mathf.Clamp01((pax * bax + pay * bay) / (bax * bax + bay * bay));
            var dx = pax - bax * h;
            var dy = pay - bay * h;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        private static float Union(params float[] d)
        {
            var m = d[0];
            for (var i = 1; i < d.Length; i++) m = Mathf.Min(m, d[i]);
            return m;
        }

        // Espada inclinada: lâmina, guarda, punho e pomo.
        private static float Sword(float x, float y) => Union(
            Segment(x, y, 0.3f, 0.3f, 0.8f, 0.8f, 0.045f),
            Segment(x, y, 0.18f, 0.42f, 0.42f, 0.18f, 0.035f),
            Segment(x, y, 0.3f, 0.3f, 0.18f, 0.18f, 0.03f),
            Circle(x, y, 0.15f, 0.15f, 0.05f));

        // Couraça: peito com ombreiras e a cintura mais estreita.
        private static float Armor(float x, float y) => Union(
            Mathf.Max(Box(x, y, 0.5f, 0.5f, 0.22f, 0.26f), -Circle(x, y, 0.5f, 0.84f, 0.11f)),
            Circle(x, y, 0.26f, 0.68f, 0.1f),
            Circle(x, y, 0.74f, 0.68f, 0.1f),
            Box(x, y, 0.5f, 0.2f, 0.17f, 0.05f));

        // Luva: palma, quatro dedos e o polegar.
        private static float Glove(float x, float y) => Union(
            Box(x, y, 0.5f, 0.38f, 0.16f, 0.16f),
            Segment(x, y, 0.38f, 0.5f, 0.38f, 0.74f, 0.045f),
            Segment(x, y, 0.47f, 0.5f, 0.47f, 0.8f, 0.045f),
            Segment(x, y, 0.56f, 0.5f, 0.56f, 0.77f, 0.045f),
            Segment(x, y, 0.64f, 0.48f, 0.64f, 0.68f, 0.04f),
            Segment(x, y, 0.36f, 0.34f, 0.24f, 0.52f, 0.05f),
            Box(x, y, 0.5f, 0.17f, 0.18f, 0.06f));

        // Bota: cano e pé virado para a direita.
        private static float Boot(float x, float y) => Union(
            Box(x, y, 0.42f, 0.56f, 0.13f, 0.26f),
            Segment(x, y, 0.36f, 0.24f, 0.72f, 0.24f, 0.1f),
            Box(x, y, 0.42f, 0.83f, 0.16f, 0.04f));

        // Anel com uma gema em cima.
        private static float Ring(float x, float y) => Union(
            Mathf.Abs(Circle(x, y, 0.5f, 0.42f, 0.2f)) - 0.05f,
            Mathf.Abs(x - 0.5f) + Mathf.Abs(y - 0.7f) - 0.11f);

        /// <summary>Canto: dois filetes que somem ao longo das bordas, com um losango e um pontinho.</summary>
        private static Texture2D MakeCorner()
        {
            const int size = 52;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var gold = UiTheme.BorderBright;
            var pixels = new Color32[size * size];
            for (var py = 0; py < size; py++)
            for (var px = 0; px < size; px++)
            {
                // Coordenadas com o canto em (0,0) embaixo à esquerda; a textura é espelhada para os outros cantos.
                var x = (px + 0.5f) / size;
                var y = (py + 0.5f) / size;
                var d = Union(
                    Segment(x, y, 0.12f, 0.12f, 0.95f, 0.12f, 0.035f),
                    Segment(x, y, 0.12f, 0.12f, 0.12f, 0.95f, 0.035f),
                    Segment(x, y, 0.27f, 0.27f, 0.66f, 0.27f, 0.02f),
                    Segment(x, y, 0.27f, 0.27f, 0.27f, 0.66f, 0.02f),
                    Mathf.Abs(x - 0.18f) + Mathf.Abs(y - 0.18f) - 0.16f,
                    Circle(x, y, 0.42f, 0.42f, 0.045f)) * size;
                var a = Mathf.Clamp01(0.5f - d);
                // Os filetes longos somem perto da ponta.
                a *= Mathf.Clamp01((1f - Mathf.Max(x, y)) / 0.35f);
                var shade = Color.Lerp(gold * 0.75f, Color.white, Mathf.Clamp01(1f - (x + y)));
                pixels[py * size + px] = new Color32((byte)(shade.r * 255), (byte)(shade.g * 255), (byte)(shade.b * 255), (byte)(Mathf.Clamp01(a) * 255));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
