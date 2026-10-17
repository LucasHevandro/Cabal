using System;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Efeitos visuais de combate: faíscas no impacto e projéteis das skills mágicas.
    /// Os materiais vêm da cena (o construtor da cena os cria), para o build incluir os shaders.
    /// </summary>
    public sealed class CombatFx : MonoBehaviour
    {
        private static CombatFx _instance;

        [SerializeField] private Material particleMaterial;
        [SerializeField] private Material projectileMaterial;

        [Header("Faíscas")]
        [SerializeField] private float sparkSpeed = 6f;
        [SerializeField] private float sparkLifetime = 0.35f;
        [SerializeField] private float sparkSize = 0.12f;

        [Header("Projétil")]
        [SerializeField] private float projectileSpeed = 28f;
        [SerializeField] private float projectileSize = 0.35f;

        private ParticleSystem _sparks;

        public static CombatFx Instance => _instance;

        /// <summary>Material das partículas (para os outros efeitos usarem o mesmo shader incluído no build).</summary>
        public Material ParticleMaterial => particleMaterial;

        public void SetMaterials(Material particles, Material projectile)
        {
            particleMaterial = particles;
            projectileMaterial = projectile;
        }

        private void Awake()
        {
            _instance = this;
            // Partícula suave do catálogo de visuais (disco esfumado e aditivo), se existir; senão a opaca da cena.
            var soft = Resources.Load<Material>("Visuals/fx/particula_suave");
            if (soft != null)
            {
                particleMaterial = soft;
                sparkSize *= 1.7f; // o disco esfumado parece menor que o quadrado do mesmo tamanho
            }
            _sparks = CreateSparkSystem();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Explosão de faíscas em <paramref name="position"/>.</summary>
        public void Sparks(Vector3 position, Color color, int count)
        {
            var emit = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = false };
            for (var i = 0; i < count; i++)
            {
                var direction = UnityEngine.Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.8f + 0.2f;
                emit.position = position;
                emit.velocity = direction * (sparkSpeed * UnityEngine.Random.Range(0.6f, 1.3f));
                emit.startSize = sparkSize * UnityEngine.Random.Range(0.7f, 1.4f);
                emit.startLifetime = sparkLifetime * UnityEngine.Random.Range(0.7f, 1.2f);
                _sparks.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Lança um projétil de <paramref name="from"/> até <paramref name="target"/> (seguindo o alvo)
        /// e chama <paramref name="onArrive"/> quando chegar.
        /// </summary>
        public void LaunchProjectile(Vector3 from, Transform target, Vector3 targetOffset, Color color, Action onArrive)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Projétil";
            Destroy(orb.GetComponent<Collider>());
            orb.transform.position = from;
            orb.transform.localScale = Vector3.one * projectileSize;
            var renderer = orb.GetComponent<Renderer>();
            if (projectileMaterial != null)
            {
                renderer.sharedMaterial = projectileMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var projectile = orb.AddComponent<Projectile>();
            projectile.Launch(target, targetOffset, projectileSpeed, () =>
            {
                Sparks(orb.transform.position, color, 10);
                onArrive?.Invoke();
            });
            projectile.TrailEmitter = (position) => _sparks.Emit(new ParticleSystem.EmitParams
            {
                position = position,
                velocity = UnityEngine.Random.insideUnitSphere * 0.5f,
                startColor = color,
                startSize = sparkSize * 0.8f,
                startLifetime = 0.2f,
                applyShapeToPosition = false,
            }, 1);
        }

        private ParticleSystem CreateSparkSystem()
        {
            var host = new GameObject("Faíscas");
            host.transform.SetParent(transform, false);
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.2f;
            main.maxParticles = 1000;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (particleMaterial != null) renderer.sharedMaterial = particleMaterial;

            system.Play();
            return system;
        }
    }
}
