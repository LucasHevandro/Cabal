using Game.Server.World;
using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Chefe de mundo com horário: dorme, avisa, desperta e dá crédito a todos que lutaram.</summary>
public class WorldBossTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<string> Announcements(int playerId) =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<Notice>().Where(n => n.Announcement)
                .Select(n => n.Text.ToString()).ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.5;
    }

    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    private static SpawnDefinition Boss() => new()
    {
        CenterX = 0, CenterZ = 3, Count = 1, IsBoss = true, Scheduled = true,
        Data = new MonsterData
        {
            Id = "guardiao_das_ruinas", Level = 30, MaxHp = 1000, AttackDamage = 1, AttackPower = 0, Defense = 0,
            AttackRange = 2f, MoveSpeed = 1f, Aggressive = false, AggroRadius = 5, LeashRadius = 40, WanderRadius = 0,
            XpReward = 1000, RespawnSeconds = 120, Drops = new DropTable(10, 20),
        },
    };

    [Fact]
    public void Horas_pares_de_Brasilia()
    {
        var at = new DateTimeOffset(2026, 10, 20, 17, 40, 0, Brasilia);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 16, 0, 0, Brasilia), WorldBossRules.CurrentSlot(at));
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 18, 0, 0, Brasilia), WorldBossRules.NextSlot(at));
        // Virada do dia: 23h30 → próximo é 0h do dia seguinte.
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 0, 0, 0, Brasilia), WorldBossRules.NextSlot(new DateTimeOffset(2026, 10, 20, 23, 30, 0, Brasilia)));
    }

    [Fact]
    public void Dorme_ate_o_horario_avisa_dez_minutos_antes_e_desperta()
    {
        var clock = new DateTimeOffset(2026, 10, 20, 17, 0, 0, Brasilia);
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { Boss() }, new FixedRandom()) { Clock = () => clock };
        var player = world.AddPlayer("Ana", 0);
        var boss = world.Monsters.Single();

        world.Tick(0.05);
        Assert.True(boss.IsDead); // ligou às 17h: o das 16h já passou

        clock = new DateTimeOffset(2026, 10, 20, 17, 51, 0, Brasilia);
        world.Tick(1);
        world.Tick(2);
        Assert.Single(outbox.Announcements(player.Id), a => a.Contains("desperta em 9 minutos"));

        clock = new DateTimeOffset(2026, 10, 20, 18, 0, 1, Brasilia);
        world.Tick(3);
        Assert.False(boss.IsDead);
        Assert.Contains(outbox.Announcements(player.Id), a => a.Contains("despertou"));
    }

    [Fact]
    public void Todos_que_bateram_ganham_XP_e_contam_a_diaria()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { Boss() }, new FixedRandom());
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 0);
        foreach (var p in new[] { ana, bia })
        {
            p.Progress = new Game.Shared.Progression.CharacterProgress(29, 0); // no 30 (máximo) a XP não acumula
            p.Movement.Teleport(0, 0, 0);
            Assert.Null(p.Quests.Accept("diaria_guardiao", 30));
        }
        world.Handle(ana.Id, new GmCommand { Text = "chefe" }, 0); // Ana não é GM: não chama
        var boss = world.Monsters.Single();
        Assert.True(boss.IsDead);

        ana.IsGm = true;
        world.Handle(ana.Id, new GmCommand { Text = "chefe" }, 0);
        Assert.False(boss.IsDead);

        // As duas batem; a Ana dá o último golpe.
        boss.DamageBy[bia.Id] = 300;
        boss.DamageBy[ana.Id] = 400;
        boss.Hp.Add(-(boss.Hp.Current - 1));
        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = boss.Id, ClientTime = 1 }, now: 1);
        world.Tick(1.6);

        Assert.True(boss.IsDead);
        Assert.All(new[] { ana, bia }, p =>
        {
            Assert.True(p.Progress.Xp > 0);
            Assert.Equal(1, p.Quests.ProgressOf("diaria_guardiao")!.Counts[0]);
        });
        Assert.Contains(outbox.Announcements(bia.Id), a => a.Contains("derrotado por Ana e mais 1"));
    }
}
