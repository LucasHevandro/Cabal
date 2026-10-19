using UnityEngine;

namespace Game.Client.World
{
    /// <summary>Água do rio correndo: a textura desliza devagar ao longo da corrente.</summary>
    public sealed class FlowingWater : MonoBehaviour
    {
        [SerializeField] private float speed = 0.12f;

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private float _offset;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (_renderer == null) return;
            _offset = (_offset + speed * Time.deltaTime) % 1f;
            _block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, _offset, Mathf.Sin(Time.time * 0.3f) * 0.05f));
            _renderer.SetPropertyBlock(_block);
        }
    }

    /// <summary>Bicho de enfeite: começa a animação num ponto qualquer e num ritmo um pouco diferente dos outros.</summary>
    public sealed class FaunaIdle : MonoBehaviour
    {
        private void Start()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.Play(0, -1, Random.value);
            animator.speed = Random.Range(0.85f, 1.15f);
        }
    }

    /// <summary>Luz que fica mais forte à noite (lanternas da vila): fraca de dia, cheia à noite.</summary>
    [RequireComponent(typeof(Light))]
    public sealed class NightLight : MonoBehaviour
    {
        private Light _light;
        private float _intensity;

        private void Awake()
        {
            _light = GetComponent<Light>();
            _intensity = _light.intensity;
        }

        private void Update()
        {
            _light.intensity = _intensity * (0.2f + 1.3f * Atmosphere.Night);
        }
    }
}
