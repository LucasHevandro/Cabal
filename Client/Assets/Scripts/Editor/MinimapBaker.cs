using System.IO;
using System.Linq;
using Game.Client.UI;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Fotografa o mundo de cima (câmera ortográfica, norte para cima) depois de montar a cena e guarda em
    /// <c>Resources/Visuals/mapa_mundo.png</c>: é o fundo do minimapa e do mapa grande. A área é a de
    /// <see cref="Minimap.WorldMapCenter"/> e <see cref="Minimap.WorldMapHalf"/>.
    /// </summary>
    public static class MinimapBaker
    {
        private const string Path = "Assets/Resources/Visuals/mapa_mundo.png";
        private const int Resolution = 1024;

        public static void Bake()
        {
            var center = Minimap.WorldMapCenter;
            var half = Minimap.WorldMapHalf;
            var cameraObject = new GameObject("Câmera do mapa");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = half;
            camera.aspect = 1f;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, 150f, center.y), Quaternion.Euler(90f, 0f, 0f));
            camera.nearClipPlane = 1f;
            camera.farClipPlane = 300f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.18f, 0.22f, 0.16f);
            camera.enabled = false;

            var target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            camera.targetTexture = target;
            // A neblina apagaria tudo vista de 150 m de altura.
            var fog = RenderSettings.fog;
            RenderSettings.fog = false;
            camera.Render();
            RenderSettings.fog = fog;

            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var picture = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
            // Vistas da câmera de cima em lote, as pedras e colunas (cinza) saem azul forte; a causa não está na luz
            // ambiente, nas sondas nem nos mipmaps. Esses pixels voltam para cinza pelo brilho (a água é verde-azulada
            // e não entra na regra).
            var pixels = picture.GetPixels32();
            for (var i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.b > c.g * 1.3f + 10 && c.b > c.r * 1.6f + 10)
                {
                    var gray = (byte)Mathf.Clamp((c.r + c.g + c.b) / 3 + 40, 0, 255);
                    pixels[i] = new Color32(gray, gray, (byte)Mathf.Min(255, gray + 4), 255);
                }
            }
            picture.SetPixels32(pixels);
            picture.Apply();
            RenderTexture.active = previous;
            var bytes = picture.EncodeToPNG();
            Object.DestroyImmediate(picture);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            if (!File.Exists(Path) || !File.ReadAllBytes(Path).SequenceEqual(bytes))
            {
                AssetWrite.Bytes(Path, bytes);
                AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
            }
            var importer = (TextureImporter)AssetImporter.GetAtPath(Path);
            if (importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp || importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
            Debug.Log($"[Protótipo] Mapa do mundo fotografado em {Path}");
        }
    }
}
