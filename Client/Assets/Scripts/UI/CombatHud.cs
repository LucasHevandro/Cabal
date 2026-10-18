using System.Collections.Generic;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.World;
using Game.Shared.Combat;
using Game.Shared.Progression;
using Game.Shared.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Client.UI
{
    /// <summary>
    /// HUD do protótipo em IMGUI: barra de combo, SP, skills, alvo e mensagens.
    /// Desenhado numa tela virtual de 1920x1080 escalada para a resolução real.
    /// </summary>
    public sealed class CombatHud : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;

        [SerializeField] private PlayerCombat combat;

        private GUIStyle _label;
        private GUIStyle _center;
        private GUIStyle _big;
        private GUIStyle _small;

        private static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color BarBack = new Color(0.12f, 0.12f, 0.14f, 0.9f);
        private static readonly Color SpColor = new Color(0.95f, 0.75f, 0.2f);
        private static readonly Color HpColor = new Color(0.85f, 0.25f, 0.25f);
        private static readonly Color MpColor = new Color(0.25f, 0.45f, 0.95f);
        private static readonly Color WindowColor = new Color(0.25f, 0.75f, 0.35f, 0.85f);
        private static readonly Color ExcellentColor = new Color(1f, 0.9f, 0.3f, 0.95f);
        private static readonly Color PhysicalColor = new Color(0.95f, 0.55f, 0.25f);
        private static readonly Color MagicalColor = new Color(0.45f, 0.6f, 1f);
        private static readonly Color RouteColor = new Color(0.85f, 0.65f, 1f);

        private PlayerProgression _progression;
        private PlayerLoot _loot;
        private bool _showInventory;

        private static string _announcement;
        private static float _announcementAt = float.NegativeInfinity;

        /// <summary>Mostra um aviso grande no topo da tela por alguns segundos.</summary>
        public static void Announce(string text)
        {
            _announcement = text;
            _announcementAt = Time.unscaledTime;
        }

        public void SetCombat(PlayerCombat playerCombat) => combat = playerCombat;

        private void Awake()
        {
            if (combat == null) combat = FindAnyObjectByType<PlayerCombat>();
            if (combat != null)
            {
                _progression = combat.GetComponent<PlayerProgression>();
                _loot = combat.GetComponent<PlayerLoot>();
            }
        }

        private void Update()
        {
            var keyboard = GameInput.Keyboard;
            // Online o inventário de verdade fica nas janelas do jogo (GameWindows).
            if (KeyBindings.Pressed(GameAction.Inventory) && !GameMode.IsOnline) _showInventory = !_showInventory;
            if (KeyBindings.Pressed(GameAction.Map)) Minimap.Toggle();
            if (KeyBindings.Pressed(GameAction.Help)) HelpVisible = !HelpVisible;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Minimap.BigOpen = false;
        }

        // Só GUI (sem GUILayout): sem a passada de layout, o OnGUI roda menos vezes por quadro.
        private void OnEnable() => useGUILayout = false;

        private void OnGUI()
        {
            if (combat == null || combat.Combo == null) return;
            EnsureStyles();

            // Plaquinhas dos monstros ficam em coordenadas de tela, antes da escala do resto do HUD.
            DrawNameplates();

            var scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            GUI.matrix = Matrix4x4.TRS(
                new Vector3((Screen.width - VirtualWidth * scale) / 2, (Screen.height - VirtualHeight * scale) / 2, 0),
                Quaternion.identity, new Vector3(scale, scale, 1));

            if (Event.current.type == EventType.Repaint) PanelRects.Clear();
            PanelRects.Add(new Rect(VirtualWidth - 460, 20, 440, 74)); // zona e conexão
            PanelRects.Add(new Rect(40, VirtualHeight - 150, 380, 96)); // quadro do jogador
            if (!Minimap.BigOpen && !_showInventory && !GameWindows.RightPanelOpen)
                PanelRects.Add(new Rect(VirtualWidth - Minimap.Size - 20, Minimap.Top, Minimap.Size, Minimap.Size));
            DrawHelp();
            DrawTargetFrame();
            DrawComboBar();
            DrawSpBar();
            DrawAwakening();
            DrawPlayerFrame();
            DrawSkillBar();
            DrawGradePopup();
            DrawMessage();
            DrawXpBar();
            DrawZone();
            DrawNetwork();
            DrawAnnouncement();
            DrawDeathOverlay();
            if (_showInventory) DrawInventory();
            // Com o inventário (ou outra janela do lado direito) aberto, o minimapa sai da frente.
            Minimap.Draw(combat.transform, combat.Target, VirtualWidth, VirtualHeight, _showInventory || GameWindows.RightPanelOpen);

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        private readonly string[] _skillKeys = new string[8];
        private int _skillKeysVersion = -1;

        /// <summary>Tecla da skill em negrito, guardada até os atalhos mudarem (a barra é desenhada todo quadro).</summary>
        private string SkillKeyLabel(int index)
        {
            if (_skillKeysVersion != KeyBindings.Version)
            {
                for (var i = 0; i < _skillKeys.Length; i++) _skillKeys[i] = "<b>" + KeyBindings.Label(GameAction.Skill1 + i) + "</b>";
                _skillKeysVersion = KeyBindings.Version;
            }
            return index < _skillKeys.Length ? _skillKeys[index] : "";
        }

        private string _helpText;
        private (int Language, int Keys, bool Online, int BestChain) _helpKey;

        private const string HelpPref = "ajuda.visivel";
        private static bool? _helpVisible;

        /// <summary>Painel de controles aberto (F1 alterna; a escolha fica salva).</summary>
        public static bool HelpVisible
        {
            get
            {
                if (_helpVisible == null)
                {
                    try { _helpVisible = PlayerPrefs.GetInt(HelpPref, 1) == 1; }
                    catch (System.Exception) { _helpVisible = true; }
                }
                return _helpVisible.Value;
            }
            set
            {
                _helpVisible = value;
                try { PlayerPrefs.SetInt(HelpPref, value ? 1 : 0); }
                catch (System.Exception) { /* fica só nesta sessão */ }
            }
        }

        /// <summary>Retângulos do HUD no quadro atual (tela virtual): os nomes dos NPCs não são escritos por cima deles.</summary>
        public static readonly List<Rect> PanelRects = new List<Rect>();

        private void DrawHelp()
        {
            var online = GameMode.IsOnline;
            if (!HelpVisible)
            {
                GUI.Label(new Rect(24, 20, 300, 24), Loc.T("help.show_hint", KeyBindings.Label(GameAction.Help)), _small);
                return;
            }
            PanelRects.Add(new Rect(20, 20, 470, online ? 346 : 270));
            // O texto só muda com o idioma, os atalhos, o modo ou a melhor corrente: montado de novo só nessa hora.
            var key = (Loc.Version, KeyBindings.Version, online, combat.BestChain);
            if (_helpText == null || key != _helpKey)
            {
                _helpKey = key;
                _helpText =
                    Loc.T("help.controls", KeyBindings.MoveLabel(), KeyBindings.Label(GameAction.Run), KeyBindings.Label(GameAction.Dodge),
                        KeyBindings.Label(GameAction.Options), KeyBindings.Label(GameAction.NextTarget), KeyBindings.Label(GameAction.Map),
                        KeyBindings.Label(GameAction.Help)) + "\n" +
                    (online
                        ? Loc.T("help.keys_online", KeyBindings.SkillsLabel(8), KeyBindings.Label(GameAction.Pickup),
                            KeyBindings.Label(GameAction.Inventory), KeyBindings.Label(GameAction.Attributes), KeyBindings.Label(GameAction.Talk),
                            KeyBindings.Label(GameAction.QuestLog), KeyBindings.Label(GameAction.Awakening), KeyBindings.Label(GameAction.Friends))
                        : Loc.T("help.keys_offline", KeyBindings.SkillsLabel(4), KeyBindings.Label(GameAction.Pickup),
                            KeyBindings.Label(GameAction.Inventory))) + "\n" +
                    Loc.T("help.combo", KeyBindings.Label(GameAction.StartCombo), KeyBindings.Label(GameAction.Finisher)) + "\n\n" +
                    Loc.T("help.best_chain", combat.BestChain);
            }
            Box(new Rect(20, 20, 470, online ? 346 : 270), Panel);
            GUI.Label(new Rect(36, 30, 450, online ? 336 : 260), _helpText, _small);
        }

        private void DrawTargetFrame()
        {
            var target = combat.Target;
            if (target == null) return;

            var rect = new Rect(VirtualWidth / 2 - 220, 24, 440, 74);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x, rect.y + 6, rect.width, 24), target.DisplayName, _center);
            var hp = target.Hp;
            Bar(new Rect(rect.x + 16, rect.y + 34, rect.width - 32, 16), (float)hp.Current / Mathf.Max(1, hp.Max), HpColor);
            GUI.Label(new Rect(rect.x, rect.y + 32, rect.width, 20), Loc.T("hud.hp_value", hp.Current, hp.Max), _small.WithCenter());

            var dummy = target.GetComponent<TrainingDummy>();
            if (dummy != null)
                GUI.Label(new Rect(rect.x, rect.y + 52, rect.width, 20), Loc.T("hud.dps", dummy.Dps, dummy.TotalDamage), _small.WithCenter());
        }

        private void DrawComboBar()
        {
            var combo = combat.Combo;
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 300, 660, 34);

            if (combo.State != ComboState.Active)
            {
                var cost = combo.Config.ActivationSpCost;
                var ready = combat.Sp.Has(cost) && combat.Target != null;
                GUI.color = ready ? Color.white : new Color(1, 1, 1, 0.5f);
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height), Loc.T("hud.combo_start", KeyBindings.Label(GameAction.StartCombo), cost), _center);
                GUI.color = Color.white;
                return;
            }

            var now = Time.time;
            Box(new Rect(rect.x - 6, rect.y - 6, rect.width + 12, rect.height + 12), Panel);
            Box(rect, BarBack);

            // Janela-alvo e zona Excellent.
            var start = (float)combo.WindowStart;
            var end = (float)combo.WindowEnd;
            Box(new Rect(rect.x + rect.width * start, rect.y, rect.width * (end - start), rect.height), WindowColor);
            var half = (end - start) / 2f * (float)combo.Config.ExcellentZoneRatio;
            var center = (float)combo.Config.WindowCenter;
            Box(new Rect(rect.x + rect.width * (center - half), rect.y, rect.width * half * 2, rect.height), ExcellentColor);

            // Marcador.
            var cursor = Mathf.Clamp01((float)combo.GetCursor(now));
            Box(new Rect(rect.x + rect.width * cursor - 3, rect.y - 8, 6, rect.height + 16), Color.white);

            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), Loc.T("hud.combo_hits", combo.HitCount), _big.WithAlignment(TextAnchor.MiddleLeft));
            GUI.Label(new Rect(rect.x, rect.y - 46, rect.width, 36), Loc.T("hud.combo_bonus", combo.ChainBonus * 100), _label.WithAlignment(TextAnchor.MiddleRight));
            if (combo.CanFinish)
                GUI.Label(new Rect(rect.x, rect.y + rect.height + 6, rect.width, 24), Loc.T("hud.combo_finish", KeyBindings.Label(GameAction.Finisher)), _center);

            // Últimos tipos acertados na corrente, para o jogador planejar a próxima rota.
            var kinds = combo.ChainKinds;
            var shown = Mathf.Min(6, kinds.Count);
            for (var i = 0; i < shown; i++)
            {
                var kind = kinds[kinds.Count - shown + i];
                var chip = new Rect(rect.x + i * 30, rect.y + rect.height + 8, 26, 22);
                Box(chip, kind == SkillKind.Physical ? PhysicalColor : MagicalColor);
                GUI.Label(chip, kind == SkillKind.Physical ? Loc.T("hud.kind_physical_short") : Loc.T("hud.kind_magical_short"), _small.WithCenter());
            }
        }

        private void DrawXpBar()
        {
            if (_progression == null) return;
            var progress = _progression.Progress;
            var rect = new Rect(0, VirtualHeight - 14, VirtualWidth, 14);
            var toNext = progress.XpToNext;
            var fraction = toNext == 0 ? 1f : (float)progress.Xp / toNext;
            Bar(rect, fraction, new Color(0.65f, 0.45f, 0.95f));
            var text = progress.Level >= LevelCurve.MaxLevel
                ? Loc.T("hud.max_level")
                : Loc.T("hud.xp", progress.Xp, toNext, fraction * 100);
            GUI.Label(new Rect(0, VirtualHeight - 17, VirtualWidth, 20), text, _small.WithCenter());
        }

        private void DrawZone()
        {
            var zone = ZoneArea.At(combat.transform.position);
            var levels = zone == null ? "" : zone.MinLevel == zone.MaxLevel ? $"{zone.MinLevel}" : $"{zone.MinLevel}–{zone.MaxLevel}";
            var dungeon = Net.CharacterState.Dungeon != null ? Game.Shared.Dungeon.DungeonCatalog.Find(Net.CharacterState.Dungeon.DungeonId) : null;
            var arena = Net.CharacterState.Pvp != null && Net.CharacterState.Pvp.Mode == Game.Shared.Net.PvpMode.Arena;
            var text = arena ? Loc.T("hud.zone_arena") : dungeon != null ? Loc.T("hud.zone_dungeon", dungeon.Name) : zone == null ? Loc.T("hud.zone_road") : Loc.T("hud.zone", zone.DisplayName, levels);
            var rect = new Rect(VirtualWidth - 460, 20, 440, 40);
            Box(rect, Panel);
            GUI.Label(rect, text, _center);
        }

        private void DrawNetwork()
        {
            var session = NetworkSession.Instance;
            if (session == null) return;

            string text;
            var color = Color.white;
            if (!string.IsNullOrEmpty(session.Error))
            {
                text = Loc.T("hud.disconnected", session.Error);
                color = new Color(1f, 0.5f, 0.45f);
            }
            else if (session.State == NetClient.Status.Connecting) text = Loc.T("hud.connecting", GameMode.Host, GameMode.Port);
            else if (!session.IsWelcomed) text = Loc.T("hud.entering_world");
            else text = Loc.T("hud.online", GameMode.PlayerName, session.Clock.LastRtt * 1000);

            var rect = new Rect(VirtualWidth - 460, 64, 440, 30);
            Box(rect, Panel);
            GUI.color = color;
            GUI.Label(rect, text, _small.WithCenter());
            GUI.color = Color.white;
        }

        private void DrawAnnouncement()
        {
            var age = Time.unscaledTime - _announcementAt;
            if (age > 3.5f || string.IsNullOrEmpty(_announcement)) return;
            ShadowLabel(new Rect(0, 210, VirtualWidth, 60), $"<b>{_announcement}</b>", _big.WithCenter(),
                new Color(1f, 0.55f, 0.3f), 1f - Mathf.Clamp01((age - 2.8f) / 0.7f));
        }

        private void DrawDeathOverlay()
        {
            if (!combat.IsDead) return;
            Box(new Rect(0, 0, VirtualWidth, VirtualHeight), new Color(0.3f, 0f, 0f, 0.35f));
            ShadowLabel(new Rect(0, VirtualHeight / 2 - 80, VirtualWidth, 80), Loc.T("hud.you_died"), _big.WithCenter(),
                new Color(1f, 0.4f, 0.35f), 1f);
            GUI.Label(new Rect(0, VirtualHeight / 2, VirtualWidth, 30),
                Loc.T("hud.respawning", combat.RespawnRemaining), _center);
        }

        private void DrawInventory()
        {
            var rect = new Rect(VirtualWidth - 440, Minimap.PanelsTop, 400, 500);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + 16, rect.y + 10, rect.width, 30), Loc.T("hud.inventory_title", KeyBindings.Label(GameAction.Inventory)), _label);
            if (_loot == null || _loot.Inventory.Count == 0)
            {
                GUI.Label(new Rect(rect.x + 16, rect.y + 50, rect.width - 32, 60),
                    Loc.T("hud.inventory_empty", KeyBindings.Label(GameAction.Pickup)), _small);
                return;
            }

            var y = rect.y + 50;
            foreach (var stack in _loot.Inventory)
            {
                var color = LootPickup.ColorFor(stack.Rarity);
                Box(new Rect(rect.x + 16, y + 4, 16, 16), color);
                GUI.color = color;
                GUI.Label(new Rect(rect.x + 42, y, rect.width - 120, 26), stack.Name, _small);
                GUI.color = Color.white;
                GUI.Label(new Rect(rect.x, y, rect.width - 16, 26), $"x{stack.Quantity}", _small.WithAlignment(TextAnchor.UpperRight));
                y += 28;
                if (y > rect.yMax - 30) break;
            }
            GUI.Label(new Rect(rect.x + 16, rect.yMax - 30, rect.width, 24),
                Loc.T("hud.monsters_killed", _loot.MonstersKilled), _small);
        }

        /// <summary>Nome, nível e vida acima dos monstros vivos por perto.</summary>
        private void DrawNameplates()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var scale = Mathf.Clamp(Screen.height / VirtualHeight, 0.6f, 2f);

            foreach (var target in Targetable.All)
            {
                if (target.Level <= 0 || !target.IsAlive) continue;
                if ((target.transform.position - combat.transform.position).sqrMagnitude > 30f * 30f) continue;
                var screen = cam.WorldToScreenPoint(target.HeadPosition + Vector3.up * 0.3f);
                if (screen.z <= 0) continue;

                var width = 140f * scale;
                var x = screen.x - width / 2;
                var y = Screen.height - screen.y - 30f * scale;
                var selected = target == combat.Target;
                GUI.color = selected ? new Color(1f, 0.9f, 0.4f) : Color.white;
                _small.fontSize = Mathf.RoundToInt(14 * scale);
                GUI.Label(new Rect(x - 40, y - 4, width + 80, 20 * scale), target.NameplateText, _small.WithCenter());
                _small.fontSize = 16;
                GUI.color = Color.white;
                Bar(new Rect(x, y + 16 * scale, width, 6 * scale), (float)target.Hp.Current / Mathf.Max(1, target.Hp.Max), HpColor);
            }
        }

        private void DrawSpBar()
        {
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 210, 660, 18);
            Bar(rect, (float)combat.Sp.Current / Mathf.Max(1, combat.Sp.Max), SpColor);
            GUI.Label(rect, Loc.T("hud.sp", combat.Sp.Current, combat.Sp.Max), _small.WithCenter());
        }

        private void DrawPlayerFrame()
        {
            var rect = new Rect(40, VirtualHeight - 150, 380, 96);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + 16, rect.y + 6, rect.width, 26), Loc.T("hud.you", combat.Level), _label);
            if (_loot != null)
                GUI.Label(new Rect(rect.x, rect.y + 6, rect.width - 16, 26), Loc.T("hud.alz", _loot.Alz), _label.WithAlignment(TextAnchor.UpperRight));

            var hpRect = new Rect(rect.x + 16, rect.y + 38, rect.width - 32, 20);
            Bar(hpRect, (float)combat.Hp.Current / Mathf.Max(1, combat.Hp.Max), HpColor);
            GUI.Label(hpRect, Loc.T("hud.hp", combat.Hp.Current, combat.Hp.Max), _small.WithCenter());

            var mpRect = new Rect(rect.x + 16, rect.y + 64, rect.width - 32, 20);
            Bar(mpRect, (float)combat.Mp.Current / Mathf.Max(1, combat.Mp.Max), MpColor);
            GUI.Label(mpRect, Loc.T("hud.mp", combat.Mp.Current, combat.Mp.Max), _small.WithCenter());
        }

        /// <summary>Barra do Despertar (online): enche com acertos de combo bons; cheia, R ativa.</summary>
        private void DrawAwakening()
        {
            var sheet = CharacterState.Sheet;
            if (!GameMode.IsOnline || sheet == null) return;
            var rect = new Rect(VirtualWidth / 2 - 330, VirtualHeight - 232, 660, 14);
            var session = NetworkSession.Instance;
            var remaining = session != null ? sheet.AwakeningUntil - session.ServerNow : 0;
            if (remaining > 0)
            {
                var pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 8f);
                Bar(rect, (float)(remaining / AwakeningState.DurationSeconds), new Color(1f, 0.35f * pulse, 0.9f * pulse));
                GUI.Label(new Rect(rect.x, rect.y - 4, rect.width, 22), Loc.T("hud.awakening_active", remaining), _small.WithCenter());
                return;
            }
            var full = sheet.AwakeningGauge >= AwakeningState.MaxGauge;
            Bar(rect, sheet.AwakeningGauge / (float)AwakeningState.MaxGauge, full ? new Color(1f, 0.4f, 0.95f) : new Color(0.6f, 0.3f, 0.6f));
            GUI.Label(new Rect(rect.x, rect.y - 4, rect.width, 22), full ? Loc.T("hud.awakening_ready", KeyBindings.Label(GameAction.Awakening)) : Loc.T("hud.awakening_gauge", sheet.AwakeningGauge), _small.WithCenter());
        }

        private void DrawSkillBar()
        {
            var skills = combat.Skills;
            var size = skills.Count > 4 ? 104f : 150f;
            var gap = skills.Count > 4 ? 10f : 20f;
            var total = skills.Count * size + (skills.Count - 1) * gap;
            var x = VirtualWidth / 2 - total / 2;
            var y = VirtualHeight - 180;

            for (var i = 0; i < skills.Count; i++)
            {
                var rect = new Rect(x + i * (size + gap), y, size, 120);
                var skill = skills[i];
                Box(rect, Panel);
                GUI.Label(new Rect(rect.x + 10, rect.y + 6, 40, 30), SkillKeyLabel(i), _big);
                GUI.Label(new Rect(rect.x + 8, rect.y + 44, rect.width - 16, 44), skill.Name, _small.WithCenter());
                var kindText = skill.Kind == SkillKind.Physical ? Loc.T("hud.kind_physical") : Loc.T("hud.kind_magical");
                if (skill.MpCost > 0) kindText = Loc.T("hud.kind_with_mp", kindText, skill.MpCost);
                if (skills.Count > 4) _small.fontSize = 13; // com 8 skills a caixa é estreita: "mágico · 25 MP" cabe menor
                GUI.Label(new Rect(rect.x + 4, rect.y + 88, rect.width - 8, 24), kindText, _small.WithCenter());
                _small.fontSize = 16;

                // Sem mana: escurece a skill inteira.
                if (!combat.Mp.Has(skill.MpCost)) Box(rect, new Color(0.1f, 0.1f, 0.4f, 0.5f));

                if (GameMode.IsOnline && !CharacterState.SkillUnlocked(i))
                {
                    Box(rect, new Color(0.05f, 0.05f, 0.07f, 0.95f));
                    GUI.Label(new Rect(rect.x + 10, rect.y + 6, 40, 30), SkillKeyLabel(i), _big);
                    GUI.Label(new Rect(rect.x + 4, rect.y + 36, rect.width - 8, 80), $"<color=#999999>{skill.Name}</color>\n{combat.UnlockText(i, compact: true)}", _small.WithCenter());
                    continue;
                }

                var remaining = combat.CooldownRemaining(i);
                if (remaining > 0)
                {
                    var fraction = Mathf.Clamp01(remaining / Mathf.Max(0.01f, skill.CooldownSeconds));
                    Box(new Rect(rect.x, rect.y + rect.height * (1 - fraction), rect.width, rect.height * fraction), new Color(0, 0, 0, 0.6f));
                    GUI.Label(rect, $"<b>{remaining:0.0}</b>", _big.WithCenter());
                }
            }
        }

        private void DrawGradePopup()
        {
            var age = Time.unscaledTime - combat.LastGradeAt;
            if (age > 0.6f) return;

            string text;
            Color color;
            switch (combat.LastGrade)
            {
                case ComboGrade.Excellent: text = Loc.T("hud.grade_excellent"); color = ExcellentColor; break;
                case ComboGrade.Good: text = Loc.T("hud.grade_good"); color = new Color(0.6f, 0.9f, 1f); break;
                default: text = Loc.T("hud.grade_miss"); color = new Color(1f, 0.35f, 0.3f); break;
            }

            var pop = 1f + Mathf.Max(0, 0.15f - age) * 3f;
            _big.fontSize = Mathf.RoundToInt(56 * pop);
            ShadowLabel(new Rect(0, VirtualHeight - 490, VirtualWidth, 80), $"<b>{text}</b>", _big.WithCenter(),
                color, 1f - Mathf.Clamp01((age - 0.35f) / 0.25f));
            _big.fontSize = 30;

            var routeAge = Time.unscaledTime - combat.LastRouteAt;
            if (combat.LastRoute != null && routeAge <= 0.9f)
            {
                ShadowLabel(new Rect(0, VirtualHeight - 412, VirtualWidth, 40),
                    Loc.T("hud.route", combat.LastRoute.Name.ToUpperInvariant(), combat.LastRoute.DamageBonus * 100), _big.WithCenter(),
                    RouteColor, 1f - Mathf.Clamp01((routeAge - 0.6f) / 0.3f));
            }
        }

        /// <summary>Texto com sombra escura, legível sobre qualquer fundo.</summary>
        private static void ShadowLabel(Rect rect, string text, GUIStyle style, Color color, float alpha)
        {
            GUI.color = new Color(0, 0, 0, alpha * 0.85f);
            GUI.Label(new Rect(rect.x + 3, rect.y + 3, rect.width, rect.height), text, style);
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(rect, text, style);
            GUI.color = Color.white;
        }

        private void DrawMessage()
        {
            var age = Time.unscaledTime - combat.MessageAt;
            if (age > 2.5f || string.IsNullOrEmpty(combat.Message)) return;
            GUI.color = new Color(1, 1, 1, 1f - Mathf.Clamp01((age - 2f) / 0.5f));
            // Com janelas do lado direito abertas (loja, troca, ferreira) o aviso vai para o espaço livre à esquerda delas.
            var messageRect = GameWindows.RightPanelOpen ? new Rect(420, VirtualHeight - 548, 540, 30) : new Rect(0, VirtualHeight - 548, VirtualWidth, 30);
            ShadowLabel(messageRect, combat.Message, _center, Color.white, 1f);
            GUI.color = Color.white;
        }

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Bar(Rect rect, float fraction, Color color)
        {
            Box(rect, BarBack);
            Box(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(fraction), rect.height), color);
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true };
            _label.normal.textColor = Color.white;
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
            _big = new GUIStyle(_label) { fontSize = 30 };
            _small = new GUIStyle(_label) { fontSize = 16 };
        }
    }

    internal static class GuiStyleExtensions
    {
        private sealed class ByReference : IEqualityComparer<GUIStyle>
        {
            public bool Equals(GUIStyle a, GUIStyle b) => ReferenceEquals(a, b);
            public int GetHashCode(GUIStyle style) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(style);
        }

        // Criar um GUIStyle por chamada (dezenas por quadro, em cada passada do OnGUI) gerava lixo e coletas frequentes.
        private static readonly Dictionary<GUIStyle, GUIStyle[]> Aligned = new Dictionary<GUIStyle, GUIStyle[]>(new ByReference());

        public static GUIStyle WithCenter(this GUIStyle style) => style.WithAlignment(TextAnchor.MiddleCenter);

        /// <summary>
        /// O mesmo estilo com outro alinhamento, criado uma vez e reaproveitado. O tamanho, a cor e o negrito são copiados
        /// do original a cada uso, porque o HUD muda o tamanho da fonte na hora (ex.: plaquinhas que crescem com a tela).
        /// </summary>
        public static GUIStyle WithAlignment(this GUIStyle style, TextAnchor alignment)
        {
            if (!Aligned.TryGetValue(style, out var variants))
            {
                variants = new GUIStyle[9];
                Aligned[style] = variants;
            }
            var variant = variants[(int)alignment] ??= new GUIStyle(style) { alignment = alignment };
            variant.fontSize = style.fontSize;
            variant.fontStyle = style.fontStyle;
            variant.richText = style.richText;
            variant.wordWrap = style.wordWrap;
            variant.normal.textColor = style.normal.textColor;
            return variant;
        }
    }
}
