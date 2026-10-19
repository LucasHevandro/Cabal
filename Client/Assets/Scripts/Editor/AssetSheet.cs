using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Conferência de pacotes de modelos: para cada .fbx de uma pasta, lista tamanho, materiais e animações, e monta uma
    /// folha de fotos (grade com o nome embaixo). Uso em lote:
    /// <c>-executeMethod Game.Client.Editor.AssetSheet.Run -sheetFolder Assets/Art/... -sheetOut pasta</c>.
    /// </summary>
    public static class AssetSheet
    {
        private const int Cell = 192;

        public static void Run()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string name, string fallback)
            {
                var i = System.Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
            }
            var folders = Arg("-sheetFolder", "Assets/Art").Split(';');
            var output = Arg("-sheetOut", "Folhas");
            Directory.CreateDirectory(output);
            foreach (var folder in folders) Sheet(folder, output);
        }

        private static void Sheet(string folder, string output)
        {
            var paths = AssetDatabase.FindAssets("t:Model", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".fbx")).OrderBy(p => p).ToList();
            var report = new StringBuilder();
            var columns = 8;
            var rows = Mathf.CeilToInt(paths.Count / (float)columns);
            var sheet = new Texture2D(columns * Cell, Mathf.Max(1, rows) * Cell, TextureFormat.RGB24, false);
            var target = new RenderTexture(Cell, Cell, 24);
            var cameraObject = new GameObject("Câmera da folha");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.55f, 0.62f, 0.7f);
            camera.targetTexture = target;
            camera.fieldOfView = 28f;
            var light = new GameObject("Luz").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            light.intensity = 1.3f;

            for (var i = 0; i < paths.Count; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                instance.transform.position = new Vector3(0, -1000, 0);
                var renderers = instance.GetComponentsInChildren<Renderer>();
                var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(instance.transform.position, Vector3.one);
                foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m =>
                    m.name + (m.mainTexture != null ? $"[{m.mainTexture.name}]" : "")).Distinct();
                var clips = AssetDatabase.LoadAllAssetsAtPath(paths[i]).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).Select(c => $"{c.name}({c.length:0.00}s)");
                report.AppendLine($"{Path.GetFileNameWithoutExtension(paths[i])}: tamanho {bounds.size.x:0.00} x {bounds.size.y:0.00} x {bounds.size.z:0.00}" +
                                  $" | materiais {string.Join(", ", materials)} | animações {string.Join(", ", clips)}");

                var radius = Mathf.Max(0.05f, bounds.extents.magnitude);
                camera.transform.position = bounds.center + new Vector3(0.6f, 0.5f, -1f).normalized * radius / Mathf.Sin(14f * Mathf.Deg2Rad);
                camera.transform.LookAt(bounds.center);
                camera.Render();
                RenderTexture.active = target;
                var cell = new Texture2D(Cell, Cell, TextureFormat.RGB24, false);
                cell.ReadPixels(new Rect(0, 0, Cell, Cell), 0, 0);
                cell.Apply();
                var x = i % columns * Cell;
                var y = (rows - 1 - i / columns) * Cell;
                sheet.SetPixels(x, y, Cell, Cell, cell.GetPixels());
                Object.DestroyImmediate(cell);
                Object.DestroyImmediate(instance);
            }
            RenderTexture.active = null;
            sheet.Apply();
            var name = folder.Replace("Assets/Art/", "").Replace('/', '_');
            File.WriteAllBytes(Path.Combine(output, name + ".png"), sheet.EncodeToPNG());
            File.WriteAllText(Path.Combine(output, name + ".txt"), report.ToString());
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(light.gameObject);
            Debug.Log($"[Folha] {folder}: {paths.Count} modelos");
        }
    }
}
