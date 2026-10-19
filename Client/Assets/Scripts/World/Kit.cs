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

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int LifeId = Shader.PropertyToID("_Life");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static MaterialPropertyBlock _runeBlock;

        /// <summary>
        /// Círculo rúnico brilhando no chão (o shader das marcas das skills, parado no ponto em que já apareceu inteiro).
        /// A cor é HDR: o bloom faz o brilho. Nulo sem o material.
        /// </summary>
        public static Renderer Rune(Transform parent, string name, Vector3 localPosition, float diameter, Color color, float seed = 0f)
        {
            var material = Resources.Load<Material>("Visuals/fx/marca_chao");
            if (material == null) return null;
            var rune = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rune.name = name;
            Object.Destroy(rune.GetComponent<Collider>());
            rune.transform.SetParent(parent, false);
            rune.transform.localPosition = localPosition;
            rune.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rune.transform.localScale = new Vector3(diameter, diameter, 1f);
            var renderer = rune.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            SetRuneColor(renderer, color, seed);
            return renderer;
        }

        public static void SetRuneColor(Renderer rune, Color color, float seed = 0f)
        {
            if (rune == null) return;
            _runeBlock ??= new MaterialPropertyBlock();
            rune.GetPropertyBlock(_runeBlock);
            _runeBlock.SetColor(ColorId, color);
            _runeBlock.SetFloat(PatternId, 0f);
            _runeBlock.SetFloat(LifeId, 0.2f);
            if (seed != 0f) _runeBlock.SetFloat(SeedId, seed);
            rune.SetPropertyBlock(_runeBlock);
        }

        /// <summary>
        /// Partículas paradas no ar dentro de uma caixa (poeira, névoa): <paramref name="perCubicMeter"/> define quantas,
        /// e elas acendem e apagam devagar. Para lugares fechados, que não mudam de clima.
        /// </summary>
        public static ParticleSystem Haze(Transform parent, string name, Vector3 center, Vector3 box, Color color, float minSize, float maxSize,
            float lifetime, float rate, Vector3 drift)
        {
            var material = Resources.Load<Material>("Visuals/fx/particula_suave");
            if (material == null) return null;
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = center;
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.3f) + 10;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = drift.x;
            velocity.y = drift.y;
            velocity.z = drift.z;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
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
