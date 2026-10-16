using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Combat
{
    /// <summary>
    /// Boneco de treino: nunca morre de vez, recupera a vida depois de um tempo sem apanhar
    /// e mede o dano por segundo (DPS) dos últimos segundos.
    /// </summary>
    [RequireComponent(typeof(Targetable))]
    public sealed class TrainingDummy : MonoBehaviour
    {
        [SerializeField] private float resetAfterSeconds = 4f;
        [SerializeField] private float dpsWindowSeconds = 5f;

        private readonly Queue<(float time, int amount)> _recentHits = new Queue<(float, int)>();
        private Targetable _target;
        private float _lastHitAt = float.NegativeInfinity;
        private Vector3 _restScale;
        private float _wobbleEndsAt;

        /// <summary>Dano por segundo na janela recente.</summary>
        public float Dps { get; private set; }

        /// <summary>Dano total desde o último reset.</summary>
        public int TotalDamage { get; private set; }

        private void Awake()
        {
            // Os bonecos são só do modo offline: online o servidor não sabe deles.
            if (Game.Client.Net.GameMode.IsOnline)
            {
                gameObject.SetActive(false);
                return;
            }
            _target = GetComponent<Targetable>();
            _restScale = transform.localScale;
        }

        private void OnEnable()
        {
            if (_target == null) return;
            _target.Damaged += OnDamaged;
            _target.Died += OnDied;
        }

        private void OnDisable()
        {
            if (_target == null) return;
            _target.Damaged -= OnDamaged;
            _target.Died -= OnDied;
        }

        private void Update()
        {
            while (_recentHits.Count > 0 && Time.time - _recentHits.Peek().time > dpsWindowSeconds) _recentHits.Dequeue();
            var sum = 0;
            foreach (var hit in _recentHits) sum += hit.amount;
            Dps = _recentHits.Count == 0 ? 0 : sum / dpsWindowSeconds;

            if (Time.time - _lastHitAt > resetAfterSeconds && _target.Hp.Current < _target.Hp.Max)
            {
                _target.Hp.Add(_target.Hp.Max);
                TotalDamage = 0;
            }

            // Balança rapidamente ao apanhar.
            var wobble = Mathf.Max(0, _wobbleEndsAt - Time.time) / 0.15f;
            transform.localScale = _restScale + new Vector3(0.12f, -0.08f, 0.12f) * wobble;
        }

        private void OnDamaged(int amount, bool critical)
        {
            _lastHitAt = Time.time;
            _recentHits.Enqueue((Time.time, amount));
            TotalDamage += amount;
            _wobbleEndsAt = Time.time + 0.15f;
        }

        private void OnDied(Targetable target)
        {
            // Boneco de treino não morre: volta com a vida cheia.
            target.Hp.Add(target.Hp.Max);
        }
    }
}
