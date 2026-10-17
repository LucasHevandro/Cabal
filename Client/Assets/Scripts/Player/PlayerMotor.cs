using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Player
{
    /// <summary>
    /// Movimento em terceira pessoa relativo à câmera: andar (WASD), correr (Shift) e esquivar (Espaço).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movimento")]
        [SerializeField] private float walkSpeed = 4.5f;
        [SerializeField] private float runSpeed = 7.5f;
        [SerializeField] private float turnSpeedDegrees = 720f;
        [SerializeField] private float gravity = -25f;

        [Header("Esquiva")]
        [SerializeField] private float dodgeDistance = 4.5f;
        [SerializeField] private float dodgeDuration = 0.22f;
        [SerializeField] private float dodgeCooldown = 0.8f;

        [Header("Referências")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController _controller;
        private float _verticalVelocity;
        private Vector3 _dodgeDirection;
        private float _dodgeEndsAt;
        private float _nextDodgeAt;

        /// <summary>Verdadeiro durante a esquiva.</summary>
        public bool IsDodging => Time.time < _dodgeEndsAt;

        /// <summary>Velocidade horizontal atual (para animação e HUD).</summary>
        public float CurrentSpeed { get; private set; }

        /// <summary>Quando falso, o jogador não anda (ex.: durante um golpe pesado).</summary>
        public bool MovementLocked { get; set; }

        public void SetCamera(Transform cam) => cameraTransform = cam;

        /// <summary>Quantas esquivas já começaram (a rede avisa o servidor quando muda).</summary>
        public int DodgeCount { get; private set; }

        /// <summary>Move o jogador na hora, sem colisão (correção do servidor, renascer).</summary>
        public void Teleport(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = new Vector3(position.x, transform.position.y, position.z);
            _controller.enabled = true;
            _verticalVelocity = 0;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            // Nulo enquanto digita no chat: o personagem fica parado (mas a gravidade e a esquiva em curso continuam).
            var keyboard = UI.GameInput.Keyboard;
            var input = keyboard != null ? ReadMoveInput(keyboard) : Vector2.zero;
            var moveDirection = ToWorldDirection(input);

            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && Time.time >= _nextDodgeAt)
            {
                // Esquiva na direção do movimento; parado, esquiva para trás.
                _dodgeDirection = moveDirection.sqrMagnitude > 0.01f ? moveDirection : -transform.forward;
                _dodgeEndsAt = Time.time + dodgeDuration;
                _nextDodgeAt = Time.time + dodgeCooldown;
                DodgeCount++;
            }

            Vector3 horizontal;
            if (IsDodging)
            {
                horizontal = _dodgeDirection * (dodgeDistance / dodgeDuration);
            }
            else if (MovementLocked)
            {
                horizontal = Vector3.zero;
            }
            else
            {
                var speed = keyboard != null && keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;
                horizontal = moveDirection * speed;
                if (moveDirection.sqrMagnitude > 0.01f) FaceDirection(moveDirection);
            }

            if (_controller.isGrounded && _verticalVelocity < 0) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            CurrentSpeed = horizontal.magnitude;
            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        /// <summary>Gira o jogador para olhar um ponto (usado ao atacar um alvo).</summary>
        public void FacePoint(Vector3 point)
        {
            var direction = point - transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(direction);
        }

        private void FaceDirection(Vector3 direction)
        {
            var target = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegrees * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput(Keyboard keyboard)
        {
            var x = (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0);
            var y = (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private Vector3 ToWorldDirection(Vector2 input)
        {
            if (input.sqrMagnitude < 0.01f) return Vector3.zero;
            var reference = cameraTransform != null ? cameraTransform : transform;
            var forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
            return (forward * input.y + right * input.x).normalized;
        }
    }
}
