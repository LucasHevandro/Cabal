using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Gera um modelo de exemplo no catálogo de visuais (Assets/Resources/Visuals/monster/coelho_saltador.prefab),
    /// montado com primitivas. Serve para conferir que o catálogo funciona e de molde para os modelos de verdade:
    /// pivô no chão, frente para +Z, altura em metros. Pode ser apagado quando chegar o modelo final.
    /// Em linha de comando: -executeMethod Game.Client.Editor.VisualSamples.CreateRabbit
    /// </summary>
    public static class VisualSamples
    {
        private const string Folder = "Assets/Resources/Visuals/monster";

        [MenuItem("Jogo/Visuais/Criar coelho de exemplo")]
        public static void CreateRabbit()
        {
            Directory.CreateDirectory(Folder);
            var fur = SaveMaterial("Coelho_pelo", new Color(0.96f, 0.95f, 0.9f));
            var inner = SaveMaterial("Coelho_orelha", new Color(0.95f, 0.65f, 0.7f));
            var eyes = SaveMaterial("Coelho_olho", new Color(0.12f, 0.08f, 0.08f));

            var root = new GameObject("coelho_saltador");
            Part(root, PrimitiveType.Sphere, "Corpo", new Vector3(0, 0.36f, -0.05f), new Vector3(0.62f, 0.55f, 0.8f), fur);
            Part(root, PrimitiveType.Sphere, "Cabeça", new Vector3(0, 0.62f, 0.32f), new Vector3(0.42f, 0.4f, 0.42f), fur);
            Part(root, PrimitiveType.Capsule, "Orelha E", new Vector3(-0.1f, 0.98f, 0.28f), new Vector3(0.11f, 0.24f, 0.07f), fur, -12f);
            Part(root, PrimitiveType.Capsule, "Orelha D", new Vector3(0.1f, 0.98f, 0.28f), new Vector3(0.11f, 0.24f, 0.07f), fur, 12f);
            Part(root, PrimitiveType.Capsule, "Dentro E", new Vector3(-0.1f, 0.98f, 0.31f), new Vector3(0.06f, 0.18f, 0.03f), inner, -12f);
            Part(root, PrimitiveType.Capsule, "Dentro D", new Vector3(0.1f, 0.98f, 0.31f), new Vector3(0.06f, 0.18f, 0.03f), inner, 12f);
            Part(root, PrimitiveType.Sphere, "Olho E", new Vector3(-0.11f, 0.68f, 0.5f), Vector3.one * 0.07f, eyes);
            Part(root, PrimitiveType.Sphere, "Olho D", new Vector3(0.11f, 0.68f, 0.5f), Vector3.one * 0.07f, eyes);
            Part(root, PrimitiveType.Sphere, "Rabo", new Vector3(0, 0.38f, -0.47f), Vector3.one * 0.2f, fur);
            Part(root, PrimitiveType.Sphere, "Pata E", new Vector3(-0.17f, 0.1f, 0.18f), new Vector3(0.14f, 0.12f, 0.2f), fur);
            Part(root, PrimitiveType.Sphere, "Pata D", new Vector3(0.17f, 0.1f, 0.18f), new Vector3(0.14f, 0.12f, 0.2f), fur);

            var path = Folder + "/coelho_saltador.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[Visuais] Coelho de exemplo salvo em " + path);
        }

        private static void Part(GameObject root, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material, float roll = 0f)
        {
            var part = GameObject.CreatePrimitive(type);
            part.name = name;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.Euler(0, 0, roll);
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Material SaveMaterial(string name, Color color)
        {
            var path = Folder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
