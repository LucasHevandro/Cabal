using Game.Client.Net;
using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.Text;
using Msg = Game.Shared.Net;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>Portal da dungeon (entrada e ranking), painel com o cronômetro e a tela de resultado.</summary>
    public sealed partial class GameWindows
    {
        private static readonly Color MedalS = new Color(1f, 0.82f, 0.3f);
        private static readonly Color MedalA = new Color(0.8f, 0.85f, 0.95f);
        private static readonly Color MedalB = new Color(0.8f, 0.55f, 0.35f);

        private bool _resultClosed;

        private static Msg.DungeonState Dungeon => CharacterState.Dungeon is { Active: true } d ? d : null;

        // Usado pelo teste automático.
        public void EnterDungeon() => Send(new Msg.DungeonCommand { Action = Msg.DungeonActionKind.Enter, DungeonId = DungeonCatalog.CryptId });

        // ----- Portal -----

        private void DrawPortal(NpcDefinition npc)
        {
            var crypt = DungeonCatalog.Crypt();
            var rect = Window(new Rect(VirtualWidth / 2 - 300, 150, 600, 560), Loc.T("dungeon.portal_title", npc.Name, crypt.Name));
            var x = rect.x + 16;
            var y = rect.y + 46;
            var keys = CharacterState.Bag.Count(crypt.KeyItemId);
            var level = Player.Level;
            var party = SocialWindows.Instance != null ? SocialWindows.Instance.Party : null;
            var members = party != null && party.Members.Count > 0 ? party.Members.Count : 1;
            var isLeader = party == null || party.Members.Count == 0 || party.LeaderId == NetworkSession.Instance.PlayerId;

            GUI.Label(new Rect(x, y, rect.width - 32, 64),
                Loc.T("dungeon.crypt_intro", DungeonRules.MaxPlayers, DungeonRules.FormatTime(DungeonRules.TimeLimitSeconds),
                    DungeonRules.FormatTime(DungeonRules.MedalSSeconds), DungeonRules.MedalSCombo, DungeonRules.FormatTime(DungeonRules.MedalASeconds)), _small);
            y += 74;
            GUI.Label(new Rect(x, y, rect.width - 32, 24), Check(level >= crypt.MinLevel, Loc.T("dungeon.req_level", crypt.MinLevel, level)), _small);
            GUI.Label(new Rect(x, y + 24, rect.width - 32, 24),
                Check(keys > 0, Loc.T("dungeon.req_key", ItemCatalog.Get(crypt.KeyItemId).Name, keys)), _small);
            GUI.Label(new Rect(x, y + 48, rect.width - 32, 24),
                Check(isLeader, members > 1 ? Loc.T("dungeon.req_party", members) : Loc.T("dungeon.req_solo")), _small);
            y += 84;

            GUI.Label(new Rect(x, y, rect.width - 32, 26), Loc.T("dungeon.best_times"), _label);
            y += 30;
            var info = CharacterState.DungeonRanking;
            if (info == null || info.Ranking.Count == 0)
            {
                GUI.Label(new Rect(x, y, rect.width - 32, 24), Loc.T("dungeon.no_runs"), _small);
            }
            else
            {
                for (var i = 0; i < info.Ranking.Count && i < 8; i++)
                {
                    var run = info.Ranking[i];
                    GUI.Label(new Rect(x, y, 40, 24), $"{i + 1}.", _small);
                    GUI.Label(new Rect(x + 34, y, 80, 24), $"<b>{DungeonRules.FormatTime(run.Seconds)}</b>", _small);
                    GUI.color = MedalColor((DungeonMedal)run.Medal);
                    GUI.Label(new Rect(x + 110, y, 40, 24), $"<b>{DungeonRules.MedalName((DungeonMedal)run.Medal)}</b>", _small);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 150, y, rect.width - 190, 24), Loc.T("dungeon.run_row", run.Members.Replace(",", ", "), run.MaxCombo), _small);
                    y += 26;
                }
            }

            // Só o líder entra; o resto (nível, chaves, distância do grupo) o servidor confere e explica no chat.
            GUI.enabled = isLeader;
            if (GUI.Button(new Rect(rect.center.x - 120, rect.yMax - 56, 240, 40), members > 1 ? Loc.T("dungeon.enter_party") : Loc.T("dungeon.enter"), _button))
            {
                EnterDungeon();
                CharacterState.CloseDialog();
                _resultClosed = false;
            }
            GUI.enabled = true;
        }

        private static string Check(bool ok, string text) => ok ? $"<color=#77ee77>✔</color> {text}" : $"<color=#ff7766>✘</color> {text}";

        // ----- Na dungeon -----

        private void DrawDungeonHud()
        {
            var state = Dungeon;
            if (state == null) return;
            var definition = DungeonCatalog.Find(state.DungeonId);
            if (definition == null) return;
            var room = definition.Rooms[Mathf.Min(state.Room, definition.Rooms.Count - 1)];
            var elapsed = state.Completed ? state.ElapsedSeconds : (float)(NetworkSession.Instance.ServerNow - state.StartedAt);
            var left = state.LimitSeconds - elapsed;

            // À direita, embaixo da zona (o rastreador de missões some na dungeon e os anúncios ficam no centro).
            var rect = new Rect(VirtualWidth - 480, 104, 460, 64);
            OpenRects.Add(rect);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + 14, rect.y + 6, 300, 26), Loc.T("dungeon.hud_room", state.Room + 1, state.Rooms, room.Name), _small);
            var info = state.Completed ? Loc.T("dungeon.hud_victory") : state.BossShield ? Loc.T("dungeon.hud_shield")
                : room.IsBossRoom ? Loc.T("dungeon.hud_boss", Mathf.Max(0, state.MonstersLeft - 1)) : Loc.T("dungeon.hud_monsters", state.MonstersLeft);
            GUI.Label(new Rect(rect.x + 14, rect.y + 32, 300, 24), $"<size=14>{info}</size>", _small);
            GUI.color = left < 120 && !state.Completed ? Bad : Color.white;
            GUI.Label(new Rect(rect.xMax - 160, rect.y + 2, 146, 34), $"<size=26><b>{DungeonRules.FormatTime(state.Completed ? elapsed : Mathf.Max(0, left))}</b></size>",
                _label.WithAlignment(TextAnchor.UpperRight));
            GUI.color = Color.white;
            if (GUI.Button(new Rect(rect.xMax - 92, rect.y + 36, 80, 22), Loc.T("dungeon.leave"), _button)) LeaveDungeon();

            if (state.Completed && !_resultClosed) DrawDungeonResult(state, definition);
        }

        private void DrawDungeonResult(Msg.DungeonState state, DungeonDefinition definition)
        {
            var medal = (DungeonMedal)state.Medal;
            var rect = Window(new Rect(VirtualWidth / 2 - 260, 200, 520, 380), Loc.T("dungeon.victory_title", definition.Name));
            GUI.color = MedalColor(medal);
            GUI.Label(new Rect(rect.x, rect.y + 46, rect.width, 90), $"<size=80><b>{DungeonRules.MedalName(medal)}</b></size>", _label.WithCenter());
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x, rect.y + 140, rect.width, 26),
                Loc.T("dungeon.victory_stats", DungeonRules.FormatTime(state.ElapsedSeconds), state.MaxCombo, DungeonRules.ChestRolls(medal)),
                _small.WithCenter());
            var ranking = CharacterState.DungeonRanking;
            var place = ranking == null ? -1 : ranking.Ranking.FindIndex(r => Mathf.Abs(r.Seconds - state.ElapsedSeconds) < 0.2f);
            GUI.Label(new Rect(rect.x, rect.y + 170, rect.width, 26),
                place >= 0 ? Loc.T("dungeon.ranking_place", place + 1) : Loc.T("dungeon.ranking_out"), _small.WithCenter());
            GUI.Label(new Rect(rect.x + 20, rect.y + 206, rect.width - 40, 60),
                Loc.T("dungeon.chest_hint"), _tiny.WithCenter());
            if (GUI.Button(new Rect(rect.x + 40, rect.yMax - 56, 200, 40), Loc.T("dungeon.take_chest"), _button)) _resultClosed = true;
            if (GUI.Button(new Rect(rect.xMax - 240, rect.yMax - 56, 200, 40), Loc.T("dungeon.leave_crypt"), _button)) LeaveDungeon();
        }

        private void LeaveDungeon()
        {
            Send(new Msg.DungeonCommand { Action = Msg.DungeonActionKind.Leave });
            _resultClosed = false;
        }

        private static Color MedalColor(DungeonMedal medal) => medal switch
        {
            DungeonMedal.S => MedalS,
            DungeonMedal.A => MedalA,
            DungeonMedal.B => MedalB,
            _ => Color.gray,
        };
    }
}
