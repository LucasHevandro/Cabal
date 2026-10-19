using System;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Projétil teleguiado simples: voa até o alvo, avisa a chegada e se destrói.</summary>
    public sealed class Projectile : MonoBehaviour
    {
        private const float MaxLifetime = 3f;
        private const float TrailInterval = 0.02f;

        private Transform _target;
        private Vector3 _targetOffset;
        private float _speed;
        private Action _onArrive;
        private float _diesAt;
        private float _nextTrailAt;

        /// <summary>Chamado ao longo do voo para deixar um rastro (opcional).</summary>
        public Action<Vector3> TrailEmitter { get; set; }

        public void Launch(Transform target, Vector3 targetOffset, float speed, Action onArrive)
        {
            _target = target;
            _targetOffset = targetOffset;
            _speed = speed;
            _onArrive = onArrive;
            _diesAt = Time.time + MaxLifetime;
        }

        private void Update()
        {
            if (_target == null || Time.time > _diesAt)
            {
                Arrive();
                return;
            }

            var destination = _target.position + _targetOffset;
            var toDestination = destination - transform.position;
            if (toDestination.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toDestination);
            transform.position = Vector3.MoveTowards(transform.position, destination, _speed * Time.deltaTime);

            if (TrailEmitter != null && Time.time >= _nextTrailAt)
            {
                TrailEmitter(transform.position);
                _nextTrailAt = Time.time + TrailInterval;
            }

            if ((transform.position - destination).sqrMagnitude < 0.04f) Arrive();
        }

        private void Arrive()
        {
            var callback = _onArrive;
            _onArrive = null;
            callback?.Invoke();
            Destroy(gameObject);
        }
    }
}
