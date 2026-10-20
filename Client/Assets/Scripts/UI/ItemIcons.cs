using System.Collections.Generic;
using Game.Client.Fx;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Ícones dos itens tirados do próprio modelo (<c>Visuals/item/&lt;id&gt;</c>): na primeira vez que a interface pede, uma
    /// câmera escondida fotografa o modelo com fundo transparente. Até a foto ficar pronta (ou sem modelo), a janela usa o
    /// quadradinho com o nome, como antes.
    /// </summary>
    public sealed class ItemIcons : MonoBehaviour
    {
        private const int Size = 128;
        private const int Layer = 31;
        private static readonly Vector3 Stage = new Vector3(0f, -4000f, 0f);

        private static ItemIcons _instance;
        private static readonly Dictionary<string, Texture2D> Done = new Dictionary<string, Texture2D>();
        private static readonly HashSet<string> Missing = new HashSet<string>();
        private static readonly Queue<string> Pending = new Queue<string>();
        private static readonly HashSet<string> Queued = new HashSet<string>();

        private Camera _camera;
        private Light _light;
        private RenderTexture _target;

        /// <summary>A foto do item, ou nulo enquanto não ficou pronta (ou se o item não tem modelo).</summary>
        public static Texture2D Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (Done.TryGetValue(itemId, out var icon)) return icon;
            if (Missing.Contains(itemId) || !Queued.Add(itemId)) return null;
            Pending.Enqueue(itemId);
            if (_instance == null)
            {
                var host = new GameObject("Ícones dos itens") { hideFlags = HideFlags.HideAndDontSave };
                _instance = host.AddComponent<ItemIcons>();
            }
            return null;
        }

        private void Awake()
        {
            _target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            _camera = new GameObject("Câmera dos ícones").AddComponent<Camera>();
            _camera.transform.SetParent(transform, false);
            _camera.enabled = false;
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0, 0, 0, 0);
            _camera.fieldOfView = 24f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.targetTexture = _target;

            // Luz própria (a do sol fica desligada durante a foto, para o ícone não escurecer à noite).
            _light = new GameObject("Luz dos ícones").AddComponent<Light>();
            _light.transform.SetParent(transform, false);
            _light.type = LightType.Directional;
            _light.intensity = 1.5f;
            _light.color = new Color(1f, 0.97f, 0.92f);
            _light.shadows = LightShadows.None;
            _light.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            _light.enabled = false;
        }

        private void LateUpdate()
        {
            // Duas fotos por quadro no máximo: abrir o inventário cheio não trava o jogo.
            for (var i = 0; i < 2 && Pending.Count > 0; i++)
            {
                var id = Pending.Dequeue();
                var icon = Shoot(id);
                if (icon != null) Done[id] = icon;
                else Missing.Add(id);
            }
        }

        private Texture2D Shoot(string id)
        {
            var prefab = VisualCatalog.Find(VisualCatalog.Item, id);
            if (prefab == null) return null;
            var model = Instantiate(prefab, Stage, Quaternion.Euler(0f, 150f, 0f));
            VisualCatalog.ApplyVariant(model, VisualCatalog.Item, id);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                DestroyImmediate(model);
                return null;
            }
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // Enquadra o modelo inteiro, olhando um pouco de cima.
            var radius = Mathf.Max(0.05f, bounds.extents.magnitude);
            var distance = radius / Mathf.Sin(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
            var direction = new Vector3(0f, 0.45f, -1f).normalized;
            _camera.transform.position = bounds.center + direction * distance;
            _camera.transform.LookAt(bounds.center);
            _camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            _camera.farClipPlane = distance + radius * 2f;

            var suns = new List<Light>();
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (light.enabled && light != _light && light.type == LightType.Directional)
                {
                    light.enabled = false;
                    suns.Add(light);
                }
            var fog = RenderSettings.fog;
            RenderSettings.fog = false;
            _light.enabled = true;
            try
            {
                _camera.Render();
            }
            finally
            {
                _light.enabled = false;
                RenderSettings.fog = fog;
                foreach (var light in suns) light.enabled = true;
                DestroyImmediate(model);
            }

            var previous = RenderTexture.active;
            RenderTexture.active = _target;
            var icon = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { name = "Ícone " + id, hideFlags = HideFlags.HideAndDontSave };
            icon.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            icon.Apply();
            RenderTexture.active = previous;
            return icon;
        }

        private void OnDestroy()
        {
            if (_target != null) _target.Release();
        }
    }
}
