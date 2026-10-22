using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Progression;

namespace Game.Shared.Tests.Progression;

public class ProgressionGoalsTests
{
    private static ProgressGoal Track(ProgressInput input, ProgressTrack track) => ProgressionGoals.For(input).Single(g => g.Track == track);

    [Fact]
    public void Tem_as_seis_barras_e_todas_com_proximo_objetivo()
    {
        var goals = ProgressionGoals.For(new ProgressInput());

        Assert.Equal(Enum.GetValues<ProgressTrack>(), goals.Select(g => g.Track));
        Assert.All(goals, g => Assert.False(string.IsNullOrWhiteSpace(g.Next.ToString()), g.Track.ToString()));
        Assert.All(goals, g => Assert.InRange(g.Fraction, 0f, 1f));
    }

    [Fact]
    public void Nivel_mostra_a_xp_que_falta()
    {
        var goal = Track(new ProgressInput { Level = 5, Xp = 100 }, ProgressTrack.Level);

        var toNext = LevelCurve.XpToNext(5);
        Assert.Equal(100f / toNext, goal.Fraction, 3);
        Assert.Contains($"{toNext - 100:N0}", goal.Next.ToString());
        Assert.True(Track(new ProgressInput { Level = LevelCurve.MaxLevel }, ProgressTrack.Level).Done);
    }

    [Fact]
    public void Rank_aponta_a_proxima_missao_de_rank()
    {
        var completed = new HashSet<string> { ClassCatalog.ComboQuestId };
        var goal = Track(new ProgressInput { Level = 12, QuestCompleted = completed.Contains }, ProgressTrack.Rank);

        Assert.Equal(1f / 3, goal.Fraction, 3);
        Assert.Contains(QuestCatalog.Get(ClassCatalog.RankTwoQuestId).Name, goal.Next.ToString());
    }

    [Fact]
    public void Skills_dizem_o_que_falta_para_a_proxima()
    {
        var goal = Track(new ProgressInput { Level = 1, SkillLearned = i => i < 2 }, ProgressTrack.Skills);

        Assert.Equal(2f / ClassCatalog.HotbarSize, goal.Fraction, 3);
        Assert.Contains(ClassCatalog.SkillsFor(CharacterClass.Blade)[2].Skill.Name, goal.Next.ToString());
    }

    [Fact]
    public void Equipamento_pede_a_peca_mais_fraca()
    {
        var equipment = new Equipment();
        foreach (var (slot, id) in new[] { (EquipSlot.Weapon, "arco_aco_negro"), (EquipSlot.Armor, "armadura_aco_negro"),
                     (EquipSlot.Gloves, "luvas_aco_negro"), (EquipSlot.Boots, "botas_aco_negro") })
            equipment.Put(slot, new ItemInstance { ItemId = id, Upgrade = 3 });

        var empty = Track(new ProgressInput { Class = CharacterClass.Ranger, Level = 12, Equipment = equipment }, ProgressTrack.Gear);
        Assert.Contains("Anel", empty.Next.ToString());

        equipment.Put(EquipSlot.Accessory, new ItemInstance { ItemId = "anel_ferro" });
        var grade = Track(new ProgressInput { Class = CharacterClass.Ranger, Level = 12, Equipment = equipment }, ProgressTrack.Gear);
        Assert.Contains(ItemCatalog.Get("anel_aco_negro").Name, grade.Next.ToString());

        var weapon = Track(new ProgressInput { Class = CharacterClass.Ranger, Level = 22, Equipment = equipment }, ProgressTrack.Gear);
        Assert.Contains(ItemCatalog.Get("anel_astralita").Name, weapon.Next.ToString()); // o anel de Ferro continua o pior
    }

    [Fact]
    public void Missoes_mostram_a_ativa_antes_da_proxima()
    {
        var first = QuestCatalog.All.First(q => !q.Daily);
        var goal = Track(new ProgressInput { ActiveQuests = new[] { first.Id } }, ProgressTrack.Quests);

        Assert.Contains(first.Name, goal.Next.ToString());
        Assert.StartsWith("terminar", goal.Next.ToString());
    }
}
