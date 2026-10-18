using System.IO;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.UI;
using Game.Client.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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

            var player = MaterialFor("Jogador", new Color(0.25f, 0.5f, 0.95f));
            var dummy = MaterialFor("Boneco", new Color(0.85f, 0.35f, 0.3f));
            var ring = MaterialFor("AnelSelecao", new Color(1f, 0.85f, 0.2f));

            var sun = CreateLight();
            var telegraph = MaterialFor("AvisoGolpe", new Color(0.9f, 0.15f, 0.1f), "Universal Render Pipeline/Unlit");
            WorldBuilder.Build(ring, telegraph);

            var playerObject = CreatePlayer(player);
            var cameraObject = CreateCamera(playerObject.transform);
            CreateAtmosphere(sun);
            playerObject.GetComponent<PlayerMotor>().SetCamera(cameraObject.transform);
            playerObject.GetComponent<TargetSelector>().SetCamera(cameraObject.GetComponent<Camera>());

            CreateDummy("Boneco de treino A", "dummy.a", new Vector3(0, 0, 6), dummy, ring);
            CreateDummy("Boneco de treino B", "dummy.b", new Vector3(-4.5f, 0, 9), dummy, ring);
            CreateDummy("Boneco de treino C", "dummy.c", new Vector3(4.5f, 0, 9), dummy, ring);

            var effects = new GameObject("Efeitos");
            effects.AddComponent<CombatFx>().SetMaterials(
                MaterialFor("Particula", Color.white, "Universal Render Pipeline/Particles/Unlit"),
                MaterialFor("Projetil", new Color(0.45f, 0.65f, 1f), "Universal Render Pipeline/Unlit"));
            effects.AddComponent<SoundPlayer>();

            var network = new GameObject("Rede");
            network.AddComponent<NetworkSession>().SetMaterials(MaterialFor("BaseLit", Color.white), telegraph, ring);
            network.AddComponent<ConnectMenu>();
            network.AddComponent<OnlineCombatBridge>().SetWarningMaterial(telegraph);

            var hud = new GameObject("HUD");
            hud.AddComponent<CombatHud>().SetCombat(playerObject.GetComponent<PlayerCombat>());
            hud.AddComponent<DamageNumbers>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Protótipo] Cena criada em {ScenePath}");
        }

        private static Light CreateLight()
        {
            var light = new GameObject("Luz do sol").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            // Sol mais baixo que o meio-dia: sombras longas dão volume ao relevo.
            light.transform.rotation = Quaternion.Euler(38, -35, 0);
            return light;
        }

        /// <summary>
        /// Céu (procedural), névoa e luz ambiente por zona (<see cref="Atmosphere"/>) e o pós-processamento global:
        /// bloom suave, tonemapping, contraste e vinheta leves.
        /// </summary>
        private static void CreateAtmosphere(Light sun)
        {
            var sky = MaterialFor("Ceu", Color.white, "Skybox/Procedural");
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 6f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetColor("_SkyTint", new Color(0.48f, 0.56f, 0.7f));
            sky.SetColor("_GroundColor", new Color(0.42f, 0.44f, 0.4f));
            sky.SetFloat("_Exposure", 1.25f);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

            var profilePath = $"{MaterialsFolder}/PosProcessamento.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.45f);
            bloom.scatter.Override(0.65f);
            var tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.contrast.Override(14f);
            color.saturation.Override(8f);
            color.postExposure.Override(0f);
            color.colorFilter.Override(Color.white);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);
            var lift = profile.Add<LiftGammaGain>(true);
            lift.lift.Override(new Vector4(1f, 1f, 1.04f, 0f)); // sombras levemente azuladas
            lift.gain.Override(new Vector4(1.03f, 1f, 0.97f, 0f)); // luzes levemente quentes
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);

            var atmosphere = new GameObject("Atmosfera");
            var volume = atmosphere.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1;
            volume.sharedProfile = profile;
            atmosphere.AddComponent<Atmosphere>().Configure(sun, volume);

            // A cena já abre com o clima da vila (sem esperar o primeiro quadro).
            var look = Atmosphere.At(Vector3.zero);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = look.Fog;
            RenderSettings.fogDensity = look.FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.Sky;
            RenderSettings.ambientEquatorColor = look.Equator;
            RenderSettings.ambientGroundColor = look.GroundLight;
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
            root.AddComponent<PlayerProgression>();
            root.AddComponent<PlayerCombat>();
            root.AddComponent<PlayerLoot>().SetLootMaterial(MaterialFor("Item", Color.white, "Universal Render Pipeline/Unlit"));
            return root;
        }

        private static GameObject CreateCamera(Transform target)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 55;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 700f;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<OrbitCamera>().SetTarget(target);
            cameraObject.transform.position = target.position + new Vector3(0, 4, -7);
            return cameraObject;
        }

        private static void CreateDummy(string name, string nameKey, Vector3 position, Material material, Material ringMaterial)
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
            targetable.Configure(nameKey, 8000, 2.2f, 0.6f, ring);
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

        internal static Material MaterialFor(string name, Color color, string shaderName = "Universal Render Pipeline/Lit")
        {
            Directory.CreateDirectory(MaterialsFolder);
            var path = $"{MaterialsFolder}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader não encontrado: {shaderName}");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            // Fosco: brilho de plástico some (o padrão do URP é 0,5).
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            material.enableInstancing = true;
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
