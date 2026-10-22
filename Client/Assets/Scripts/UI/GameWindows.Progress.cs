using System.Collections.Generic;
using Game.Client.Net;
using Game.Client.Player;
using Game.Shared.Progression;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Tela de progressão (P): todas as barras numa tela só (nível, rank, skills, equipamento, Despertar e história), cada
    /// uma com o próximo objetivo. As contas ficam no Shared (<see cref="ProgressionGoals"/>); aqui é só o desenho.
    /// </summary>
    public sealed partial class GameWindows
    {
        private const float ProgressRow = 64f;

        private bool _progressOpen;
        private IReadOnlyList<ProgressGoal> _progressGoals;
        private float _progressAt = float.NegativeInfinity;
        private int _progressLanguage = -1;

        public void SetProgressOpen(bool open) => _progressOpen = open;

        private void DrawProgress()
        {
            // Monta as barras de novo a cada meio segundo (ou ao trocar de idioma), não a cada quadro.
            if (_progressGoals == null || Time.unscaledTime - _progressAt > 0.5f || _progressLanguage != Loc.Version)
            {
                _progressGoals = ProgressionGoals.For(ProgressInputNow());
                _progressAt = Time.unscaledTime;
                _progressLanguage = Loc.Version;
            }

            var rect = Window(new Rect(VirtualWidth / 2 - 330, 240, 660, 60 + _progressGoals.Count * ProgressRow),
                Loc.T("progress.title", KeyBindings.Label(GameAction.Progress)));
            var x = rect.x + 18;
            var y = rect.y + 52;
            foreach (var goal in _progressGoals)
            {
                GUI.Label(new Rect(x, y, 260, 24), $"<b>{TrackName(goal.Track)}</b>", _small);
                GUI.Label(new Rect(rect.xMax - 378, y, 360, 24), goal.Value.ToString(), _small.WithAlignment(TextAnchor.UpperRight));
                UiTheme.Bar(new Rect(x, y + 26, rect.width - 36, 10), goal.Fraction, TrackColor(goal));
                var next = goal.Done ? $"<color=#7fe07f>{goal.Next}</color>" : Loc.T("progress.next", goal.Next.ToString());
                GUI.Label(new Rect(x, y + 38, rect.width - 36, 22), next, _tiny);
                y += ProgressRow;
            }
        }

        private static ProgressInput ProgressInputNow()
        {
            var progress = Player != null ? Player.GetComponent<PlayerProgression>()?.Progress : null;
            var active = new List<string>();
            foreach (var quest in CharacterState.Quests.Active) active.Add(quest.QuestId);
            return new ProgressInput
            {
                Class = CharacterState.Class,
                Level = progress?.Level ?? 1,
                Xp = progress?.Xp ?? 0,
                QuestCompleted = CharacterState.QuestCompleted,
                SkillLearned = CharacterState.SkillUnlocked,
                ActiveQuests = active,
                Equipment = CharacterState.Equipment,
                AwakeningGauge = CharacterState.Sheet?.AwakeningGauge ?? 0,
            };
        }

        private static string TrackName(ProgressTrack track) => track switch
        {
            ProgressTrack.Level => Loc.T("progress.track_level"),
            ProgressTrack.Rank => Loc.T("progress.track_rank"),
            ProgressTrack.Skills => Loc.T("progress.track_skills"),
            ProgressTrack.Gear => Loc.T("progress.track_gear"),
            ProgressTrack.Awakening => Loc.T("progress.track_awakening"),
            _ => Loc.T("progress.track_quests"),
        };

        private static Color TrackColor(ProgressGoal goal)
        {
            if (goal.Done) return new Color(0.45f, 0.85f, 0.45f);
            return goal.Track switch
            {
                ProgressTrack.Level => new Color(0.65f, 0.45f, 0.95f),
                ProgressTrack.Rank => new Color(0.95f, 0.75f, 0.3f),
                ProgressTrack.Skills => new Color(0.35f, 0.6f, 1f),
                ProgressTrack.Gear => new Color(0.75f, 0.78f, 0.85f),
                ProgressTrack.Awakening => new Color(1f, 0.55f, 0.2f),
                _ => new Color(0.55f, 0.85f, 0.75f),
            };
        }
    }
}
