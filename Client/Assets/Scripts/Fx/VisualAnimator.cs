using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Liga um modelo com Animator aos parâmetros padrão do jogo, se o Animator Controller tiver cada um:
    /// <c>Speed</c> (float, metros por segundo), <c>Dead</c> (bool), <c>Attack</c>, <c>Hit</c> e <c>Dodge</c> (triggers).
    /// Parâmetro que não existe é ignorado; assim um modelo só com "andar" já funciona.
    /// </summary>
    public sealed class VisualAnimator : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int DeadId = Animator.StringToHash("Dead");

        private readonly HashSet<int> _parameters = new HashSet<int>();
        private Animator _animator;
        private Vector3 _lastPosition;
        private float _speed;

        /// <summary>Morto (o objeto do jogo avisa; o modelo pode ter uma pose de queda).</summary>
        public bool Dead { get; set; }

        /// <summary>Dispara um trigger (Attack, Hit, Dodge) no modelo filho de <paramref name="owner"/>, se houver.</summary>
        public static void Trigger(Component owner, string trigger)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>();
            if (visual != null) visual.Fire(trigger);
        }

        /// <summary>Marca morto ou vivo no modelo filho de <paramref name="owner"/>.</summary>
        public static void SetDead(Component owner, bool dead)
        {
            if (owner == null) return;
            var visual = owner.GetComponentInChildren<VisualAnimator>(true);
            if (visual != null) visual.Dead = dead;
        }

        private void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                enabled = false;
                return;
            }
            _animator.applyRootMotion = false; // quem move é o jogo (servidor ou controle), não a animação
            foreach (var parameter in _animator.parameters) _parameters.Add(parameter.nameHash);
            _lastPosition = transform.position;
        }

        private void Update()
        {
            var position = transform.position;
            var delta = position - _lastPosition;
            delta.y = 0;
            _lastPosition = position;
            var instant = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, instant, 1f - Mathf.Exp(-Time.deltaTime * 12f));
            if (_parameters.Contains(SpeedId)) _animator.SetFloat(SpeedId, _speed);
            if (_parameters.Contains(DeadId)) _animator.SetBool(DeadId, Dead);
        }

        private void Fire(string trigger)
        {
            var id = Animator.StringToHash(trigger);
            if (_animator != null && _parameters.Contains(id)) _animator.SetTrigger(id);
        }
    }
}
