using System.IO;
using Game.Client.World;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Terreno do mundo aberto: relevo da fórmula de <see cref="Ground"/>, chão pintado por zona (grama dos campos,
    /// folhas da mata, pedra das ruínas, cascalho da arena, terra nas estradas e na vila, rocha nas encostas) e grama.
    /// As texturas são geradas aqui (ruído que repete sem emenda), sem arquivo de fora.
    /// </summary>
    internal static class TerrainBuilder
    {
        public const string Folder = "Assets/Art/Mundo";

        private const int Grass = 0, ForestFloor = 1, Dirt = 2, Stone = 3, Gravel = 4, Rock = 5;

        public static Terrain Build(Transform parent)
        {
            Directory.CreateDirectory(Folder);
            var layers = new[]
            {
                Layer("grama", new Color(0.29f, 0.45f, 0.21f), new Color(0.47f, 0.58f, 0.3f), 7f, 0.1f, 11),
                Layer("chao_mata", new Color(0.2f, 0.3f, 0.15f), new Color(0.33f, 0.3f, 0.17f), 6f, 0.12f, 12),
                Layer("terra", new Color(0.52f, 0.42f, 0.3f), new Color(0.64f, 0.54f, 0.4f), 5f, 0.1f, 13),
                Layer("pedra", new Color(0.46f, 0.45f, 0.47f), new Color(0.6f, 0.58f, 0.56f), 4f, 0.14f, 14),
                Layer("cascalho", new Color(0.5f, 0.34f, 0.27f), new Color(0.64f, 0.48f, 0.38f), 4f, 0.16f, 15),
                Layer("rocha", new Color(0.4f, 0.38f, 0.37f), new Color(0.56f, 0.53f, 0.5f), 10f, 0.12f, 16),
            };

            var dataPath = $"{Folder}/Terreno.asset";
            AssetDatabase.DeleteAsset(dataPath);
            var data = new TerrainData { heightmapResolution = 513 };
            data.size = new Vector3(Ground.SizeX, Ground.MaxHeight, Ground.SizeZ);
            data.alphamapResolution = 512;
            data.baseMapResolution = 512;
            data.SetDetailResolution(512, 32);
            // Cada valor da camada de detalhe é a quantidade de tufos na célula (no modo "cobertura", 1 a 3 quase não aparece).
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.terrainLayers = layers;
            AssetDatabase.CreateAsset(data, dataPath);

            Heights(data);
            Paint(data);
            GrassDetails(data);
            EditorUtility.SetDirty(data);

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terreno";
            terrainObject.transform.SetParent(parent, false);
            terrainObject.transform.position = new Vector3(Ground.MinX, 0, Ground.MinZ);
            GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 160;
            terrain.detailObjectDistance = 65;
            terrain.detailObjectDensity = 1f;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
            return terrain;
        }

        private static void Heights(TerrainData data)
        {
            var res = data.heightmapResolution;
            var heights = new float[res, res];
            for (var zi = 0; zi < res; zi++)
            {
                for (var xi = 0; xi < res; xi++)
                {
                    var x = Ground.MinX + xi / (float)(res - 1) * Ground.SizeX;
                    var z = Ground.MinZ + zi / (float)(res - 1) * Ground.SizeZ;
                    heights[zi, xi] = Ground.Shape(x, z) / Ground.MaxHeight;
                }
            }
            data.SetHeights(0, 0, heights);
        }

        private static void Paint(TerrainData data)
        {
            var res = data.alphamapResolution;
            var layers = data.terrainLayers.Length;
            var maps = new float[res, res, layers];
            var weights = new float[layers];
            for (var zi = 0; zi < res; zi++)
            {
                for (var xi = 0; xi < res; xi++)
                {
                    var u = (xi + 0.5f) / res;
                    var v = (zi + 0.5f) / res;
                    var x = Ground.MinX + u * Ground.SizeX;
                    var z = Ground.MinZ + v * Ground.SizeZ;
                    System.Array.Clear(weights, 0, layers);

                    var patches = Ground.Fbm(x * 0.06f + 40f, z * 0.06f + 9f, 3);
                    weights[Grass] = 1f;
                    weights[ForestFloor] = Ground.ZoneWeight("mata_sombria", x, z) * 3f;
                    var ruins = Ground.ZoneWeight("ruinas_esquecidas", x, z);
                    weights[Stone] = ruins * (1.2f + 2f * patches);
                    weights[Grass] += ruins * (1f - patches) * 0.6f;
                    weights[Gravel] = Ground.ZoneWeight("arena_do_guardiao", x, z) * 3.5f;
                    var village = Ground.ZoneWeight("vila", x, z);
                    var plaza = 1f - Mathf.Clamp01((Mathf.Sqrt(x * x + z * z) - 9f) / 8f);
                    weights[Dirt] = village * 0.8f + plaza * 3f;
                    // Manchas de terra nos campos, para o verde não ficar liso.
                    weights[Dirt] += Mathf.Clamp01((patches - 0.62f) * 4f) * 0.8f * (1f - ruins);

                    // Estrada: terra batida, com a borda irregular.
                    var road = Ground.DistanceToRoad(x, z) + (patches - 0.5f) * 1.6f;
                    weights[Dirt] += (1f - Mathf.Clamp01((road - 2f) / 1.6f)) * 6f;

                    // Encostas e montanhas: rocha nas partes íngremes ou altas, mato escuro no pé.
                    var steep = data.GetSteepness(u, v);
                    var height = data.GetInterpolatedHeight(u, v);
                    var rock = Mathf.Clamp01((steep - 24f) / 12f) + Mathf.Clamp01((height - 14f) / 8f);
                    weights[Rock] = rock * 8f;
                    var foothill = Mathf.Clamp01((Ground.OutsideDistance(x, z) + 10f) / 25f);
                    weights[ForestFloor] += foothill * 1.5f;

                    var total = 0f;
                    for (var l = 0; l < layers; l++) total += weights[l];
                    for (var l = 0; l < layers; l++) maps[zi, xi, l] = weights[l] / total;
                }
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>Grama (e algumas flores) nos campos e na mata; nada nas estradas, na praça e nas rochas.</summary>
        private static void GrassDetails(TerrainData data)
        {
            data.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = GrassTexture("tufo_grama", false), renderMode = DetailRenderMode.Grass,
                    healthyColor = new Color(0.55f, 0.75f, 0.35f), dryColor = new Color(0.7f, 0.68f, 0.38f),
                    minWidth = 0.3f, maxWidth = 0.55f, minHeight = 0.22f, maxHeight = 0.45f, noiseSpread = 0.4f, useInstancing = false,
                },
                new DetailPrototype
                {
                    prototypeTexture = GrassTexture("flores", true), renderMode = DetailRenderMode.Grass,
                    healthyColor = Color.white, dryColor = new Color(0.95f, 0.9f, 0.8f),
                    minWidth = 0.25f, maxWidth = 0.4f, minHeight = 0.2f, maxHeight = 0.35f, noiseSpread = 0.5f, useInstancing = false,
                },
            };
            var res = data.detailResolution;
            var grass = new int[res, res];
            var flowers = new int[res, res];
            var maps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            for (var zi = 0; zi < res; zi++)
            {
                for (var xi = 0; xi < res; xi++)
                {
                    var u = (xi + 0.5f) / res;
                    var v = (zi + 0.5f) / res;
                    var ai = Mathf.Min(data.alphamapWidth - 1, (int)(u * data.alphamapWidth));
                    var aj = Mathf.Min(data.alphamapHeight - 1, (int)(v * data.alphamapHeight));
                    var green = maps[aj, ai, Grass] + maps[aj, ai, ForestFloor] * 0.5f;
                    var x = Ground.MinX + u * Ground.SizeX;
                    var z = Ground.MinZ + v * Ground.SizeZ;
                    var noise = Ground.Fbm(x * 0.09f + 3f, z * 0.09f + 77f, 2);
                    var amount = green * (0.4f + noise);
                    grass[zi, xi] = amount > 0.6f ? (amount > 1f ? 3 : 2) : amount > 0.4f ? 1 : 0;
                    flowers[zi, xi] = maps[aj, ai, Grass] > 0.7f && noise > 0.66f && ((xi * 7 + zi * 13) % 5 == 0) ? 1 : 0;
                }
            }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, flowers);
        }

        private static TerrainLayer Layer(string name, Color dark, Color light, float tile, float speckle, int seed)
        {
            var path = $"{Folder}/Camada_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = GroundTexture(name, dark, light, speckle, seed);
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static Material TerrainMaterial()
        {
            var path = $"{Folder}/Terreno.mat";
            var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.enableInstancing = true;
            return material;
        }

        /// <summary>Textura de chão 512x512 que repete sem emenda: duas cores misturadas por ruído e pintinhas.</summary>
        private static Texture2D GroundTexture(string name, Color dark, Color light, float speckle, int seed)
        {
            const int size = 512;
            var pixels = new Color32[size * size];
            var random = new System.Random(seed);
            var noise = new TileNoise(seed);
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x / (float)size;
                    var v = y / (float)size;
                    var n = noise.Fbm(u, v, 4, 4) * 0.7f + noise.Fbm(u + 0.37f, v + 0.11f, 32, 2) * 0.3f;
                    var color = Color.Lerp(dark, light, Mathf.SmoothStep(0.15f, 0.85f, n));
                    var grain = ((float)random.NextDouble() - 0.5f) * speckle;
                    color = new Color(color.r + grain, color.g + grain, color.b + grain * 0.8f);
                    pixels[y * size + x] = color;
                }
            }
            return SaveTexture($"Textura_{name}", size, size, pixels, TextureWrapMode.Repeat, alpha: false);
        }

        /// <summary>Tufo de grama (ou flores) com fundo transparente, para os detalhes do terreno.</summary>
        private static Texture2D GrassTexture(string name, bool flowers)
        {
            const int width = 128, height = 128;
            var pixels = new Color32[width * height];
            var random = new System.Random(flowers ? 71 : 70);
            var blades = flowers ? 7 : 22;
            for (var b = 0; b < blades; b++)
            {
                var baseX = 10 + random.Next(width - 20);
                var tall = flowers ? 50 + random.Next(40) : 60 + random.Next(66);
                var lean = (float)(random.NextDouble() - 0.5) * 30f;
                var bladeWidth = flowers ? 2.5f : 3.5f + (float)random.NextDouble() * 3f;
                for (var y = 0; y < tall; y++)
                {
                    var t = y / (float)tall;
                    var cx = baseX + lean * t * t;
                    var half = bladeWidth * (1f - t);
                    var shade = Color.Lerp(new Color(0.18f, 0.32f, 0.1f), new Color(0.62f, 0.85f, 0.36f), t);
                    if (flowers) shade = Color.Lerp(new Color(0.2f, 0.4f, 0.15f), new Color(0.35f, 0.6f, 0.25f), t);
                    for (var x = Mathf.FloorToInt(cx - half - 1); x <= Mathf.CeilToInt(cx + half + 1); x++)
                    {
                        if (x < 0 || x >= width) continue;
                        var coverage = Mathf.Clamp01(half + 0.5f - Mathf.Abs(x - cx));
                        if (coverage <= 0) continue;
                        Blend(pixels, width, x, y, shade, coverage);
                    }
                }
                if (!flowers) continue;
                // Cabeça da flor: amarela, branca ou lilás.
                var petal = new[] { new Color(1f, 0.85f, 0.25f), new Color(0.97f, 0.97f, 0.95f), new Color(0.8f, 0.6f, 0.95f) }[random.Next(3)];
                var top = tall;
                var topX = baseX + lean;
                for (var y = top - 6; y <= top + 6; y++)
                for (var x = (int)topX - 6; x <= (int)topX + 6; x++)
                {
                    if (x < 0 || x >= width || y < 0 || y >= height) continue;
                    var d = Mathf.Sqrt((x - topX) * (x - topX) + (y - top) * (y - top));
                    if (d < 6f) Blend(pixels, width, x, y, petal, Mathf.Clamp01(6f - d));
                }
            }
            return SaveTexture($"Textura_{name}", width, height, pixels, TextureWrapMode.Clamp, alpha: true);
        }

        private static void Blend(Color32[] pixels, int width, int x, int y, Color color, float coverage)
        {
            var index = y * width + x;
            Color old = pixels[index];
            var alpha = Mathf.Max(old.a, coverage);
            var mixed = Color.Lerp(old.a > 0 ? old : color, color, coverage);
            mixed.a = alpha;
            pixels[index] = mixed;
        }

        private static Texture2D SaveTexture(string name, int width, int height, Color32[] pixels, TextureWrapMode wrap, bool alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            var path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = true;
            importer.isReadable = alpha; // os detalhes do terreno leem a textura
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Ruído de valor que repete a cada 1 unidade (sem emenda ao ladrilhar).</summary>
        private sealed class TileNoise
        {
            private readonly float[] _values = new float[256 * 256];

            public TileNoise(int seed)
            {
                var random = new System.Random(seed);
                for (var i = 0; i < _values.Length; i++) _values[i] = (float)random.NextDouble();
            }

            private float Lattice(int x, int y, int period) => _values[((y % period + period) % period) * 256 + ((x % period + period) % period)];

            public float Value(float u, float v, int period)
            {
                var x = u * period;
                var y = v * period;
                var x0 = Mathf.FloorToInt(x);
                var y0 = Mathf.FloorToInt(y);
                var fx = x - x0;
                var fy = y - y0;
                fx = fx * fx * (3 - 2 * fx);
                fy = fy * fy * (3 - 2 * fy);
                var a = Mathf.Lerp(Lattice(x0, y0, period), Lattice(x0 + 1, y0, period), fx);
                var b = Mathf.Lerp(Lattice(x0, y0 + 1, period), Lattice(x0 + 1, y0 + 1, period), fx);
                return Mathf.Lerp(a, b, fy);
            }

            public float Fbm(float u, float v, int period, int octaves)
            {
                var sum = 0f;
                var amplitude = 1f;
                var norm = 0f;
                for (var i = 0; i < octaves; i++)
                {
                    sum += Value(u, v, Mathf.Min(256, period)) * amplitude;
                    norm += amplitude;
                    amplitude *= 0.5f;
                    period *= 2;
                }
                return sum / norm;
            }
        }
    }
}
