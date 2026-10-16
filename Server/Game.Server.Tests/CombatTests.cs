using Game.Server.World;
using Game.Shared.Combat;
using Game.Shared.Loot;
using Game.Shared.Monsters;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

public class CombatTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();

        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> Notices(int playerId) => To<Notice>(playerId).Select(n => n.Text).ToList();
    }

    /// <summary>Sempre o mesmo número: 0,5 = sem variação de dano e sem crítico.</summary>
    private sealed class FixedRandom : IRandomSource
    {
        private readonly double _value;
        public FixedRandom(double value) => _value = value;
        public double NextDouble() => _value;
    }

    private static MonsterData Dummy(bool aggressive = false, int hp = 5000, int attack = 50, float windup = 0.5f) => new()
    {
        Id = "teste", Name = "Monstro de teste", Level = 1, MaxHp = hp, AttackDamage = attack, AttackPower = 0,
        Defense = 0, AttackRange = 1.5f, AttackCooldownSeconds = 2f, AttackWindupSeconds = windup, MoveSpeed = 3f,
        Aggressive = aggressive, AggroRadius = 8, LeashRadius = 30, WanderRadius = 0, XpReward = 50, RespawnSeconds = 5,
        Drops = new DropTable(10, 20, new DropEntry("pele", "Pele", ItemRarity.Common, 0.9)),
    };

    private static (GameWorld World, FakeOutbox Outbox, ServerPlayer Player, ServerMonster Monster) Setup(
        MonsterData data, float monsterZ = 2f, double random = 0.5, bool boss = false)
    {
        var outbox = new FakeOutbox();
        var spawn = new SpawnDefinition { Data = data, CenterX = 0, CenterZ = monsterZ, Count = 1, Radius = 0, IsBoss = boss };
        var world = new GameWorld(outbox, new[] { spawn }, new FixedRandom(random));
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(0, 0, 0); // os testes medem distâncias a partir da origem
        return (world, outbox, player, world.Monsters.Single());
    }

    [Fact]
    public void Skill_acerta_o_monstro_e_respeita_a_recarga()
    {
        var (world, outbox, player, monster) = Setup(Dummy());

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1 }, now: 1);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 1.1 }, now: 1.1);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.Equal((player.Id, monster.Id, (byte)0), (hit.AttackerId, hit.TargetId, hit.Source));
        // (120 + ataque 60 do nível 1) × 1, sem defesa, sem variação.
        Assert.Equal(180, hit.Damage);
        Assert.Equal(5000 - 180, monster.Hp.Current);
        Assert.Contains("Corte em recarga", outbox.Notices(player.Id));
    }

    [Fact]
    public void Fora_do_alcance_nao_acerta()
    {
        var (world, outbox, player, monster) = Setup(Dummy(), monsterZ: 20);

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);

        Assert.Empty(outbox.To<CombatEvent>(player.Id));
        Assert.Contains("Fora de alcance", outbox.Notices(player.Id));
    }

    [Fact]
    public void Magia_gasta_mana()
    {
        var (world, outbox, player, monster) = Setup(Dummy(), monsterZ: 8);

        world.Handle(player.Id, new UseSkill { Slot = 2, TargetId = monster.Id }, now: 1);

        Assert.Equal(player.Mp.Max - 40, player.Mp.Current);
        world.Tick(1.0); // no tick a mana já começa a voltar (20/s) e o cliente é avisado
        Assert.InRange(outbox.To<PlayerStats>(player.Id).Last().Mp, player.Mp.Max - 40, player.Mp.Max - 39);
    }

    [Fact]
    public void Matar_da_XP_Alz_e_drop_do_dono_e_o_monstro_renasce()
    {
        var (world, outbox, player, monster) = Setup(Dummy(hp: 100), random: 0.0);

        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);
        world.Tick(1.0);

        Assert.True(monster.IsDead);
        var flags = outbox.To<Snapshot>(player.Id).Last().Entities.Single(e => e.Id == monster.Id).Flags;
        Assert.NotEqual(0, flags & EntityState.FlagDead);
        var stats = outbox.To<PlayerStats>(player.Id).Last();
        Assert.Equal(50, stats.Xp);  // monstro do mesmo nível: XP cheia
        Assert.Equal(10, stats.Alz); // sorteio 0 = mínimo da faixa
        var loot = Assert.Single(outbox.To<LootSpawned>(player.Id));
        Assert.Equal("pele", loot.ItemId);

        world.Tick(1.0 + monster.Data.RespawnSeconds + 0.1);
        Assert.False(monster.IsDead);
        Assert.Equal(monster.Hp.Max, monster.Hp.Current);
    }

    [Fact]
    public void Pegar_item_vai_para_o_inventario_e_some_para_todos()
    {
        var (world, outbox, player, monster) = Setup(Dummy(hp: 100), random: 0.0);
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, now: 1);
        world.Tick(1.0);
        var loot = world.Loot.Single();

        world.Handle(player.Id, new PickupLoot { LootId = loot.Id }, now: 1.1);

        Assert.Equal(1, player.Inventory["pele"].Quantity);
        Assert.Equal(loot.Id, Assert.Single(outbox.To<LootRemoved>(player.Id)).LootId);
        Assert.Empty(world.Loot);
    }

    [Fact]
    public void Combo_julga_pelo_horario_do_aperto_compensando_a_latencia()
    {
        var (world, outbox, player, monster) = Setup(Dummy());
        world.Handle(player.Id, new StartCombo { TargetId = monster.Id, ClientTime = 10 }, now: 10);

        // Apertou no centro da janela (10 + 0,75 × 1,2 = 10,9), mas a mensagem chegou 0,2 s depois:
        // sem compensação, 11,1 já passou do fim da janela.
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id, ClientTime = 10.9 }, now: 11.1);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id));
        Assert.Equal((byte)ComboGrade.Excellent, hit.Grade);
        Assert.Equal(216, hit.Damage); // 180 × 1,20
        Assert.Equal(1, player.Combo.HitCount);
        Assert.Equal(PlayerRulesStartingSpMinusCombo(), player.Sp.Current);
    }

    private static int PlayerRulesStartingSpMinusCombo() => PlayerRules.StartingSp - ComboConfig.Default.ActivationSpCost;

    [Theory]
    [InlineData(50.0, 10.0, 10.0)]   // do futuro: vira agora
    [InlineData(9.0, 10.0, 9.65)]    // antigo demais: limitado a 0,35 s
    [InlineData(9.8, 10.0, 9.8)]     // dentro do limite: aceito
    public void Horario_do_aperto_tem_limites(double clientTime, double now, double expected)
    {
        Assert.Equal(expected, GameWorld.PressTime(clientTime, now), 6);
    }

    [Fact]
    public void Monstro_agressivo_ataca_depois_do_preparo()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true), monsterZ: 1.5f);

        for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id), e => e.AttackerId == monster.Id);
        Assert.Equal(CombatSource.MonsterAttack, hit.Source);
        Assert.True(player.Hp.Current < player.Hp.Max);
    }

    [Fact]
    public void Esquiva_na_hora_certa_evita_o_golpe()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true), monsterZ: 1.5f);
        world.Tick(0.05); // começa o preparo; o golpe sai em 0,55

        world.Handle(player.Id, new MoveInput { Sequence = 1, X = 0, Z = 0, Dodging = true }, now: 0.45);
        for (var t = 0.1; t <= 0.7; t += 0.05) world.Tick(t);

        var hit = Assert.Single(outbox.To<CombatEvent>(player.Id), e => e.AttackerId == monster.Id);
        Assert.True(hit.Dodged);
        Assert.Equal(player.Hp.Max, player.Hp.Current);
    }

    [Fact]
    public void Jogador_morre_e_renasce_na_vila()
    {
        var (world, outbox, player, monster) = Setup(Dummy(aggressive: true, attack: 999_999), monsterZ: 1.5f);

        for (var t = 0.05; t <= 0.7; t += 0.05) world.Tick(t);
        Assert.True(player.IsDead);
        Assert.Contains("Você morreu", outbox.Notices(player.Id));

        world.Tick(0.7 + PlayerRules.RespawnSeconds);
        Assert.False(player.IsDead);
        Assert.Equal(player.Hp.Max, player.Hp.Current);
        Assert.NotEmpty(outbox.To<PositionCorrection>(player.Id));
    }

    [Fact]
    public void So_ve_o_que_esta_perto()
    {
        var near = new SpawnDefinition { Data = Dummy(), CenterX = 0, CenterZ = 30, Count = 1 };
        var farData = Dummy();
        farData.Id = "longe";
        var far = new SpawnDefinition { Data = farData, CenterX = 0, CenterZ = 200, Count = 1 };
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, new[] { near, far }, new FixedRandom(0.5));
        var player = world.AddPlayer("Ana", 0);
        player.Movement.Teleport(0, 0, 0);

        world.Tick(0.05);

        var spawned = outbox.To<EntitySpawned>(player.Id).Where(e => e.Kind == EntityKind.Monster).ToList();
        Assert.Equal("teste", Assert.Single(spawned).TypeId);
        Assert.DoesNotContain(outbox.To<Snapshot>(player.Id).Last().Entities,
            e => e.Id == world.Monsters.Single(m => m.Data.Id == "longe").Id);
    }

    [Fact]
    public void Chefe_entra_na_fase_2_e_marca_area_que_acerta_quem_ficou()
    {
        var data = Dummy(aggressive: true, hp: 1000);
        data.AttackDamage = 1; // o golpe normal quase não machuca; o teste é a área
        var (world, outbox, player, boss) = Setup(data, monsterZ: 6, boss: true);

        boss.Hp.Add(-600);
        boss.AggroPlayerId = player.Id;
        boss.Brain.Provoke();
        double t = 0;
        while (t < 2.0) world.Tick(t += 0.05);

        Assert.Contains(outbox.To<Notice>(player.Id), n => n.Announcement && n.Text.Contains("furioso"));
        var warning = Assert.Single(outbox.To<AreaWarning>(player.Id));
        Assert.Equal(BossRules.AreaRadius, warning.Radius);

        while (t < 2.0 + BossRules.AreaWarningSeconds + 0.2) world.Tick(t += 0.05);
        Assert.Contains(outbox.To<CombatEvent>(player.Id), e => e.Source == CombatSource.BossArea && e.Damage > 0);
    }
}
