using Game.Client.Net;
using Game.Client.World;
using Game.Shared.Pvp;
using UnityEngine;
using Msg = Game.Shared.Net;

namespace Game.Client.UI
{
    /// <summary>Desafio de duelo, contagem do duelo, fila da arena, placar da partida e tela final.</summary>
    public sealed partial class GameWindows
    {
        private static double ServerNow => NetworkSession.Instance != null ? NetworkSession.Instance.ServerNow : 0;

        private void DrawPvp()
        {
            DrawDuelInvite();
            var pvp = CharacterState.Pvp;
            if (pvp == null) return;
            if (pvp.InQueue) DrawQueue(pvp);
            if (pvp.Mode == Msg.PvpMode.Duel) DrawDuel(pvp);
            if (pvp.Mode == Msg.PvpMode.Arena) DrawArena(pvp);
        }

        private void DrawDuelInvite()
        {
            var invite = CharacterState.PendingDuelInvite;
            if (invite == null) return;
            var rect = Window(new Rect(VirtualWidth / 2 - 230, 580, 460, 130), "Desafio de duelo");
            GUI.Label(new Rect(rect.x, rect.y + 40, rect.width, 26), $"<b>{invite.FromName}</b> desafiou você para um duelo", _label.WithCenter());
            if (GUI.Button(new Rect(rect.x + 60, rect.y + 80, 150, 36), "<b>Aceitar</b>", _button))
            {
                Send(new Msg.DuelCommand { Action = Msg.DuelActionKind.Accept, TargetId = invite.FromId });
                CharacterState.ClearDuelInvite();
            }
            if (GUI.Button(new Rect(rect.xMax - 210, rect.y + 80, 150, 36), "Recusar", _button))
            {
                Send(new Msg.DuelCommand { Action = Msg.DuelActionKind.Decline, TargetId = invite.FromId });
                CharacterState.ClearDuelInvite();
            }
        }

        private void DrawQueue(Msg.PvpState pvp)
        {
            var rect = new Rect(VirtualWidth - 480, 104, 460, 44);
            OpenRects.Add(rect);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + 14, rect.y + 10, 300, 26), $"<b>Fila da arena</b>  {pvp.QueueSize}/{pvp.QueueNeeded} jogadores", _small);
            if (GUI.Button(new Rect(rect.xMax - 124, rect.y + 8, 110, 28), "Sair da fila", _button))
                Send(new Msg.ArenaCommand { Action = Msg.ArenaActionKind.LeaveQueue });
        }

        private void DrawDuel(Msg.PvpState pvp)
        {
            var rect = new Rect(VirtualWidth / 2 - 220, 106, 440, 52);
            OpenRects.Add(rect);
            Box(rect, Panel);
            var now = ServerNow;
            var text = now < pvp.StartsAt
                ? $"<b>Duelo contra {pvp.OpponentName}</b> em {Mathf.CeilToInt((float)(pvp.StartsAt - now))}…"
                : $"<b>Duelo contra {pvp.OpponentName}</b>  {Shared.Dungeon.DungeonRules.FormatTime(pvp.EndsAt - now)}";
            GUI.Label(new Rect(rect.x + 14, rect.y + 12, 300, 28), text, _small);
            if (GUI.Button(new Rect(rect.xMax - 110, rect.y + 12, 96, 28), "Desistir", _button))
                Send(new Msg.DuelCommand { Action = Msg.DuelActionKind.Forfeit });
        }

        private void DrawArena(Msg.PvpState pvp)
        {
            var now = ServerNow;
            var rect = new Rect(VirtualWidth / 2 - 260, 106, 520, 92);
            OpenRects.Add(rect);
            Box(rect, Panel);
            GUI.color = ArenaView.Blue;
            GUI.Label(new Rect(rect.x + 14, rect.y + 6, 200, 40), $"<size=30><b>{pvp.ScoreBlue}</b></size>  Azul", _label);
            GUI.color = ArenaView.Red;
            GUI.Label(new Rect(rect.xMax - 214, rect.y + 6, 200, 40), $"Vermelho  <size=30><b>{pvp.ScoreRed}</b></size>", _label.WithAlignment(TextAnchor.UpperRight));
            GUI.color = Color.white;
            var clock = now < pvp.StartsAt ? $"começa em {Mathf.CeilToInt((float)(pvp.StartsAt - now))}"
                : pvp.Finished ? "fim" : Shared.Dungeon.DungeonRules.FormatTime(pvp.EndsAt - now);
            GUI.Label(new Rect(rect.x, rect.y + 6, rect.width, 30), $"<b>{clock}</b>", _label.WithCenter());
            GUI.Label(new Rect(rect.x, rect.y + 32, rect.width, 20), $"<size=12>até {ArenaRules.WinScore} · você: {ArenaRules.TeamName((ArenaTeam)pvp.Team)}</size>", _small.WithCenter());

            // Os três pontos: cor do dono e a barra de captura.
            for (var i = 0; i < pvp.Points.Count && i < ArenaRules.Points.Count; i++)
            {
                var point = pvp.Points[i];
                var chip = new Rect(rect.center.x - 105 + i * 75, rect.y + 56, 60, 28);
                Box(chip, point.Owner == (sbyte)ArenaTeam.Blue ? ArenaView.Blue : point.Owner == (sbyte)ArenaTeam.Red ? ArenaView.Red : new Color(0.35f, 0.35f, 0.35f));
                if (point.Contested) Outline(chip, Color.white);
                var fill = Mathf.Abs(point.Progress);
                Box(new Rect(chip.x, chip.yMax - 4, chip.width * fill, 4), point.Progress < 0 ? ArenaView.Blue : ArenaView.Red);
                GUI.Label(chip, $"<b>{ArenaRules.Points[i].Name}</b>", _small.WithCenter());
            }
            if (!pvp.Finished && GUI.Button(new Rect(rect.xMax - 64, rect.yMax - 30, 56, 24), "Sair", _button))
                Send(new Msg.ArenaCommand { Action = Msg.ArenaActionKind.Leave });

            if (pvp.Finished) DrawArenaResult(pvp);
        }

        private void DrawArenaResult(Msg.PvpState pvp)
        {
            var winner = (ArenaTeam)pvp.Winner;
            var mine = (ArenaTeam)pvp.Team;
            var title = winner == ArenaTeam.None ? "Empate!" : winner == mine ? "Vitória!" : "Derrota";
            var rect = Window(new Rect(VirtualWidth / 2 - 330, 230, 660, 120 + pvp.Players.Count * 26), $"Arena — {title}");
            GUI.Label(new Rect(rect.x, rect.y + 40, rect.width, 30), $"Azul <b>{pvp.ScoreBlue}</b> x <b>{pvp.ScoreRed}</b> Vermelho", _label.WithCenter());
            var y = rect.y + 80;
            var x = rect.x + 24;
            GUI.Label(new Rect(x, y, 260, 22), "<b>Jogador</b>", _small);
            GUI.Label(new Rect(x + 300, y, 80, 22), "<b>Abates</b>", _small);
            GUI.Label(new Rect(x + 390, y, 80, 22), "<b>Mortes</b>", _small);
            GUI.Label(new Rect(x + 480, y, 120, 22), "<b>Capturas</b>", _small);
            y += 26;
            foreach (var team in new[] { ArenaTeam.Blue, ArenaTeam.Red })
            {
                foreach (var p in pvp.Players)
                {
                    if (p.Team != (sbyte)team) continue;
                    GUI.color = team == ArenaTeam.Blue ? ArenaView.Blue : ArenaView.Red;
                    GUI.Label(new Rect(x, y, 280, 22), $"{p.Name} <size=12>Nv {p.Level}</size>", _small);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(x + 300, y, 80, 22), p.Kills.ToString(), _small);
                    GUI.Label(new Rect(x + 390, y, 80, 22), p.Deaths.ToString(), _small);
                    GUI.Label(new Rect(x + 480, y, 80, 22), p.Captures.ToString(), _small);
                    y += 26;
                }
            }
            GUI.Label(new Rect(rect.x, rect.yMax - 26, rect.width, 22), "<size=12>Todos voltam à vila em instantes.</size>", _small.WithCenter());
        }
    }
}
