using Game.Client.Combat;
using Game.Client.Fx;
using Game.Client.UI;
using Game.Shared.Combat;
using Game.Shared.Monsters;
using UnityEngine;

namespace Game.Client.Monsters
{
    /// <summary>
    /// Habilidades extras do chefe. Fase 1: luta normal. Abaixo de 50% de vida entra na fase 2: anuncia,
    /// ataca mais rápido e a cada poucos segundos marca um círculo no chão onde o jogador está; quem não
    /// sair (ou esquivar) até o círculo encher leva um golpe forte.
    /// </summary>
    [RequireComponent(typeof(MonsterController), typeof(Targetable))]
    public sealed class BossAbilities : MonoBehaviour
    {
        [SerializeField] private float phaseTwoAtHpFraction = 0.5f;
        [SerializeField] private float areaInterval = 6f;
        [SerializeField] private float areaWarningSeconds = 1.6f;
        [SerializeField] private float areaRadius = 4f;
        [SerializeField] private int areaDamage = 350;
        [SerializeField] private Material warningMaterial;

        private MonsterController _monster;
        private Targetable _target;
        private bool _phaseTwo;
        private float _nextAreaAt;
        private Transform _warning;
        private float _warningStartedAt;
        private float _baseCooldown;
        private readonly IRandomSource _random = new SystemRandomSource();

        public bool PhaseTwo => _phaseTwo;

        public void SetWarningMaterial(Material material) => warningMaterial = material;

        private void Awake()
        {
            _monster = GetComponent<MonsterController>();
            _target = GetComponent<Targetable>();
        }

        private void Update()
        {
            if (_monster.Data == null) return;
            if (_baseCooldown <= 0) _baseCooldown = _monster.Data.AttackCooldownSeconds;

            if (_monster.IsDead)
            {
                ResetFight();
                return;
            }

            var hpFraction = (float)_target.Hp.Current / _target.Hp.Max;
            if (!_phaseTwo && hpFraction <= phaseTwoAtHpFraction) EnterPhaseTwo();
            if (_phaseTwo && hpFraction >= 0.999f) ResetFight(); // voltou para casa e se curou

            var fighting = _monster.State == MonsterState.Chase || _monster.State == MonsterState.WindUp;
            if (_warning != null) UpdateWarning();
            else if (_phaseTwo && fighting && Time.time >= _nextAreaAt) StartArea();
        }

        private void EnterPhaseTwo()
        {
            _phaseTwo = true;
            _monster.Data.AttackCooldownSeconds = _baseCooldown * 0.6f;
            _nextAreaAt = Time.time + 1.5f;
            CombatHud.Announce($"{_target.DisplayName} ficou furioso!");
            SoundPlayer.Play(SoundId.Finisher, 1f, 0f);
        }

        private void ResetFight()
        {
            if (_warning != null) Destroy(_warning.gameObject);
            _warning = null;
            if (!_phaseTwo) return;
            _phaseTwo = false;
            _monster.Data.AttackCooldownSeconds = _baseCooldown;
        }

        private void StartArea()
        {
            var player = PlayerCombat.Local;
            if (player == null || player.IsDead) return;

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Aviso de área do chefe";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.position = new Vector3(player.transform.position.x, 0.04f, player.transform.position.z);
            if (warningMaterial != null) disc.GetComponent<Renderer>().sharedMaterial = warningMaterial;
            _warning = disc.transform;
            _warningStartedAt = Time.time;
            UpdateWarning();
        }

        /// <summary>O círculo enche do centro até a borda; cheio, o golpe cai.</summary>
        private void UpdateWarning()
        {
            var progress = Mathf.Clamp01((Time.time - _warningStartedAt) / areaWarningSeconds);
            var size = areaRadius * 2f * Mathf.Lerp(0.15f, 1f, progress);
            _warning.localScale = new Vector3(size, 0.01f, size);
            if (progress < 1f) return;

            var center = _warning.position;
            Destroy(_warning.gameObject);
            _warning = null;
            _nextAreaAt = Time.time + areaInterval;

            if (CombatFx.Instance != null) CombatFx.Instance.Sparks(center + Vector3.up * 0.3f, new Color(1f, 0.3f, 0.2f), 50);
            SoundPlayer.Play(SoundId.HitHeavy, 1f, 0f);

            var player = PlayerCombat.Local;
            if (player == null || player.IsDead) return;
            var offset = player.transform.position - center;
            offset.y = 0;
            if (offset.magnitude > areaRadius + player.BodyRadius) return;

            var hit = DamageFormula.Calculate(
                new DamageInput(areaDamage, _monster.Data.AttackPower, 1.0, 0, 1, player.Defense), _random);
            player.TakeHit(hit.Amount, hit.Critical);
        }

        private void OnDestroy()
        {
            if (_warning != null) Destroy(_warning.gameObject);
        }
    }
}
