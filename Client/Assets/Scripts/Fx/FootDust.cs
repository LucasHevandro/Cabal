using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Poeira nos pés: uma nuvenzinha a cada passada de quem corre e um punhado maior numa arrancada (esquiva, investida).
    /// Mede a velocidade pelo próprio movimento, então serve para o jogador, os outros jogadores e os monstros. Na
    /// praça de pedra e na água sai menos (o chão é pintado na vila e o rio fica abaixo da margem).
    /// </summary>
    public sealed class FootDust : MonoBehaviour
    {
        private const float RunSpeed = 3.6f;
        private const float BurstSpeed = 11f;
        private static readonly Color Dust = new Color(0.62f, 0.55f, 0.45f, 0.55f);

        /// <summary>Tamanho da nuvem (1 = personagem; monstros grandes levantam mais).</summary>
        public float Scale = 1f;

        private Vector3 _last;
        private float _nextStep;
        private float _nextBurst;

        private void OnEnable() => _last = transform.position;

        private void LateUpdate()
        {
            var position = transform.position;
            var delta = position - _last;
            _last = position;
            var dt = Time.deltaTime;
            if (dt <= 0f || CombatFx.Instance == null) return;
            delta.y = 0f;
            var speed = delta.magnitude / dt;
            // Teleporte (entrar na dungeon, renascer): não é corrida.
            if (speed > 40f) return;

            var now = Time.time;
            if (speed > BurstSpeed && now >= _nextBurst)
            {
                _nextBurst = now + 0.5f;
                Puff(position, -delta.normalized, 9, 1.4f);
                return;
            }
            if (speed < RunSpeed || now < _nextStep) return;
            // Uma passada a cada ~1,6 m de corrida.
            _nextStep = now + Mathf.Clamp(1.6f / speed, 0.18f, 0.45f);
            Puff(position, -delta.normalized, 2, 1f);
        }

        private void Puff(Vector3 at, Vector3 back, int count, float strength)
        {
            var ground = World.Ground.HeightAt(at.x, at.z);
            if (at.y - ground > 0.6f) return; // no ar (pulo, ponte alta): sem poeira
            for (var i = 0; i < count; i++)
            {
                var spread = Random.insideUnitSphere * 0.25f * Scale;
                spread.y = Mathf.Abs(spread.y) * 0.4f;
                var velocity = (back * Random.Range(0.4f, 1.2f) + Random.insideUnitSphere * 0.35f) * strength;
                velocity.y = Random.Range(0.15f, 0.5f) * strength;
                CombatFx.Instance.Glow(new Vector3(at.x, ground + 0.12f, at.z) + spread, velocity, Dust,
                    Random.Range(0.35f, 0.6f) * Scale * strength, Random.Range(0.45f, 0.75f));
            }
        }
    }
}
