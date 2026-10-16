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
    [RequireComponent(typeof(TargetSelector), typeof(PlayerProgression))]
    public sealed class PlayerCombat : MonoBehaviour
    {
        [Header("Skills (teclas 1 a 4)")]
        [SerializeField] private List<SkillData> skills = new List<SkillData>
        {
            new SkillData("corte", "Corte", SkillKind.Physical, 120, 2.5f, 0.5f),
            new SkillData("giro_de_lamina", "Giro de Lâmina", SkillKind.Physical, 90, 2.5f, 2.0f, areaRadius: 3.5f),
            new SkillData("lanca_de_forca", "Lança de Força", SkillKind.Magical, 160, 12f, 1.5f, mpCost: 40),
            new SkillData("explosao_arcana", "Explosão Arcana", SkillKind.Magical, 220, 10f, 5.0f, areaRadius: 4f, mpCost: 90),
        };

        [Header("Vida e mana")]
        [SerializeField] private float mpRegenPerSecond = 20f;
        [SerializeField] private float respawnSeconds = 4f;

        [Header("SP")]
        [SerializeField] private int maxSp = 2000;
        [SerializeField] private int startingSp = 1000;
        [Tooltip("SP ganho por acerto fora do combo.")]
        [SerializeField] private int spPerHit = 60;

        [Header("Dano (fórmula em Game.Shared.Combat.DamageFormula)")]
        [Tooltip("Ataque extra somado ao do nível (equipamento virá depois).")]
        [SerializeField] private int attackBonus;
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
        private PlayerProgression _progression;
        private PlayerMotor _motor;
        private OrbitCamera _camera;
        private SkillCooldowns _cooldowns;
        private float _unlockMovementAt;
        private float _mpRegenCarry;
        private Vector3 _spawnPoint;
        private float _respawnAt;

        /// <summary>O jogador local (o protótipo tem um só).</summary>
        public static PlayerCombat Local { get; private set; }

        public ComboSession Combo { get; private set; }
        public int Defense => _progression.Stats.Defense;
        public int AttackPower => _progression.Stats.AttackPower + attackBonus;
        public int Level => _progression.Level;
        public bool IsDead { get; private set; }

        /// <summary>Segundos até renascer (0 se vivo).</summary>
        public float RespawnRemaining => IsDead ? Mathf.Max(0, _respawnAt - Time.time) : 0f;

        /// <summary>Raio do corpo do jogador, para o alcance dos monstros.</summary>
        public float BodyRadius => 0.4f;
        public ResourcePool Hp { get; private set; }
        public ResourcePool Mp { get; private set; }
        public ResourcePool Sp { get; private set; }
        public IReadOnlyList<SkillData> Skills => skills;
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

        public float CooldownRemaining(int index) => (float)_cooldowns.Remaining(index, Time.time);

        private void Awake()
        {
            _selector = GetComponent<TargetSelector>();
            _progression = GetComponent<PlayerProgression>();
            _motor = GetComponent<PlayerMotor>();
            foreach (var skill in skills) skill.Validate();
            var stats = _progression.Stats;
            Hp = new ResourcePool(stats.MaxHp);
            Mp = new ResourcePool(stats.MaxMp);
            _progression.LeveledUp += OnLeveledUp;
            Sp = new ResourcePool(maxSp, startingSp);
            Combo = new ComboSession(comboTuning.ToConfig());
            _cooldowns = new SkillCooldowns(skills.Count);
            _spawnPoint = transform.position;
            Local = this;
        }

        private void OnDestroy()
        {
            if (Local == this) Local = null;
        }

        private void Start()
        {
            if (Camera.main != null) _camera = Camera.main.GetComponent<OrbitCamera>();
        }

        private void Update()
        {
            var now = Time.time;
            if (_motor != null) _motor.MovementLocked = IsDead || now < _unlockMovementAt;

            if (IsDead)
            {
                if (now >= _respawnAt) Respawn();
                return;
            }

            if (Combo.Tick(now)) OnComboEnded("Tempo esgotado");
            KeepComboOnTargetDeath();
            RegenerateMp();

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
            double? distance = target != null && target.IsAlive ? DistanceToEdge(target) : (double?)null;

            switch (SkillRules.Check(skill, index, _cooldowns, Mp, distance, Time.time))
            {
                case SkillUseCheck.NoTarget:
                    ShowMessage("Selecione um alvo (Tab ou clique)");
                    return;
                case SkillUseCheck.OnCooldown:
                    // Como no CABAL: usar skill em recarga durante o combo quebra a corrente.
                    if (Combo.State == ComboState.Active)
                    {
                        Combo.Cancel();
                        OnComboEnded($"{skill.Name} em recarga");
                    }
                    else
                    {
                        Refuse($"{skill.Name} em recarga");
                    }
                    return;
                case SkillUseCheck.OutOfRange:
                    Refuse("Fora de alcance");
                    return;
                case SkillUseCheck.NotEnoughMp:
                    Refuse($"MP insuficiente ({Mp.Current}/{skill.MpCost})");
                    return;
            }

            var multiplier = 1.0;
            var inCombo = Combo.State == ComboState.Active;
            ComboGrade? grade = null;
            if (inCombo)
            {
                var result = Combo.Press(now, skill.Kind);
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

            SkillRules.TryUse(skill, index, _cooldowns, Mp, distance, Time.time);
            _unlockMovementAt = Time.time + skill.LockSeconds;
            if (_motor != null) _motor.FacePoint(target.transform.position);

            // Skills mágicas viajam como projétil; o dano (já decidido agora) é aplicado na chegada.
            if (skill.Kind == SkillKind.Magical && CombatFx.Instance != null)
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
        private void LandSkill(SkillData skill, Targetable target, double multiplier, bool inCombo, ComboGrade? grade)
        {
            var totalHits = DealSkillDamage(skill, target, multiplier, grade);
            if (!inCombo && totalHits > 0) Sp.Add(spPerHit);

            var excellent = grade == ComboGrade.Excellent;
            SoundPlayer.Play(excellent ? SoundId.HitHeavy : SoundId.Hit, 0.9f);
            HitStop.Trigger(hitStopSeconds + (excellent ? excellentHitStopBonus : 0f));
            if (_camera != null) _camera.Shake(shakeAmplitude * (excellent ? 2f : 1f), 0.12f);
        }

        private int DealSkillDamage(SkillData skill, Targetable mainTarget, double multiplier, ComboGrade? grade)
        {
            _hitBuffer.Clear();
            if (skill.AreaRadius > 0)
            {
                foreach (var candidate in Targetable.All)
                {
                    if (candidate.IsAlive && Vector3.Distance(candidate.transform.position, mainTarget.transform.position) <= skill.AreaRadius)
                        _hitBuffer.Add(candidate);
                }
            }
            else
            {
                _hitBuffer.Add(mainTarget);
            }

            foreach (var target in _hitBuffer)
            {
                var hit = RollDamage(skill.BaseDamage, multiplier, target);
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
                new DamageInput(skillBaseDamage, AttackPower, multiplier, critChance, critMultiplier, target.Defense),
                _random);

        private bool InRange(Targetable target, float range) => DistanceToEdge(target) <= range;

        /// <summary>Distância até a borda do alvo, como as regras do Shared esperam.</summary>
        private double DistanceToEdge(Targetable target) =>
            Vector3.Distance(transform.position, target.transform.position) - target.Radius;

        /// <summary>
        /// Recebe um golpe de monstro. Durante a esquiva o golpe erra.
        /// Retorna o dano sofrido (0 se esquivou ou já estava morto).
        /// </summary>
        public int TakeHit(int amount, bool critical)
        {
            if (IsDead) return 0;
            if (_motor != null && _motor.IsDodging)
            {
                DamageNumbers.Spawn(transform.position + Vector3.up * 2.2f, "Esquivou!", new Color(0.7f, 1f, 0.7f), 24f);
                return 0;
            }

            var taken = -Hp.Add(-amount);
            DamageNumbers.Spawn(transform.position + Vector3.up * 2.2f, critical ? $"-{taken}!" : $"-{taken}",
                new Color(1f, 0.3f, 0.3f), critical ? 30f : 24f);
            SoundPlayer.Play(SoundId.Hit, 0.7f);
            if (_camera != null) _camera.Shake(shakeAmplitude * (critical ? 3f : 1.5f), 0.15f);

            if (Hp.Current == 0) Die();
            return taken;
        }

        /// <summary>Subiu de nível: novos máximos de HP e MP, e volta cheio.</summary>
        private void OnLeveledUp(int level)
        {
            var stats = _progression.Stats;
            Hp.SetMax(stats.MaxHp);
            Mp.SetMax(stats.MaxMp);
            Hp.Add(Hp.Max);
            Mp.Add(Mp.Max);
            ShowMessage($"Nível {level}! HP {stats.MaxHp} · MP {stats.MaxMp} · ataque {stats.AttackPower} · defesa {stats.Defense}");
        }

        private void Die()
        {
            IsDead = true;
            _respawnAt = Time.time + respawnSeconds;
            if (Combo.State == ComboState.Active) Combo.Cancel();
            _selector.Select(null);
            SoundPlayer.Play(SoundId.Miss, 1f, 0f);
            ShowMessage("Você morreu");
            transform.localScale = new Vector3(1f, 0.25f, 1f);
        }

        private void Respawn()
        {
            var controller = GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            transform.position = _spawnPoint;
            if (controller != null) controller.enabled = true;
            transform.localScale = Vector3.one;

            Hp.Add(Hp.Max);
            Mp.Add(Mp.Max);
            IsDead = false;
            ShowMessage("Você renasceu na cidade");
        }

        /// <summary>
        /// Se o alvo morrer no meio do combo, pula para o próximo alvo perto para não quebrar a corrente;
        /// sem outro alvo, o combo termina.
        /// </summary>
        private void KeepComboOnTargetDeath()
        {
            if (Combo.State != ComboState.Active) return;
            if (Target != null && Target.IsAlive) return;

            _selector.SelectNext();
            if (Target == null || !Target.IsAlive)
            {
                Combo.Cancel();
                OnComboEnded("Sem alvo");
            }
        }

        private void RegenerateMp()
        {
            _mpRegenCarry += mpRegenPerSecond * Time.deltaTime;
            var whole = Mathf.FloorToInt(_mpRegenCarry);
            if (whole <= 0) return;
            _mpRegenCarry -= whole;
            Mp.Add(whole);
        }

        private void Refuse(string message)
        {
            ShowMessage(message);
            SoundPlayer.Play(SoundId.Error, 0.6f, 0f);
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
