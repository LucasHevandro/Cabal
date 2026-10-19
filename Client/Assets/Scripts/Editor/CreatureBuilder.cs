using System.Collections.Generic;
using System.IO;
using Game.Client.Fx;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Bichos que os pacotes KayKit não têm (coelho, lobo, javali, aranha e golem de pedra), em low-poly no mesmo estilo,
    /// montados em partes articuladas (corpo, cabeça, patas) e animados por código (<see cref="ProceduralRig"/>).
    /// Grava <c>Resources/Visuals/monster/&lt;id&gt;.prefab</c>. Menu: Jogo &gt; Visuais &gt; Montar bichos.
    /// </summary>
    internal static class CreatureBuilder
    {
        private const string MeshFolder = "Assets/Art/Monstros/Malhas";
        private const string PrefabFolder = "Assets/Resources/Visuals/monster";

        [MenuItem("Jogo/Visuais/Montar bichos")]
        public static void Build()
        {
            Directory.CreateDirectory(MeshFolder);
            Directory.CreateDirectory(PrefabFolder);
            Rabbit();
            Wolf();
            Boar();
            Spider();
            Golem();
            AssetDatabase.SaveAssets();
            // Com os modelos do Quaternius no projeto, o coelho e o lobo animados ficam no lugar dos feitos em código.
            QuaterniusBuilder.Build();
            Debug.Log("[Bichos] coelho, lobo, javali, aranha e golem montados.");
        }

        // ===== Peças =====

        private static Material Mat(string name, Color color) => TrainingSceneBuilder.MaterialFor($"Bicho_{name}", color);

        private static Material Glow(string name, Color color) =>
            TrainingSceneBuilder.MaterialFor($"Bicho_{name}", color * 2f, "Universal Render Pipeline/Unlit");

        private sealed class Creature
        {
            public readonly GameObject Root;
            public readonly Transform Body;
            public readonly List<ProceduralRig.Limb> Limbs = new List<ProceduralRig.Limb>();
            private readonly string _id;
            private int _parts;

            public Creature(string id, float bodyHeight)
            {
                _id = id;
                Root = new GameObject(id);
                Body = new GameObject("Corpo").transform;
                Body.SetParent(Root.transform, false);
                Body.localPosition = new Vector3(0, bodyHeight, 0);
            }

            /// <summary>Uma peça (malha própria) presa em <paramref name="parent"/>, na posição local dada.</summary>
            public Transform Part(Transform parent, Vector3 position, Material material, System.Action<MeshKit> draw, string name = "Peça")
            {
                var kit = new MeshKit(1);
                draw(kit);
                var mesh = kit.Build($"{_id}_{++_parts}");
                var path = $"{MeshFolder}/{mesh.name}.asset";
                AssetDatabase.DeleteAsset(path);
                AssetWrite.Create(mesh, path);
                var obj = new GameObject(name);
                obj.transform.SetParent(parent, false);
                obj.transform.localPosition = position;
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                obj.AddComponent<MeshRenderer>().sharedMaterial = material;
                return obj.transform;
            }

            /// <summary>Junta (sem malha) para girar uma pata ou um braço.</summary>
            public Transform Joint(Transform parent, Vector3 position, string name)
            {
                var joint = new GameObject(name).transform;
                joint.SetParent(parent, false);
                joint.localPosition = position;
                return joint;
            }

            public void Limb(Transform pivot, float phase, float swing, Vector3 axis, float attackSwing = 0f) =>
                Limbs.Add(new ProceduralRig.Limb { Pivot = pivot, Phase = phase, Swing = swing, Axis = axis, AttackSwing = attackSwing });

            public void Save(float stride, bool hop, float bob, float lunge)
            {
                Root.AddComponent<ProceduralRig>().Configure(Body, Limbs.ToArray(), stride, hop, bob, lunge);
                var path = $"{PrefabFolder}/{_id}.prefab";
                AssetDatabase.DeleteAsset(path);
                PrefabUtility.SaveAsPrefabAsset(Root, path);
                Object.DestroyImmediate(Root);
            }
        }

        private static readonly Quaternion Down = Quaternion.Euler(180, 0, 0);
        private static readonly Quaternion Forward = Quaternion.Euler(90, 0, 0);
        private static readonly Quaternion Back = Quaternion.Euler(-90, 0, 0);

        /// <summary>Quatro patas: pivô no quadril/ombro, pata para baixo; diagonais alternadas.</summary>
        private static void Legs(Creature c, Material material, float x, float frontZ, float backZ, float y, float length, float radius, System.Random r)
        {
            var corners = new[] { (x, frontZ, 0f), (-x, frontZ, 0.5f), (x, backZ, 0.5f), (-x, backZ, 0f) };
            foreach (var (lx, lz, phase) in corners)
            {
                var joint = c.Joint(c.Body, new Vector3(lx, y, lz), "Pata");
                c.Part(joint, Vector3.zero, material, k =>
                {
                    k.Frustum(0, Vector3.zero, Down, radius, radius * 0.7f, length, 6, r, 0.01f, bottomCap: true);
                    k.Blob(0, new Vector3(0, -length, 0.02f), new Vector3(radius * 1.1f, radius * 0.7f, radius * 1.4f), r, 0.1f);
                }, "Pata");
                c.Limb(joint, phase, 32f, Vector3.right);
            }
        }

        // ===== Bichos =====

        private static void Rabbit()
        {
            var r = new System.Random(101);
            var fur = Mat("pelo_coelho", new Color(0.93f, 0.9f, 0.84f));
            var pink = Mat("orelha_coelho", new Color(0.95f, 0.7f, 0.72f));
            var dark = Mat("olho", new Color(0.08f, 0.07f, 0.07f));
            var c = new Creature("coelho_saltador", 0.3f);
            c.Part(c.Body, Vector3.zero, fur, k => k.Blob(0, Vector3.zero, new Vector3(0.24f, 0.22f, 0.3f), r, 0.08f), "Tronco");
            var head = c.Joint(c.Body, new Vector3(0, 0.17f, 0.25f), "Cabeça");
            c.Part(head, Vector3.zero, fur, k => k.Blob(0, Vector3.zero, new Vector3(0.16f, 0.15f, 0.16f), r, 0.06f));
            foreach (var side in new[] { -1f, 1f })
            {
                c.Part(head, new Vector3(side * 0.06f, 0.1f, -0.02f), fur,
                    k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-12, 0, side * -10), 0.045f, 0.03f, 0.26f, 5, r, 0.005f), "Orelha");
                c.Part(head, new Vector3(side * 0.06f, 0.12f, 0.005f), pink,
                    k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-12, 0, side * -10), 0.025f, 0.015f, 0.2f, 4, r, 0f), "Orelha (dentro)");
                c.Part(head, new Vector3(side * 0.08f, 0.03f, 0.12f), dark, k => k.Blob(0, Vector3.zero, Vector3.one * 0.025f, r, 0.05f), "Olho");
            }
            c.Part(c.Body, new Vector3(0, 0.05f, -0.3f), fur, k => k.Blob(0, Vector3.zero, Vector3.one * 0.08f, r, 0.1f), "Rabo");
            Legs(c, fur, 0.12f, 0.16f, -0.14f, -0.12f, 0.16f, 0.045f, r);
            c.Save(0.6f, true, 0.03f, 0.25f);
        }

        private static void Wolf()
        {
            var r = new System.Random(202);
            var fur = Mat("pelo_lobo", new Color(0.52f, 0.53f, 0.57f));
            var back = Mat("pelo_lobo_escuro", new Color(0.33f, 0.34f, 0.38f));
            var light = Mat("pelo_lobo_claro", new Color(0.78f, 0.77f, 0.74f));
            var eyes = Glow("olho_lobo", new Color(1f, 0.8f, 0.25f));
            var c = new Creature("lobo_cinzento", 0.72f);
            c.Part(c.Body, Vector3.zero, fur, k => k.Blob(0, Vector3.zero, new Vector3(0.32f, 0.3f, 0.62f), r, 0.08f), "Tronco");
            c.Part(c.Body, new Vector3(0, 0.15f, -0.05f), back, k => k.Blob(0, Vector3.zero, new Vector3(0.26f, 0.12f, 0.55f), r, 0.1f), "Dorso");
            c.Part(c.Body, new Vector3(0, -0.06f, 0.35f), light, k => k.Blob(0, Vector3.zero, new Vector3(0.24f, 0.22f, 0.25f), r, 0.1f), "Peito");
            var head = c.Joint(c.Body, new Vector3(0, 0.18f, 0.62f), "Cabeça");
            c.Part(head, Vector3.zero, fur, k =>
            {
                k.Blob(0, Vector3.zero, new Vector3(0.2f, 0.19f, 0.22f), r, 0.06f);
                k.Frustum(0, new Vector3(0, -0.04f, 0.12f), Forward, 0.1f, 0.05f, 0.24f, 6, r, 0.01f, bottomCap: true);
            });
            foreach (var side in new[] { -1f, 1f })
            {
                c.Part(head, new Vector3(side * 0.1f, 0.14f, -0.04f), back, k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-8, 0, side * -12), 0.06f, 0f, 0.17f, 4, r, 0f, bottomCap: true), "Orelha");
                c.Part(head, new Vector3(side * 0.08f, 0.04f, 0.15f), eyes, k => k.Blob(0, Vector3.zero, Vector3.one * 0.025f, r, 0.05f), "Olho");
            }
            var tail = c.Joint(c.Body, new Vector3(0, 0.12f, -0.6f), "Rabo");
            c.Part(tail, Vector3.zero, back, k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-130, 0, 0), 0.08f, 0.03f, 0.45f, 5, r, 0.02f));
            c.Limb(tail, 0.25f, 14f, Vector3.up);
            c.Limb(head, 0.5f, 4f, Vector3.right, 25f);
            Legs(c, fur, 0.16f, 0.4f, -0.4f, -0.12f, 0.6f, 0.07f, r);
            c.Save(1.5f, false, 0.05f, 0.5f);
        }

        private static void Boar()
        {
            var r = new System.Random(303);
            var hide = Mat("couro_javali", new Color(0.4f, 0.27f, 0.17f));
            var bristle = Mat("pelo_javali", new Color(0.22f, 0.15f, 0.1f));
            var tusk = Mat("presa", new Color(0.95f, 0.92f, 0.82f));
            var snout = Mat("focinho", new Color(0.72f, 0.5f, 0.45f));
            var eyes = Glow("olho_javali", new Color(1f, 0.3f, 0.15f));
            var c = new Creature("javali_furioso", 0.62f);
            c.Part(c.Body, Vector3.zero, hide, k => k.Blob(0, Vector3.zero, new Vector3(0.45f, 0.42f, 0.72f), r, 0.1f), "Tronco");
            for (var i = 0; i < 5; i++)
                c.Part(c.Body, new Vector3(0, 0.36f - i * 0.03f, 0.35f - i * 0.18f), bristle,
                    k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-20, 0, 0), 0.09f, 0f, 0.2f, 4, r, 0f, bottomCap: true), "Crina");
            var head = c.Joint(c.Body, new Vector3(0, 0.02f, 0.65f), "Cabeça");
            c.Part(head, Vector3.zero, hide, k => k.Blob(0, Vector3.zero, new Vector3(0.28f, 0.26f, 0.3f), r, 0.08f));
            c.Part(head, new Vector3(0, -0.06f, 0.28f), snout, k => k.Frustum(0, Vector3.zero, Forward, 0.13f, 0.12f, 0.14f, 7, r, 0f));
            foreach (var side in new[] { -1f, 1f })
            {
                c.Part(head, new Vector3(side * 0.12f, -0.12f, 0.3f), tusk, k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-35, side * 15, side * -20), 0.035f, 0f, 0.2f, 4, r, 0f, bottomCap: true), "Presa");
                c.Part(head, new Vector3(side * 0.14f, 0.08f, 0.2f), eyes, k => k.Blob(0, Vector3.zero, Vector3.one * 0.03f, r, 0.05f), "Olho");
                c.Part(head, new Vector3(side * 0.16f, 0.2f, 0f), hide, k => k.Frustum(0, Vector3.zero, Quaternion.Euler(-30, 0, side * -25), 0.07f, 0f, 0.14f, 4, r, 0f, bottomCap: true), "Orelha");
            }
            c.Limb(head, 0.5f, 3f, Vector3.right, 22f);
            Legs(c, hide, 0.24f, 0.42f, -0.42f, -0.2f, 0.4f, 0.09f, r);
            c.Save(1.3f, false, 0.06f, 0.6f);
        }

        private static void Spider()
        {
            var r = new System.Random(404);
            var shell = Mat("carapaca", new Color(0.18f, 0.26f, 0.12f));
            var stripes = Mat("listra_aranha", new Color(0.55f, 0.6f, 0.2f));
            var leg = Mat("pata_aranha", new Color(0.12f, 0.15f, 0.08f));
            var eyes = Glow("olho_aranha", new Color(1f, 0.2f, 0.15f));
            var c = new Creature("aranha_da_mata", 0.5f);
            c.Part(c.Body, new Vector3(0, 0, 0.22f), shell, k => k.Blob(0, Vector3.zero, new Vector3(0.32f, 0.22f, 0.32f), r, 0.08f), "Cefalotórax");
            c.Part(c.Body, new Vector3(0, 0.12f, -0.38f), shell, k => k.Blob(0, Vector3.zero, new Vector3(0.48f, 0.4f, 0.55f), r, 0.1f), "Abdômen");
            c.Part(c.Body, new Vector3(0, 0.42f, -0.4f), stripes, k => k.Blob(0, Vector3.zero, new Vector3(0.22f, 0.08f, 0.35f), r, 0.12f), "Desenho");
            for (var i = 0; i < 4; i++)
                c.Part(c.Body, new Vector3((i % 2 == 0 ? -1 : 1) * (0.06f + (i / 2) * 0.07f), 0.1f, 0.52f), eyes,
                    k => k.Blob(0, Vector3.zero, Vector3.one * 0.03f, r, 0.05f), "Olho");
            c.Part(c.Body, new Vector3(0, -0.08f, 0.52f), leg, k =>
            {
                foreach (var side in new[] { -1f, 1f })
                    k.Frustum(0, new Vector3(side * 0.06f, 0, 0), Quaternion.Euler(130, 0, side * 10), 0.04f, 0f, 0.16f, 4, r, 0f, bottomCap: true);
            }, "Quelíceras");
            // Oito patas: cada uma sai para o lado, sobe e desce até o chão; varrem para a frente e para trás.
            for (var i = 0; i < 8; i++)
            {
                var side = i < 4 ? 1f : -1f;
                var row = i % 4;
                var z = 0.38f - row * 0.15f;
                var joint = c.Joint(c.Body, new Vector3(side * 0.22f, 0, z), "Pata");
                joint.localRotation = Quaternion.Euler(0, side * (row * 22f - 35f), 0);
                c.Part(joint, Vector3.zero, leg, k =>
                {
                    k.Frustum(0, Vector3.zero, Quaternion.Euler(0, 0, side * -55), 0.045f, 0.035f, 0.42f, 5, r, 0f, bottomCap: true);
                    var knee = new Vector3(side * 0.42f * Mathf.Sin(55 * Mathf.Deg2Rad), 0.42f * Mathf.Cos(55 * Mathf.Deg2Rad), 0);
                    k.Frustum(0, knee, Quaternion.Euler(0, 0, side * -150), 0.035f, 0.01f, 0.62f, 5, r, 0f, bottomCap: true);
                }, "Pata");
                c.Limb(joint, (row % 2 == 0) == (side > 0) ? 0f : 0.5f, 20f, Vector3.up);
            }
            c.Save(0.9f, false, 0.03f, 0.45f);
        }

        private static void Golem()
        {
            var r = new System.Random(505);
            var stone = Mat("pedra_golem", new Color(0.52f, 0.51f, 0.53f));
            var dark = Mat("pedra_golem_escura", new Color(0.36f, 0.35f, 0.38f));
            var moss = Mat("musgo_golem", new Color(0.3f, 0.45f, 0.22f));
            var core = Glow("nucleo_golem", new Color(0.35f, 0.9f, 1f));
            var c = new Creature("golem_de_pedra", 1.35f);
            c.Part(c.Body, new Vector3(0, 0.25f, 0), stone, k => k.Blob(0, Vector3.zero, new Vector3(0.75f, 0.62f, 0.55f), r, 0.16f), "Tronco");
            c.Part(c.Body, new Vector3(0, 0.75f, -0.1f), moss, k => k.Blob(0, Vector3.zero, new Vector3(0.55f, 0.18f, 0.4f), r, 0.2f), "Musgo");
            c.Part(c.Body, new Vector3(0, 0.3f, 0.48f), core, k => k.Blob(0, Vector3.zero, new Vector3(0.14f, 0.14f, 0.08f), r, 0.1f), "Núcleo");
            var head = c.Joint(c.Body, new Vector3(0, 0.95f, 0.15f), "Cabeça");
            c.Part(head, Vector3.zero, dark, k => k.Blob(0, Vector3.zero, new Vector3(0.28f, 0.24f, 0.26f), r, 0.15f));
            foreach (var side in new[] { -1f, 1f })
                c.Part(head, new Vector3(side * 0.1f, 0.02f, 0.22f), core, k => k.Blob(0, Vector3.zero, Vector3.one * 0.05f, r, 0.05f), "Olho");
            c.Limb(head, 0.5f, 3f, Vector3.right, 10f);
            foreach (var side in new[] { -1f, 1f })
            {
                // Braços pesados: balançam ao andar e sobem e descem no golpe.
                var shoulder = c.Joint(c.Body, new Vector3(side * 0.85f, 0.55f, 0), "Braço");
                c.Part(shoulder, Vector3.zero, stone, k =>
                {
                    k.Blob(0, new Vector3(0, -0.15f, 0), new Vector3(0.3f, 0.32f, 0.3f), r, 0.15f);
                    k.Blob(0, new Vector3(0, -0.65f, 0.05f), new Vector3(0.26f, 0.32f, 0.26f), r, 0.15f);
                    k.Blob(0, new Vector3(0, -1.05f, 0.1f), new Vector3(0.34f, 0.28f, 0.34f), r, 0.18f);
                }, "Braço");
                c.Limb(shoulder, side > 0 ? 0f : 0.5f, 22f, Vector3.right, -70f);
                var hip = c.Joint(c.Body, new Vector3(side * 0.38f, -0.35f, 0), "Perna");
                c.Part(hip, Vector3.zero, dark, k =>
                {
                    k.Blob(0, new Vector3(0, -0.3f, 0), new Vector3(0.26f, 0.32f, 0.26f), r, 0.15f);
                    k.Blob(0, new Vector3(0, -0.85f, 0.06f), new Vector3(0.3f, 0.2f, 0.36f), r, 0.12f);
                }, "Perna");
                c.Limb(hip, side > 0 ? 0.5f : 0f, 24f, Vector3.right);
            }
            c.Save(1.4f, false, 0.08f, 0.3f);
        }
    }
}
