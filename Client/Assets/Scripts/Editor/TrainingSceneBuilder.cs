using System.IO;
using Game.Client.Combat;
using Game.Client.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Monta a cena de treino do protótipo a partir de primitivas.
    /// No Editor: menu Protótipo &gt; Criar cena de treino.
    /// Em linha de comando: Unity.exe -batchmode -quit -projectPath Client
    ///   -executeMethod Game.Client.Editor.TrainingSceneBuilder.Build
    /// </summary>
    public static class TrainingSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Treino.unity";
        private const string MaterialsFolder = "Assets/Materials/Prototipo";

        [MenuItem("Protótipo/Criar cena de treino")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var ground = MaterialFor("Chao", new Color(0.42f, 0.45f, 0.42f));
            var pillar = MaterialFor("Pilar", new Color(0.55f, 0.52f, 0.48f));
            var player = MaterialFor("Jogador", new Color(0.25f, 0.5f, 0.95f));
            var dummy = MaterialFor("Boneco", new Color(0.85f, 0.35f, 0.3f));
            var ring = MaterialFor("AnelSelecao", new Color(1f, 0.85f, 0.2f));

            CreateLight();
            CreateGround(ground);
            CreatePillar(new Vector3(-7, 0, 3), pillar);
            CreatePillar(new Vector3(7, 0, 5), pillar);

            var playerObject = CreatePlayer(player);
            var cameraObject = CreateCamera(playerObject.transform);
            playerObject.GetComponent<PlayerMotor>().SetCamera(cameraObject.transform);
            playerObject.GetComponent<TargetSelector>().SetCamera(cameraObject.GetComponent<Camera>());

            CreateDummy("Boneco de treino A", new Vector3(0, 0, 6), dummy, ring);
            CreateDummy("Boneco de treino B", new Vector3(-4.5f, 0, 9), dummy, ring);
            CreateDummy("Boneco de treino C", new Vector3(4.5f, 0, 9), dummy, ring);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Protótipo] Cena criada em {ScenePath}");
        }

        private static void CreateLight()
        {
            var light = new GameObject("Luz do sol").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        private static void CreateGround(Material material)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Chão";
            plane.transform.localScale = new Vector3(6, 1, 6);
            plane.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void CreatePillar(Vector3 position, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Pilar";
            cube.transform.position = position + Vector3.up * 2;
            cube.transform.localScale = new Vector3(1.5f, 4, 1.5f);
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static GameObject CreatePlayer(Material material)
        {
            var root = new GameObject("Jogador");
            var controller = root.AddComponent<CharacterController>();
            controller.center = new Vector3(0, 1, 0);
            controller.height = 2;
            controller.radius = 0.4f;

            var body = VisualPrimitive(PrimitiveType.Capsule, "Corpo", root.transform, material);
            body.transform.localPosition = new Vector3(0, 1, 0);
            body.transform.localScale = new Vector3(0.8f, 1, 0.8f);

            // Indica para onde o personagem está virado.
            var nose = VisualPrimitive(PrimitiveType.Cube, "Frente", root.transform, material);
            nose.transform.localPosition = new Vector3(0, 1.5f, 0.42f);
            nose.transform.localScale = new Vector3(0.25f, 0.15f, 0.2f);

            root.AddComponent<PlayerMotor>();
            root.AddComponent<TargetSelector>();
            return root;
        }

        private static GameObject CreateCamera(Transform target)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 55;
            camera.nearClipPlane = 0.1f;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<OrbitCamera>().SetTarget(target);
            cameraObject.transform.position = target.position + new Vector3(0, 4, -7);
            return cameraObject;
        }

        private static void CreateDummy(string name, Vector3 position, Material material, Material ringMaterial)
        {
            var root = new GameObject(name);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0, 180, 0);

            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0, 1, 0);
            collider.height = 2;
            collider.radius = 0.6f;

            var body = VisualPrimitive(PrimitiveType.Capsule, "Corpo", root.transform, material);
            body.transform.localPosition = new Vector3(0, 1, 0);
            body.transform.localScale = new Vector3(1.1f, 1, 1.1f);

            var ring = VisualPrimitive(PrimitiveType.Cylinder, "Anel de seleção", root.transform, ringMaterial);
            ring.transform.localPosition = new Vector3(0, 0.02f, 0);
            ring.transform.localScale = new Vector3(1.8f, 0.01f, 1.8f);

            var targetable = root.AddComponent<Targetable>();
            targetable.Configure(name, 8000, 2.2f, 0.6f, ring);
            root.AddComponent<TrainingDummy>();
        }

        /// <summary>Primitiva só visual (sem colisor), filha de <paramref name="parent"/>.</summary>
        private static GameObject VisualPrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Object.DestroyImmediate(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            return primitive;
        }

        private static Material MaterialFor(string name, Color color)
        {
            Directory.CreateDirectory(MaterialsFolder);
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AddToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
