using Game.Server.World;
using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Comportamentos que dependem do servidor: a matilha, o golpe em área do golem e a terceira fase do chefe.</summary>
public class MonsterBehaviorServerTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();

        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.5;
    }

    private static MonsterData Data(MonsterBehavior behavior, bool aggressive, int hp = 5000) => new()
    {
        Id = "teste", Level = 1, MaxHp = hp, AttackDamage = 40, AttackPower = 0, Defense = 0, AttackRange = 2.5f,
        AttackCooldownSeconds = 2f, AttackWindupSeconds = 0.5f, MoveSpeed = 3f, Aggressive = aggressive, AggroRadius = 8,
        LeashRadius = 30, WanderRadius = 0, XpReward = 50, RespawnSeconds = 5, Behavior = behavior,
        Drops = new DropTable(10, 20, new DropEntry("pelo_macio", ItemRarity.Common, 0.9)),
    };

    private static (GameWorld World, FakeOutbox Outbox, ServerPlayer Player) Setup(SpawnDefinition spawn)
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom());
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(0, 0, 0);
        return (world, outbox, player);
    }

    [Fact]
    public void Lobo_que_apanha_chama_a_matilha()
    {
        var spawn = new SpawnDefinition { Data = Data(MonsterBehavior.Pack, aggressive: false), CenterX = 0, CenterZ = 2, Count = 3, Radius = 0 };
        var (world, _, player) = Setup(spawn);
        var wolves = world.Monsters.ToList();
        Assert.All(wolves, w => Assert.False(w.Brain.Provoked));

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = wolves[0].Id, ClientTime = 1 }, now: 1);
        world.Tick(1.5); // o golpe sai depois da conjuração

        Assert.All(wolves, w =>
        {
            Assert.True(w.Brain.Provoked);
            Assert.Equal(player.Id, w.AggroPlayerId);
        });
    }

    [Fact]
    public void Golem_avisa_o_chao_e_o_golpe_acerta_em_volta_dele()
    {
        var spawn = new SpawnDefinition { Data = Data(MonsterBehavior.AreaSlam, aggressive: true), CenterX = 0, CenterZ = 2, Count = 1 };
        var (world, outbox, player) = Setup(spawn);
        var golem = world.Monsters.Single();

        double t = 0;
        while (t < 0.3) world.Tick(t += 0.05);
        var warning = Assert.Single(outbox.To<AreaWarning>(player.Id));
        Assert.Equal((golem.X, golem.Z, MonsterBehaviors.SlamRadius), (warning.X, warning.Z, warning.Radius));
        Assert.Equal(golem.Data.AttackWindupSeconds, warning.Seconds);

        while (t < 1.0) world.Tick(t += 0.05);
        Assert.Contains(outbox.To<CombatEvent>(player.Id), e => e.TargetId == player.Id && e.Damage > 0);
    }

    [Fact]
    public void Chefe_com_um_quarto_da_vida_joga_dois_golpes_em_area()
    {
        var data = Data(MonsterBehavior.Melee, aggressive: true, hp: 1000);
        data.AttackDamage = 1;
        var spawn = new SpawnDefinition { Data = data, CenterX = 0, CenterZ = 6, Count = 1, IsBoss = true };
        var (world, outbox, player) = Setup(spawn);
        var boss = world.Monsters.Single();

        boss.Hp.Add(-800); // 20% de vida: já passou das duas fases
        boss.AggroPlayerId = player.Id;
        boss.Brain.Provoke();
        double t = 0;
        while (t < 2.0) world.Tick(t += 0.05);

        Assert.Contains(outbox.To<Notice>(player.Id), n => n.Announcement && n.Text.ToString().Contains("desesperado"));
        var warnings = outbox.To<AreaWarning>(player.Id);
        Assert.Equal(2, warnings.Count);
        var gap = MathF.Sqrt(MathF.Pow(warnings[0].X - warnings[1].X, 2) + MathF.Pow(warnings[0].Z - warnings[1].Z, 2));
        Assert.Equal(BossRules.PhaseThreeSecondAreaOffset, gap, 2);
    }
}
