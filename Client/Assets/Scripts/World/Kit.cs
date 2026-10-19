using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Peças prontas da Cripta e da arena (Resources/Visuals/dungeon, feitas pelo DungeonKitBuilder no Editor) e os pisos
    /// com textura repetida. Sem a peça (projeto sem elas), quem chama continua só com os blocos simples.
    /// </summary>
    public static class Kit
    {
        private const string Folder = "Visuals/dungeon/";

        public static GameObject Place(Transform parent, string name, Vector3 localPosition, Quaternion localRotation, Vector3? scale = null)
        {
            var prefab = Resources.Load<GameObject>(Folder + name);
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            instance.name = name;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = localRotation;
            if (scale is { } s) instance.transform.localScale = s;
            return instance;
        }

        /// <summary>Material de piso com a textura repetida a cada <paramref name="tile"/> metros no tamanho dado.</summary>
        public static bool Floor(Renderer renderer, string material, Vector2 size, float tile)
        {
            var floor = Resources.Load<Material>(Folder + material);
            if (floor == null || renderer == null) return false;
            renderer.sharedMaterial = floor;
            var block = new MaterialPropertyBlock();
            block.SetVector("_BaseMap_ST", new Vector4(size.x / tile, size.y / tile, 0, 0));
            renderer.SetPropertyBlock(block);
            return true;
        }

        /// <summary>Esconde a malha de um bloco que fica só como colisão (o visual vem das peças).</summary>
        public static void HideMesh(GameObject block)
        {
            if (block != null && block.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = false;
        }
    }
}
