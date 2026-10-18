using System;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Animação por código para modelos montados em partes (animais e golem, que não têm esqueleto): patas que balançam
    /// conforme a velocidade, pulinho (coelho), respiração parado, bote no ataque, recuo ao apanhar e tombo na morte.
    /// Recebe do <see cref="VisualAnimator"/> os mesmos sinais de um Animator (Speed, Dead, Attack, Hit).
    /// As partes são montadas no Editor (CreatureBuilder).
    /// </summary>
    public sealed class ProceduralRig : MonoBehaviour
    {
        [Serializable]
        public struct Limb
        {
            public Transform Pivot;

            /// <summary>Defasagem na passada (0 a 1): patas opostas andam alternadas.</summary>
            public float Phase;

            /// <summary>Quanto gira (graus) andando, e em volta de qual eixo local.</summary>
            public float Swing;

            public Vector3 Axis;

            /// <summary>Giro extra no ataque (braços do golem, presas): 0 = não participa.</summary>
            public float AttackSwing;
        }

        [SerializeField] private Transform body;
        [SerializeField] private Limb[] limbs = Array.Empty<Limb>();
        [SerializeField] private float strideLength = 1f;
        [SerializeField] private bool hop;
        [SerializeField] private float bobHeight = 0.04f;
        [SerializeField] private float lunge = 0.3f;

        public float Speed { get; set; }
        public bool Dead { get; set; }

        private Quaternion[] _rest = Array.Empty<Quaternion>();
        private Vector3 _bodyPosition;
        private Quaternion _bodyRotation;
        private float _phase;
        private float _attack = -1f;
        private float _hit = -1f;
        private float _death;
        private float _seed;

        public void Configure(Transform bodyPart, Limb[] parts, float stride, bool hops, float bob, float lungeDistance)
        {
            body = bodyPart;
            limbs = parts;
            strideLength = stride;
            hop = hops;
            bobHeight = bob;
            lunge = lungeDistance;
        }

        /// <summary>Attack ou Hit (os outros sinais são ignorados).</summary>
        public void Trigger(string name)
        {
            if (name == "Attack") _attack = 0f;
            else if (name == "Hit" && _attack < 0f) _hit = 0f;
        }

        private void Awake()
        {
            _rest = new Quaternion[limbs.Length];
            for (var i = 0; i < limbs.Length; i++) _rest[i] = limbs[i].Pivot != null ? limbs[i].Pivot.localRotation : Quaternion.identity;
            if (body != null)
            {
                _bodyPosition = body.localPosition;
                _bodyRotation = body.localRotation;
            }
            _seed = UnityEngine.Random.value * 10f;
        }

        private void Update()
        {
            if (body == null) return;
            var dt = Time.deltaTime;

            // Morte: tomba de lado e afunda um pouco; as patas ficam paradas.
            _death = Mathf.MoveTowards(_death, Dead ? 1f : 0f, dt * (Dead ? 2.2f : 4f));
            var fall = _death * _death * (3f - 2f * _death);

            var moving = Mathf.Clamp01(Speed / 2f);
            _phase += dt * Mathf.Max(0.0f, Speed) / Mathf.Max(0.1f, strideLength);
            var cycle = _phase * Mathf.PI * 2f;
            var breathe = Mathf.Sin((Time.time + _seed) * 2.2f) * 0.012f;

            var position = _bodyPosition;
            var pitch = 0f;
            if (hop) position.y += Mathf.Abs(Mathf.Sin(cycle * 0.5f)) * 0.25f * moving;
            else position.y += Mathf.Abs(Mathf.Sin(cycle)) * bobHeight * moving;
            position.y += breathe * (1f - moving);

            // Ataque: recolhe e dá o bote para a frente (0,5 s).
            var attackSwing = 0f;
            if (_attack >= 0f)
            {
                _attack += dt / 0.5f;
                var t = Mathf.Clamp01(_attack);
                // -1 = recolhido (preparando), +1 = golpe esticado, 0 = descanso.
                var a = t < 0.4f ? -(t / 0.4f) : t < 0.6f ? Mathf.Lerp(-1f, 1f, (t - 0.4f) / 0.2f) : Mathf.Lerp(1f, 0f, (t - 0.6f) / 0.4f);
                attackSwing = a;
                position.z += (a > 0f ? a : a * 0.3f) * lunge;
                pitch += a * 15f;
                if (_attack >= 1f) _attack = -1f;
            }

            // Dano: recua e treme (0,25 s).
            if (_hit >= 0f)
            {
                _hit += dt / 0.25f;
                var t = Mathf.Clamp01(_hit);
                pitch -= Mathf.Sin(t * Mathf.PI) * 14f;
                position.z -= Mathf.Sin(t * Mathf.PI) * 0.08f;
                if (_hit >= 1f) _hit = -1f;
            }

            position.y -= fall * 0.15f;
            body.localPosition = position;
            body.localRotation = _bodyRotation * Quaternion.Euler(pitch * (1f - fall), 0, fall * 88f);

            for (var i = 0; i < limbs.Length; i++)
            {
                var limb = limbs[i];
                if (limb.Pivot == null) continue;
                var swing = Mathf.Sin(cycle + limb.Phase * Mathf.PI * 2f) * limb.Swing * moving * (1f - fall);
                var extra = limb.AttackSwing * (_attack >= 0f ? attackSwing : 0f);
                limb.Pivot.localRotation = _rest[i] * Quaternion.AngleAxis(swing + extra, limb.Axis);
            }
        }
    }
}
