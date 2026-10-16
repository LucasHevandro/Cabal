using System.Collections.Generic;
using Game.Client.Fx;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using Msg = Game.Shared.Net;

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
        [SerializeField] private List<SkillData> skills = PlayerRules.StarterSkills();

        [Header("Vida e mana")]
        [SerializeField] private float mpRegenPerSecond = PlayerRules.MpRegenPerSecond;
        [SerializeField] private float respawnSeconds = PlayerRules.RespawnSeconds;

        [Header("SP")]
        [SerializeField] private int maxSp = PlayerRules.MaxSp;
        [SerializeField] private int startingSp = PlayerRules.StartingSp;
        [Tooltip("SP ganho por acerto fora do combo.")]
        [SerializeField] private int spPerHit = PlayerRules.SpPerHit;

        [Header("Dano (fórmula em Game.Shared.Combat.DamageFormula)")]
        [Tooltip("Ataque extra somado ao do nível (equipamento virá depois).")]
        [SerializeField] private int attackBonus;
        [SerializeField, Range(0f, 1f)] private float critChance = (float)PlayerRules.CritChance;
        [SerializeField] private float critMultiplier = (float)PlayerRules.CritMultiplier;
        [SerializeField] private int finisherBaseDamage = PlayerRules.FinisherBaseDamage;
        [SerializeField] private float finisherRange = PlayerRules.FinisherRange;

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
        public float BodyRadius => PlayerRules.BodyRadius;
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

        /// <summary>Modo online e já dentro do mundo: o servidor decide dano, vida, morte e recursos.</summary>
        private static bool Online => GameMode.IsOnline && NetworkSession.Instance != null && NetworkSession.Instance.IsWelcomed;

        private static double ServerNow => NetworkSession.Instance != null ? NetworkSession.Instance.ServerNow : 0;

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
                if (!GameMode.IsOnline && now >= _respawnAt) Respawn();
                return;
            }
            if (ConnectMenu.IsOpen) return;

            if (Combo.Tick(now)) OnComboEnded("Tempo esgotado");
            KeepComboOnTargetDeath();
            if (!GameMode.IsOnline) RegenerateMp();

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
            if (Online) NetworkSession.Instance.Send(new Msg.StartCombo { TargetId = Target.NetworkId, ClientTime = ServerNow });
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
            BestChain = Mathf.Max(BestChain, chain);
            if (Online)
            {
                NetworkSession.Instance.Send(new Msg.Finisher { TargetId = target.NetworkId });
                return;
            }
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

            if (Online)
            {
                // O servidor julga o mesmo instante do aperto (no relógio dele) e manda o resultado.
                NetworkSession.Instance.Send(new Msg.UseSkill { Slot = (byte)index, TargetId = target.NetworkId, ClientTime = ServerNow });
                if (skill.Kind == SkillKind.Magical && CombatFx.Instance != null)
                {
                    SoundPlayer.Play(SoundId.Cast, 0.6f);
                    var from = transform.position + Vector3.up * 1.4f + transform.forward * 0.6f;
                    CombatFx.Instance.LaunchProjectile(from, target.transform, Vector3.up * 1.1f, ProjectileColor(grade), null);
                }
                return;
            }

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
            EnterDeath();
            _respawnAt = Time.time + respawnSeconds;
        }

        private void EnterDeath()
        {
            IsDead = true;
            _respawnAt = float.PositiveInfinity;
            if (Combo.State == ComboState.Active) Combo.Cancel();
            _selector.Select(null);
            SoundPlayer.Play(SoundId.Miss, 1f, 0f);
            ShowMessage("Você morreu");
            transform.localScale = new Vector3(1f, 0.25f, 1f);
        }

        private void LeaveDeath(string message)
        {
            transform.localScale = Vector3.one;
            IsDead = false;
            ShowMessage(message);
        }

        private void Respawn()
        {
            if (_motor != null) _motor.Teleport(_spawnPoint);
            Hp.Add(Hp.Max);
            Mp.Add(Mp.Max);
            LeaveDeath("Você renasceu na cidade");
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

        // ===== Modo online: o servidor manda, o cliente mostra =====

        /// <summary>Recursos e vida decididos pelo servidor.</summary>
        public void ApplyServerStats(Msg.PlayerStats stats)
        {
            _progression.ApplyServerProgress(stats.Level, stats.Xp);
            Hp.SetMax(stats.MaxHp);
            Hp.Add(stats.Hp - Hp.Current);
            Mp.SetMax(stats.MaxMp);
            Mp.Add(stats.Mp - Mp.Current);
            Sp.SetMax(stats.MaxSp);
            Sp.Add(stats.Sp - Sp.Current);

            if (stats.Hp == 0 && !IsDead) EnterDeath();
            else if (stats.Hp > 0 && IsDead) LeaveDeath("Você renasceu na vila");
        }

        /// <summary>O servidor é quem sabe se o combo continua; a previsão local se ajusta.</summary>
        public void ReconcileCombo(Msg.ComboState state)
        {
            if (!state.Active && Combo.State == ComboState.Active)
            {
                Combo.Cancel();
                var reason = (ComboEndReason)state.LastEndReason switch
                {
                    ComboEndReason.Missed => "fora da janela (servidor)",
                    ComboEndReason.TimedOut => "tempo esgotado (servidor)",
                    ComboEndReason.Finished => "finalizado",
                    _ => "interrompido",
                };
                OnComboEnded(reason);
            }
        }

        /// <summary>Resultado de um golpe do jogador (este ou outro) num monstro.</summary>
        public void ShowServerHit(Msg.CombatEvent hit, Targetable target, bool mine)
        {
            if (target == null) return;
            var grade = hit.Grade == 255 ? (ComboGrade?)null : (ComboGrade)hit.Grade;
            var finisher = hit.Source == Msg.CombatSource.Finisher;
            var color = finisher ? new Color(1f, 0.45f, 0.1f) : DamageColor(grade, hit.Critical);
            if (!mine) color = Color.Lerp(color, Color.gray, 0.5f);

            var text = finisher ? $"FINAL {hit.Damage}" : hit.Critical ? $"{hit.Damage}!" : hit.Damage.ToString();
            DamageNumbers.Spawn(target.HeadPosition, text, color, finisher ? 38f : mine ? (hit.Critical ? 32f : 26f) : 20f);
            if (CombatFx.Instance != null)
                CombatFx.Instance.Sparks(target.CenterPosition, color, finisher ? 45 : grade == ComboGrade.Excellent || hit.Critical ? 22 : 12);
            if (!mine) return;

            if (!string.IsNullOrEmpty(hit.RouteName) && (LastRoute == null || LastRoute.Name != hit.RouteName || Time.unscaledTime - LastRouteAt > 0.5f))
            {
                // A rota já foi prevista localmente; só mostra se a previsão não mostrou.
                ShowMessage($"Rota: {hit.RouteName}");
            }
            var heavy = finisher || grade == ComboGrade.Excellent;
            SoundPlayer.Play(finisher ? SoundId.Finisher : heavy ? SoundId.HitHeavy : SoundId.Hit, 0.9f);
            HitStop.Trigger(finisher ? 0.12f : hitStopSeconds + (heavy ? excellentHitStopBonus : 0f));
            if (_camera != null) _camera.Shake(shakeAmplitude * (finisher ? 4f : heavy ? 2f : 1f), finisher ? 0.3f : 0.12f);
        }

        /// <summary>Golpe de monstro em mim, decidido pelo servidor (a vida chega em PlayerStats).</summary>
        public void ShowIncomingHit(Msg.CombatEvent hit)
        {
            var head = transform.position + Vector3.up * 2.2f;
            if (hit.Dodged)
            {
                DamageNumbers.Spawn(head, "Esquivou!", new Color(0.7f, 1f, 0.7f), 24f);
                return;
            }
            DamageNumbers.Spawn(head, hit.Critical ? $"-{hit.Damage}!" : $"-{hit.Damage}", new Color(1f, 0.3f, 0.3f), hit.Critical ? 30f : 24f);
            SoundPlayer.Play(hit.Source == Msg.CombatSource.BossArea ? SoundId.HitHeavy : SoundId.Hit, 0.7f);
            if (_camera != null) _camera.Shake(shakeAmplitude * (hit.Critical ? 3f : 1.5f), 0.15f);
        }

        /// <summary>Mostra um aviso curto (usado também pelos avisos do servidor).</summary>
        public void ShowNotice(string text) => ShowMessage(text);

        private void ShowMessage(string text)
        {
            Message = text;
            MessageAt = Time.unscaledTime;
        }
    }
}
