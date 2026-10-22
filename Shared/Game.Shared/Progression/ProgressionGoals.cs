using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Text;

namespace Game.Shared.Progression
{
    /// <summary>As barras da tela de progressão (GDD: uma tela só, com todas as barras e o próximo objetivo de cada).</summary>
    public enum ProgressTrack
    {
        Level,
        Rank,
        Skills,
        Gear,
        Awakening,
        Quests,
    }

    /// <summary>Uma barra: quanto já foi (0 a 1), o texto do valor e o próximo objetivo.</summary>
    public sealed class ProgressGoal
    {
        public ProgressTrack Track;
        public float Fraction;
        public LocalText Value = LocalText.Empty;
        public LocalText Next = LocalText.Empty;

        /// <summary>Barra completa (não há mais objetivo nela).</summary>
        public bool Done;
    }

    /// <summary>O que a tela precisa saber do personagem (o cliente tem tudo isso na ficha, nas missões e no equipamento).</summary>
    public sealed class ProgressInput
    {
        public CharacterClass Class;
        public int Level = 1;
        public int Xp;
        public Func<string, bool> QuestCompleted = _ => false;

        /// <summary>Skills aprendidas, pela posição na lista da classe.</summary>
        public Func<int, bool> SkillLearned = _ => false;

        public ICollection<string> ActiveQuests = Array.Empty<string>();
        public Equipment Equipment = new Equipment();
        public int AwakeningGauge;
    }

    /// <summary>Calcula as barras e o próximo objetivo de cada uma. Só leitura: não decide nada do jogo.</summary>
    public static class ProgressionGoals
    {
        /// <summary>Missões de rank, na ordem: combo, rank 2 e rank 3.</summary>
        public static readonly string[] RankQuests = { ClassCatalog.ComboQuestId, ClassCatalog.RankTwoQuestId, ClassCatalog.RankThreeQuestId };

        private static readonly (EquipSlot Slot, string LabelKey, string Prefix)[] GearSlots =
        {
            (EquipSlot.Weapon, "slot.weapon", ""), (EquipSlot.Armor, "slot.armor", "armadura_"), (EquipSlot.Gloves, "slot.gloves", "luvas_"),
            (EquipSlot.Boots, "slot.boots", "botas_"), (EquipSlot.Accessory, "slot.accessory", "anel_"),
        };

        private static readonly string[] GradeSuffix = { "ferro", "aco_negro", "astralita" };

        /// <summary>Grau de equipamento que o nível já veste (Ferro no 1, Aço Negro no 10, Astralita no 20).</summary>
        public static int GradeForLevel(int level) => level >= 20 ? 3 : level >= 10 ? 2 : 1;

        public static IReadOnlyList<ProgressGoal> For(ProgressInput input) => new List<ProgressGoal>
        {
            Level(input), Rank(input), Skills(input), Gear(input), Awakening(input), Quests(input),
        };

        private static ProgressGoal Level(ProgressInput input)
        {
            var max = input.Level >= LevelCurve.MaxLevel;
            var toNext = LevelCurve.XpToNext(Math.Max(1, input.Level));
            return new ProgressGoal
            {
                Track = ProgressTrack.Level, Done = max,
                Fraction = max ? 1f : Clamp(input.Xp / (float)Math.Max(1, toNext)),
                Value = new LocalText("progress.level_value", input.Level, LevelCurve.MaxLevel),
                Next = max ? new LocalText("progress.level_max")
                    : new LocalText("progress.level_next", input.Level + 1, Math.Max(0, toNext - input.Xp), Attributes.PointsPerLevel),
            };
        }

        private static ProgressGoal Rank(ProgressInput input)
        {
            var done = RankQuests.Count(input.QuestCompleted);
            var next = RankQuests.FirstOrDefault(id => !input.QuestCompleted(id));
            var goal = new ProgressGoal
            {
                Track = ProgressTrack.Rank, Done = next == null, Fraction = done / (float)RankQuests.Length,
                Value = new LocalText("progress.rank_value", done, RankQuests.Length),
                Next = new LocalText("progress.rank_done"),
            };
            if (next != null)
            {
                var quest = QuestCatalog.Get(next);
                goal.Next = new LocalText("progress.rank_next", Loc.Ref(Loc.Key("quest.", quest.Id)), quest.MinLevel,
                    Loc.Ref(Loc.Key("npc.", quest.GiverNpcId)), Loc.Ref(Loc.Key("quest.", quest.Id, ".unlocks")));
            }
            return goal;
        }

        private static ProgressGoal Skills(ProgressInput input)
        {
            var skills = ClassCatalog.SkillsFor(input.Class);
            var learned = Enumerable.Range(0, skills.Count).Count(input.SkillLearned);
            var goal = new ProgressGoal
            {
                Track = ProgressTrack.Skills, Done = learned >= skills.Count, Fraction = learned / (float)Math.Max(1, skills.Count),
                Value = new LocalText("progress.skills_value", learned, skills.Count),
                Next = new LocalText("progress.skills_done"),
            };
            for (var i = 0; i < skills.Count; i++)
            {
                if (input.SkillLearned(i)) continue;
                var skill = skills[i];
                var name = Loc.Ref(Loc.Key("skill.", skill.Skill.Id));
                // Sem olhar o Alz: a tela diz o que falta de nível e de missão; o preço aparece junto.
                var refused = SkillTraining.CanLearn(skill, input.Level, input.QuestCompleted, long.MaxValue, false);
                goal.Next = refused == null
                    ? new LocalText("progress.skill_ready", name, SkillTraining.PriceFor(skill), Loc.Ref(Loc.Key("npc.", NpcCatalog.Trainer)))
                    : new LocalText("progress.skill_need", name, refused);
                break;
            }
            return goal;
        }

        private static ProgressGoal Gear(ProgressInput input)
        {
            var target = GradeForLevel(input.Level);
            var steps = UpgradeRules.MaxLevel + 1;
            var total = 0f;
            var atTarget = 0;
            var weakest = -1;
            var weakestScore = float.MaxValue;
            for (var i = 0; i < GearSlots.Length; i++)
            {
                var item = input.Equipment[GearSlots[i].Slot];
                var definition = item != null ? ItemCatalog.Find(item.ItemId) : null;
                var score = definition == null ? 0f : ((definition.Grade - 1) * steps + Math.Min(item!.Upgrade, UpgradeRules.MaxLevel) + 1) / (float)(3 * steps);
                total += score;
                if (definition != null && definition.Grade >= target) atTarget++;
                if (score < weakestScore)
                {
                    weakestScore = score;
                    weakest = i;
                }
            }
            var goal = new ProgressGoal
            {
                Track = ProgressTrack.Gear, Fraction = Clamp(total / GearSlots.Length),
                Value = new LocalText("progress.gear_value", atTarget, GearSlots.Length),
            };

            var (slot, labelKey, prefix) = GearSlots[weakest];
            var worn = input.Equipment[slot];
            var wornDefinition = worn != null ? ItemCatalog.Find(worn.ItemId) : null;
            var label = Loc.Ref(labelKey);
            if (wornDefinition == null)
                goal.Next = new LocalText("progress.gear_empty", label);
            else if (wornDefinition.Grade < target)
            {
                var itemId = (slot == EquipSlot.Weapon ? ClassInfo.WeaponPrefix(input.Class) : prefix) + GradeSuffix[target - 1];
                goal.Next = new LocalText("progress.gear_grade", label, Loc.Ref(Loc.Key("item.", itemId)));
            }
            else if (worn!.Upgrade < UpgradeRules.MaxLevel)
                goal.Next = new LocalText("progress.gear_upgrade", label, worn.Upgrade + 1, Loc.Ref(Loc.Key("npc.", NpcCatalog.Smith)));
            else
            {
                goal.Done = true;
                goal.Next = new LocalText("progress.gear_done");
            }
            return goal;
        }

        private static ProgressGoal Awakening(ProgressInput input)
        {
            var full = input.AwakeningGauge >= AwakeningState.MaxGauge;
            return new ProgressGoal
            {
                Track = ProgressTrack.Awakening, Fraction = Clamp(input.AwakeningGauge / (float)AwakeningState.MaxGauge),
                Value = new LocalText("progress.awakening_value", input.AwakeningGauge, AwakeningState.MaxGauge),
                Next = full
                    ? new LocalText("progress.awakening_ready", (int)Math.Round(AwakeningState.DamageBonus * 100), AwakeningState.DurationSeconds)
                    : new LocalText("progress.awakening_next", AwakeningState.ExcellentGain, AwakeningState.GoodGain),
            };
        }

        private static ProgressGoal Quests(ProgressInput input)
        {
            var story = QuestCatalog.All.Where(q => !q.Daily).ToList();
            var done = story.Count(q => input.QuestCompleted(q.Id));
            var goal = new ProgressGoal
            {
                Track = ProgressTrack.Quests, Done = done >= story.Count, Fraction = done / (float)Math.Max(1, story.Count),
                Value = new LocalText("progress.quests_value", done, story.Count),
                Next = new LocalText("progress.quests_done"),
            };
            // Primeiro a que está em andamento; depois a próxima que já dá para pegar; senão a que espera nível.
            var pending = story.Where(q => !input.QuestCompleted(q.Id)).ToList();
            var active = pending.FirstOrDefault(q => input.ActiveQuests.Contains(q.Id));
            var available = pending.FirstOrDefault(q => q.MinLevel <= input.Level &&
                                                        (string.IsNullOrEmpty(q.PreviousQuestId) || input.QuestCompleted(q.PreviousQuestId)));
            var locked = pending.Where(q => string.IsNullOrEmpty(q.PreviousQuestId) || input.QuestCompleted(q.PreviousQuestId))
                .OrderBy(q => q.MinLevel).FirstOrDefault();
            if (active != null)
                goal.Next = new LocalText("progress.quest_active", Loc.Ref(Loc.Key("quest.", active.Id)), Loc.Ref(Loc.Key("npc.", active.GiverNpcId)));
            else if (available != null)
                goal.Next = new LocalText("progress.quest_next", Loc.Ref(Loc.Key("quest.", available.Id)), Loc.Ref(Loc.Key("npc.", available.GiverNpcId)));
            else if (locked != null)
                goal.Next = new LocalText("progress.quest_locked", Loc.Ref(Loc.Key("quest.", locked.Id)), locked.MinLevel);
            return goal;
        }

        private static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
