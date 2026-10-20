using Game.Client.Combat;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Recuo de quem apanha: o modelo é empurrado para longe de quem bateu e cambaleia (inclina para trás), e volta com
    /// uma mola em uns 0,3 s. Só o modelo se mexe (a posição de verdade, que vem do servidor, não muda). Monstros grandes
    /// recuam menos; crítico, Excellent e finalizador empurram mais.
    /// </summary>
    public sealed class HitRecoil : MonoBehaviour
    {
        private const float MetersPerStrength = 0.22f;
        private const float DegreesPerStrength = 11f;
        private const float Stiffness = 140f;
        private const float Damping = 18f;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _hasRest;
        private Vector3 _offset; // no espaço do pai do modelo
        private Vector3 _velocity;
        private float _tilt; // graus, para trás
        private float _tiltVelocity;
        private Vector3 _tiltAxis = Vector3.right;

        /// <summary>Empurra o modelo de <paramref name="target"/> para longe de <paramref name="from"/>.</summary>
        public static void Push(Targetable target, Vector3 from, float strength)
        {
            if (target == null || strength <= 0f) return;
            var animator = target.GetComponentInChildren<VisualAnimator>();
            if (animator == null) return;
            var recoil = animator.GetComponent<HitRecoil>();
            if (recoil == null) recoil = animator.gameObject.AddComponent<HitRecoil>();
            // Bicho grande quase não se mexe: divide pela altura (um personagem tem uns 2 m).
            var height = Mathf.Max(1f, target.HeadPosition.y - target.transform.position.y);
            recoil.Kick(target.transform.position - from, Mathf.Clamp(strength * 2f / height, 0f, 2.5f));
        }

        private void Kick(Vector3 away, float strength)
        {
            enabled = true; // volta a rodar (dorme quando o modelo volta ao lugar)
            if (!_hasRest)
            {
                _restPosition = transform.localPosition;
                _restRotation = transform.localRotation;
                _hasRest = true;
            }
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            away.Normalize();
            var parent = transform.parent;
            var local = parent != null ? parent.InverseTransformDirection(away) : away;
            _velocity += local * (MetersPerStrength * strength * 14f);
            // Inclina para trás em relação a quem bateu: gira em volta do eixo de lado.
            _tiltAxis = Vector3.Cross(Vector3.up, local).normalized;
            if (_tiltAxis.sqrMagnitude < 0.01f) _tiltAxis = Vector3.right;
            _tiltVelocity += DegreesPerStrength * strength * 14f;
        }

        private void LateUpdate()
        {
            if (!_hasRest) return;
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            // Mola amortecida: sai rápido e volta sem ficar balançando.
            _velocity += (-Stiffness * _offset - Damping * _velocity) * dt;
            _offset += _velocity * dt;
            _tiltVelocity += (-Stiffness * _tilt - Damping * _tiltVelocity) * dt;
            _tilt += _tiltVelocity * dt;
            transform.localPosition = _restPosition + _offset;
            transform.localRotation = Quaternion.AngleAxis(_tilt, _tiltAxis) * _restRotation;
            if (_offset.sqrMagnitude < 0.00001f && _velocity.sqrMagnitude < 0.0001f && Mathf.Abs(_tilt) < 0.05f && Mathf.Abs(_tiltVelocity) < 0.1f)
            {
                transform.localPosition = _restPosition;
                transform.localRotation = _restRotation;
                _offset = _velocity = Vector3.zero;
                _tilt = _tiltVelocity = 0f;
                enabled = false; // dorme até o próximo golpe
            }
        }
    }
}
