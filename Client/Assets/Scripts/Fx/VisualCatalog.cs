using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Modelos do jogo por convenção de pasta: <c>Assets/Resources/Visuals/&lt;categoria&gt;/&lt;id&gt;.prefab</c>
    /// (ex.: <c>Visuals/monster/lobo_cinzento</c>, <c>Visuals/npc/ferreira_ilsa</c>, <c>Visuals/class/blade</c>,
    /// <c>Visuals/item/espada_ferro</c>). Sem prefab para o id, o jogo continua com as formas simples de hoje — assim os
    /// modelos entram um de cada vez, sem mexer no código. O guia está em <c>Docs/visuais.md</c>.
    /// </summary>
    public static class VisualCatalog
    {
        public const string Monster = "monster";
        public const string Npc = "npc";
        public const string Class = "class";
        public const string Item = "item";

        private const string Root = "Visuals/";

        private static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        /// <summary>O prefab do id, ou nulo se ainda não existe (usa a forma simples).</summary>
        public static GameObject Find(string category, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var path = Root + category + "/" + id;
            if (!Cache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(path);
                Cache[path] = prefab;
            }
            return prefab;
        }

        public static bool Has(string category, string id) => Find(category, id) != null;

        /// <summary>
        /// Cria o modelo como filho de <paramref name="parent"/> (sem colisores nem física: quem clica e colide é o objeto
        /// do jogo, não o modelo). Nulo se não houver prefab.
        /// </summary>
        public static GameObject Spawn(string category, string id, Transform parent)
        {
            var prefab = Find(category, id);
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = "Modelo: " + id;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) Object.Destroy(collider);
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(body);
            if (instance.GetComponentInChildren<Animator>() != null && instance.GetComponent<VisualAnimator>() == null)
                instance.AddComponent<VisualAnimator>();
            return instance;
        }

        /// <summary>Altura e raio do modelo (pelas malhas), relativos a <paramref name="root"/>.</summary>
        public static (float Height, float Radius) Measure(GameObject model, Transform root)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return (2f, 0.5f);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var height = Mathf.Max(0.3f, bounds.max.y - root.position.y);
            var radius = Mathf.Max(0.2f, Mathf.Max(bounds.extents.x, bounds.extents.z));
            return (height, radius);
        }
    }
}
