using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Client.Editor
{
    /// <summary>
    /// Gera o material de partícula suave (disco com borda esfumada, transparente e aditivo) em
    /// Assets/Resources/Visuals/fx/particula_suave.mat. Os efeitos o carregam pelo catálogo de visuais; sem ele, usam o
    /// material opaco da cena (partículas quadradas).
    /// Em linha de comando: -executeMethod Game.Client.Editor.FxAssets.CreateSoftParticle
    /// </summary>
    public static class FxAssets
    {
        private const string Folder = "Assets/Resources/Visuals/fx";

        [MenuItem("Jogo/Visuais/Criar material de partícula suave")]
        public static void CreateSoftParticle()
        {
            Directory.CreateDirectory(Folder);
            var texturePath = Folder + "/particula_suave.png";
            File.WriteAllBytes(texturePath, SoftDisc(64).EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var materialPath = Folder + "/particula_suave.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            // Transparente e aditivo: as partículas somam luz (brilho) em vez de tapar o que está atrás.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            Debug.Log("[Visuais] Partícula suave salva em " + materialPath);
        }

        private static Texture2D SoftDisc(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                var alpha = Mathf.Clamp01(1f - d);
                alpha *= alpha; // borda bem esfumada, miolo forte
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            texture.Apply();
            return texture;
        }
    }
}
