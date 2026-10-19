using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Cores do personagem: a textura dos modelos do KayKit é uma paleta (grade de 8×4 cores em degradê), então trocar
    /// a cor de uma parte é repintar a célula dela. O metal segue o grau da armadura (Ferro, Aço Negro, Astralita), e o
    /// cabelo e a roupa seguem o que o jogador escolheu na criação. Índice 0 mantém a cor original do modelo.
    /// Só o corpo (malhas com esqueleto) é repintado: a arma na mão tem o próprio brilho.
    /// </summary>
    public static class CharacterPaint
    {
        public static readonly Color[] HairColors =
        {
            Color.clear, // original
            new Color(0.86f, 0.68f, 0.34f), // loiro
            new Color(0.66f, 0.26f, 0.1f), // ruivo
            new Color(0.09f, 0.08f, 0.08f), // preto
            new Color(0.74f, 0.74f, 0.76f), // grisalho
            new Color(0.36f, 0.22f, 0.12f), // castanho
        };

        public static readonly Color[] ClothColors =
        {
            Color.clear, // original
            new Color(0.2f, 0.36f, 0.78f), // azul
            new Color(0.2f, 0.56f, 0.3f), // verde
            new Color(0.46f, 0.26f, 0.68f), // roxo
            new Color(0.14f, 0.14f, 0.16f), // preto
            new Color(0.92f, 0.9f, 0.86f), // branco
        };

        /// <summary>Metal por grau da armadura (0 = sem armadura: cor original).</summary>
        public static readonly Color[] MetalColors =
        {
            Color.clear,
            new Color(0.66f, 0.68f, 0.7f), // Ferro
            new Color(0.2f, 0.19f, 0.24f), // Aço Negro
            new Color(0.45f, 0.72f, 1f), // Astralita
        };

        /// <summary>Que células da paleta são metal, cabelo e roupa em cada modelo (coluna, linha a partir de cima).</summary>
        private sealed class Layout
        {
            public Vector2Int[] Metal;
            public Vector2Int[] Hair;
            public Vector2Int[] Cloth;
        }

        private static readonly Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>
        {
            // Knight: armadura (3,0), detalhes (7,0) e (7,1); cabelo (1,0); tabardo e capa vermelhos (0,1).
            ["knight_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(7, 0), new Vector2Int(7, 1) },
                Hair = new[] { new Vector2Int(1, 0) },
                Cloth = new[] { new Vector2Int(0, 1) },
            },
            // Mage: metal (3,0) e a capa (2,1) seguem a armadura; cabelo (1,0); túnica e chapéu (0,1) e (1,1).
            ["mage_texture"] = new Layout
            {
                Metal = new[] { new Vector2Int(3, 0), new Vector2Int(2, 1) },
                Hair = new[] { new Vector2Int(1, 0) },
                Cloth = new[] { new Vector2Int(0, 1), new Vector2Int(1, 1) },
            },
        };

        private const int Size = 256; // a paleta reduzida: cada célula vira 32×64 pixels, o degradê continua liso
        private static readonly Dictionary<(Material, int, int, int), Material> Painted = new Dictionary<(Material, int, int, int), Material>();
        private static readonly Dictionary<Texture, Color32[]> Originals = new Dictionary<Texture, Color32[]>();

        /// <summary>Repinta o corpo do modelo dentro de <paramref name="root"/> (sem modelo ou sem paleta conhecida, nada muda).</summary>
        public static void Apply(Transform root, int armorGrade, int hair, int cloth)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var painted = PaintedFor(materials[i], armorGrade, hair, cloth);
                    if (painted == null || painted == materials[i]) continue;
                    materials[i] = painted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static Material PaintedFor(Material material, int armorGrade, int hair, int cloth)
        {
            if (material == null) return null;
            // Um material já pintado guarda o original: repintar parte sempre da paleta do pacote.
            if (OriginalOf.TryGetValue(material, out var original)) material = original;
            var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
            if (texture == null || !Layouts.TryGetValue(texture.name, out var layout)) return null;

            var metal = Pick(MetalColors, armorGrade);
            var hairColor = Pick(HairColors, hair);
            var clothColor = Pick(ClothColors, cloth);
            if (metal.a == 0 && hairColor.a == 0 && clothColor.a == 0) return material;

            var key = (material, armorGrade, hair, cloth);
            if (Painted.TryGetValue(key, out var cached) && cached != null) return cached;

            var pixels = (Color32[])OriginalPixels(texture).Clone();
            Recolor(pixels, layout.Metal, metal);
            Recolor(pixels, layout.Hair, hairColor);
            Recolor(pixels, layout.Cloth, clothColor);
            var result = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = texture.name + "_pintada", wrapMode = TextureWrapMode.Clamp };
            result.SetPixels32(pixels);
            result.Apply(true, true);

            var painted = new Material(material) { name = material.name + " (pintado)" };
            if (painted.HasProperty("_BaseMap")) painted.SetTexture("_BaseMap", result);
            painted.mainTexture = result;
            Painted[key] = painted;
            OriginalOf[painted] = material;
            return painted;
        }

        private static readonly Dictionary<Material, Material> OriginalOf = new Dictionary<Material, Material>();

        private static Color Pick(Color[] palette, int index) => index > 0 && index < palette.Length ? palette[index] : Color.clear;

        /// <summary>A paleta do pacote em 256×256, lida da GPU (a textura importada não precisa ser legível).</summary>
        private static Color32[] OriginalPixels(Texture texture)
        {
            if (Originals.TryGetValue(texture, out var pixels)) return pixels;
            var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(texture, target);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            pixels = copy.GetPixels32();
            Object.Destroy(copy);
            Originals[texture] = pixels;
            return pixels;
        }

        /// <summary>
        /// Pinta as células com a cor nova mantendo o degradê: cada pixel fica com o brilho relativo que tinha em
        /// relação ao meio da célula.
        /// </summary>
        private static void Recolor(Color32[] pixels, Vector2Int[] cells, Color color)
        {
            if (color.a == 0 || cells == null) return;
            const int cellWidth = Size / 8;
            const int cellHeight = Size / 4;
            foreach (var cell in cells)
            {
                var x0 = cell.x * cellWidth;
                var y0 = Size - (cell.y + 1) * cellHeight; // a linha 0 é a de cima; a textura começa embaixo
                var middle = pixels[(y0 + cellHeight / 2) * Size + x0 + cellWidth / 2];
                var reference = Mathf.Max(0.05f, Luminance(middle));
                for (var y = y0; y < y0 + cellHeight; y++)
                for (var x = x0; x < x0 + cellWidth; x++)
                {
                    var index = y * Size + x;
                    var shade = Mathf.Clamp(Luminance(pixels[index]) / reference, 0.35f, 1.6f);
                    var c = color * shade;
                    pixels[index] = new Color32(ToByte(c.r), ToByte(c.g), ToByte(c.b), pixels[index].a);
                }
            }
        }

        private static float Luminance(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }
}
