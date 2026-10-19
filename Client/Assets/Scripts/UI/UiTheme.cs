using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Visual comum da interface (IMGUI): painéis com moldura bronze e cantos arredondados, barras com brilho, botões e
    /// campos com textura própria e a fonte do sistema (Segoe UI). Tudo gerado em código, sem arte de fora.
    /// </summary>
    public static class UiTheme
    {
        public static readonly Color Border = new Color(0.62f, 0.5f, 0.3f, 0.9f);
        public static readonly Color BorderBright = new Color(0.9f, 0.74f, 0.42f, 1f);
        private static readonly Color BarBack = new Color(0.04f, 0.04f, 0.06f, 0.9f);

        private static Texture2D _panelFill;
        private static Texture2D _gloss;
        private static Font _font;
        private static bool _skinReady;

        /// <summary>Prepara a skin (botões, campos e fonte). Chamado no OnGUI, antes de criar os estilos de cada janela.</summary>
        public static void Ensure()
        {
            if (_skinReady) return;
            _skinReady = true;
            var skin = GUI.skin;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 18);
            if (_font != null) skin.font = _font;
            // A Segoe UI é mais alta que a fonte padrão: sem isto, acentos e "g", "p" saem cortados nas caixas justas.
            skin.label.clipping = TextClipping.Overflow;

            var normal = Rounded(new Color(0.17f, 0.16f, 0.21f), new Color(0.09f, 0.09f, 0.12f), Border);
            var hover = Rounded(new Color(0.25f, 0.23f, 0.29f), new Color(0.13f, 0.12f, 0.16f), BorderBright);
            var active = Rounded(new Color(0.07f, 0.07f, 0.09f), new Color(0.14f, 0.13f, 0.17f), BorderBright);
            Style(skin.button, normal, hover, active);
            skin.button.border = new RectOffset(6, 6, 6, 6);
            skin.button.normal.textColor = new Color(0.95f, 0.92f, 0.85f);
            skin.button.hover.textColor = Color.white;
            skin.button.active.textColor = BorderBright;

            var field = Rounded(new Color(0.03f, 0.03f, 0.05f), new Color(0.06f, 0.06f, 0.08f), new Color(0.38f, 0.33f, 0.26f));
            var fieldFocus = Rounded(new Color(0.03f, 0.03f, 0.05f), new Color(0.06f, 0.06f, 0.08f), BorderBright);
            Style(skin.textField, field, field, fieldFocus);
            skin.textField.focused.background = fieldFocus;
            skin.textField.border = new RectOffset(6, 6, 6, 6);
            skin.textField.padding = new RectOffset(8, 8, 4, 4);
            skin.textField.normal.textColor = skin.textField.focused.textColor = skin.textField.hover.textColor = Color.white;
        }

        /// <summary>Painel: fundo escuro em degradê, moldura bronze e um filete claro por dentro.</summary>
        public static void Panel(Rect rect, float alpha = 0.9f)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_panelFill == null) _panelFill = Gradient(new Color(0.13f, 0.14f, 0.19f), new Color(0.035f, 0.04f, 0.06f));
            const float radius = 7f;
            GUI.DrawTexture(rect, _panelFill, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, alpha), Vector4.zero, Vector4.one * radius);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Fade(Border, alpha), Vector4.one * 1.6f, Vector4.one * radius);
            var inner = new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6);
            GUI.DrawTexture(inner, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, 0.05f * alpha), Vector4.one, Vector4.one * (radius - 3));
        }

        /// <summary>Caixa de marcar do tema: quadradinho com moldura e um miolo dourado quando ligada; o texto ao lado.</summary>
        public static bool Toggle(Rect rect, bool value, string text, GUIStyle label)
        {
            var box = new Rect(rect.x + 2, rect.y + (rect.height - 20f) / 2f, 20f, 20f);
            if (Event.current.type == EventType.Repaint)
            {
                Slot(box, rect.Contains(Event.current.mousePosition));
                if (value)
                    GUI.DrawTexture(new Rect(box.x + 5, box.y + 5, 10, 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, BorderBright, Vector4.zero, Vector4.one * 2f);
            }
            GUI.Label(new Rect(rect.x + 30, rect.y, rect.width - 30, rect.height), text.TrimStart(), label);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) value = !value;
            return value;
        }

        /// <summary>Só a moldura bronze (por cima do minimapa).</summary>
        public static void Frame(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Border, Vector4.one * 2f, Vector4.one * 4f);
        }

        /// <summary>Filete dourado que some nas pontas (embaixo do título das janelas).</summary>
        public static void Divider(Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var steps = 12;
            for (var i = 0; i < steps; i++)
            {
                var t = (i + 0.5f) / steps;
                var a = Mathf.Sin(t * Mathf.PI);
                GUI.DrawTexture(new Rect(rect.x + rect.width * i / steps, rect.y, rect.width / steps + 0.5f, 1.5f), Texture2D.whiteTexture,
                    ScaleMode.StretchToFill, true, 0, Fade(Border, a), Vector4.zero, Vector4.zero);
            }
        }

        /// <summary>Barra arredondada com brilho no alto e moldura fina (vida, mana, SP, XP).</summary>
        public static void Bar(Rect rect, float fraction, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_gloss == null) _gloss = Gloss();
            var radius = Mathf.Min(rect.height / 2f, 5f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, BarBack, Vector4.zero, Vector4.one * radius);
            var width = rect.width * Mathf.Clamp01(fraction);
            if (width > 0.5f)
            {
                var fill = new Rect(rect.x, rect.y, Mathf.Max(width, Mathf.Min(rect.width, radius * 2f)), rect.height);
                GUI.DrawTexture(fill, _gloss, ScaleMode.StretchToFill, true, 0, color, Vector4.zero, Vector4.one * radius);
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(Border.r, Border.g, Border.b, 0.55f), Vector4.one, Vector4.one * radius);
        }

        /// <summary>Casa de item ou de skill: fundo afundado com moldura (mais clara com o mouse em cima).</summary>
        public static void Slot(Rect rect, bool hover)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                hover ? new Color(0.2f, 0.2f, 0.25f, 0.95f) : new Color(0.07f, 0.07f, 0.09f, 0.95f), Vector4.zero, Vector4.one * 4f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                hover ? BorderBright : new Color(0.33f, 0.29f, 0.23f, 0.9f), Vector4.one * 1.2f, Vector4.one * 4f);
        }

        private static Color Fade(Color color, float alpha) => new Color(color.r, color.g, color.b, color.a * alpha);

        private static void Style(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active)
        {
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = normal;
            style.onNormal.background = active;
            style.onHover.background = active;
            style.onActive.background = active;
        }

        private static readonly List<Texture2D> Owned = new List<Texture2D>();

        private static Texture2D Gradient(Color top, Color bottom)
        {
            var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < 64; y++) texture.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));
            texture.Apply();
            Owned.Add(texture);
            return texture;
        }

        /// <summary>Degradê branco das barras (tingido pela cor): faixa clara em cima, mais escuro embaixo.</summary>
        private static Texture2D Gloss()
        {
            var texture = new Texture2D(1, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < 32; y++)
            {
                var t = y / 31f; // 0 embaixo, 1 em cima
                var v = Mathf.Lerp(0.55f, 0.95f, t) + (t > 0.62f && t < 0.8f ? 0.25f : 0f);
                texture.SetPixel(0, y, new Color(v, v, v, 1f));
            }
            texture.Apply();
            Owned.Add(texture);
            return texture;
        }

        /// <summary>Textura 9-slice de botão/campo: cantos arredondados, degradê e moldura de 1 px.</summary>
        private static Texture2D Rounded(Color top, Color bottom, Color border)
        {
            const int size = 24;
            const float radius = 5f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var px = x + 0.5f;
                var py = y + 0.5f;
                var cx = Mathf.Clamp(px, radius, size - radius);
                var cy = Mathf.Clamp(py, radius, size - radius);
                var d = radius - Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)); // distância até a borda
                var alpha = Mathf.Clamp01(d + 0.5f);
                var fill = Color.Lerp(bottom, top, y / (size - 1f));
                var color = Color.Lerp(border, fill, Mathf.Clamp01(d - 1f));
                color.a = alpha;
                texture.SetPixel(x, y, color);
            }
            texture.Apply();
            Owned.Add(texture);
            return texture;
        }
    }
}
