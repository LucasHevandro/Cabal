using System.IO;
using Game.Client.Fx;
using Game.Client.World;
using Game.Shared.Progression;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Vida na vila e no mundo: o rio (água que corre) com a ponte de madeira na estrada, o canto de cada NPC (forja e
    /// bigorna da ferreira, barraca da mercadora, caixotes e barris do guarda-volumes, estante do ancião, arco de pedra
    /// do portal da Cripta), postes com lanterna, e os bonecos de palha do treino. Chamado pelo WorldBuilder.
    /// </summary>
    internal static class VillageBuilder
    {
        private const string MeshFolder = TerrainBuilder.Folder + "/Malhas";
        private static int _parts;

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Vila_{name}", color);

        private static Material Glow(string name, Color color) =>
            TrainingSceneBuilder.MaterialFor($"Vila_{name}", color * 1.6f, "Universal Render Pipeline/Unlit");

        public static void Build(Transform world)
        {
            Directory.CreateDirectory(MeshFolder);
            var root = new GameObject("Vila e rio").transform;
            root.SetParent(world, false);
            River(root);
            Bridge(root);
            NpcCorners(root);
            Lamps(root);
        }

        private static GameObject Part(Transform parent, string name, Material material, System.Action<MeshKit> draw, bool collider = false)
        {
            var kit = new MeshKit(1);
            draw(kit);
            var mesh = kit.Build($"vila_{name}_{++_parts}");
            var path = $"{MeshFolder}/{mesh.name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            obj.AddComponent<MeshRenderer>().sharedMaterial = material;
            if (collider)
            {
                var box = obj.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }
            GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic);
            return obj;
        }

        private static Transform Group(Transform parent, string name, Vector3 position, Quaternion rotation)
        {
            var group = new GameObject(name).transform;
            group.SetParent(parent, false);
            group.position = Ground.Point(position.x, position.z);
            group.rotation = rotation;
            return group;
        }

        // ===== Rio =====

        /// <summary>Faixa de água seguindo a curva do rio, de uma ponta à outra da área jogável, com textura que corre.</summary>
        private static void River(Transform parent)
        {
            var mesh = new Mesh { name = "rio_agua" };
            var vertices = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var triangles = new System.Collections.Generic.List<int>();
            const float halfWidth = 4.6f;
            var segments = 0;
            for (var x = -128f; x <= 128f; x += 2f)
            {
                var z = Ground.RiverZ(x);
                vertices.Add(new Vector3(x, Ground.WaterLevel, z - halfWidth));
                vertices.Add(new Vector3(x, Ground.WaterLevel, z + halfWidth));
                uvs.Add(new Vector2(x / 8f, 0));
                uvs.Add(new Vector2(x / 8f, 1));
                if (segments > 0)
                {
                    var i = vertices.Count - 4;
                    triangles.AddRange(new[] { i, i + 1, i + 2, i + 1, i + 3, i + 2 });
                }
                segments++;
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var path = $"{MeshFolder}/rio_agua.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);

            var water = TrainingSceneBuilder.MaterialFor("Agua", new Color(0.25f, 0.5f, 0.62f, 0.72f), "Universal Render Pipeline/Unlit");
            water.SetFloat("_Surface", 1f);
            water.SetFloat("_Blend", 0f);
            water.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            water.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            water.SetFloat("_ZWrite", 0f);
            water.SetOverrideTag("RenderType", "Transparent");
            water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            water.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            water.SetTexture("_BaseMap", TerrainBuilder.GroundTexture("agua", new Color(0.55f, 0.75f, 0.85f), new Color(0.95f, 1f, 1f), 0.04f, 31));
            EditorUtility.SetDirty(water);

            var river = new GameObject("Rio");
            river.transform.SetParent(parent, false);
            river.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = river.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = water;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            river.AddComponent<FlowingWater>();
        }

        /// <summary>Ponte de madeira em arco onde a estrada cruza o rio.</summary>
        private static void Bridge(Transform parent)
        {
            var z = Ground.RiverZ(0);
            var bridge = Group(parent, "Ponte", new Vector3(0, 0, z), Quaternion.identity);
            var wood = Mat("madeira_ponte", new Color(0.48f, 0.33f, 0.2f));
            var dark = Mat("madeira_escura", new Color(0.3f, 0.2f, 0.13f));
            Part(bridge, "Tábuas", wood, k =>
            {
                for (var i = -9; i <= 9; i++)
                {
                    var t = i / 9f;
                    k.Box(0, new Vector3(0, 0.25f * (1f - t * t) - 0.05f, i * 0.55f), Quaternion.identity, new Vector3(4.6f, 0.12f, 0.5f));
                }
            });
            Part(bridge, "Corrimão", dark, k =>
            {
                foreach (var side in new[] { -1f, 1f })
                {
                    for (var i = -2; i <= 2; i++)
                        k.Box(0, new Vector3(side * 2.35f, -0.1f, i * 2.4f), Quaternion.identity, new Vector3(0.16f, 1.25f, 0.16f));
                    k.Box(0, new Vector3(side * 2.35f, 0.95f, 0), Quaternion.identity, new Vector3(0.12f, 0.1f, 10f));
                }
            });
        }

        // ===== Cantos dos NPCs =====

        private static void NpcCorners(Transform parent)
        {
            var wood = Mat("madeira", new Color(0.5f, 0.35f, 0.22f));
            var darkWood = Mat("madeira_escura", new Color(0.3f, 0.2f, 0.13f));
            var iron = Mat("ferro", new Color(0.25f, 0.25f, 0.27f));
            var stone = Mat("pedra", new Color(0.55f, 0.53f, 0.5f));
            var clothRed = Mat("pano_vermelho", new Color(0.7f, 0.22f, 0.2f));
            var clothBlue = Mat("pano_azul", new Color(0.25f, 0.4f, 0.65f));
            var ember = Glow("brasa", new Color(1f, 0.45f, 0.1f));
            var portalGlow = Glow("portal", new Color(0.55f, 0.35f, 1f));
            var books = Mat("livros", new Color(0.55f, 0.25f, 0.3f));
            var r = new System.Random(7);

            foreach (var npc in NpcCatalog.All)
            {
                // O canto fica atrás do NPC (mais longe do centro da praça), virado para a praça.
                var at = new Vector3(npc.X, 0, npc.Z);
                var outward = at.sqrMagnitude > 0.01f ? at.normalized : Vector3.forward;
                var spot = at + outward * 2.2f;
                // NPC na saída da estrada (o ancião): o canto vai para o lado, para não fechar o caminho.
                if (Ground.DistanceToRoad(spot.x, spot.z) < 3.5f)
                {
                    outward = new Vector3(outward.z, 0, -outward.x);
                    spot = at + outward * 3f;
                }
                var facing = Quaternion.LookRotation(-outward);
                var corner = Group(parent, $"Canto: {npc.Id}", spot, facing);
                switch (npc.Role)
                {
                    case NpcRole.Smith:
                        Part(corner, "Forja", stone, k => k.Box(0, new Vector3(0.9f, 0, 0.3f), Quaternion.identity, new Vector3(1.4f, 1f, 1.1f)), collider: true);
                        Part(corner, "Brasas", ember, k => k.Box(0, new Vector3(0.9f, 1f, 0.3f), Quaternion.identity, new Vector3(1f, 0.08f, 0.7f)));
                        Part(corner, "Bigorna", iron, k =>
                        {
                            k.Box(0, new Vector3(-0.8f, 0, -0.2f), Quaternion.identity, new Vector3(0.35f, 0.5f, 0.35f));
                            k.Box(0, new Vector3(-0.8f, 0.5f, -0.2f), Quaternion.identity, new Vector3(0.8f, 0.25f, 0.4f));
                        }, collider: true);
                        PropBuilder.Fire(corner, new Vector3(0.9f, 1.05f, 0.3f), withLight: true);
                        break;
                    case NpcRole.Merchant:
                        Part(corner, "Balcão", wood, k => k.Box(0, new Vector3(0, 0, 0.2f), Quaternion.identity, new Vector3(2.4f, 0.9f, 0.8f)), collider: true);
                        Part(corner, "Postes", darkWood, k =>
                        {
                            foreach (var x in new[] { -1.15f, 1.15f })
                            foreach (var z in new[] { -0.15f, 0.9f })
                                k.Box(0, new Vector3(x, 0, z), Quaternion.identity, new Vector3(0.1f, 2.3f, 0.1f));
                        });
                        Part(corner, "Toldo", clothRed, k => k.Roof(0, new Vector3(0, 2.3f, 0.38f), Quaternion.Euler(0, 90, 0), 1.3f, 2.6f, 0.45f, 0.15f));
                        Part(corner, "Mercadorias", books, k =>
                        {
                            for (var i = 0; i < 5; i++)
                                k.Blob(0, new Vector3(-0.9f + i * 0.45f, 1f, 0.15f), Vector3.one * 0.13f, r, 0.15f);
                        });
                        break;
                    case NpcRole.Warehouse:
                        Part(corner, "Caixotes", wood, k =>
                        {
                            k.Box(0, new Vector3(-0.7f, 0, 0), Quaternion.Euler(0, 10, 0), new Vector3(0.8f, 0.8f, 0.8f));
                            k.Box(0, new Vector3(0.2f, 0, 0.1f), Quaternion.Euler(0, -15, 0), new Vector3(0.75f, 0.75f, 0.75f));
                            k.Box(0, new Vector3(-0.6f, 0.8f, 0.05f), Quaternion.Euler(0, 30, 0), new Vector3(0.6f, 0.6f, 0.6f));
                        }, collider: true);
                        Part(corner, "Barris", darkWood, k =>
                        {
                            k.Frustum(0, new Vector3(1.1f, 0, -0.1f), Quaternion.identity, 0.38f, 0.38f, 0.95f, 8, null);
                            k.Frustum(0, new Vector3(1.0f, 0, 0.75f), Quaternion.identity, 0.36f, 0.36f, 0.9f, 8, null);
                        }, collider: true);
                        break;
                    case NpcRole.QuestGiver:
                        Part(corner, "Estante", darkWood, k =>
                        {
                            k.Box(0, new Vector3(0, 0, 0.3f), Quaternion.identity, new Vector3(1.8f, 2.2f, 0.45f));
                        }, collider: true);
                        Part(corner, "Livros", books, k =>
                        {
                            for (var shelf = 0; shelf < 3; shelf++)
                                for (var i = 0; i < 6; i++)
                                    k.Box(0, new Vector3(-0.7f + i * 0.27f, 0.25f + shelf * 0.65f, 0.05f), Quaternion.identity, new Vector3(0.18f, 0.42f, 0.12f));
                        });
                        Part(corner, "Tapete", clothBlue, k => k.Box(0, new Vector3(0, 0.01f, -1.6f), Quaternion.identity, new Vector3(2.2f, 0.02f, 1.6f)));
                        break;
                    case NpcRole.DungeonPortal:
                        // Arco de pedra com o portal brilhando (a entrada da Cripta).
                        Part(corner, "Arco", stone, k =>
                        {
                            foreach (var x in new[] { -1.3f, 1.3f })
                                k.Box(0, new Vector3(x, 0, 0.3f), Quaternion.identity, new Vector3(0.6f, 3.2f, 0.7f));
                            k.Box(0, new Vector3(0, 3.2f, 0.3f), Quaternion.identity, new Vector3(3.4f, 0.6f, 0.8f));
                            k.Box(0, new Vector3(0, 0, 0.3f), Quaternion.identity, new Vector3(3.6f, 0.2f, 1.2f));
                        }, collider: true);
                        Part(corner, "Portal", portalGlow, k => k.Box(0, new Vector3(0, 0.2f, 0.3f), Quaternion.identity, new Vector3(2f, 3f, 0.08f)));
                        var light = new GameObject("Luz do portal").AddComponent<Light>();
                        light.transform.SetParent(corner, false);
                        light.transform.localPosition = new Vector3(0, 1.6f, -0.4f);
                        light.type = LightType.Point;
                        light.color = new Color(0.6f, 0.4f, 1f);
                        light.range = 6f;
                        light.intensity = 1.6f;
                        light.shadows = LightShadows.None;
                        break;
                    case NpcRole.Trainer:
                        // Mestre de skills: armeiro com espadas e cajados, e um boneco de palha para as aulas.
                        Part(corner, "Armeiro", darkWood, k =>
                        {
                            foreach (var x in new[] { -1f, 1f })
                                k.Box(0, new Vector3(x, 0, 0.3f), Quaternion.identity, new Vector3(0.12f, 1.6f, 0.12f));
                            k.Box(0, new Vector3(0, 1.35f, 0.3f), Quaternion.identity, new Vector3(2.2f, 0.1f, 0.14f));
                            k.Box(0, new Vector3(0, 0.25f, 0.3f), Quaternion.identity, new Vector3(2.2f, 0.1f, 0.3f));
                        }, collider: true);
                        Part(corner, "Armas", iron, k =>
                        {
                            for (var i = 0; i < 4; i++)
                                k.Box(0, new Vector3(-0.66f + i * 0.44f, 0.3f, 0.36f), Quaternion.Euler(-8f, 0, 0), new Vector3(0.07f, 1.25f, 0.03f));
                        });
                        var dummy = Group(corner, "Boneco do mestre", new Vector3(1.9f, 0, -0.4f), Quaternion.Euler(0, 200f, 0));
                        StrawDummy(dummy);
                        break;
                }
            }
        }

        /// <summary>Postes com lanterna em volta da praça (acendem a vila à noite).</summary>
        private static void Lamps(Transform parent)
        {
            var post = Mat("poste", new Color(0.22f, 0.2f, 0.2f));
            var lantern = Glow("lanterna", new Color(1f, 0.8f, 0.45f));
            for (var i = 0; i < 6; i++)
            {
                var angle = (i * 60f + 30f) * Mathf.Deg2Rad;
                var position = new Vector3(Mathf.Cos(angle) * 12.5f, 0, Mathf.Sin(angle) * 12.5f);
                if (Ground.DistanceToRoad(position.x, position.z) < 2.5f) continue;
                var lamp = Group(parent, "Poste", position, Quaternion.identity);
                Part(lamp, "Poste", post, k =>
                {
                    k.Frustum(0, Vector3.zero, Quaternion.identity, 0.09f, 0.07f, 3f, 6, null);
                    k.Box(0, new Vector3(0.3f, 2.9f, 0), Quaternion.identity, new Vector3(0.65f, 0.06f, 0.06f));
                }, collider: true);
                Part(lamp, "Lanterna", lantern, k => k.Box(0, new Vector3(0.55f, 2.5f, 0), Quaternion.identity, new Vector3(0.22f, 0.32f, 0.22f)));
                var light = new GameObject("Luz da lanterna").AddComponent<Light>();
                light.transform.SetParent(lamp, false);
                light.transform.localPosition = new Vector3(0.55f, 2.4f, 0);
                light.type = LightType.Point;
                light.color = new Color(1f, 0.75f, 0.45f);
                light.range = 8f;
                light.intensity = 1.2f;
                light.shadows = LightShadows.None;
                light.gameObject.AddComponent<NightLight>();
            }
        }

        // ===== Boneco de treino =====

        /// <summary>Boneco de palha: poste, corpo de palha amarrado, cabeça de saco e os braços de madeira.</summary>
        public static void StrawDummy(Transform parent)
        {
            var straw = Mat("palha", new Color(0.85f, 0.72f, 0.38f));
            var sack = Mat("saco", new Color(0.78f, 0.68f, 0.52f));
            var wood = Mat("madeira", new Color(0.5f, 0.35f, 0.22f));
            var rope = Mat("corda", new Color(0.45f, 0.32f, 0.2f));
            var r = new System.Random(parent.name.GetHashCode());
            Part(parent, "Poste", wood, k =>
            {
                k.Frustum(0, Vector3.zero, Quaternion.identity, 0.09f, 0.08f, 1.9f, 6, null);
                k.Box(0, new Vector3(0, 1.35f, 0), Quaternion.identity, new Vector3(1.5f, 0.1f, 0.1f));
            });
            Part(parent, "Corpo", straw, k => k.Blob(0, new Vector3(0, 1.15f, 0), new Vector3(0.36f, 0.48f, 0.3f), r, 0.12f));
            Part(parent, "Cabeça", sack, k => k.Blob(0, new Vector3(0, 1.82f, 0), new Vector3(0.24f, 0.24f, 0.22f), r, 0.08f));
            Part(parent, "Cordas", rope, k =>
            {
                k.Frustum(0, new Vector3(0, 0.95f, 0), Quaternion.identity, 0.34f, 0.34f, 0.06f, 8, null);
                k.Frustum(0, new Vector3(0, 1.4f, 0), Quaternion.identity, 0.33f, 0.33f, 0.06f, 8, null);
            });
        }
    }
}
