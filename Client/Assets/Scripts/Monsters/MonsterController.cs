using System;
using Game.Client.Combat;
using Game.Client.Fx;
using Game.Shared.Combat;
using Game.Shared.Monsters;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;

namespace Game.Client.Monsters
{
    /// <summary>
    /// Corpo de um monstro na cena: executa o que a <see cref="MonsterBrain"/> do Shared pede
    /// (andar, virar, avisar e soltar o golpe), recebe dano como <see cref="Targetable"/> e renasce.
    /// </summary>
    [RequireComponent(typeof(Targetable), typeof(CharacterController))]
    public sealed class MonsterController : MonoBehaviour
    {
        private const float Gravity = -20f;

        private MonsterData _data;
        private MonsterBrain _brain;
        private Targetable _target;
        private CharacterController _controller;
        private GameObject _body;
        private Transform _telegraph;
        private float _telegraphStartedAt;
        private float _verticalVelocity;
        private float _respawnAt;
        private Vector3 _home;

        private static readonly IRandomSource Random = new SystemRandomSource();

        public MonsterData Data => _data;
        public MonsterState State => _brain?.State ?? MonsterState.Idle;
        public bool IsDead => _brain != null && _brain.State == MonsterState.Dead;

        /// <summary>Disparado quando o monstro morre (para XP e drop).</summary>
        public event Action<MonsterController> Killed;

        /// <summary>Monta o monstro. Chamado pelo <see cref="MonsterSpawner"/> logo depois de criar o objeto.</summary>
        public void Initialize(MonsterData data, GameObject body, Transform telegraph)
        {
            _data = data;
            _body = body;
            _telegraph = telegraph;
            _home = transform.position;
            _brain = new MonsterBrain(data, ToPlane(_home), Random);
            _brain.Revive(Time.time);
        }

        private void Awake()
        {
            _target = GetComponent<Targetable>();
            _controller = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            _target.Damaged += OnDamaged;
            _target.Died += OnDied;
        }

        private void OnDisable()
        {
            _target.Damaged -= OnDamaged;
            _target.Died -= OnDied;
        }

        private void Update()
        {
            if (_brain == null) return;

            if (IsDead)
            {
                if (Time.time >= _respawnAt) Respawn();
                return;
            }

            var player = PlayerCombat.Local;
            Vector2? playerPoint = player != null && !player.IsDead ? ToPlane(player.transform.position) : (Vector2?)null;
            var action = _brain.Tick(Time.time, ToPlane(transform.position), playerPoint);

            var horizontal = Vector3.zero;
            switch (action.Kind)
            {
                case MonsterActionKind.MoveTo:
                    var toPoint = ToWorld(action.Point) - transform.position;
                    toPoint.y = 0;
                    if (toPoint.sqrMagnitude > 0.01f)
                    {
                        horizontal = toPoint.normalized * _data.MoveSpeed;
                        Face(toPoint);
                    }
                    break;
                case MonsterActionKind.Face:
                    Face(ToWorld(action.Point) - transform.position);
                    break;
                case MonsterActionKind.AttackStarted:
                    Face(ToWorld(action.Point) - transform.position);
                    ShowTelegraph();
                    break;
                case MonsterActionKind.AttackLanded:
                    HideTelegraph();
                    LandAttack(player);
                    break;
                case MonsterActionKind.ResetHealth:
                    _target.RestoreFullHealth();
                    break;
            }

            if (_brain.State != MonsterState.WindUp) HideTelegraph();
            else UpdateTelegraph();

            if (_controller.isGrounded && _verticalVelocity < 0) _verticalVelocity = -2f;
            _verticalVelocity += Gravity * Time.deltaTime;
            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        private void LandAttack(PlayerCombat player)
        {
            if (player == null || player.IsDead) return;

            // Um pouco de folga: o jogador precisa sair de fato do alcance (ou esquivar) para escapar.
            var distance = Vector3.Distance(transform.position, player.transform.position) - player.BodyRadius;
            if (distance > _data.AttackRange * 1.3f) return;

            var hit = DamageFormula.Calculate(
                new DamageInput(_data.AttackDamage, _data.AttackPower, 1.0, 0.05, 1.5, player.Defense), Random);
            player.TakeHit(hit.Amount, hit.Critical);
        }

        private void OnDamaged(int amount, bool critical) => _brain?.Provoke();

        private void OnDied(Targetable target)
        {
            _brain.Kill();
            _respawnAt = Time.time + _data.RespawnSeconds;
            HideTelegraph();
            if (CombatFx.Instance != null) CombatFx.Instance.Sparks(_target.CenterPosition, new Color(0.9f, 0.9f, 0.9f), 30);
            SetVisible(false);
            Killed?.Invoke(this);
        }

        private void Respawn()
        {
            _controller.enabled = false;
            transform.position = _home;
            _controller.enabled = true;
            _target.RestoreFullHealth();
            _brain.Revive(Time.time);
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            if (_body != null) _body.SetActive(visible);
            _controller.enabled = visible;
        }

        private void ShowTelegraph()
        {
            if (_telegraph == null) return;
            _telegraphStartedAt = Time.time;
            _telegraph.gameObject.SetActive(true);
            UpdateTelegraph();
        }

        /// <summary>O aviso no chão cresce até o tamanho do alcance quando o golpe vai sair.</summary>
        private void UpdateTelegraph()
        {
            if (_telegraph == null || !_telegraph.gameObject.activeSelf) return;
            var progress = _data.AttackWindupSeconds <= 0 ? 1f
                : Mathf.Clamp01((Time.time - _telegraphStartedAt) / _data.AttackWindupSeconds);
            var size = (_data.AttackRange + _target.Radius) * 2f * Mathf.Lerp(0.3f, 1f, progress);
            _telegraph.localScale = new Vector3(size, 0.01f, size);
        }

        private void HideTelegraph()
        {
            if (_telegraph != null && _telegraph.gameObject.activeSelf) _telegraph.gameObject.SetActive(false);
        }

        private void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
        }

        private static Vector2 ToPlane(Vector3 world) => new Vector2(world.x, world.z);

        private Vector3 ToWorld(Vector2 point) => new Vector3(point.X, transform.position.y, point.Y);
    }
}
