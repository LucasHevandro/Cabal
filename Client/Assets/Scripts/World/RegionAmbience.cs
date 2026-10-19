using System.Collections.Generic;
using Game.Shared.World;
using UnityEngine;

namespace Game.Client.World
{
    /// <summary>
    /// Clima de cada região, em partículas que acompanham a câmera e mudam aos poucos conforme a distância do centro de
    /// cada região: pólen e folhas no vento nos Campos do Vento, vaga-lumes (mais fortes à noite) e névoa rasteira na Mata
    /// Sombria, partículas mágicas subindo e brilhos fantasmas nas Ruínas Esquecidas. Só no mundo aberto.
    /// </summary>
    public sealed class RegionAmbience : MonoBehaviour
    {
        private sealed class Layer
        {
            public ParticleSystem System;
            public string Zone = "";
            public float Rate;
            public bool NightBoost;
        }

        private readonly List<Layer> _layers = new List<Layer>();
        private readonly Dictionary<string, ZoneDefinition> _zones = new Dictionary<string, ZoneDefinition>();
        private Material _material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<RegionAmbience>() != null) return;
            new GameObject("Clima das regiões").AddComponent<RegionAmbience>();
        }

        private void Start()
        {
            _material = Resources.Load<Material>("Visuals/fx/particula_suave");
            if (_material == null)
            {
                enabled = false;
                return;
            }
            foreach (var zone in WorldContent.Zones()) _zones[zone.Id] = zone;

            // Campos do Vento: pólen claro e folhinhas levadas pelo vento.
            Add("campos_do_vento", "Pólen", 9f, false, new Color(1f, 0.95f, 0.6f, 0.7f), 0.06f, 0.11f, 9f,
                new Vector3(1.4f, 0.15f, 0.6f), 0.5f, new Vector3(36f, 6f, 36f), 2.5f);
            Add("campos_do_vento", "Folhas", 3f, false, new Color(0.65f, 0.85f, 0.35f, 0.9f), 0.1f, 0.16f, 8f,
                new Vector3(2.2f, -0.3f, 0.9f), 1f, new Vector3(36f, 5f, 36f), 4f);

            // Mata Sombria: vaga-lumes que acendem à noite e névoa baixa entre as árvores.
            Add("mata_sombria", "Vaga-lumes", 18f, true, new Color(2f, 2.4f, 0.7f, 1f), 0.12f, 0.2f, 6f,
                Vector3.zero, 0.8f, new Vector3(32f, 3f, 32f), 1.6f);
            Add("mata_sombria", "Névoa", 7f, false, new Color(0.75f, 0.85f, 0.8f, 0.09f), 6f, 10f, 12f,
                new Vector3(0.2f, 0f, 0.1f), 0.1f, new Vector3(34f, 0.6f, 34f), 0.6f);

            // Ruínas Esquecidas: partículas mágicas subindo e brilhos que vagam.
            Add("ruinas_esquecidas", "Magia", 24f, false, new Color(1.5f, 0.7f, 2.4f, 1f), 0.1f, 0.18f, 5f,
                new Vector3(0f, 0.7f, 0f), 0.6f, new Vector3(34f, 2f, 34f), 0.8f);
            Add("ruinas_esquecidas", "Brilhos", 5f, true, new Color(0.5f, 1.4f, 1.8f, 0.8f), 0.25f, 0.4f, 7f,
                new Vector3(0.3f, 0.15f, 0f), 0.9f, new Vector3(34f, 3f, 34f), 1.5f);
        }

        private void Add(string zone, string name, float rate, bool nightBoost, Color color, float minSize, float maxSize, float lifetime,
            Vector3 velocity, float noise, Vector3 box, float height)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(transform, false);
            var ps = obj.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt(rate * lifetime * 1.5f) + 10;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = box;
            shape.position = new Vector3(0, height, 0);

            var velocityOverLifetime = ps.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            velocityOverLifetime.x = velocity.x;
            velocityOverLifetime.y = velocity.y;
            velocityOverLifetime.z = velocity.z;

            var noiseModule = ps.noise;
            noiseModule.enabled = noise > 0f;
            noiseModule.strength = noise;
            noiseModule.frequency = 0.35f;
            noiseModule.scrollSpeed = 0.2f;

            // Acende e apaga devagar (nada aparece nem some de repente).
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = obj.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 2f;

            ps.Play();
            _layers.Add(new Layer { System = ps, Zone = zone, Rate = rate, NightBoost = nightBoost });
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;
            // Em volta do que a câmera vê, na altura do chão.
            var eye = camera.transform.position;
            var focus = new Vector3(eye.x, Ground.HeightAt(eye.x, eye.z), eye.z);
            transform.position = focus;
            foreach (var layer in _layers)
            {
                var weight = Weight(layer.Zone, focus);
                if (layer.NightBoost) weight *= Mathf.Lerp(0.25f, 1.6f, Atmosphere.Night);
                var emission = layer.System.emission;
                emission.rateOverTime = layer.Rate * weight;
            }
        }

        /// <summary>1 no meio da região, 0 longe dela (some aos poucos na borda).</summary>
        private float Weight(string zoneId, Vector3 at)
        {
            if (!_zones.TryGetValue(zoneId, out var zone)) return 0f;
            var distance = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(zone.CenterX, zone.CenterZ));
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.Radius * 0.8f, zone.Radius * 1.3f, distance));
        }
    }
}
