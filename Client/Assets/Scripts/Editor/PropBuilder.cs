using System.Collections.Generic;
using System.IO;
using Game.Client.World;
using Game.Shared.Progression;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Cenário low-poly gerado por código (árvores, pinheiros, arbustos, pedras, colunas e muros das ruínas, casas,
    /// cercas e poço da vila, braseiros da arena), espalhado por zona sem cair nos lugares de luta, nas estradas e
    /// perto dos NPCs. As malhas ficam em Assets/Art/Mundo/Malhas e são reaproveitadas (poucas variações de cada).
    /// Até chegarem os modelos de verdade, isto dá forma ao mapa; trocar depois é só substituir as malhas/prefabs.
    /// </summary>
    internal static class PropBuilder
    {
        private const string MeshFolder = TerrainBuilder.Folder + "/Malhas";

        private sealed class Kind
        {
            public string Name = "";
            public Mesh[] Variants = System.Array.Empty<Mesh>();
            public Material[] Materials = System.Array.Empty<Material>();
            /// <summary>Raio do colisor (0 = sem colisão) e altura dele.</summary>
            public float ColliderRadius;
            public float ColliderHeight;
            public bool BoxCollider;
            public float MinScale = 0.85f;
            public float MaxScale = 1.2f;
        }

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Cenario_{name}", color);

        public static void Build(Transform parent)
        {
            Directory.CreateDirectory(MeshFolder);
            var bark = Mat("tronco", new Color(0.36f, 0.25f, 0.17f));
            var leaf = Mat("folha", new Color(0.36f, 0.58f, 0.24f));
            var leafDark = Mat("folha_escura", new Color(0.16f, 0.34f, 0.2f));
            var leafAutumn = Mat("folha_outono", new Color(0.72f, 0.5f, 0.2f));
            var bush = Mat("arbusto", new Color(0.27f, 0.45f, 0.2f));
            var stone = Mat("pedra", new Color(0.5f, 0.49f, 0.5f));
            var stoneLight = Mat("pedra_clara", new Color(0.66f, 0.63f, 0.58f));
            var moss = Mat("musgo", new Color(0.33f, 0.43f, 0.25f));
            var wall = Mat("parede", new Color(0.86f, 0.8f, 0.68f));
            var wood = Mat("madeira", new Color(0.5f, 0.35f, 0.22f));
            var roof = Mat("telhado", new Color(0.62f, 0.27f, 0.2f));
            var roofBlue = Mat("telhado_azul", new Color(0.26f, 0.36f, 0.52f));
            var iron = Mat("ferro", new Color(0.22f, 0.21f, 0.22f));

            var tree = new Kind { Name = "arvore", Materials = new[] { bark, leaf }, ColliderRadius = 0.35f, ColliderHeight = 3f,
                Variants = Variants("arvore", 4, Tree) };
            var autumnTree = new Kind { Name = "arvore_outono", Materials = new[] { bark, leafAutumn }, ColliderRadius = 0.35f, ColliderHeight = 3f,
                Variants = tree.Variants };
            var darkTree = new Kind { Name = "arvore_escura", Materials = new[] { bark, leafDark }, ColliderRadius = 0.35f, ColliderHeight = 3f,
                Variants = tree.Variants, MinScale = 1f, MaxScale = 1.45f };
            var pine = new Kind { Name = "pinheiro", Materials = new[] { bark, leafDark }, ColliderRadius = 0.35f, ColliderHeight = 3f,
                Variants = Variants("pinheiro", 4, Pine), MinScale = 0.9f, MaxScale = 1.5f };
            var deadTree = new Kind { Name = "arvore_seca", Materials = new[] { bark }, ColliderRadius = 0.3f, ColliderHeight = 3f,
                Variants = Variants("arvore_seca", 3, DeadTree) };
            var shrub = new Kind { Name = "arbusto", Materials = new[] { bush }, Variants = Variants("arbusto", 4, Shrub), MinScale = 0.7f, MaxScale = 1.3f };
            var rock = new Kind { Name = "pedra", Materials = new[] { stone, moss }, ColliderRadius = 0.8f, ColliderHeight = 1.4f,
                Variants = Variants("pedra", 5, Rock), MinScale = 0.6f, MaxScale = 1.6f };
            var bigRock = new Kind { Name = "rochedo", Materials = new[] { stone, moss }, Variants = rock.Variants, MinScale = 3f, MaxScale = 6f };
            var column = new Kind { Name = "coluna", Materials = new[] { stoneLight }, ColliderRadius = 0.6f, ColliderHeight = 4f,
                Variants = Variants("coluna", 4, BrokenColumn), MinScale = 0.9f, MaxScale = 1.15f };
            var ruinWall = new Kind { Name = "muro", Materials = new[] { stoneLight, moss }, BoxCollider = true,
                Variants = Variants("muro", 3, RuinWall), MinScale = 0.9f, MaxScale = 1.2f };
            var mountainPine = new Kind { Name = "pinheiro_montanha", Materials = pine.Materials, Variants = pine.Variants, MinScale = 1.2f, MaxScale = 2f };

            var props = new GameObject("Cenário").transform;
            props.SetParent(parent, false);

            // ----- Vila: casas em volta da praça (a estrada sai para o norte), cercas, poço e algumas árvores -----
            var village = Group(props, "Vila");
            var house = Variants("casa", 2, House);
            var houseAngles = new[] { 200f, 238f, 272f, 306f, 340f, 18f, 150f };
            for (var i = 0; i < houseAngles.Length; i++)
            {
                var angle = houseAngles[i] * Mathf.Deg2Rad;
                var radius = 21f + (i % 2) * 2.5f;
                var position = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                var facing = Quaternion.LookRotation(-position.normalized);
                Place(village, "Casa", house[i % house.Length], new[] { wall, i % 3 == 0 ? roofBlue : roof, wood },
                    position, facing, 1f, box: true);
            }
            var fence = Variants("cerca", 1, Fence)[0];
            for (var a = 0f; a < 360f; a += 6.5f)
            {
                if (a > 66 && a < 114) continue; // passagem da estrada
                var angle = a * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 29f, 0, Mathf.Sin(angle) * 29f);
                if (Ground.DistanceToRoad(position.x, position.z) < 4f) continue;
                Place(village, "Cerca", fence, new[] { wood }, position, Quaternion.LookRotation(position.normalized), 1f);
            }
            Place(village, "Poço", Variants("poco", 1, Well)[0], new[] { stoneLight, wood, roof }, new Vector3(-11f, 0, -11f),
                Quaternion.Euler(0, 30, 0), 1f, collider: 1.3f);
            ScatterRing(village, tree, new Vector2(0, 0), 24f, 34f, 14, 101, keepOut: 2f);

            // ----- Campos do Vento -----
            var fields = Group(props, "Campos do Vento");
            ScatterRing(fields, tree, new Vector2(0, 40), 12f, 50f, 40, 201);
            ScatterRing(fields, autumnTree, new Vector2(0, 40), 20f, 50f, 8, 202);
            ScatterRing(fields, shrub, new Vector2(0, 40), 6f, 46f, 45, 203, keepOut: 0.5f);
            ScatterRing(fields, rock, new Vector2(0, 40), 10f, 45f, 14, 204);

            // ----- Mata Sombria: densa, escura, com pinheiros -----
            var forest = Group(props, "Mata Sombria");
            ScatterRing(forest, pine, new Vector2(45, 85), 4f, 50f, 120, 301);
            ScatterRing(forest, darkTree, new Vector2(45, 85), 4f, 48f, 45, 302);
            ScatterRing(forest, shrub, new Vector2(45, 85), 2f, 44f, 60, 303, keepOut: 0.5f);
            ScatterRing(forest, rock, new Vector2(45, 85), 6f, 45f, 18, 304);

            // ----- Ruínas Esquecidas -----
            var ruins = Group(props, "Ruínas Esquecidas");
            ScatterRing(ruins, column, new Vector2(-45, 125), 4f, 38f, 26, 401);
            ScatterRing(ruins, ruinWall, new Vector2(-45, 125), 6f, 40f, 14, 402, alignToCenter: true);
            ScatterRing(ruins, rock, new Vector2(-45, 125), 4f, 44f, 24, 403);
            ScatterRing(ruins, deadTree, new Vector2(-45, 125), 8f, 46f, 14, 404);
            ScatterRing(ruins, shrub, new Vector2(-45, 125), 6f, 44f, 18, 405, keepOut: 0.5f);

            // ----- Arena do Guardião: anel de colunas e braseiros -----
            var arena = Group(props, "Arena do Guardião");
            for (var i = 0; i < 16; i++)
            {
                var angle = i * Mathf.PI * 2 / 16;
                var position = new Vector3(Mathf.Cos(angle) * 20.5f, 0, 170 + Mathf.Sin(angle) * 20.5f);
                if (Ground.DistanceToRoad(position.x, position.z) < 3f) continue; // entrada
                Place(arena, "Coluna", column.Variants[0], column.Materials, position, Quaternion.Euler(0, i * 37, 0), 1.25f, collider: 0.7f);
            }
            var brazier = Variants("braseiro", 1, Brazier)[0];
            for (var i = 0; i < 4; i++)
            {
                var angle = (45 + i * 90) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 16f, 0, 170 + Mathf.Sin(angle) * 16f);
                var obj = Place(arena, "Braseiro", brazier, new[] { iron, stone }, position, Quaternion.identity, 1f);
                Fire(obj.transform, new Vector3(0, 1.1f, 0), withLight: i % 2 == 0);
            }

            // ----- Entre as zonas e na borda: árvores soltas, pinheiros nas encostas e rochedos -----
            var wild = Group(props, "Natureza");
            ScatterArea(wild, tree, 120, 501, -120, 120, -60, 220);
            ScatterArea(wild, shrub, 90, 502, -120, 120, -60, 220);
            ScatterArea(wild, rock, 35, 503, -120, 120, -60, 220);
            ScatterBorder(wild, mountainPine, 420, 504);
            ScatterBorder(wild, bigRock, 70, 505);
        }

        // ===== Espalhar =====

        private static Transform Group(Transform parent, string name)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static void ScatterRing(Transform parent, Kind kind, Vector2 center, float minRadius, float maxRadius, int count, int seed,
            float keepOut = 1.5f, bool alignToCenter = false)
        {
            var random = new System.Random(seed);
            var placed = 0;
            for (var attempt = 0; attempt < count * 12 && placed < count; attempt++)
            {
                var angle = (float)(random.NextDouble() * Mathf.PI * 2);
                var distance = Mathf.Sqrt(Mathf.Lerp(minRadius * minRadius, maxRadius * maxRadius, (float)random.NextDouble()));
                var x = center.x + Mathf.Cos(angle) * distance;
                var z = center.y + Mathf.Sin(angle) * distance;
                if (!Free(x, z, keepOut, kind.ColliderRadius > 0 || kind.BoxCollider)) continue;
                var rotation = alignToCenter ? Quaternion.LookRotation(new Vector3(center.x - x, 0, center.y - z)) * Quaternion.Euler(0, 90, 0)
                    : Quaternion.Euler(0, (float)random.NextDouble() * 360f, 0);
                Spawn(parent, kind, random, x, z, rotation);
                placed++;
            }
        }

        private static void ScatterArea(Transform parent, Kind kind, int count, int seed, float minX, float maxX, float minZ, float maxZ)
        {
            var random = new System.Random(seed);
            var placed = 0;
            for (var attempt = 0; attempt < count * 12 && placed < count; attempt++)
            {
                var x = Mathf.Lerp(minX, maxX, (float)random.NextDouble());
                var z = Mathf.Lerp(minZ, maxZ, (float)random.NextDouble());
                if (!Free(x, z, 2f, true)) continue;
                // Longe do centro das zonas (cada zona tem o próprio cenário) e da praça.
                var nearZone = false;
                foreach (var zone in Game.Shared.World.WorldContent.Zones())
                    if (Vector2.Distance(new Vector2(x, z), new Vector2(zone.CenterX, zone.CenterZ)) < zone.Radius + 8f) nearZone = true;
                if (nearZone) continue;
                Spawn(parent, kind, random, x, z, Quaternion.Euler(0, (float)random.NextDouble() * 360f, 0));
                placed++;
            }
        }

        /// <summary>Encostas fora da área jogável: só enfeite, sem colisão (ninguém chega lá).</summary>
        private static void ScatterBorder(Transform parent, Kind kind, int count, int seed)
        {
            var random = new System.Random(seed);
            var placed = 0;
            for (var attempt = 0; attempt < count * 10 && placed < count; attempt++)
            {
                var x = Ground.MinX + 6 + (float)random.NextDouble() * (Ground.SizeX - 12);
                var z = Ground.MinZ + 6 + (float)random.NextDouble() * (Ground.SizeZ - 12);
                var outside = Ground.OutsideDistance(x, z);
                if (outside < -2f || outside > 60f) continue;
                if (Ground.Shape(x, z) > 30f) continue; // pico pelado
                var instance = Spawn(parent, kind, random, x, z, Quaternion.Euler(0, (float)random.NextDouble() * 360f, 0), noCollider: true);
                // Rochedos afundam um pouco no morro.
                if (kind.Name == "rochedo") instance.transform.position -= Vector3.up * instance.transform.localScale.y * 0.3f;
                placed++;
            }
        }

        private static bool Free(float x, float z, float keepOut, bool solid)
        {
            if (Ground.OutsideDistance(x, z) > -4f) return false;
            // Árvores e pedras mais longe dos lugares de luta: não tampam a câmera nem o monstro.
            if (Ground.IsKeepOut(x, z, solid ? keepOut + 3f : keepOut)) return false;
            if (!solid) return true;
            foreach (var npc in NpcCatalog.All)
                if (Vector2.Distance(new Vector2(x, z), new Vector2(npc.X, npc.Z)) < 5f) return false;
            return Vector2.Distance(new Vector2(x, z), Vector2.zero) > 13f; // praça e ponto de nascer
        }

        private static GameObject Spawn(Transform parent, Kind kind, System.Random random, float x, float z, Quaternion rotation, bool noCollider = false)
        {
            var mesh = kind.Variants[random.Next(kind.Variants.Length)];
            var scale = Mathf.Lerp(kind.MinScale, kind.MaxScale, (float)random.NextDouble());
            var collider = noCollider ? 0f : kind.ColliderRadius;
            return Place(parent, kind.Name, mesh, kind.Materials, new Vector3(x, 0, z), rotation, scale, collider, !noCollider && kind.BoxCollider,
                colliderHeight: kind.ColliderHeight);
        }

        private static GameObject Place(Transform parent, string name, Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation,
            float scale, float collider = 0f, bool box = false, float colliderHeight = 3f)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            // Base um pouco enterrada, para não flutuar no morro.
            obj.transform.position = Ground.Point(position.x, position.z) - Vector3.up * 0.15f;
            obj.transform.rotation = rotation;
            obj.transform.localScale = Vector3.one * scale;
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = obj.AddComponent<MeshRenderer>();
            var shared = new Material[mesh.subMeshCount];
            for (var i = 0; i < shared.Length; i++) shared[i] = materials[Mathf.Min(i, materials.Length - 1)];
            renderer.sharedMaterials = shared;
            if (box)
            {
                var b = obj.AddComponent<BoxCollider>();
                b.center = mesh.bounds.center;
                b.size = mesh.bounds.size;
            }
            else if (collider > 0f)
            {
                var c = obj.AddComponent<CapsuleCollider>();
                c.radius = collider;
                c.height = colliderHeight;
                c.center = new Vector3(0, colliderHeight / 2f, 0);
            }
            GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return obj;
        }

        /// <summary>Fogo do braseiro: partículas e, em metade deles, uma luz que tremula.</summary>
        internal static void Fire(Transform parent, Vector3 offset, bool withLight)
        {
            var fire = new GameObject("Fogo");
            fire.transform.SetParent(parent, false);
            fire.transform.localPosition = offset;
            fire.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var particles = fire.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.85f, 0.35f));
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission;
            emission.rateOverTime = 28;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12;
            shape.radius = 0.25f;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0), new GradientColorKey(new Color(0.9f, 0.25f, 0.05f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = fire.GetComponent<ParticleSystemRenderer>();
            var soft = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Visuals/fx/particula_suave.mat");
            renderer.sharedMaterial = soft != null ? soft : TrainingSceneBuilder.MaterialFor("Particula", Color.white, "Universal Render Pipeline/Particles/Unlit");
            if (!withLight) return;
            var light = new GameObject("Luz do fogo").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.transform.localPosition = offset + Vector3.up * 0.6f;
            light.type = LightType.Point;
            light.range = 9f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.62f, 0.3f);
            light.shadows = LightShadows.None;
            light.gameObject.AddComponent<Fx.FlickerLight>();
        }

        // ===== Malhas =====

        private static Mesh[] Variants(string name, int count, System.Func<System.Random, Mesh> make)
        {
            var meshes = new Mesh[count];
            for (var i = 0; i < count; i++)
            {
                var mesh = make(new System.Random(name.GetHashCode() * 31 + i * 7919));
                mesh.name = $"{name}_{i + 1}";
                var path = $"{MeshFolder}/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
                meshes[i] = mesh;
            }
            return meshes;
        }

        private static float Range(System.Random r, float min, float max) => Mathf.Lerp(min, max, (float)r.NextDouble());

        private static Mesh Tree(System.Random r)
        {
            var kit = new MeshKit(2);
            var height = Range(r, 1.9f, 2.6f);
            kit.Frustum(0, Vector3.zero, Quaternion.identity, 0.26f, 0.15f, height + 0.6f, 6, r, 0.04f);
            var blobs = 3 + r.Next(2);
            for (var i = 0; i < blobs; i++)
            {
                var offset = new Vector3(Range(r, -0.9f, 0.9f), height + Range(r, 0.4f, 1.6f), Range(r, -0.9f, 0.9f));
                var size = Range(r, 1.1f, 1.6f);
                kit.Blob(1, offset, new Vector3(size, size * 0.85f, size), r, 0.14f);
            }
            return kit.Build("arvore");
        }

        private static Mesh Pine(System.Random r)
        {
            var kit = new MeshKit(2);
            kit.Frustum(0, Vector3.zero, Quaternion.identity, 0.24f, 0.14f, 1.6f, 6, r, 0.03f);
            var radius = Range(r, 1.6f, 2f);
            var y = 1.1f;
            for (var layer = 0; layer < 4; layer++)
            {
                var h = Range(r, 1.6f, 2f);
                kit.Frustum(1, new Vector3(0, y, 0), Quaternion.Euler(0, layer * 23f, 0), radius, 0f, h, 7, r, 0.2f, bottomCap: true);
                y += h * 0.55f;
                radius *= 0.74f;
            }
            return kit.Build("pinheiro");
        }

        private static Mesh DeadTree(System.Random r)
        {
            var kit = new MeshKit(1);
            var height = Range(r, 2.6f, 3.6f);
            kit.Frustum(0, Vector3.zero, Quaternion.identity, 0.28f, 0.08f, height, 6, r, 0.03f);
            var branches = 3 + r.Next(3);
            for (var i = 0; i < branches; i++)
            {
                var at = new Vector3(0, Range(r, height * 0.4f, height * 0.85f), 0);
                var rotation = Quaternion.Euler(Range(r, 35f, 70f), Range(r, 0, 360), 0);
                kit.Frustum(0, at, Quaternion.Euler(0, rotation.eulerAngles.y, 0) * Quaternion.Euler(rotation.eulerAngles.x, 0, 0),
                    0.09f, 0.02f, Range(r, 0.8f, 1.5f), 5, r, 0.01f);
            }
            return kit.Build("arvore_seca");
        }

        private static Mesh Shrub(System.Random r)
        {
            var kit = new MeshKit(1);
            var blobs = 2 + r.Next(2);
            for (var i = 0; i < blobs; i++)
            {
                var size = Range(r, 0.45f, 0.7f);
                kit.Blob(0, new Vector3(Range(r, -0.5f, 0.5f), size * 0.6f, Range(r, -0.5f, 0.5f)), new Vector3(size, size * 0.75f, size), r, 0.18f);
            }
            return kit.Build("arbusto");
        }

        private static Mesh Rock(System.Random r)
        {
            var kit = new MeshKit(2);
            var size = new Vector3(Range(r, 0.8f, 1.2f), Range(r, 0.55f, 0.85f), Range(r, 0.7f, 1.1f));
            kit.Blob(0, new Vector3(0, size.y * 0.55f, 0), size, r, 0.22f);
            // Musgo por cima, às vezes.
            if (r.NextDouble() < 0.5)
                kit.Blob(1, new Vector3(Range(r, -0.2f, 0.2f), size.y * 1.05f, Range(r, -0.2f, 0.2f)), size * 0.55f + new Vector3(0, -0.25f, 0), r, 0.1f);
            return kit.Build("pedra");
        }

        private static Mesh BrokenColumn(System.Random r)
        {
            var kit = new MeshKit(1);
            kit.Box(0, Vector3.zero, Quaternion.identity, new Vector3(1.5f, 0.35f, 1.5f));
            var height = Range(r, 1.6f, 4.6f);
            kit.Frustum(0, new Vector3(0, 0.35f, 0), Quaternion.identity, 0.55f, 0.5f, height, 9, r, 0.06f, topJitterY: 0.6f);
            if (height > 3.8f) kit.Box(0, new Vector3(0, 0.35f + height - 0.3f, 0), Quaternion.identity, new Vector3(1.4f, 0.3f, 1.4f));
            return kit.Build("coluna");
        }

        private static Mesh RuinWall(System.Random r)
        {
            var kit = new MeshKit(2);
            var length = Range(r, 3.5f, 6f);
            var pieces = 3 + r.Next(3);
            for (var i = 0; i < pieces; i++)
            {
                var x = -length / 2 + (i + 0.5f) * length / pieces;
                kit.Box(0, new Vector3(x, 0, 0), Quaternion.identity, new Vector3(length / pieces + 0.05f, Range(r, 0.8f, 2.8f), 0.8f), r, 0.5f);
            }
            kit.Blob(1, new Vector3(Range(r, -1f, 1f), 0.1f, 0.5f), new Vector3(0.7f, 0.3f, 0.5f), r, 0.2f);
            return kit.Build("muro");
        }

        private static Mesh House(System.Random r)
        {
            var kit = new MeshKit(3);
            var width = Range(r, 5f, 6.5f);
            var depth = Range(r, 4f, 5f);
            var height = 2.8f;
            kit.Box(0, Vector3.zero, Quaternion.identity, new Vector3(width, height, depth));
            kit.Roof(1, new Vector3(0, height, 0), Quaternion.identity, width, depth, 1.8f, 0.4f);
            // Porta e janelas (frente = +Z), vigas nos cantos.
            kit.Box(2, new Vector3(0, 0, depth / 2 + 0.02f), Quaternion.identity, new Vector3(1.1f, 2f, 0.1f));
            kit.Box(2, new Vector3(-width * 0.3f, 1.1f, depth / 2 + 0.02f), Quaternion.identity, new Vector3(0.8f, 0.8f, 0.08f));
            kit.Box(2, new Vector3(width * 0.3f, 1.1f, depth / 2 + 0.02f), Quaternion.identity, new Vector3(0.8f, 0.8f, 0.08f));
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sz in new[] { -1f, 1f })
                kit.Box(2, new Vector3(sx * (width / 2), 0, sz * (depth / 2)), Quaternion.identity, new Vector3(0.25f, height, 0.25f));
            kit.Box(0, new Vector3(width * 0.25f, height + 0.6f, -depth * 0.15f), Quaternion.identity, new Vector3(0.6f, 1.7f, 0.6f));
            return kit.Build("casa");
        }

        private static Mesh Fence(System.Random r)
        {
            var kit = new MeshKit(1);
            foreach (var x in new[] { -1.5f, 1.5f })
                kit.Box(0, new Vector3(x, 0, 0), Quaternion.identity, new Vector3(0.18f, 1.1f, 0.18f));
            kit.Box(0, new Vector3(0, 0.45f, 0), Quaternion.identity, new Vector3(3.2f, 0.12f, 0.08f));
            kit.Box(0, new Vector3(0, 0.85f, 0), Quaternion.identity, new Vector3(3.2f, 0.12f, 0.08f));
            return kit.Build("cerca");
        }

        private static Mesh Well(System.Random r)
        {
            var kit = new MeshKit(3);
            kit.Frustum(0, Vector3.zero, Quaternion.identity, 1.1f, 1.1f, 0.8f, 10, null);
            foreach (var x in new[] { -0.95f, 0.95f })
                kit.Box(1, new Vector3(x, 0.8f, 0), Quaternion.identity, new Vector3(0.15f, 1.5f, 0.15f));
            kit.Roof(2, new Vector3(0, 2.3f, 0), Quaternion.Euler(0, 90, 0), 1.6f, 2.2f, 0.7f, 0.15f);
            return kit.Build("poco");
        }

        private static Mesh Brazier(System.Random r)
        {
            var kit = new MeshKit(2);
            kit.Frustum(1, Vector3.zero, Quaternion.identity, 0.6f, 0.6f, 0.25f, 8, null);
            kit.Frustum(0, new Vector3(0, 0.25f, 0), Quaternion.identity, 0.12f, 0.12f, 0.6f, 6, null);
            kit.Frustum(0, new Vector3(0, 0.85f, 0), Quaternion.identity, 0.25f, 0.6f, 0.3f, 8, null);
            return kit.Build("braseiro");
        }
    }
}
