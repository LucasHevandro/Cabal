using System.Collections.Generic;
using System.Linq;
using Game.Client.Fx;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.UI;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Core;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;
using UnityEngine;
using Msg = Game.Shared.Net;
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
        private CharacterClass? _classSkillsFor;

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

        /// <summary>Golpes deste jogador que acertaram (confirmados pelo servidor no online); o tutorial acompanha.</summary>
        public int HitsLanded { get; private set; }

        /// <summary>Finalizadores que acertaram.</summary>
        public int FinishersLanded { get; private set; }

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
            if (!Online) ApplyClassModel(CharacterClass.Blade);
        }

        /// <summary>
        /// Troca a cápsula pelo modelo da classe (Visuals/class/blade ou arcanist), se existir. As formas simples ficam
        /// escondidas, não apagadas: sem modelo para a classe nova, elas voltam.
        /// </summary>
        private void ApplyClassModel(CharacterClass characterClass)
        {
            if (_classModel != null) Destroy(_classModel);
            var key = characterClass == CharacterClass.Arcanist ? "arcanist" : "blade";
            _classModel = VisualCatalog.Spawn(VisualCatalog.Class, key, transform);
            if (_classModel != null && _lookWeapon.Length > 0) WeaponFx.SetLook(this, _lookWeapon, _lookUpgrade);
            foreach (var name in new[] { "Corpo", "Frente" })
            {
                var part = transform.Find(name);
                if (part != null && part.TryGetComponent<Renderer>(out var renderer)) renderer.enabled = _classModel == null;
            }
        }

        private GameObject _classModel;
        private string _lookWeapon = "";
        private int _lookUpgrade;

        /// <summary>Arma equipada e melhoria (do servidor): o modelo na mão e o brilho acompanham.</summary>
        public void ApplyLook(string weaponId, int upgrade)
        {
            _lookWeapon = weaponId ?? "";
            _lookUpgrade = upgrade;
            WeaponFx.SetLook(this, _lookWeapon, _lookUpgrade);
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

            if (Combo.Tick(now)) OnComboEnded(Loc.T("combo.reason_timeout"));
            KeepComboOnTargetDeath();
            if (!GameMode.IsOnline) RegenerateMp();

            var keyboard = UI.GameInput.Keyboard;
            if (keyboard == null) return;

            if (KeyBindings.Pressed(GameAction.StartCombo)) RequestStartCombo();
            if (KeyBindings.Pressed(GameAction.Finisher)) RequestFinisher();
            if (KeyBindings.Pressed(GameAction.Skill1)) RequestSkill(0);
            if (KeyBindings.Pressed(GameAction.Skill2)) RequestSkill(1);
            if (KeyBindings.Pressed(GameAction.Skill3)) RequestSkill(2);
            if (KeyBindings.Pressed(GameAction.Skill4)) RequestSkill(3);
            if (KeyBindings.Pressed(GameAction.Skill5)) RequestSkill(4);
            if (KeyBindings.Pressed(GameAction.Skill6)) RequestSkill(5);
            if (KeyBindings.Pressed(GameAction.Skill7)) RequestSkill(6);
            if (KeyBindings.Pressed(GameAction.Skill8)) RequestSkill(7);
            if (KeyBindings.Pressed(GameAction.Awakening) && Online) NetworkSession.Instance.Send(new Msg.ActivateAwakening());
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
            if (Online && CharacterState.HasSheet && !CharacterState.Sheet.ComboUnlocked)
            {
                Refuse(Loc.T("combo.locked", QuestCatalog.Get(ClassCatalog.ComboQuestId).Name));
                return;
            }
            if (Target == null || !Target.IsAlive)
            {
                ShowMessage(Loc.T("combat.need_target_hint", KeyBindings.Label(GameAction.NextTarget)));
                return;
            }
            var cost = Combo.Config.ActivationSpCost;
            if (!Sp.TrySpend(cost))
            {
                ShowMessage(Loc.T("combo.no_sp", Sp.Current, cost));
                SoundPlayer.Play(SoundId.Error, 0.6f, 0f);
                return;
            }
            Combo.TryStart(now);
            if (Online) NetworkSession.Instance.Send(new Msg.StartCombo { TargetId = Target.NetworkId, ClientTime = ServerNow });
            SoundPlayer.Play(SoundId.ComboStart, 0.8f, 0f);
            ShowMessage(Loc.T("combo.started"));
        }

        private void TryFinisher()
        {
            if (!Combo.CanFinish) return;
            var target = Target;
            if (target == null || !InRange(target, finisherRange))
            {
                ShowMessage(Loc.T("combo.finisher_closer"));
                return;
            }

            var chain = Combo.HitCount;
            Combo.TryFinish(out var multiplier);
            if (_motor != null) _motor.FacePoint(target.transform.position);
            VisualAnimator.Attack(this, AttackStyle.Finisher);
            _finisherDue = Time.time + 0.45f; // a lâmina desce no meio do golpe de duas mãos
            BestChain = Mathf.Max(BestChain, chain);
            if (Online)
            {
                NetworkSession.Instance.Send(new Msg.Finisher { TargetId = target.NetworkId });
                return;
            }
            var hit = RollDamage(finisherBaseDamage, multiplier, target);
            var dealt = target.ApplyDamage(hit.Amount, hit.Critical);
            FinishersLanded++;
            DamageNumbers.Spawn(target.HeadPosition, Loc.T("fx.final", dealt), new Color(1f, 0.45f, 0.1f), 38f);
            SoundPlayer.Play(SoundId.Finisher, 1f, 0f);
            if (CombatFx.Instance != null) CombatFx.Instance.Sparks(target.CenterPosition, new Color(1f, 0.5f, 0.15f), 45);

            BestChain = Mathf.Max(BestChain, chain);
            HitStop.Trigger(0.12f);
            if (_camera != null) _camera.Shake(shakeAmplitude * 4f, 0.3f);
            ShowMessage(Loc.T("combo.finisher", multiplier, chain));
        }

        private void UseSkill(int index, double now)
        {
            if (index >= skills.Count) return;
            var skill = skills[index];
            if (Online && !CharacterState.SkillUnlocked(index))
            {
                Refuse(Loc.T("skill.locked_detail", skill.Name, UnlockText(index)));
                return;
            }
            var target = Target;
            double? distance = target != null && target.IsAlive ? DistanceToEdge(target) : (double?)null;

            switch (SkillRules.Check(skill, index, _cooldowns, Mp, distance, Time.time))
            {
                case SkillUseCheck.NoTarget:
                    ShowMessage(Loc.T("combat.need_target_hint", KeyBindings.Label(GameAction.NextTarget)));
                    return;
                case SkillUseCheck.OnCooldown:
                    // Como no CABAL: usar skill em recarga durante o combo quebra a corrente.
                    if (Combo.State == ComboState.Active)
                    {
                        Combo.Cancel();
                        OnComboEnded(Loc.T("skill.cooldown", skill.Name));
                    }
                    else
                    {
                        Refuse(Loc.T("skill.cooldown", skill.Name));
                    }
                    return;
                case SkillUseCheck.OutOfRange:
                    Refuse(Loc.T("combat.out_of_range"));
                    return;
                case SkillUseCheck.NotEnoughMp:
                    Refuse(Loc.T("combat.no_mp", Mp.Current, skill.MpCost));
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
                    OnComboEnded(Loc.T("combo.reason_window"));
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

            // O golpe da skill (SkillMoves): a animação toca agora e o efeito cai quando a arma acerta (ou a magia sai).
            // Excellent sai um pouco mais rápido. A nota e a rota do combo já apareceram no aperto, acima.
            var move = SkillMoves.For(skill.Id);
            var pace = grade == ComboGrade.Excellent ? 1.15f : 1f;
            var impact = (move != null ? move.FirstImpact : 0f) / pace;
            VisualAnimator.Attack(this, AttackStyle.For(skill), pace);
            WeaponFx.Play(this, skill, grade);
            var magical = skill.Kind == SkillKind.Magical && CombatFx.Instance != null;
            if (!magical)
            {
                After(Mathf.Max(0f, impact - 0.1f), () => SoundPlayer.Play(SoundId.SwordSwing, 0.55f, 0.1f));
                After(impact, () => SkillFx.OnCast(skill, transform));
            }

            if (Online)
            {
                // O servidor julga o mesmo instante do aperto (no relógio dele) e manda o resultado; o que ele mandar
                // deste golpe só aparece quando a arma chega no alvo (ShowServerHit espera até lá).
                NetworkSession.Instance.Send(new Msg.UseSkill { Slot = (byte)index, TargetId = target.NetworkId, ClientTime = ServerNow });
                var due = impact;
                if (magical)
                {
                    due += SkillFx.TravelSeconds(skill, WeaponFx.TipOf(this) ?? transform.position + Vector3.up * 1.4f, target.CenterPosition);
                    After(impact, () =>
                    {
                        if (target == null) return;
                        SoundPlayer.Play(SoundId.Cast, 0.6f);
                        SkillFx.Launch(skill, transform, target.transform, Vector3.up * 1.1f, grade, null, WeaponFx.TipOf(this));
                    });
                }
                _impactDue[index] = Time.time + due;
                return;
            }

            // Skills mágicas viajam como projétil; o dano (já decidido agora) é aplicado na chegada.
            if (magical)
            {
                After(impact, () =>
                {
                    if (target == null) return;
                    SoundPlayer.Play(SoundId.Cast, 0.6f);
                    SkillFx.Launch(skill, transform, target.transform, Vector3.up * 1.1f, grade, () =>
                    {
                        if (target != null) LandSkill(skill, target, multiplier, inCombo, grade);
                    }, WeaponFx.TipOf(this));
                });
            }
            else
            {
                After(impact, () =>
                {
                    if (target != null && target.IsAlive) LandSkill(skill, target, multiplier, inCombo, grade);
                });
            }
        }

        // Quando o efeito de cada golpe meu (por posição da skill) deve aparecer no online; o finalizador à parte.
        private readonly Dictionary<int, float> _impactDue = new Dictionary<int, float>();
        private float _finisherDue;

        /// <summary>Roda <paramref name="action"/> daqui a <paramref name="seconds"/> (na hora, se for 0).</summary>
        private void After(float seconds, System.Action action)
        {
            if (seconds <= 0f) action();
            else StartCoroutine(AfterRoutine(seconds, action));
        }

        private static System.Collections.IEnumerator AfterRoutine(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        /// <summary>O golpe chega ao alvo: dano, faíscas, som, hit stop e tremida.</summary>
        private void LandSkill(SkillData skill, Targetable target, double multiplier, bool inCombo, ComboGrade? grade)
        {
            var totalHits = DealSkillDamage(skill, target, multiplier, grade);
            if (!inCombo && totalHits > 0) Sp.Add(spPerHit);
            ImpactFeedback(skill, grade == ComboGrade.Excellent, _lastImpactWeight);
        }

        private float _lastImpactWeight;

        /// <summary>
        /// Som, hit stop e tremida de um golpe que acertou, pelo peso da skill (<see cref="SkillFx"/>): golpes grandes
        /// (Execução, Meteoro, Cataclismo, Fenda Sísmica) param mais o quadro, tremem mais a câmera e soam mais graves.
        /// </summary>
        private void ImpactFeedback(SkillData skill, bool excellent, float weight)
        {
            var heavy = excellent || weight >= 1f;
            SoundPlayer.Play(heavy ? SoundId.HitHeavy : SoundId.Hit, 0.9f);
            if (skill != null && skill.Kind == SkillKind.Magical) SoundPlayer.Play(SoundId.MagicImpact, 0.35f + 0.25f * Mathf.Min(1f, weight));
            HitStop.Trigger(hitStopSeconds + (excellent ? excellentHitStopBonus : 0f) + weight * 0.04f);
            if (_camera != null) _camera.Shake(shakeAmplitude * ((excellent ? 2f : 1f) + weight * 1.5f), 0.12f + weight * 0.1f);
        }

        private int DealSkillDamage(SkillData skill, Targetable mainTarget, double multiplier, ComboGrade? grade)
        {
            _lastImpactWeight = 0f;
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

            if (_hitBuffer.Count > 0) HitsLanded++;
            foreach (var target in _hitBuffer)
            {
                var hit = RollDamage(skill.BaseDamage, multiplier, target);
                var dealt = target.ApplyDamage(hit.Amount, hit.Critical);
                var color = DamageColor(grade, hit.Critical);
                DamageNumbers.Spawn(target.HeadPosition, hit.Critical ? $"{dealt}!" : dealt.ToString(),
                    color, hit.Critical ? 32f : 26f);
                _lastImpactWeight = Mathf.Max(_lastImpactWeight, SkillFx.OnImpact(skill, target.CenterPosition, grade, hit.Critical));
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
                DamageNumbers.Spawn(transform.position + Vector3.up * 2.2f, Loc.T("fx.dodged"), new Color(0.7f, 1f, 0.7f), 24f);
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
            // Online quem avisa e calcula os novos valores (com atributos e equipamento) é o servidor.
            if (GameMode.IsOnline) return;
            var stats = _progression.Stats;
            Hp.SetMax(stats.MaxHp);
            Mp.SetMax(stats.MaxMp);
            Hp.Add(Hp.Max);
            Mp.Add(Mp.Max);
            ShowMessage(Loc.T("level.up_offline", level, stats.MaxHp, stats.MaxMp, stats.AttackPower, stats.Defense));
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
            ShowMessage(Loc.T("player.died"));
            // Com modelo animado, ele cai (animação de morte); sem modelo, a cápsula achata.
            if (_classModel != null) VisualAnimator.SetDead(this, true);
            else transform.localScale = new Vector3(1f, 0.25f, 1f);
        }

        private void LeaveDeath(string message)
        {
            transform.localScale = Vector3.one;
            VisualAnimator.SetDead(this, false);
            IsDead = false;
            ShowMessage(message);
        }

        private void Respawn()
        {
            if (_motor != null) _motor.Teleport(_spawnPoint);
            Hp.Add(Hp.Max);
            Mp.Add(Mp.Max);
            LeaveDeath(Loc.T("respawn.city"));
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
                OnComboEnded(Loc.T("combat.no_target"));
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
            ShowMessage(Loc.T("combo.ended", reason, Combo.HitCount));
        }

        // ===== Modo online: o servidor manda, o cliente mostra =====

        /// <summary>
        /// Ficha do personagem: a classe define as 8 skills da barra e a rota de combo própria. Troca só quando a
        /// classe muda (na primeira ficha), para não zerar recargas a cada atualização.
        /// </summary>
        public void ApplyCharacterSheet(Msg.CharacterSheet sheet)
        {
            var characterClass = (CharacterClass)sheet.Class;
            if (_classSkillsFor == characterClass) return;
            _classSkillsFor = characterClass;
            ApplyClassModel(characterClass);
            skills = ClassCatalog.SkillsFor(characterClass).Select(s => s.Skill).ToList();
            _cooldowns = new SkillCooldowns(skills.Count);
            var config = comboTuning.ToConfig();
            config.Routes.Add(ClassCatalog.RouteFor(characterClass));
            Combo = new ComboSession(config);
        }

        /// <summary>O que falta para liberar a skill da barra (para o HUD e os avisos).</summary>
        public string UnlockText(int index, bool compact = false)
        {
            if (_classSkillsFor == null || index >= skills.Count) return "";
            var classSkill = ClassCatalog.SkillsFor(_classSkillsFor.Value)[index];
            if (string.IsNullOrEmpty(classSkill.RequiredQuestId)) return Loc.T("skill.unlock_level", classSkill.RequiredLevel);
            return compact
                ? Loc.T("skill.unlock_level_quest_short", classSkill.RequiredLevel)
                : Loc.T("skill.unlock_level_quest", classSkill.RequiredLevel, QuestCatalog.Get(classSkill.RequiredQuestId).Name);
        }

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
            else if (stats.Hp > 0 && IsDead) LeaveDeath(Loc.T("respawn.village"));
        }

        /// <summary>O servidor é quem sabe se o combo continua; a previsão local se ajusta.</summary>
        public void ReconcileCombo(Msg.ComboState state)
        {
            if (!state.Active && Combo.State == ComboState.Active)
            {
                Combo.Cancel();
                var reason = (ComboEndReason)state.LastEndReason switch
                {
                    ComboEndReason.Missed => Loc.T("combo.reason_window_server"),
                    ComboEndReason.TimedOut => Loc.T("combo.reason_timeout_server"),
                    ComboEndReason.Finished => Loc.T("combo.reason_finished"),
                    _ => Loc.T("combo.reason_interrupted"),
                };
                OnComboEnded(reason);
            }
        }

        /// <summary>Resultado de um golpe do jogador (este ou outro) num monstro.</summary>
        public void ShowServerHit(Msg.CombatEvent hit, Targetable target, bool mine, bool waited = false)
        {
            if (target == null) return;
            // Meu golpe: o resultado do servidor costuma chegar antes de a arma (ou a magia) chegar no alvo; espera.
            if (mine && !waited)
            {
                var due = hit.Source == Msg.CombatSource.Finisher ? _finisherDue : _impactDue.TryGetValue(hit.Source, out var at) ? at : 0f;
                var wait = due - Time.time;
                if (wait > 0.02f)
                {
                    After(Mathf.Min(wait, 1.5f), () => ShowServerHit(hit, target, true, true));
                    return;
                }
            }
            var grade = hit.Grade == 255 ? (ComboGrade?)null : (ComboGrade)hit.Grade;
            var finisher = hit.Source == Msg.CombatSource.Finisher;
            if (mine && finisher) FinishersLanded++;
            else if (mine) HitsLanded++;
            var color = finisher ? new Color(1f, 0.45f, 0.1f) : DamageColor(grade, hit.Critical);
            if (!mine) color = Color.Lerp(color, Color.gray, 0.5f);

            var text = finisher ? Loc.T("fx.final", hit.Damage) : hit.Critical ? $"{hit.Damage}!" : hit.Damage.ToString();
            DamageNumbers.Spawn(target.HeadPosition, text, color, finisher ? 38f : mine ? (hit.Critical ? 32f : 26f) : 20f);
            var skill = mine && !finisher && hit.Source < skills.Count ? skills[hit.Source] : null;
            var weight = 0f;
            if (skill != null) weight = SkillFx.OnImpact(skill, target.CenterPosition, grade, hit.Critical);
            else if (CombatFx.Instance != null)
                CombatFx.Instance.Sparks(target.CenterPosition, color, finisher ? 45 : grade == ComboGrade.Excellent || hit.Critical ? 22 : 12);
            if (!mine) return;

            if (!string.IsNullOrEmpty(hit.RouteId) && (LastRoute == null || LastRoute.Id != hit.RouteId || Time.unscaledTime - LastRouteAt > 0.5f))
            {
                // A rota já foi prevista localmente; só mostra se a previsão não mostrou.
                ShowMessage(Loc.T("combo.route", Loc.T("route." + hit.RouteId)));
            }
            if (finisher)
            {
                SoundPlayer.Play(SoundId.Finisher, 0.9f);
                HitStop.Trigger(0.12f);
                if (_camera != null) _camera.Shake(shakeAmplitude * 4f, 0.3f);
                return;
            }
            ImpactFeedback(skill, grade == ComboGrade.Excellent, weight);
        }

        /// <summary>Golpe de monstro em mim, decidido pelo servidor (a vida chega em PlayerStats).</summary>
        public void ShowIncomingHit(Msg.CombatEvent hit)
        {
            var head = transform.position + Vector3.up * 2.2f;
            if (hit.Dodged)
            {
                DamageNumbers.Spawn(head, Loc.T("fx.dodged"), new Color(0.7f, 1f, 0.7f), 24f);
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
