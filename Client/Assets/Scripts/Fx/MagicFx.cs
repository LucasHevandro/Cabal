using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Desenho da marca no chão (o shader <c>Jogo/MarcaChao</c> faz cada um em código).</summary>
    public enum GroundMarkStyle
    {
        Rune = 0,
        Crack = 1,
        Scorch = 2,
        Frost = 3,
    }

    /// <summary>
    /// Efeitos com shader das skills: marcas no chão (runa, rachadura, queimado, geada), cúpulas de energia que crescem
    /// e se dissolvem, a aura dos projéteis e o impacto de tela (borrão radial e clarão no finalizador e no Excellent, feito
    /// no mesmo passo do contorno). Tudo reaproveitado de uma reserva: nada é criado nem destruído por golpe.
    /// </summary>
    public sealed class MagicFx : MonoBehaviour
    {
        private const int MaxMarks = 24;
        private const int MaxBursts = 16;

        private static MagicFx _instance;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int LifeId = Shader.PropertyToID("_Life");
        private static readonly int PatternId = Shader.PropertyToID("_Pattern");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int ImpactStrengthId = Shader.PropertyToID("_ImpactStrength");
        private static readonly int ImpactColorId = Shader.PropertyToID("_ImpactColor");
        private static readonly int ImpactCenterId = Shader.PropertyToID("_ImpactCenter");

        private sealed class Effect
        {
            public GameObject Object;
            public Renderer Renderer;
            public MaterialPropertyBlock Block;
            public float StartedAt;
            public float Duration;
            public float Size;
            public bool Active;
        }

        private readonly List<Effect> _marks = new List<Effect>();
        private readonly List<Effect> _bursts = new List<Effect>();
        private Material _markMaterial;
        private Material _energyMaterial;
        private Mesh _quad;
        private Mesh _sphere;
        private float _impact;
        private Color _impactColor;
        private Vector2 _impactCenter = new Vector2(0.5f, 0.5f);

        /// <summary>Material de energia (para a aura dos projéteis); nulo se o asset não está no build.</summary>
        public static Material EnergyMaterial => Instance != null ? Instance._energyMaterial : null;

        private static MagicFx Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var host = new GameObject("Efeitos mágicos");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<MagicFx>();
                return _instance;
            }
        }

        private void Awake()
        {
            _markMaterial = Resources.Load<Material>("Visuals/fx/marca_chao");
            _energyMaterial = Resources.Load<Material>("Visuals/fx/energia");
            _quad = BuildQuad();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _sphere = sphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(sphere);
        }

        private void OnDestroy()
        {
            Shader.SetGlobalFloat(ImpactStrengthId, 0f);
            if (_instance == this) _instance = null;
        }

        /// <summary>Marca no chão em <paramref name="ground"/> (já na altura do chão), alinhada ao morro.</summary>
        public static void Mark(GroundMarkStyle style, Vector3 ground, float radius, Color color, float seconds)
        {
            var fx = Instance;
            if (fx._markMaterial == null) return;
            var effect = fx.Take(fx._marks, MaxMarks, "Marca no chão", fx._quad, fx._markMaterial);
            var normal = GroundNormal(ground, Mathf.Max(0.5f, radius * 0.6f));
            effect.Object.transform.SetPositionAndRotation(ground + normal * 0.06f, Quaternion.FromToRotation(Vector3.up, normal) *
                Quaternion.Euler(0, Random.value * 360f, 0));
            effect.Object.transform.localScale = Vector3.one * radius * 2f;
            effect.Duration = seconds;
            effect.Block.SetColor(ColorId, color);
            effect.Block.SetFloat(PatternId, (float)style);
            effect.Block.SetFloat(SeedId, Random.value * 100f);
            effect.Block.SetFloat(LifeId, 0f);
            effect.Renderer.SetPropertyBlock(effect.Block);
        }

        /// <summary>Cúpula de energia que cresce até <paramref name="radius"/> e se desfaz (explosões e magias de área).</summary>
        public static void Burst(Vector3 center, float radius, Color color, float seconds = 0.6f)
        {
            var fx = Instance;
            if (fx._energyMaterial == null) return;
            var effect = fx.Take(fx._bursts, MaxBursts, "Cúpula de energia", fx._sphere, fx._energyMaterial);
            effect.Object.transform.position = center;
            effect.Object.transform.rotation = Random.rotation;
            effect.Size = radius * 2f;
            effect.Duration = seconds;
            effect.Block.SetColor(ColorId, color);
            effect.Block.SetFloat(DissolveId, 0f);
            effect.Renderer.SetPropertyBlock(effect.Block);
            effect.Object.transform.localScale = Vector3.one * effect.Size * 0.3f;
        }

        /// <summary>
        /// Impacto de tela: borrão radial a partir de <paramref name="world"/> e um clarão da cor dada, que somem em
        /// ~0,3 s. <paramref name="strength"/> de 0 a 1 (o finalizador usa 1).
        /// </summary>
        public static void ScreenImpact(float strength, Color color, Vector3 world)
        {
            var fx = Instance;
            fx._impact = Mathf.Max(fx._impact, Mathf.Clamp01(strength));
            fx._impactColor = color;
            var camera = Camera.main;
            if (camera != null)
            {
                var screen = camera.WorldToViewportPoint(world);
                fx._impactCenter = screen.z > 0 ? new Vector2(Mathf.Clamp01(screen.x), Mathf.Clamp01(screen.y)) : new Vector2(0.5f, 0.5f);
            }
        }

        private Effect Take(List<Effect> pool, int max, string name, Mesh mesh, Material material)
        {
            Effect effect = null;
            foreach (var candidate in pool)
                if (!candidate.Active) { effect = candidate; break; }
            if (effect == null && pool.Count < max)
            {
                var obj = new GameObject(name);
                obj.transform.SetParent(transform, false);
                obj.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = obj.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                effect = new Effect { Object = obj, Renderer = renderer, Block = new MaterialPropertyBlock() };
                pool.Add(effect);
            }
            // Reserva cheia: reaproveita a mais antiga.
            if (effect == null)
            {
                effect = pool[0];
                foreach (var candidate in pool)
                    if (candidate.StartedAt < effect.StartedAt) effect = candidate;
            }
            effect.Active = true;
            effect.StartedAt = Time.time;
            effect.Object.SetActive(true);
            return effect;
        }

        private void Update()
        {
            var now = Time.time;
            foreach (var mark in _marks)
            {
                if (!mark.Active) continue;
                var life = (now - mark.StartedAt) / Mathf.Max(0.01f, mark.Duration);
                if (life >= 1f) { Stop(mark); continue; }
                mark.Block.SetFloat(LifeId, life);
                mark.Renderer.SetPropertyBlock(mark.Block);
            }
            foreach (var burst in _bursts)
            {
                if (!burst.Active) continue;
                var t = (now - burst.StartedAt) / Mathf.Max(0.01f, burst.Duration);
                if (t >= 1f) { Stop(burst); continue; }
                // Cresce rápido (sai do centro) e se desfaz em pedaços na segunda metade.
                var grow = 1f - (1f - t) * (1f - t) * (1f - t);
                burst.Object.transform.localScale = Vector3.one * burst.Size * Mathf.Lerp(0.3f, 1f, grow);
                burst.Block.SetFloat(DissolveId, Mathf.Clamp01((t - 0.35f) / 0.65f));
                burst.Renderer.SetPropertyBlock(burst.Block);
            }

            // Impacto de tela some sozinho (tempo real: não para no hit stop).
            _impact = Mathf.MoveTowards(_impact, 0f, Time.unscaledDeltaTime * 3.2f);
            Shader.SetGlobalFloat(ImpactStrengthId, _impact);
            Shader.SetGlobalColor(ImpactColorId, _impactColor);
            Shader.SetGlobalVector(ImpactCenterId, _impactCenter);
        }

        private static void Stop(Effect effect)
        {
            effect.Active = false;
            effect.Object.SetActive(false);
        }

        private static Vector3 GroundNormal(Vector3 at, float spread)
        {
            float H(float dx, float dz) => World.Ground.HeightAt(at.x + dx, at.z + dz);
            var normal = new Vector3(H(-spread, 0) - H(spread, 0), 2f * spread, H(0, -spread) - H(0, spread));
            return normal.normalized;
        }

        /// <summary>Quadrado de 1 m deitado no chão (XZ), com UV de 0 a 1.</summary>
        private static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "marca_chao" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
