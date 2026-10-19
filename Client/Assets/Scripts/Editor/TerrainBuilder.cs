using System.IO;
using System.Linq;
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

        private const int Grass = 0, ForestFloor = 1, Dirt = 2, Stone = 3, Gravel = 4, Rock = 5, Cobble = 6;

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
                // Praça da vila calçada (lajotas de pedra clara).
                LayerWith("calcamento", DungeonKitBuilder.TileTexture("calcamento", new Color(0.55f, 0.52f, 0.47f),
                    new Color(0.7f, 0.66f, 0.58f), new Color(0.32f, 0.3f, 0.27f), 17), 3f),
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
            AssetWrite.Create(data, dataPath);

            Heights(data);
            Paint(data);
            GrassDetails(data);
            EditorUtility.SetDirty(data);

            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "Terreno";
            terrainObject.transform.SetParent(parent, false);
            // O terreno começa abaixo do zero, para o leito do rio poder descer.
            terrainObject.transform.position = new Vector3(Ground.MinX, -Ground.Base, Ground.MinZ);
            GameObjectUtility.SetStaticEditorFlags(terrainObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic);
            var terrain = terrainObject.GetComponent<Terrain>();
            terrain.materialTemplate = TerrainMaterial();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4;
            terrain.basemapDistance = 160;
            terrain.detailObjectDistance = 45;
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
                    heights[zi, xi] = (Ground.Shape(x, z) + Ground.Base) / Ground.MaxHeight;
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
                    weights[Dirt] = village * 0.8f;
                    weights[Cobble] = plaza * 4f;
                    // Manchas de terra nos campos, para o verde não ficar liso.
                    weights[Dirt] += Mathf.Clamp01((patches - 0.62f) * 4f) * 0.8f * (1f - ruins);

                    // Estrada: terra batida, com a borda irregular.
                    var road = Ground.DistanceToRoad(x, z) + (patches - 0.5f) * 1.6f;
                    weights[Dirt] += (1f - Mathf.Clamp01((road - 2f) / 1.6f)) * 6f;

                    // Margens e leito do rio: terra molhada e pedra.
                    var river = Ground.RiverDepth(x, z);
                    weights[Dirt] += river * 5f;
                    weights[Stone] += Mathf.Clamp01(river - 0.6f) * 6f;

                    // Encostas e montanhas: rocha nas partes íngremes ou altas, mato escuro no pé.
                    var steep = data.GetSteepness(u, v);
                    var height = data.GetInterpolatedHeight(u, v) - Ground.Base;
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

        /// <summary>
        /// Tufos de grama e flores low-poly (malhas paradas, no estilo das árvores) nos campos e na mata; nada nas
        /// estradas, na praça e nas rochas. Esparsos e só perto da câmera: de longe quem dá o verde é o chão.
        /// </summary>
        private static void GrassDetails(TerrainData data)
        {
            data.detailPrototypes = new[]
            {
                MeshDetail("tufo_grama", GrassTuft, TrainingSceneBuilder.MaterialFor("Cenario_grama_tufo", new Color(0.36f, 0.53f, 0.24f))),
                MeshDetail("flores", FlowerPatch, TrainingSceneBuilder.MaterialFor("Cenario_flores", new Color(0.95f, 0.85f, 0.4f))),
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
                    if (Ground.RiverDepth(x, z) > 0.15f) amount = 0f; // nada de grama dentro do rio
                    grass[zi, xi] = amount > 0.95f ? 2 : amount > 0.62f ? 1 : 0;
                    flowers[zi, xi] = maps[aj, ai, Grass] > 0.7f && noise > 0.66f && ((xi * 7 + zi * 13) % 5 == 0) ? 1 : 0;
                }
            }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, flowers);
        }

        private static TerrainLayer LayerWith(string name, Texture2D texture, float tile)
        {
            var path = $"{Folder}/Camada_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetWrite.Create(layer, path);
            }
            layer.diffuseTexture = texture;
            layer.tileSize = new Vector2(tile, tile);
            layer.smoothness = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        private static TerrainLayer Layer(string name, Color dark, Color light, float tile, float speckle, int seed)
        {
            var path = $"{Folder}/Camada_{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetWrite.Create(layer, path);
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
                AssetWrite.Create(material, path);
            }
            material.enableInstancing = true;
            return material;
        }

        /// <summary>Textura de chão 512x512 que repete sem emenda: duas cores misturadas por ruído e pintinhas.</summary>
        internal static Texture2D GroundTexture(string name, Color dark, Color light, float speckle, int seed)
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

        private static DetailPrototype MeshDetail(string name, System.Func<System.Random, Mesh> make, Material material)
        {
            var folder = $"{Folder}/Detalhes";
            Directory.CreateDirectory(folder);
            var mesh = make(new System.Random(name.GetHashCode()));
            mesh.name = name;
            var meshPath = $"{folder}/{name}_malha.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetWrite.Create(mesh, meshPath);
            var temp = new GameObject(name);
            temp.AddComponent<MeshFilter>().sharedMesh = mesh;
            temp.AddComponent<MeshRenderer>().sharedMaterial = material;
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, $"{folder}/{name}.prefab");
            Object.DestroyImmediate(temp);
            return new DetailPrototype
            {
                usePrototypeMesh = true, prototype = prefab, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                healthyColor = Color.white, dryColor = new Color(0.92f, 0.9f, 0.8f),
                minWidth = 0.8f, maxWidth = 1.3f, minHeight = 0.75f, maxHeight = 1.25f, noiseSpread = 0.4f,
            };
        }

        /// <summary>Tufo de 5 a 7 folhas pontudas, abertas para fora.</summary>
        private static Mesh GrassTuft(System.Random r)
        {
            var kit = new MeshKit(1);
            var blades = 6;
            for (var i = 0; i < blades; i++)
            {
                var angle = i * 360f / blades + (float)r.NextDouble() * 30f;
                var lean = 12f + (float)r.NextDouble() * 22f;
                var offset = Quaternion.Euler(0, angle, 0) * new Vector3(0.05f + (float)r.NextDouble() * 0.06f, 0, 0);
                var rotation = Quaternion.Euler(0, angle, 0) * Quaternion.Euler(0, 0, -lean);
                kit.Frustum(0, offset, rotation, 0.035f + (float)r.NextDouble() * 0.02f, 0f, 0.26f + (float)r.NextDouble() * 0.2f, 3, r, 0f);
            }
            return kit.Build("tufo_grama");
        }

        /// <summary>Florzinhas rentes ao chão: três ou quatro bolinhas.</summary>
        private static Mesh FlowerPatch(System.Random r)
        {
            var kit = new MeshKit(1);
            for (var i = 0; i < 4; i++)
            {
                var size = 0.05f + (float)r.NextDouble() * 0.03f;
                kit.Blob(0, new Vector3((float)r.NextDouble() * 0.3f - 0.15f, 0.06f + (float)r.NextDouble() * 0.06f, (float)r.NextDouble() * 0.3f - 0.15f),
                    new Vector3(size, size * 0.7f, size), r, 0.15f);
            }
            return kit.Build("flores");
        }

        internal static Texture2D SaveTexture(string name, int width, int height, Color32[] pixels, TextureWrapMode wrap, bool alpha)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            var path = $"{Folder}/{name}.png";
            var bytes = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);
            // Igual à que já está no disco: não regrava. Regravar uma textura que o Unity ainda está importando (em
            // paralelo) falha com "Win32 IO 1224", e assim a montagem também fica mais rápida.
            if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes))
            {
                AssetWrite.Bytes(path, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settingsChanged = importer.wrapMode != wrap || importer.alphaIsTransparency != alpha || !importer.mipmapEnabled ||
                                  importer.isReadable != alpha || importer.textureCompression != TextureImporterCompression.CompressedHQ;
            importer.wrapMode = wrap;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = true;
            importer.isReadable = alpha; // os detalhes do terreno leem a textura
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            if (settingsChanged) importer.SaveAndReimport();
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
