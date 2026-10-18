using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Luz de fogo que tremula (braseiros, tochas): intensidade e alcance variam com ruído.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class FlickerLight : MonoBehaviour
    {
        private Light _light;
        private float _intensity;
        private float _range;
        private float _seed;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _intensity = _light.intensity;
            _range = _light.range;
            _seed = Random.value * 100f;
        }

        private void Update()
        {
            var n = Mathf.PerlinNoise(_seed, Time.time * 6f);
            _light.intensity = _intensity * (0.75f + 0.5f * n);
            _light.range = _range * (0.92f + 0.16f * n);
        }
    }
}
