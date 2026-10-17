using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Dungeon;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Dungeon em instância: entrada, portões, chefe com escudo, baú, medalha, ranking e saída.</summary>
public class DungeonWorldTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> Texts(int playerId) =>
            To<ChatMessage>(playerId).Select(m => m.Display).Concat(To<Notice>(playerId).Select(n => n.Text.ToString())).ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.01; // sempre sorteia os drops do baú
    }

    private static readonly NpcDefinition Portal = NpcCatalog.Find(NpcCatalog.DungeonPortal)!;
    private double _now = 1;
    private long _uid = 9000;

    /// <summary>Monstros com 1% da vida (o chefe fica com 4.500) e sem nível mínimo.</summary>
    private static (GameWorld World, FakeOutbox Outbox) NewWorld()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>(), new FixedRandom(), new ItemUidAllocator(5000),
            new DungeonTuning(0.01f, 0.01f, 1));
        return (world, outbox);
    }

    private ServerPlayer AddAtPortal(GameWorld world, string name, CharacterRecord? record = null)
    {
        var player = world.AddPlayer(name, _now, null, record);
        player.Movement.Teleport(Portal.X, Portal.Z - 1, _now);
        return player;
    }

    private void Group(GameWorld world, ServerPlayer leader, params ServerPlayer[] members)
    {
        foreach (var member in members)
        {
            world.Handle(leader.Id, new PartyCommand { Action = PartyActionKind.Invite, TargetName = member.Name }, _now);
            world.Handle(member.Id, new PartyCommand { Action = PartyActionKind.Accept, TargetId = leader.Id }, _now);
        }
    }

    private void Enter(GameWorld world, ServerPlayer player) =>
        world.Handle(player.Id, new DungeonCommand { Action = DungeonActionKind.Enter, DungeonId = DungeonCatalog.CryptId }, _now);

    private void Tick(GameWorld world, double seconds = 0.05)
    {
        _now += seconds;
        world.Tick(_now);
    }

    /// <summary>Mata o monstro com um golpe de verdade (deixa com 1 de vida e usa a skill 0 de perto).</summary>
    private void Kill(GameWorld world, ServerPlayer player, ServerMonster monster)
    {
        if (monster.Hp.Current > 1) monster.Hp.Add(-(monster.Hp.Current - 1));
        player.Movement.Teleport(monster.X, monster.Z - monster.Data.BodyRadius - 1f, _now);
        _now += 2; // recarga da skill
        world.Handle(player.Id, new UseSkill { Slot = 0, TargetId = monster.Id }, _now);
        Assert.True(monster.IsDead, $"{monster.Data.Id} não morreu");
    }

    private void ClearRoom(GameWorld world, ServerPlayer player, DungeonInstance instance)
    {
        foreach (var id in instance.RoomMonsters[instance.Room].ToList())
        {
            var monster = world.FindMonster(id)!;
            if (!monster.IsDead) Kill(world, player, monster);
        }
        Tick(world);
    }

    [Fact]
    public void Lider_entra_com_o_grupo_gasta_as_chaves_e_vai_para_a_instancia()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        var bia = AddAtPortal(world, "Bia");
        Group(world, ana, bia);

        Enter(world, bia);
        Assert.Contains("Só o líder do grupo pode entrar com todos", outbox.Texts(bia.Id));

        Enter(world, ana);
        var instance = Assert.Single(world.Dungeons);
        Assert.Equal((instance.Id, instance.Id), (ana.DungeonId, bia.DungeonId));
        Assert.Equal(2, ana.Bag.Count(DungeonCatalog.CryptKey)); // convidado começa com 3
        Assert.Equal(DungeonRules.InstanceOriginZ, instance.OriginZ);
        Assert.InRange(ana.Z, instance.OriginZ, instance.OriginZ + 10);
        Assert.Equal(15, world.Monsters.Count(m => m.InstanceId == instance.Id)); // 4 + 5 + 5 + o chefe

        // O estado chega antes da posição (o cliente monta o chão primeiro).
        var mine = outbox.Sent.Where(s => s.PlayerId == ana.Id).Select(s => s.Message).ToList();
        Assert.True(mine.FindIndex(m => m is DungeonState) < mine.FindLastIndex(m => m is PositionCorrection));
    }

    [Fact]
    public void Sem_chave_ou_longe_do_portal_nao_entra()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        ana.Bag.TryRemove(DungeonCatalog.CryptKey, 3);
        Enter(world, ana);
        Assert.Contains(outbox.Texts(ana.Id), t => t.Contains("precisa de uma Chave da Cripta"));

        var bia = AddAtPortal(world, "Bia");
        bia.Movement.Teleport(40, 40, _now);
        Enter(world, bia);
        Assert.Contains(outbox.Texts(bia.Id), t => t.StartsWith("Fale com Guardiã Selene"));
        Assert.Empty(world.Dungeons);
    }

    [Fact]
    public void Portao_segura_ate_a_sala_ficar_limpa()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        Enter(world, ana);
        var instance = world.Dungeons.Single();
        var gate = instance.OriginZ + instance.Definition.Rooms[0].GateZ;

        // Tenta passar do portão andando aos poucos (velocidade válida): o servidor segura.
        ana.Movement.Teleport(0, gate - 1, _now);
        Tick(world, 0.5);
        world.Handle(ana.Id, new MoveInput { Sequence = 1, X = 0, Z = gate + 2 }, _now);
        Assert.True(ana.Z < gate);
        Assert.Contains(outbox.To<PositionCorrection>(ana.Id), c => c.Z < gate);

        ClearRoom(world, ana, instance);
        Assert.Equal(1, instance.Room);
        Assert.Contains(outbox.Texts(ana.Id), t => t.StartsWith("O portão se abriu"));
        ana.Movement.Teleport(0, gate - 1, _now);
        Tick(world, 0.5);
        world.Handle(ana.Id, new MoveInput { Sequence = 2, X = 0, Z = gate + 2 }, _now);
        Assert.True(ana.Z > gate);
    }

    [Fact]
    public void Chefe_ergue_escudo_com_servos_e_a_vitoria_da_bau_medalha_e_ranking()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        var bia = AddAtPortal(world, "Bia");
        Group(world, ana, bia);
        Enter(world, ana);
        var instance = world.Dungeons.Single();
        for (var i = 0; i < 3; i++) ClearRoom(world, ana, instance);
        Assert.True(instance.InBossRoom);

        var boss = world.FindMonster(instance.BossId)!;
        boss.Hp.Add(-(int)(boss.Hp.Max * 0.4f)); // 60% da vida
        Tick(world);
        Assert.True(instance.Shield && boss.Invulnerable);
        Assert.Equal(CryptBossRules.ServantsPerShield, instance.Servants.Count);
        Assert.Contains(outbox.Texts(ana.Id), t => t.Contains("ergueu um escudo"));

        // Com escudo, o golpe acerta mas não tira vida.
        var hp = boss.Hp.Current;
        ana.Movement.Teleport(boss.X, boss.Z - 2, _now);
        _now += 2;
        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = boss.Id }, _now);
        Assert.Equal(hp, boss.Hp.Current);

        foreach (var id in instance.Servants) Kill(world, ana, world.FindMonster(id)!);
        Tick(world);
        Assert.False(instance.Shield || boss.Invulnerable);

        Kill(world, ana, boss);
        Tick(world);
        Assert.True(instance.Completed);
        Assert.Equal(DungeonMedal.A, instance.Medal); // rápido, mas sem combo de 12
        Assert.Contains(outbox.Texts(bia.Id), t => t.StartsWith("Vitória em"));

        // Baú: 2 sorteios (A) para cada um, só o dono pega.
        var chest = world.Loot.Where(l => l.InstanceId == instance.Id && l.OwnerUntil > _now + 60).ToList();
        Assert.Contains(chest, l => l.OwnerId == ana.Id);
        Assert.Contains(chest, l => l.OwnerId == bia.Id);
        Assert.All(chest, l => Assert.False(l.CanBePickedBy(l.OwnerId == ana.Id ? bia.Id : ana.Id, ana.PartyId, _now)));

        var run = Assert.Single(world.Ranking(DungeonCatalog.CryptId));
        Assert.Equal("Ana,Bia", run.Members);
        Assert.Contains(outbox.To<DungeonInfo>(ana.Id), i => i.Ranking.Count == 1);
        var state = outbox.To<DungeonState>(ana.Id).Last();
        Assert.True(state.Completed);
        Assert.Equal((byte)DungeonMedal.A, state.Medal);

        // A instância fecha sozinha depois de um tempo e todos voltam ao portal.
        Tick(world, DungeonRules.CloseAfterCompletionSeconds + 1);
        Assert.Empty(world.Dungeons);
        Assert.Equal(0, ana.DungeonId);
        Assert.True(Math.Abs(ana.Z - Portal.Z) < 5);
        Assert.DoesNotContain(world.Monsters, m => m.InstanceId == instance.Id);
        Assert.False(outbox.To<DungeonState>(ana.Id).Last().Active);
    }

    [Fact]
    public void Vitoria_de_contas_vai_para_o_banco_e_convidados_nao()
    {
        var (world, _) = NewWorld();
        var runs = new List<DungeonRunRecord>();
        world.RunCompleted += runs.Add;
        var record = AuthService.InitialCharacter(CharacterClass.Blade, () => ++_uid) with { Id = 1, AccountId = 1, Name = "Ana" };
        var ana = AddAtPortal(world, "", record);
        ana.Bag.TryAdd(DungeonCatalog.CryptKey, 1, () => ++_uid);
        ana.Progress = new CharacterProgress(20, 0);
        // Conta: o combo só libera na missão; aqui só a medalha e o registro importam.
        Enter(world, ana);
        var instance = world.Dungeons.Single();
        for (var i = 0; i < 3; i++) ClearRoom(world, ana, instance);
        var boss = world.FindMonster(instance.BossId)!;
        instance.ShieldsUsed = CryptBossRules.ShieldAtHpFractions.Length; // pula os escudos
        Kill(world, ana, boss);
        Tick(world);
        Assert.Single(runs);
        Assert.Equal(0, ana.Bag.Count(DungeonCatalog.CryptKey));
        var (_, logs) = world.CaptureForSave(ana);
        Assert.Contains(logs, l => l.Kind == "chave_dungeon");
        Assert.Contains(logs, l => l.Kind == "bau_dungeon" && l.AlzDelta > 0);
    }

    [Fact]
    public void Tempo_esgotado_expulsa_e_renasce_na_entrada()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        Enter(world, ana);
        var instance = world.Dungeons.Single();

        ana.Hp.Add(-ana.Hp.Max);
        ana.IsDead = true;
        ana.RespawnAt = _now;
        Tick(world);
        Assert.False(ana.IsDead);
        Assert.Equal((instance.EntranceX, instance.EntranceZ), (ana.X, ana.Z));

        Tick(world, DungeonRules.TimeLimitSeconds + 1);
        Assert.Empty(world.Dungeons);
        Assert.Equal(0, ana.DungeonId);
        Assert.Contains(outbox.Texts(ana.Id), t => t.StartsWith("Tempo esgotado"));
    }

    [Fact]
    public void Sair_do_grupo_tira_da_dungeon_e_ultimo_a_sair_fecha_a_instancia()
    {
        var (world, _) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        var bia = AddAtPortal(world, "Bia");
        var caio = AddAtPortal(world, "Caio");
        Group(world, ana, bia, caio);
        Enter(world, ana);
        var instance = world.Dungeons.Single();

        world.Handle(bia.Id, new PartyCommand { Action = PartyActionKind.Leave }, _now);
        Assert.Equal(0, bia.DungeonId);
        world.Handle(caio.Id, new DungeonCommand { Action = DungeonActionKind.Leave }, _now);
        Assert.Equal(0, caio.DungeonId);
        Tick(world);
        Assert.Single(world.Dungeons);

        world.RemovePlayer(ana.Id);
        Tick(world);
        Assert.Empty(world.Dungeons);
        Assert.DoesNotContain(world.Monsters, m => m.InstanceId == instance.Id);
    }

    [Fact]
    public void Dois_grupos_ficam_em_copias_separadas_e_nao_se_veem()
    {
        var (world, outbox) = NewWorld();
        var ana = AddAtPortal(world, "Ana");
        var bia = AddAtPortal(world, "Bia");
        Enter(world, ana);
        Enter(world, bia);
        Assert.Equal(2, world.Dungeons.Count);
        var a = world.FindDungeon(ana.DungeonId)!;
        var b = world.FindDungeon(bia.DungeonId)!;
        Assert.NotEqual(a.OriginZ, b.OriginZ);
        Tick(world);
        var seenByAna = outbox.To<EntitySpawned>(ana.Id).Select(s => s.Id).ToHashSet();
        Assert.DoesNotContain(bia.Id, seenByAna);
        Assert.DoesNotContain(world.Monsters.Where(m => m.InstanceId == b.Id), m => seenByAna.Contains(m.Id));
        Assert.Contains(world.Monsters.Where(m => m.InstanceId == a.Id), m => seenByAna.Contains(m.Id));
    }
}
