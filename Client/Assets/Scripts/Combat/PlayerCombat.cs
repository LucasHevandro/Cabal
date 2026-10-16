using System.Collections.Generic;
using Game.Client.Fx;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Combat;
using Game.Shared.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.Combat
{
    /// <summary>
    /// Combate do jogador no protótipo offline: skills nas teclas 1 a 4, combo na tecla Q e finalizador na F.
    /// As regras do combo vêm do <see cref="ComboSession"/> do Shared; aqui ficam entrada, efeitos e dano.
    /// </summary>
    [RequireComponent(typeof(TargetSelector))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Skills (teclas 1 a 4)")]
        [SerializeField] private List<SkillDefinition> skills = new List<SkillDefinition>
        {
            new SkillDefinition("Corte", SkillKind.Physical, 120, 2.5f, 0.5f),
            new SkillDefinition("Giro de Lâmina", SkillKind.Physical, 90, 2.5f, 2.0f, areaRadius: 3.5f),
            new SkillDefinition("Lança de Força", SkillKind.Magical, 160, 12f, 1.5f),
            new SkillDefinition("Explosão Arcana", SkillKind.Magical, 220, 10f, 5.0f, areaRadius: 4f),
        };

        [Header("SP")]
        [SerializeField] private int maxSp = 2000;
        [SerializeField] private int startingSp = 1000;
        [Tooltip("SP ganho por acerto fora do combo.")]
        [SerializeField] private int spPerHit = 60;

        [Header("Dano (fórmula em Game.Shared.Combat.DamageFormula)")]
        [SerializeField] private int attackPower = 80;
        [SerializeField, Range(0f, 1f)] private float critChance = 0.1f;
        [SerializeField] private float critMultiplier = 1.5f;
        [SerializeField] private int finisherBaseDamage = 400;
        [SerializeField] private float finisherRange = 3f;

        [Header("Combo")]
        [SerializeField] private ComboTuning comboTuning = new ComboTuning();

        [Header("Sensação de impacto")]
        [SerializeField] private float hitStopSeconds = 0.035f;
        [SerializeField] private float excellentHitStopBonus = 0.03f;
        [SerializeField] private float shakeAmplitude = 0.06f;

        private readonly List<Targetable> _hitBuffer = new List<Targetable>();
        private readonly IRandomSource _random = new SystemRandomSource();
        private TargetSelector _selector;
        private PlayerMotor _motor;
        private OrbitCamera _camera;
        private float[] _readyAt;
        private float _unlockMovementAt;

        public ComboSession Combo { get; private set; }
        public ResourcePool Sp { get; private set; }
        public IReadOnlyList<SkillDefinition> Skills => skills;
        public Targetable Target => _selector != null ? _selector.Current : null;

        /// <summary>Última nota do combo e quando aconteceu (para o HUD).</summary>
        public ComboGrade LastGrade { get; private set; }
        public float LastGradeAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Última rota de combo completada e quando (para o HUD).</summary>
        public ComboRoute LastRoute { get; private set; }
        public float LastRouteAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Mensagem curta de feedback (ex.: "Fora de alcance") e quando apareceu.</summary>
        public string Message { get; private set; } = "";
        public float MessageAt { get; private set; } = float.NegativeInfinity;

        /// <summary>Maior corrente feita nesta sessão de jogo.</summary>
        public int BestChain { get; private set; }

        public float CooldownRemaining(int index) => Mathf.Max(0, _readyAt[index] - Time.time);

        private void Awake()
        {
            _selector = GetComponent<TargetSelector>();
            _motor = GetComponent<PlayerMotor>();
            Sp = new ResourcePool(maxSp, startingSp);
            Combo = new ComboSession(comboTuning.ToConfig());
            _readyAt = new float[skills.Count];
        }

        private void Start()
        {
            if (Camera.main != null) _camera = Camera.main.GetComponent<OrbitCamera>();
        }

        private void Update()
        {
            var now = Time.time;
            if (_motor != null) _motor.MovementLocked = now < _unlockMovementAt;

            if (Combo.Tick(now)) OnComboEnded("Tempo esgotado");

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.qKey.wasPressedThisFrame) RequestStartCombo();
            if (keyboard.fKey.wasPressedThisFrame) RequestFinisher();
            if (keyboard.digit1Key.wasPressedThisFrame) RequestSkill(0);
            if (keyboard.digit2Key.wasPressedThisFrame) RequestSkill(1);
            if (keyboard.digit3Key.wasPressedThisFrame) RequestSkill(2);
            if (keyboard.digit4Key.wasPressedThisFrame) RequestSkill(3);
        }

        // Ações públicas: usadas pelo teclado hoje e, depois, pela rede, IA e testes automáticos.

        /// <summary>Tenta iniciar o combo no alvo atual.</summary>
        public void RequestStartCombo() => TryStartCombo(Time.time);

        /// <summary>Tenta usar o finalizador.</summary>
        public void RequestFinisher() => TryFinisher();

        /// <summary>Tenta usar a skill do slot <paramref name="index"/> (0 a 3).</summary>
        public void RequestSkill(int index) => UseSkill(index, Time.time);

        private void TryStartCombo(double now)
        {
            if (Combo.State == ComboState.Active) return;
            if (Target == null || !Target.IsAlive)
            {
                ShowMessage("Selecione um alvo (Tab ou clique)");
                return;
            }
            var cost = Combo.Config.ActivationSpCost;
            if (!Sp.TrySpend(cost))
            {
                ShowMessage($"SP insuficiente ({Sp.Current}/{cost})");
                SoundPlayer.Play(SoundId.Error, 0.6f, 0f);
                return;
            }
            Combo.TryStart(now);
            SoundPlayer.Play(SoundId.ComboStart, 0.8f, 0f);
            ShowMessage("Combo! Aperte as skills quando o marcador estiver na janela");
        }

        private void TryFinisher()
        {
            if (!Combo.CanFinish) return;
            var target = Target;
            if (target == null || !InRange(target, finisherRange))
            {
                ShowMessage("Chegue perto do alvo para finalizar");
                return;
            }

            var chain = Combo.HitCount;
            Combo.TryFinish(out var multiplier);
            if (_motor != null) _motor.FacePoint(target.transform.position);
            var hit = RollDamage(finisherBaseDamage, multiplier, target);
            var dealt = target.ApplyDamage(hit.Amount, hit.Critical);
            DamageNumbers.Spawn(target.HeadPosition, $"FINAL {dealt}", new Color(1f, 0.45f, 0.1f), 38f);
            SoundPlayer.Play(SoundId.Finisher, 1f, 0f);
            if (CombatFx.Instance != null) CombatFx.Instance.Sparks(target.CenterPosition, new Color(1f, 0.5f, 0.15f), 45);

            BestChain = Mathf.Max(BestChain, chain);
            HitStop.Trigger(0.12f);
            if (_camera != null) _camera.Shake(shakeAmplitude * 4f, 0.3f);
            ShowMessage($"Finalizador x{multiplier:0.00} depois de {chain} acertos");
        }

        private void UseSkill(int index, double now)
        {
            if (index >= skills.Count) return;
            var skill = skills[index];
            var target = Target;

            if (target == null || !target.IsAlive)
            {
                ShowMessage("Selecione um alvo (Tab ou clique)");
                return;
            }

            if (Time.time < _readyAt[index])
            {
                // Como no CABAL: usar skill em recarga durante o combo quebra a corrente.
                if (Combo.State == ComboState.Active)
                {
                    Combo.Cancel();
                    OnComboEnded($"{skill.name} em recarga");
                }
                else
                {
                    ShowMessage($"{skill.name} em recarga");
                    SoundPlayer.Play(SoundId.Error, 0.6f, 0f);
                }
                return;
            }

            if (!InRange(target, skill.range))
            {
                ShowMessage("Fora de alcance");
                SoundPlayer.Play(SoundId.Error, 0.6f, 0f);
                return;
            }

            var multiplier = 1.0;
            var inCombo = Combo.State == ComboState.Active;
            ComboGrade? grade = null;
            if (inCombo)
            {
                var result = Combo.Press(now, skill.kind);
                LastGrade = result.Grade;
                LastGradeAt = Time.unscaledTime;
                if (result.ComboEnded)
                {
                    OnComboEnded("Fora da janela");
                }
                else
                {
                    grade = result.Grade;
                    multiplier = result.DamageMultiplier;
                    SoundPlayer.Play(grade == ComboGrade.Excellent ? SoundId.Excellent : SoundId.Good, 0.7f, 0.02f);
                }
                if (result.Route != null)
                {
                    LastRoute = result.Route;
                    LastRouteAt = Time.unscaledTime;
                    SoundPlayer.Play(SoundId.Route, 0.7f, 0f);
                }
            }

            _readyAt[index] = Time.time + skill.cooldown;
            _unlockMovementAt = Time.time + skill.lockSeconds;
            if (_motor != null) _motor.FacePoint(target.transform.position);

            // Skills mágicas viajam como projétil; o dano (já decidido agora) é aplicado na chegada.
            if (skill.kind == SkillKind.Magical && CombatFx.Instance != null)
            {
                SoundPlayer.Play(SoundId.Cast, 0.6f);
                var origin = transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;
                CombatFx.Instance.LaunchProjectile(origin, target.transform, Vector3.up * 1.1f, ProjectileColor(grade), () =>
                {
                    if (target != null) LandSkill(skill, target, multiplier, inCombo, grade);
                });
            }
            else
            {
                LandSkill(skill, target, multiplier, inCombo, grade);
            }
        }

        /// <summary>O golpe chega ao alvo: dano, faíscas, som, hit stop e tremida.</summary>
        private void LandSkill(SkillDefinition skill, Targetable target, double multiplier, bool inCombo, ComboGrade? grade)
        {
            var totalHits = DealSkillDamage(skill, target, multiplier, grade);
            if (!inCombo && totalHits > 0) Sp.Add(spPerHit);

            var excellent = grade == ComboGrade.Excellent;
            SoundPlayer.Play(excellent ? SoundId.HitHeavy : SoundId.Hit, 0.9f);
            HitStop.Trigger(hitStopSeconds + (excellent ? excellentHitStopBonus : 0f));
            if (_camera != null) _camera.Shake(shakeAmplitude * (excellent ? 2f : 1f), 0.12f);
        }

        private int DealSkillDamage(SkillDefinition skill, Targetable mainTarget, double multiplier, ComboGrade? grade)
        {
            _hitBuffer.Clear();
            if (skill.areaRadius > 0)
            {
                foreach (var candidate in Targetable.All)
                {
                    if (candidate.IsAlive && Vector3.Distance(candidate.transform.position, mainTarget.transform.position) <= skill.areaRadius)
                        _hitBuffer.Add(candidate);
                }
            }
            else
            {
                _hitBuffer.Add(mainTarget);
            }

            foreach (var target in _hitBuffer)
            {
                var hit = RollDamage(skill.baseDamage, multiplier, target);
                var dealt = target.ApplyDamage(hit.Amount, hit.Critical);
                var color = DamageColor(grade, hit.Critical);
                DamageNumbers.Spawn(target.HeadPosition, hit.Critical ? $"{dealt}!" : dealt.ToString(),
                    color, hit.Critical ? 32f : 26f);
                if (CombatFx.Instance != null)
                    CombatFx.Instance.Sparks(target.CenterPosition, color, grade == ComboGrade.Excellent || hit.Critical ? 22 : 12);
            }
            return _hitBuffer.Count;
        }

        private static Color DamageColor(ComboGrade? grade, bool critical)
        {
            if (critical) return new Color(1f, 0.55f, 0.15f);
            if (grade == ComboGrade.Excellent) return new Color(1f, 0.88f, 0.25f);
            if (grade == ComboGrade.Good) return new Color(0.6f, 0.9f, 1f);
            return Color.white;
        }

        private static Color ProjectileColor(ComboGrade? grade) =>
            grade == ComboGrade.Excellent ? new Color(1f, 0.85f, 0.3f) : new Color(0.45f, 0.65f, 1f);

        private DamageResult RollDamage(int skillBaseDamage, double multiplier, Targetable target) =>
            DamageFormula.Calculate(
                new DamageInput(skillBaseDamage, attackPower, multiplier, critChance, critMultiplier, target.Defense),
                _random);

        private bool InRange(Targetable target, float range)
        {
            var distance = Vector3.Distance(transform.position, target.transform.position) - target.Radius;
            return distance <= range;
        }

        private void OnComboEnded(string reason)
        {
            BestChain = Mathf.Max(BestChain, Combo.HitCount);
            if (Combo.LastEndReason != ComboEndReason.Finished)
            {
                LastGrade = ComboGrade.Miss;
                LastGradeAt = Time.unscaledTime;
                SoundPlayer.Play(SoundId.Miss, 0.7f, 0f);
            }
            ShowMessage($"Combo encerrado: {reason} ({Combo.HitCount} acertos)");
        }

        private void ShowMessage(string text)
        {
            Message = text;
            MessageAt = Time.unscaledTime;
        }
    }
}
