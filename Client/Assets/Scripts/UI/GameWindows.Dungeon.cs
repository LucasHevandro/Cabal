using Game.Client.Net;
using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Progression;
using UnityEngine;
using Msg = Game.Shared.Net;

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
            var rect = Window(new Rect(VirtualWidth / 2 - 300, 150, 600, 560), $"{npc.Name} — {crypt.Name}");
            var x = rect.x + 16;
            var y = rect.y + 46;
            var keys = CharacterState.Bag.Count(crypt.KeyItemId);
            var level = Player.Level;
            var party = SocialWindows.Instance != null ? SocialWindows.Instance.Party : null;
            var members = party != null && party.Members.Count > 0 ? party.Members.Count : 1;
            var isLeader = party == null || party.Members.Count == 0 || party.LeaderId == NetworkSession.Instance.PlayerId;

            GUI.Label(new Rect(x, y, rect.width - 32, 64),
                $"Três salas e o Senhor da Cripta, para até {DungeonRules.MaxPlayers} jogadores. " +
                $"Vençam em {DungeonRules.FormatTime(DungeonRules.TimeLimitSeconds)}: medalha <b>S</b> até {DungeonRules.FormatTime(DungeonRules.MedalSSeconds)} " +
                $"com combo de {DungeonRules.MedalSCombo}+, <b>A</b> até {DungeonRules.FormatTime(DungeonRules.MedalASeconds)}. A medalha dá mais sorteios no baú.", _small);
            y += 74;
            GUI.Label(new Rect(x, y, rect.width - 32, 24), Check(level >= crypt.MinLevel, $"Nível {crypt.MinLevel} ou mais (você: {level})"), _small);
            GUI.Label(new Rect(x, y + 24, rect.width - 32, 24),
                Check(keys > 0, $"{ItemCatalog.Get(crypt.KeyItemId).Name} na bolsa (você tem {keys}; a mercadora vende)"), _small);
            GUI.Label(new Rect(x, y + 48, rect.width - 32, 24),
                Check(isLeader, members > 1 ? $"Grupo de {members}: o líder entra com todos, que precisam estar aqui perto" : "Sozinho (ou forme um grupo com /convidar)"), _small);
            y += 84;

            GUI.Label(new Rect(x, y, rect.width - 32, 26), "<b>Melhores tempos</b>", _label);
            y += 30;
            var info = CharacterState.DungeonRanking;
            if (info == null || info.Ranking.Count == 0)
            {
                GUI.Label(new Rect(x, y, rect.width - 32, 24), "Ninguém venceu ainda. Sejam os primeiros!", _small);
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
                    GUI.Label(new Rect(x + 150, y, rect.width - 190, 24), $"{run.Members.Replace(",", ", ")}  <size=12>combo {run.MaxCombo}</size>", _small);
                    y += 26;
                }
            }

            // Só o líder entra; o resto (nível, chaves, distância do grupo) o servidor confere e explica no chat.
            GUI.enabled = isLeader;
            if (GUI.Button(new Rect(rect.center.x - 120, rect.yMax - 56, 240, 40), members > 1 ? "<b>Entrar com o grupo</b>" : "<b>Entrar</b>", _button))
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
            GUI.Label(new Rect(rect.x + 14, rect.y + 6, 300, 26), $"<b>Sala {state.Room + 1}/{state.Rooms}</b>  <size=14>{room.Name}</size>", _small);
            var info = state.Completed ? "Vitória!" : state.BossShield ? "<color=#8fd3ff>Escudo: derrote os servos!</color>"
                : room.IsBossRoom ? $"Chefe e {Mathf.Max(0, state.MonstersLeft - 1)} servos" : $"{state.MonstersLeft} monstros para abrir o portão";
            GUI.Label(new Rect(rect.x + 14, rect.y + 32, 300, 24), $"<size=14>{info}</size>", _small);
            GUI.color = left < 120 && !state.Completed ? Bad : Color.white;
            GUI.Label(new Rect(rect.xMax - 160, rect.y + 2, 146, 34), $"<size=26><b>{DungeonRules.FormatTime(state.Completed ? elapsed : Mathf.Max(0, left))}</b></size>",
                _label.WithAlignment(TextAnchor.UpperRight));
            GUI.color = Color.white;
            if (GUI.Button(new Rect(rect.xMax - 92, rect.y + 36, 80, 22), "Sair", _button)) LeaveDungeon();

            if (state.Completed && !_resultClosed) DrawDungeonResult(state, definition);
        }

        private void DrawDungeonResult(Msg.DungeonState state, DungeonDefinition definition)
        {
            var medal = (DungeonMedal)state.Medal;
            var rect = Window(new Rect(VirtualWidth / 2 - 260, 200, 520, 380), $"{definition.Name} — vitória!");
            GUI.color = MedalColor(medal);
            GUI.Label(new Rect(rect.x, rect.y + 46, rect.width, 90), $"<size=80><b>{DungeonRules.MedalName(medal)}</b></size>", _label.WithCenter());
            GUI.color = Color.white;
            GUI.Label(new Rect(rect.x, rect.y + 140, rect.width, 26),
                $"Tempo <b>{DungeonRules.FormatTime(state.ElapsedSeconds)}</b> · combo máximo <b>{state.MaxCombo}</b> · {DungeonRules.ChestRolls(medal)} sorteios no baú",
                _small.WithCenter());
            var ranking = CharacterState.DungeonRanking;
            var place = ranking == null ? -1 : ranking.Ranking.FindIndex(r => Mathf.Abs(r.Seconds - state.ElapsedSeconds) < 0.2f);
            GUI.Label(new Rect(rect.x, rect.y + 170, rect.width, 26),
                place >= 0 ? $"<color=#ffd25a>{place + 1}º lugar no ranking!</color>" : "Fora do top 10 desta vez.", _small.WithCenter());
            GUI.Label(new Rect(rect.x + 20, rect.y + 206, rect.width - 40, 60),
                "O baú apareceu no fundo da sala: os itens com o seu nome são só seus. A cripta fecha sozinha em instantes.", _tiny.WithCenter());
            if (GUI.Button(new Rect(rect.x + 40, rect.yMax - 56, 200, 40), "Pegar o baú", _button)) _resultClosed = true;
            if (GUI.Button(new Rect(rect.xMax - 240, rect.yMax - 56, 200, 40), "Sair da cripta", _button)) LeaveDungeon();
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
