using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Ícone do jogo (exe, atalho e barra de tarefas): um brasão azul com borda dourada, a estrela de Astralita ao fundo
    /// e uma espada. Desenhado por código (distâncias com borda suavizada), sem arte externa.
    /// Gerar de novo: -executeMethod Game.Client.Editor.GameIcon.Generate (ou o menu Protótipo &gt; Gerar ícone).
    /// </summary>
    public static class GameIcon
    {
        public const string AssetPath = "Assets/Art/Icone/TerrasDeAstralis.png";
        private const int Size = 1024;

        private static readonly Color Gold = new Color(0.96f, 0.78f, 0.33f);
        private static readonly Color GoldDark = new Color(0.62f, 0.42f, 0.12f);
        private static readonly Color BlueTop = new Color(0.13f, 0.27f, 0.52f);
        private static readonly Color BlueBottom = new Color(0.04f, 0.07f, 0.18f);
        private static readonly Color Astralite = new Color(0.45f, 0.85f, 1f);
        private static readonly Color Steel = new Color(0.86f, 0.9f, 0.95f);
        private static readonly Color SteelDark = new Color(0.5f, 0.56f, 0.66f);

        [MenuItem("Protótipo/Gerar ícone")]
        public static void Generate()
        {
            var pixels = new Color32[Size * Size];
            var px = 2f / Size;
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var u = (x + 0.5f) / Size * 2f - 1f;
                var v = (y + 0.5f) / Size * 2f - 1f;
                pixels[y * Size + x] = Shade(u, v, px);
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath)!);
            File.WriteAllBytes(AssetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            if (AssetImporter.GetAtPath(AssetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            Debug.Log($"[Ícone] {AssetPath} ({Size}x{Size})");
        }

        /// <summary>Põe o ícone no Player Settings (todas as plataformas). O build do Windows chama antes de gerar.</summary>
        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            if (icon == null)
            {
                Generate();
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetPath);
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }

        private static Color32 Shade(float u, float v, float px)
        {
            var shield = Shield(u, v);
            var outside = Coverage(shield, px);
            if (outside <= 0f) return new Color32(0, 0, 0, 0);

            // Fundo azul, mais claro em cima, com um brilho no alto à esquerda.
            var t = Mathf.InverseLerp(-0.95f, 0.85f, v);
            var color = Color.Lerp(BlueBottom, BlueTop, t * t);
            color += new Color(0.08f, 0.1f, 0.14f) * Mathf.Exp(-((u + 0.3f) * (u + 0.3f) + (v - 0.55f) * (v - 0.55f)) * 4f);

            // Estrela de Astralita (quatro pontas) com brilho em volta.
            var sx = u;
            var sy = v - 0.08f;
            var glow = Mathf.Exp(-(sx * sx + sy * sy) * 5f);
            color = Color.Lerp(color, Astralite * 0.75f, glow * 0.55f);
            var star = Star(sx, sy, 0.62f);
            color = Color.Lerp(color, Color.Lerp(Astralite, Color.white, Mathf.Clamp01(-star * 6f)), Coverage(star, px) * 0.9f);
            var small = Star(sx, sy, 0.3f, 0.785f);
            color = Color.Lerp(color, Color.Lerp(Astralite, Color.white, 0.5f), Coverage(small, px) * 0.7f);

            // Espada de ponta para baixo.
            color = Sword(color, u, v, px);

            // Bordas: filete dourado por dentro e moldura dourada larga.
            var inner = Mathf.Abs(shield + 0.115f) - 0.012f;
            color = Color.Lerp(color, Gold * 0.9f, Coverage(inner, px));
            var rim = Coverage(-(shield + 0.07f), px);
            var rimShade = Color.Lerp(GoldDark, Gold, Mathf.Clamp01(0.55f + v * 0.45f - u * 0.15f));
            color = Color.Lerp(color, rimShade, rim);
            var edge = Mathf.Abs(shield + 0.07f) - 0.006f;
            color = Color.Lerp(color, GoldDark * 0.7f, Coverage(edge, px) * 0.8f);

            color.a = outside;
            return color;
        }

        /// <summary>Distância (aproximada) até a borda do brasão: negativo dentro.</summary>
        private static float Shield(float u, float v)
        {
            const float top = 0.86f;
            const float half = 0.8f;
            const float shoulder = 0.05f;
            const float tip = -0.96f;
            float width;
            var slope = 0f;
            if (v >= shoulder) width = half;
            else
            {
                var k = Mathf.Clamp01((shoulder - v) / (shoulder - tip));
                var root = Mathf.Sqrt(Mathf.Max(0.01f, 1f - k * k));
                width = half * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
                slope = half * k / root / (shoulder - tip);
            }
            // Topo levemente arqueado para cima no meio.
            var topEdge = top + 0.06f * (1f - u * u / (half * half));
            // Na curva de baixo, divide pela inclinação: a distância fica certa e a borda tem a mesma largura em volta.
            var side = (Mathf.Abs(u) - width) / Mathf.Sqrt(1f + slope * slope);
            return Mathf.Max(side, v - topEdge, tip - v);
        }

        /// <summary>Estrela de quatro pontas (astroide): negativo dentro.</summary>
        private static float Star(float x, float y, float radius, float angle = 0f)
        {
            var c = Mathf.Cos(angle);
            var s = Mathf.Sin(angle);
            var rx = Mathf.Abs(x * c - y * s);
            var ry = Mathf.Abs(x * s + y * c);
            var f = Mathf.Pow(rx / radius, 0.5f) + Mathf.Pow(ry / radius, 0.5f);
            return (f - 1f) * radius * 0.5f;
        }

        private static Color Sword(Color color, float u, float v, float px)
        {
            // Lâmina: larga até perto da ponta, depois afina.
            var bladeHalf = v > -0.42f ? 0.075f : 0.075f * Mathf.Clamp01((v + 0.66f) / 0.24f);
            var blade = Mathf.Max(Mathf.Abs(u) - bladeHalf, Mathf.Max(v - 0.3f, -0.66f - v));
            var bladeColor = u < 0f ? Steel : Color.Lerp(Steel, SteelDark, 0.55f);
            color = Color.Lerp(color, GoldDark * 0.5f, Coverage(blade - 0.018f, px)); // contorno
            color = Color.Lerp(color, bladeColor, Coverage(blade, px));
            var fuller = Mathf.Max(Mathf.Abs(u) - 0.012f, Mathf.Max(v - 0.24f, -0.38f - v));
            color = Color.Lerp(color, SteelDark, Coverage(fuller, px) * 0.8f);

            // Guarda, punho e pomo dourados.
            var guard = RoundBox(u, v - 0.34f, 0.3f, 0.04f, 0.035f);
            var grip = RoundBox(u, v - 0.5f, 0.042f, 0.13f, 0.02f);
            var pommel = Mathf.Sqrt(u * u + (v - 0.67f) * (v - 0.67f)) - 0.075f;
            foreach (var part in new[] { grip, guard, pommel })
            {
                color = Color.Lerp(color, GoldDark * 0.5f, Coverage(part - 0.018f, px));
                color = Color.Lerp(color, Color.Lerp(GoldDark, Gold, Mathf.Clamp01(0.6f - u * 2f)), Coverage(part, px));
            }
            var gem = Mathf.Sqrt(u * u + (v - 0.34f) * (v - 0.34f)) - 0.032f;
            color = Color.Lerp(color, Astralite, Coverage(gem, px));
            return color;
        }

        private static float RoundBox(float x, float y, float hx, float hy, float r)
        {
            var qx = Mathf.Abs(x) - hx + r;
            var qy = Mathf.Abs(y) - hy + r;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static float Coverage(float distance, float px) => Mathf.Clamp01(0.5f - distance / px);
    }
}
