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
        private ParticleSystem _shards;
        private ParticleSystem _glow;

        public static CombatFx Instance => _instance;

        /// <summary>Velocidade base dos projéteis (m/s), para estimar quando a magia chega.</summary>
        public float ProjectileSpeed => projectileSpeed;

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
            _glow = CreateSparkSystem(0f, "Brilho");
            // Lascas: riscos curtos e claros esticados na direção do voo (o metal batendo), caindo rápido.
            _shards = CreateSparkSystem(2.2f, "Lascas");
            var stretched = _shards.GetComponent<ParticleSystemRenderer>();
            stretched.renderMode = ParticleSystemRenderMode.Stretch;
            stretched.velocityScale = 0.045f;
            stretched.lengthScale = 1.2f;
        }

        /// <summary>Partícula sem gravidade (brilho, energia, fumaça leve): flutua e some.</summary>
        public void Glow(Vector3 position, Vector3 velocity, Color color, float size, float lifetime)
        {
            _glow.Emit(new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startColor = color, startSize = size, startLifetime = lifetime,
                applyShapeToPosition = false,
            }, 1);
        }

        /// <summary>
        /// Clarão curto (explosões, golpes pesados): um brilho grande que o bloom espalha. Antes eram luzes pontuais,
        /// que deixavam um ponto branco solto na tela.
        /// </summary>
        public void Flash(Vector3 position, Color color, float intensity, float range, float seconds)
        {
            var size = Mathf.Clamp(range * 0.35f, 0.8f, 4f);
            var bright = Color.Lerp(color, Color.white, 0.35f) * Mathf.Clamp(intensity * 0.5f, 1f, 3f);
            Glow(position, Vector3.zero, bright, size, Mathf.Max(0.08f, seconds));
            Glow(position, Vector3.zero, color, size * 1.8f, Mathf.Max(0.08f, seconds) * 0.7f);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>Explosão de faíscas em <paramref name="position"/>, com algumas lascas esticadas e quentes.</summary>
        public void Sparks(Vector3 position, Color color, int count)
        {
            var hot = Color.Lerp(color, new Color(1f, 0.92f, 0.7f), 0.6f) * 2f;
            var shard = new ParticleSystem.EmitParams { startColor = hot, applyShapeToPosition = false };
            for (var i = 0; i < Mathf.Max(3, count / 2); i++)
            {
                var direction = UnityEngine.Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) * 0.9f + 0.15f;
                shard.position = position;
                shard.velocity = direction * (sparkSpeed * UnityEngine.Random.Range(1.1f, 2f));
                shard.startSize = sparkSize * UnityEngine.Random.Range(0.35f, 0.55f);
                shard.startLifetime = sparkLifetime * UnityEngine.Random.Range(0.6f, 1f);
                _shards.Emit(shard, 1);
            }
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

        /// <summary>Uma partícula solta (para anéis, arcos e outros desenhos de efeito).</summary>
        public void Emit(Vector3 position, Vector3 velocity, Color color, float size, float lifetime)
        {
            _sparks.Emit(new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startColor = color, startSize = size, startLifetime = lifetime,
                applyShapeToPosition = false,
            }, 1);
        }

        /// <summary>
        /// Lança um projétil de <paramref name="from"/> até <paramref name="target"/> (seguindo o alvo)
        /// e chama <paramref name="onArrive"/> quando chegar.
        /// </summary>
        public void LaunchProjectile(Vector3 from, Transform target, Vector3 targetOffset, Color color, Action onArrive,
            float sizeMultiplier = 1f, float speedMultiplier = 1f)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Projétil";
            Destroy(orb.GetComponent<Collider>());
            orb.transform.position = from;
            orb.transform.localScale = Vector3.one * (projectileSize * sizeMultiplier);
            // Lança (rápida): alongada na direção do voo, como um raio de luz.
            if (speedMultiplier >= 1.5f) orb.transform.localScale = new Vector3(0.7f, 0.7f, 2.6f) * (projectileSize * sizeMultiplier);
            var renderer = orb.GetComponent<Renderer>();
            if (projectileMaterial != null)
            {
                renderer.sharedMaterial = projectileMaterial;
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Aura de energia em volta do miolo: brilho na borda e ondulando (shader Jogo/Energia).
            if (MagicFx.EnergyMaterial != null)
            {
                var aura = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(aura.GetComponent<Collider>());
                aura.name = "Aura";
                aura.transform.SetParent(orb.transform, false);
                aura.transform.localScale = Vector3.one * 1.9f;
                var auraRenderer = aura.GetComponent<MeshRenderer>();
                auraRenderer.sharedMaterial = MagicFx.EnergyMaterial;
                auraRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var auraBlock = new MaterialPropertyBlock();
                auraBlock.SetColor("_Color", color);
                auraRenderer.SetPropertyBlock(auraBlock);
            }
            var projectile = orb.AddComponent<Projectile>();
            projectile.Launch(target, targetOffset, projectileSpeed * speedMultiplier, () =>
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

        /// <summary>
        /// Flecha da Ranger: o modelo (Visuals/fx/flecha, apontando para +Z) voando reto até o alvo, com um rastro fino da
        /// cor da skill. Sem o modelo, um palito no lugar.
        /// </summary>
        public void LaunchArrow(Vector3 from, Transform target, Vector3 targetOffset, Color color, Action onArrive,
            float sizeMultiplier = 1f, float speedMultiplier = 2f)
        {
            var prefab = Resources.Load<GameObject>("Visuals/fx/flecha");
            GameObject arrow;
            if (prefab != null)
            {
                arrow = Instantiate(prefab);
            }
            else
            {
                arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(arrow.GetComponent<Collider>());
                arrow.transform.localScale = new Vector3(0.05f, 0.05f, 0.8f);
            }
            arrow.name = "Flecha";
            arrow.transform.position = from;
            arrow.transform.localScale *= sizeMultiplier;
            if (target != null && (target.position + targetOffset - from).sqrMagnitude > 0.0001f)
                arrow.transform.rotation = Quaternion.LookRotation(target.position + targetOffset - from);
            foreach (var renderer in arrow.GetComponentsInChildren<Renderer>())
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var projectile = arrow.AddComponent<Projectile>();
            projectile.Launch(target, targetOffset, projectileSpeed * speedMultiplier, () =>
            {
                Sparks(arrow.transform.position, color, 6);
                onArrive?.Invoke();
            });
            projectile.TrailEmitter = (position) => _sparks.Emit(new ParticleSystem.EmitParams
            {
                position = position,
                velocity = Vector3.zero,
                startColor = color,
                startSize = sparkSize * 0.5f * sizeMultiplier,
                startLifetime = 0.15f,
                applyShapeToPosition = false,
            }, 1);
        }

        private ParticleSystem CreateSparkSystem(float gravity = 1.2f, string name = "Faíscas")
        {
            var host = new GameObject(name);
            host.transform.SetParent(transform, false);
            var system = host.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
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
