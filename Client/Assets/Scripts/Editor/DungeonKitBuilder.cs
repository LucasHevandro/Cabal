using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Peças da Cripta Esquecida e da arena PvP (as duas são montadas no jogo, na origem da instância, pelo DungeonView e
    /// pelo ArenaView): muros de tijolo, pilares, grade de ferro, tochas com fogo, caixões, ossos, velas, estandartes,
    /// trono, baú, bandeiras dos times, arquibancada, e os pisos (pedra da cripta, areia batida da arena) com textura.
    /// Grava em Resources/Visuals/dungeon. Menu: Jogo &gt; Visuais &gt; Montar cripta e arena.
    /// </summary>
    internal static class DungeonKitBuilder
    {
        private const string Folder = "Assets/Resources/Visuals/dungeon";
        private const string MeshFolder = "Assets/Art/Dungeon/Malhas";

        private static int _parts;

        [MenuItem("Jogo/Visuais/Montar cripta e arena")]
        public static void Build()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(TerrainBuilder.Folder);
            var stone = Mat("pedra_cripta", new Color(0.36f, 0.34f, 0.4f));
            var stoneDark = Mat("pedra_cripta_escura", new Color(0.24f, 0.23f, 0.28f));
            var iron = Mat("ferro_cripta", new Color(0.2f, 0.2f, 0.22f));
            var wood = Mat("madeira_cripta", new Color(0.32f, 0.22f, 0.15f));
            var bone = Mat("osso", new Color(0.88f, 0.85f, 0.74f));
            var cloth = Mat("pano_roxo", new Color(0.35f, 0.15f, 0.42f));
            var gold = Mat("ouro", new Color(0.92f, 0.72f, 0.25f));
            var wax = Mat("cera", new Color(0.93f, 0.9f, 0.78f));
            var flame = TrainingSceneBuilder.MaterialFor("Dungeon_chama", new Color(1f, 0.7f, 0.3f) * 1.8f, "Universal Render Pipeline/Unlit");
            var sand = Mat("muro_arena", new Color(0.66f, 0.58f, 0.46f));
            var sandDark = Mat("muro_arena_escuro", new Color(0.5f, 0.44f, 0.36f));
            var banner = Mat("bandeira", Color.white); // a cor do time vem no jogo (MaterialPropertyBlock)

            Floors();
            Wall(stone, stoneDark);
            Pillar("pilar", stone, stoneDark, 1.4f, 7f);
            Gate(iron);
            Torch(iron, wood);
            Coffin(wood, iron);
            Bones(bone);
            Candles(wax, flame);
            Banner(wood, cloth, gold);
            Throne(stoneDark, cloth, gold);
            Chest(wood, gold);
            ArenaWall(sand, sandDark);
            Boulder(sandDark, Mat("musgo_arena", new Color(0.4f, 0.45f, 0.28f)));
            Flag(wood, banner);
            Stands(sand, sandDark);
            Brazier(iron, stoneDark);
            AssetDatabase.SaveAssets();
            Debug.Log("[Cripta] peças da cripta e da arena montadas.");
        }

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Dungeon_{name}", color);

        private static Transform Root(string name)
        {
            _parts = 0;
            return new GameObject(name).transform;
        }

        private static void Part(Transform parent, Material material, System.Action<MeshKit> draw, bool shadows = true)
        {
            var kit = new MeshKit(1);
            draw(kit);
            var mesh = kit.Build($"{parent.root.name}_{++_parts}");
            var path = $"{MeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var obj = new GameObject("Peça");
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (!shadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void Save(Transform root)
        {
            var path = $"{Folder}/{root.name}.prefab";
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
            Object.DestroyImmediate(root.gameObject);
        }

        // ===== Pisos =====

        private static void Floors()
        {
            FloorMaterial("piso_cripta", TileTexture("piso_cripta", new Color(0.3f, 0.29f, 0.33f), new Color(0.42f, 0.4f, 0.44f), new Color(0.12f, 0.11f, 0.13f), 21));
            FloorMaterial("piso_arena", TerrainBuilder.GroundTexture("piso_arena", new Color(0.55f, 0.48f, 0.36f), new Color(0.7f, 0.62f, 0.48f), 0.12f, 22));
            FloorMaterial("tapete", TileTexture("tapete", new Color(0.45f, 0.08f, 0.1f), new Color(0.55f, 0.12f, 0.14f), new Color(0.75f, 0.58f, 0.2f), 23, stripes: true));
        }

        private static void FloorMaterial(string name, Texture2D texture)
        {
            var material = Mat(name, Color.white);
            material.SetTexture("_BaseMap", texture);
            EditorUtility.SetDirty(material);
            var path = $"Assets/Resources/Visuals/dungeon/{name}.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(material), path);
        }

        /// <summary>Lajotas de pedra (ou o tapete com borda), 512x512, que repetem sem emenda.</summary>
        internal static Texture2D TileTexture(string name, Color dark, Color light, Color grout, int seed, bool stripes = false)
        {
            const int size = 512;
            var pixels = new Color32[size * size];
            var random = new System.Random(seed);
            var tones = new float[16];
            for (var i = 0; i < tones.Length; i++) tones[i] = (float)random.NextDouble();
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    Color color;
                    if (stripes)
                    {
                        var u = x / (float)size;
                        var border = u < 0.08f || u > 0.92f || (u > 0.12f && u < 0.15f) || (u > 0.85f && u < 0.88f);
                        color = border ? grout : Color.Lerp(dark, light, Mathf.PerlinNoise(x * 0.05f, y * 0.05f));
                    }
                    else
                    {
                        // Quatro fileiras de lajotas, desencontradas.
                        var row = y / 128;
                        var shifted = (x + (row % 2) * 64) % size;
                        var col = shifted / 128;
                        var lx = shifted % 128;
                        var ly = y % 128;
                        var joint = lx < 5 || ly < 5;
                        var tone = tones[(row * 4 + col) % tones.Length];
                        color = joint ? grout : Color.Lerp(dark, light, tone * 0.7f + Mathf.PerlinNoise(x * 0.04f, y * 0.04f) * 0.3f);
                    }
                    var grain = ((float)random.NextDouble() - 0.5f) * 0.06f;
                    pixels[y * size + x] = new Color(color.r + grain, color.g + grain, color.b + grain);
                }
            }
            return TerrainBuilder.SaveTexture($"Textura_{name}", size, size, pixels, TextureWrapMode.Repeat, alpha: false);
        }

        // ===== Cripta =====

        /// <summary>Muro de 4 m x 7 m de tijolos desencontrados (o jogo repete ao longo do corredor).</summary>
        private static void Wall(Material stone, Material dark)
        {
            var root = Root("parede");
            var r = new System.Random(31);
            Part(root, dark, k => k.Box(0, Vector3.zero, Quaternion.identity, new Vector3(4f, 7f, 0.6f)));
            Part(root, stone, k =>
            {
                for (var row = 0; row < 10; row++)
                {
                    var y = row * 0.7f;
                    var offset = row % 2 == 0 ? 0f : 0.65f;
                    for (var x = -2f + offset - 1.3f; x < 2f; x += 1.3f)
                    {
                        var left = Mathf.Max(-2f, x);
                        var right = Mathf.Min(2f, x + 1.24f);
                        if (right - left < 0.2f) continue;
                        var depth = 0.12f + (float)r.NextDouble() * 0.12f;
                        k.Box(0, new Vector3((left + right) / 2f, y + 0.03f, -0.3f - depth / 2f), Quaternion.identity,
                            new Vector3(right - left - 0.05f, 0.64f, depth), r, 0.06f);
                    }
                }
            });
            Save(root);
        }

        private static void Pillar(string name, Material stone, Material dark, float width, float height)
        {
            var root = Root(name);
            var r = new System.Random(name.GetHashCode());
            Part(root, dark, k =>
            {
                k.Box(0, Vector3.zero, Quaternion.identity, new Vector3(width + 0.4f, 0.5f, width + 0.4f));
                k.Box(0, new Vector3(0, height - 0.5f, 0), Quaternion.identity, new Vector3(width + 0.4f, 0.5f, width + 0.4f));
            });
            Part(root, stone, k => k.Frustum(0, new Vector3(0, 0.5f, 0), Quaternion.Euler(0, 45, 0), width * 0.62f, width * 0.58f, height - 1f, 8, r, 0.04f));
            Save(root);
        }

        /// <summary>Grade de ferro de 1 m de largura (o jogo estica até a largura da passagem) e 4 m de altura.</summary>
        private static void Gate(Material iron)
        {
            var root = Root("portao");
            Part(root, iron, k =>
            {
                for (var i = 0; i < 4; i++)
                    k.Frustum(0, new Vector3(-0.375f + i * 0.25f, 0, 0), Quaternion.identity, 0.035f, 0.025f, 4.2f, 5, null, 0f, bottomCap: true);
                foreach (var y in new[] { 0.6f, 2f, 3.4f })
                    k.Box(0, new Vector3(0, y, 0), Quaternion.identity, new Vector3(1f, 0.12f, 0.1f));
            });
            Save(root);
        }

        private static void Torch(Material iron, Material wood)
        {
            var root = Root("tocha");
            Part(root, iron, k => k.Box(0, new Vector3(0, -0.2f, -0.15f), Quaternion.identity, new Vector3(0.12f, 0.4f, 0.3f)));
            Part(root, wood, k => k.Frustum(0, new Vector3(0, -0.3f, 0.05f), Quaternion.Euler(-20, 0, 0), 0.05f, 0.07f, 0.6f, 6, null));
            PropBuilder.Fire(root, new Vector3(0, 0.3f, 0.15f), withLight: true);
            var light = root.GetComponentInChildren<Light>();
            if (light != null)
            {
                light.range = 13f;
                light.intensity = 2.4f;
            }
            Save(root);
        }

        private static void Coffin(Material wood, Material iron)
        {
            var root = Root("caixao");
            Part(root, wood, k =>
            {
                k.Box(0, Vector3.zero, Quaternion.identity, new Vector3(0.8f, 0.55f, 2f));
                k.Box(0, new Vector3(0, 0.55f, 0), Quaternion.Euler(0, 0, 0), new Vector3(0.9f, 0.12f, 2.1f));
            });
            Part(root, iron, k =>
            {
                k.Box(0, new Vector3(0, 0.67f, 0.4f), Quaternion.identity, new Vector3(0.12f, 0.03f, 0.6f));
                k.Box(0, new Vector3(0, 0.67f, 0.55f), Quaternion.identity, new Vector3(0.4f, 0.03f, 0.12f));
            });
            Save(root);
        }

        private static void Bones(Material bone)
        {
            var root = Root("ossos");
            var r = new System.Random(41);
            Part(root, bone, k =>
            {
                k.Blob(0, new Vector3(0, 0.15f, 0), new Vector3(0.17f, 0.15f, 0.19f), r, 0.06f);
                for (var i = 0; i < 6; i++)
                {
                    var a = i * 61f;
                    k.Frustum(0, new Vector3(Mathf.Cos(a) * 0.35f, 0.03f, Mathf.Sin(a) * 0.35f), Quaternion.Euler(90, a, 0), 0.03f, 0.03f, 0.5f, 5, r, 0f, bottomCap: true);
                }
            });
            Save(root);
        }

        private static void Candles(Material wax, Material flame)
        {
            var root = Root("velas");
            var r = new System.Random(51);
            Part(root, wax, k =>
            {
                for (var i = 0; i < 4; i++)
                    k.Frustum(0, new Vector3(Mathf.Cos(i * 1.7f) * 0.18f, 0, Mathf.Sin(i * 1.7f) * 0.18f), Quaternion.identity,
                        0.05f, 0.045f, 0.2f + i * 0.08f, 6, null);
            });
            Part(root, flame, k =>
            {
                for (var i = 0; i < 4; i++)
                    k.Blob(0, new Vector3(Mathf.Cos(i * 1.7f) * 0.18f, 0.24f + i * 0.08f, Mathf.Sin(i * 1.7f) * 0.18f), new Vector3(0.025f, 0.045f, 0.025f), r, 0.05f);
            }, shadows: false);
            Save(root);
        }

        private static void Banner(Material wood, Material cloth, Material gold)
        {
            var root = Root("estandarte");
            Part(root, wood, k => k.Box(0, new Vector3(0, 4.6f, -0.1f), Quaternion.identity, new Vector3(1.8f, 0.12f, 0.12f)));
            Part(root, cloth, k => k.Box(0, new Vector3(0, 1.8f, 0), Quaternion.identity, new Vector3(1.4f, 2.8f, 0.05f), new System.Random(61), 0f));
            Part(root, gold, k =>
            {
                k.Box(0, new Vector3(0, 3.2f, -0.03f), Quaternion.identity, new Vector3(0.5f, 0.5f, 0.04f));
                k.Box(0, new Vector3(0, 1.8f, -0.03f), Quaternion.identity, new Vector3(1.42f, 0.08f, 0.06f));
            });
            Save(root);
        }

        private static void Throne(Material stone, Material cloth, Material gold)
        {
            var root = Root("trono");
            Part(root, stone, k =>
            {
                k.Box(0, Vector3.zero, Quaternion.identity, new Vector3(3.6f, 0.6f, 3f));
                k.Box(0, new Vector3(0, 0.6f, 0), Quaternion.identity, new Vector3(2.4f, 1.2f, 1.6f));
                k.Box(0, new Vector3(0, 1.8f, 0.7f), Quaternion.identity, new Vector3(2.4f, 3.6f, 0.4f));
                foreach (var side in new[] { -1f, 1f })
                {
                    k.Box(0, new Vector3(side * 1.25f, 1.8f, 0), Quaternion.identity, new Vector3(0.3f, 0.6f, 1.6f));
                    k.Frustum(0, new Vector3(side * 1f, 5.4f, 0.7f), Quaternion.identity, 0.22f, 0f, 1.1f, 4, null, 0f, bottomCap: true);
                }
                k.Frustum(0, new Vector3(0, 5.4f, 0.7f), Quaternion.identity, 0.3f, 0f, 1.6f, 4, null, 0f, bottomCap: true);
            });
            Part(root, cloth, k => k.Box(0, new Vector3(0, 1.82f, -0.1f), Quaternion.identity, new Vector3(2f, 0.08f, 1.4f)));
            Part(root, gold, k => k.Box(0, new Vector3(0, 4.8f, 0.48f), Quaternion.identity, new Vector3(2.5f, 0.15f, 0.06f)));
            Save(root);
        }

        private static void Chest(Material wood, Material gold)
        {
            var root = Root("bau");
            Part(root, wood, k =>
            {
                k.Box(0, Vector3.zero, Quaternion.identity, new Vector3(1.6f, 0.8f, 1f));
                k.Box(0, new Vector3(0, 0.8f, 0), Quaternion.identity, new Vector3(1.62f, 0.3f, 1.02f));
            });
            Part(root, gold, k =>
            {
                foreach (var x in new[] { -0.6f, 0.6f })
                    k.Box(0, new Vector3(x, 0, 0), Quaternion.identity, new Vector3(0.1f, 1.12f, 1.05f));
                k.Box(0, new Vector3(0, 0.65f, -0.52f), Quaternion.identity, new Vector3(0.2f, 0.25f, 0.06f));
            });
            Save(root);
        }

        // ===== Arena =====

        private static void ArenaWall(Material sand, Material dark)
        {
            var root = Root("muro_arena");
            var r = new System.Random(71);
            Part(root, sand, k =>
            {
                for (var row = 0; row < 4; row++)
                {
                    var offset = row % 2 == 0 ? 0f : 0.5f;
                    for (var x = -2f + offset - 1f; x < 2f; x += 1f)
                    {
                        var left = Mathf.Max(-2f, x);
                        var right = Mathf.Min(2f, x + 0.95f);
                        if (right - left < 0.2f) continue;
                        k.Box(0, new Vector3((left + right) / 2f, row * 0.75f, 0), Quaternion.identity, new Vector3(right - left - 0.04f, 0.72f, 1f), r, 0.04f);
                    }
                }
            });
            Part(root, dark, k => k.Box(0, new Vector3(0, 3f, 0), Quaternion.identity, new Vector3(4.05f, 0.25f, 1.2f)));
            Save(root);
        }

        /// <summary>Obstáculo da arena: um rochedo de 3 m (o colisor do jogo é um cubo de 3 m).</summary>
        private static void Boulder(Material stone, Material moss)
        {
            var root = Root("pilar_arena");
            var r = new System.Random(91);
            Part(root, stone, k =>
            {
                k.Blob(0, new Vector3(0, 1.3f, 0), new Vector3(1.75f, 1.45f, 1.7f), r, 0.18f);
                k.Blob(0, new Vector3(0.8f, 0.5f, 0.6f), new Vector3(0.8f, 0.6f, 0.7f), r, 0.2f);
                k.Blob(0, new Vector3(-0.9f, 0.4f, -0.5f), new Vector3(0.7f, 0.5f, 0.8f), r, 0.2f);
            });
            Part(root, moss, k => k.Blob(0, new Vector3(0.2f, 2.55f, 0.1f), new Vector3(1f, 0.3f, 0.9f), r, 0.2f));
            Save(root);
        }

        private static void Flag(Material wood, Material banner)
        {
            var root = Root("bandeira");
            Part(root, wood, k => k.Frustum(0, Vector3.zero, Quaternion.identity, 0.08f, 0.06f, 4.6f, 6, null));
            var cloth = new GameObject("Pano").transform;
            cloth.SetParent(root, false);
            Part(cloth, banner, k => k.Box(0, new Vector3(0.65f, 3.2f, 0), Quaternion.identity, new Vector3(1.2f, 1.2f, 0.04f), new System.Random(81), 0f));
            Save(root);
        }

        private static void Stands(Material sand, Material dark)
        {
            var root = Root("arquibancada");
            Part(root, sand, k =>
            {
                for (var step = 0; step < 4; step++)
                    k.Box(0, new Vector3(0, step * 0.6f, step * 1f), Quaternion.identity, new Vector3(8f, 0.6f + step * 0f, 1f));
            });
            Part(root, dark, k =>
            {
                for (var step = 0; step < 4; step++)
                    k.Box(0, new Vector3(0, step * 0.6f + 0.6f, step * 1f - 0.45f), Quaternion.identity, new Vector3(8f, 0.06f, 0.1f));
            });
            Save(root);
        }

        private static void Brazier(Material iron, Material stone)
        {
            var root = Root("braseiro_arena");
            Part(root, stone, k => k.Frustum(0, Vector3.zero, Quaternion.identity, 0.7f, 0.6f, 0.4f, 8, null));
            Part(root, iron, k =>
            {
                k.Frustum(0, new Vector3(0, 0.4f, 0), Quaternion.identity, 0.14f, 0.14f, 0.7f, 6, null);
                k.Frustum(0, new Vector3(0, 1.1f, 0), Quaternion.identity, 0.3f, 0.7f, 0.35f, 8, null);
            });
            PropBuilder.Fire(root, new Vector3(0, 1.4f, 0), withLight: true);
            Save(root);
        }
    }
}
