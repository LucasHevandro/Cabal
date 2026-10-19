using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Malhas e materiais dos pacotes KayKit de cenário (Forest Nature e Medieval Hexagon, CC0), em
    /// <c>Assets/Art/KayKit/Natureza</c> e <c>Assets/Art/KayKit/Vila</c>. Cada pacote usa uma textura de degradê só (atlas):
    /// um material por pacote basta, e tudo entra no mesmo lote de desenho. Sem o pacote, quem chama volta às formas geradas.
    /// </summary>
    internal static class PackMeshes
    {
        public const string Nature = "Assets/Art/KayKit/Natureza";
        public const string Village = "Assets/Art/KayKit/Vila";

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        /// <summary>As malhas dos modelos pedidos (os que faltarem ficam de fora; vazio se o pacote não está no projeto).</summary>
        public static Mesh[] Load(string folder, params string[] names)
        {
            var meshes = new List<Mesh>();
            foreach (var name in names)
            {
                var path = $"{folder}/{name}.fbx";
                if (!File.Exists(path)) continue;
                var mesh = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().FirstOrDefault();
                if (mesh != null) meshes.Add(mesh);
            }
            return meshes.ToArray();
        }

        /// <summary>Material do atlas do pacote (com um tom por cima, para variar a mesma árvore entre regiões).</summary>
        public static Material Atlas(string folder, string texture, string name, Color? tint = null)
        {
            var key = folder + name;
            if (Materials.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = TrainingSceneBuilder.MaterialFor("Pacote_" + name, tint ?? Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{folder}/{texture}"));
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(material);
            Materials[key] = material;
            return material;
        }

        public static bool Has(string folder) => Directory.Exists(folder) && Directory.GetFiles(folder, "*.fbx").Length > 0;

        /// <summary>Nomes de A até a letra dada (ex.: "Rock_1_", 'D', 'I', "_Color1" → Rock_1_D_Color1 … Rock_1_I_Color1).</summary>
        public static string[] Range(string prefix, char from, char to, string suffix = "_Color1")
        {
            var names = new List<string>();
            for (var c = from; c <= to; c++) names.Add(prefix + c + suffix);
            return names.ToArray();
        }
    }
}
