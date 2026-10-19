using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Client.Editor
{
    /// <summary>
    /// Acabamento único do jogo: o contorno de desenho animado (<c>Shaders/Contorno.shader</c>) em tela cheia no
    /// renderizador do PC, antes do pós-processamento. Junta no mesmo visual os pacotes (KayKit, Quaternius) e as peças
    /// feitas em código. Usa a profundidade e as normais que a oclusão de ambiente (SSAO) já calcula.
    /// </summary>
    internal static class RenderLook
    {
        private const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        private const string MaterialPath = "Assets/Materials/Prototipo/Contorno.mat";
        private const string FeatureName = "Contorno";

        [MenuItem("Jogo/Visuais/Ligar o contorno")]
        public static void Setup()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var shader = Shader.Find("Jogo/Contorno");
            if (data == null || shader == null)
            {
                Debug.LogWarning("[Contorno] Renderizador do PC ou shader não encontrado.");
                return;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = FeatureName };
                AssetWrite.Create(material, MaterialPath);
            }
            material.shader = shader;
            // Traço leve: a grama é cheia de folhinhas, e com traço forte os campos ficam escuros e riscados.
            material.SetFloat("_Strength", 0.5f);
            material.SetFloat("_Thickness", 1.1f);
            material.SetFloat("_DepthThreshold", 0.1f);
            material.SetFloat("_NormalThreshold", 0.6f);
            material.SetFloat("_MaxDistance", 45f);
            EditorUtility.SetDirty(material);

            var feature = data.rendererFeatures.OfType<FullScreenPassRendererFeature>().FirstOrDefault(f => f.name == FeatureName);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = FeatureName;
                AssetDatabase.AddObjectToAsset(feature, data);
                // Como o inspetor do URP faz: a lista de etapas e o mapa com o id local de cada uma.
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
                var serialized = new SerializedObject(data);
                var list = serialized.FindProperty("m_RendererFeatures");
                var map = serialized.FindProperty("m_RendererFeatureMap");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            feature.passMaterial = material;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing;
            feature.requirements = ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal;
            feature.fetchColorBuffer = true;
            feature.SetActive(true);
            data.SetDirty();
            EditorUtility.SetDirty(data);
            // Materiais dos efeitos com shader das skills (carregados do Resources pelo Fx/MagicFx).
            EffectMaterial("Jogo/Energia", "Assets/Resources/Visuals/fx/energia.mat");
            EffectMaterial("Jogo/MarcaChao", "Assets/Resources/Visuals/fx/marca_chao.mat");
            AssetDatabase.SaveAssets();
            Debug.Log("[Contorno] Ligado no renderizador do PC.");
        }

        private static void EffectMaterial(string shaderName, string path)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Efeitos] Shader {shaderName} não encontrado.");
                return;
            }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetWrite.Create(material, path);
            }
            material.shader = shader;
            // Energia: borda fina e brilho contido (com o bloom, mais que isso estoura para branco).
            if (shaderName == "Jogo/Energia")
            {
                material.SetFloat("_Intensity", 1.2f);
                material.SetFloat("_Rim", 3.2f);
            }
            EditorUtility.SetDirty(material);
        }
    }
}
