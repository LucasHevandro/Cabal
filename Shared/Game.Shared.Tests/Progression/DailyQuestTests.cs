using System;
using System.Linq;
using Game.Shared.Items;
using Game.Shared.Progression;
using Xunit;

namespace Game.Shared.Tests.Progression;

/// <summary>Missões diárias: voltam no dia seguinte e não ocupam as vagas da história.</summary>
public class DailyQuestTests
{
    private long _uid = 1;
    private long NewUid() => _uid++;

    private (QuestLog Log, Func<int> Advance) LogOnDay(int start = 20000)
    {
        var day = start;
        return (new QuestLog(() => day), () => ++day);
    }

    private void Finish(QuestLog log, string questId, ItemContainer bag)
    {
        Assert.Null(log.Accept(questId, 30));
        for (var i = 0; i < 12; i++) log.OnKill("coelho_saltador");
        Assert.Null(log.TurnIn(questId, bag, NewUid, out _));
    }

    [Fact]
    public void Diaria_entregue_volta_no_dia_seguinte()
    {
        var (log, nextDay) = LogOnDay();
        var bag = new ItemContainer();
        Finish(log, "diaria_coelhos", bag);

        Assert.True(log.IsCompleted("diaria_coelhos"));
        Assert.Contains("diaria_coelhos", log.Completed);
        Assert.Equal("quest.daily_done", log.Accept("diaria_coelhos", 30)?.Key);
        Assert.DoesNotContain(log.AvailableFrom(NpcCatalog.Hunter, 30), q => q.Id == "diaria_coelhos");

        nextDay();
        Assert.False(log.IsCompleted("diaria_coelhos"));
        Assert.DoesNotContain("diaria_coelhos", log.Completed);
        Assert.Contains(log.AvailableFrom(NpcCatalog.Hunter, 30), q => q.Id == "diaria_coelhos");
        Finish(log, "diaria_coelhos", bag); // e dá para fazer de novo
    }

    [Fact]
    public void Diarias_nao_ocupam_as_vagas_de_missao()
    {
        var (log, _) = LogOnDay();
        var story = QuestCatalog.All.Where(q => !q.Daily && q.MinLevel <= 30 && string.IsNullOrEmpty(q.PreviousQuestId))
            .Take(QuestCatalog.MaxActive).ToList();
        foreach (var quest in story) Assert.Null(log.Accept(quest.Id, 30));

        // As cinco vagas cheias: outra da história não entra, mas as diárias sim.
        var extra = QuestCatalog.All.First(q => !q.Daily && q.MinLevel <= 30 && string.IsNullOrEmpty(q.PreviousQuestId) && !log.IsActive(q.Id));
        Assert.Equal("quest.max_active", log.Accept(extra.Id, 30)?.Key);
        foreach (var daily in QuestCatalog.All.Where(q => q.Daily)) Assert.Null(log.Accept(daily.Id, 30));
    }

    [Fact]
    public void Restaurar_guarda_o_dia_de_cada_diaria()
    {
        var (log, nextDay) = LogOnDay(30000);
        log.Restore(new[] { "primeiros_passos", "diaria_lobos" }, Array.Empty<QuestProgress>(),
            new[] { new System.Collections.Generic.KeyValuePair<string, int>("diaria_lobos", 30000) });

        Assert.True(log.IsCompleted("primeiros_passos"));
        Assert.True(log.IsCompleted("diaria_lobos")); // entregue hoje
        Assert.Equal(30000, log.DailyDone["diaria_lobos"]);
        nextDay();
        Assert.False(log.IsCompleted("diaria_lobos"));
        Assert.True(log.IsCompleted("primeiros_passos"));
    }

    [Fact]
    public void O_dia_vira_a_meia_noite_de_Brasilia()
    {
        var before = DailyRules.Day(new DateTimeOffset(2026, 10, 21, 2, 59, 0, TimeSpan.Zero)); // 23h59 em Brasília
        var after = DailyRules.Day(new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero)); // 0h em Brasília
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public void Toda_diaria_existe_no_catalogo_com_NPC_e_texto()
    {
        var dailies = QuestCatalog.All.Where(q => q.Daily).ToList();
        Assert.Equal(5, dailies.Count);
        Assert.All(dailies, q =>
        {
            Assert.NotNull(NpcCatalog.Find(q.GiverNpcId));
            Assert.True(string.IsNullOrEmpty(q.PreviousQuestId));
            Assert.NotEqual("quest." + q.Id, q.Name);
        });
    }
}
