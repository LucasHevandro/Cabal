using Game.Server.World;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

public class GameWorldTests
{
    private static readonly SpawnDefinition[] NoMonsters = Array.Empty<SpawnDefinition>();

    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();

        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();
    }

    [Fact]
    public void Quem_entra_recebe_boas_vindas()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);

        var player = world.AddPlayer("Ana", now: 5);

        var welcome = Assert.Single(outbox.To<Welcome>(player.Id));
        Assert.Equal(player.Id, welcome.PlayerId);
        Assert.Equal(GameWorld.TickRate, welcome.TickRate);
        Assert.Equal(5, welcome.ServerTime);
    }

    [Fact]
    public void Jogadores_veem_uns_aos_outros()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 1);

        world.Tick(1.05);

        // Cada um conhece a si mesmo e ao outro.
        Assert.Contains(outbox.To<EntitySpawned>(bia.Id), e => e.Name == "Ana");
        Assert.Contains(outbox.To<EntitySpawned>(ana.Id), e => e.Name == "Bia");
    }

    [Fact]
    public void Jogadores_veem_uns_aos_outros_so_uma_vez()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 1);
        world.Tick(1.05);
        world.Tick(1.10);

        Assert.Single(outbox.To<EntitySpawned>(bia.Id), e => e.Name == "Ana");
        Assert.Single(outbox.To<EntitySpawned>(ana.Id), e => e.Name == "Bia");
    }

    [Fact]
    public void Id_eh_informado_antes_das_boas_vindas()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var sentBeforeCallback = -1;

        world.AddPlayer("Ana", 0, _ => sentBeforeCallback = outbox.Sent.Count);

        Assert.Equal(0, sentBeforeCallback);
    }

    [Fact]
    public void Movimento_valido_aparece_no_snapshot()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);

        world.Handle(ana.Id, new MoveInput { Sequence = 1, X = 3, Z = 0, Yaw = 90 }, now: 1);
        world.Tick(1.05);

        var state = Assert.Single(outbox.To<Snapshot>(ana.Id).Last().Entities);
        Assert.Equal((3f, 0f, 90f), (state.X, state.Z, state.Yaw));
        Assert.Empty(outbox.To<PositionCorrection>(ana.Id));
    }

    [Fact]
    public void Teleporte_eh_recusado_e_corrigido()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var (startX, startZ) = (ana.Movement.X, ana.Movement.Z);

        world.Handle(ana.Id, new MoveInput { Sequence = 1, X = 100, Z = 0 }, now: 0.1);

        var correction = Assert.Single(outbox.To<PositionCorrection>(ana.Id));
        Assert.Equal((startX, startZ), (correction.X, correction.Z));
        Assert.Equal(startX, ana.Movement.X);
    }

    [Fact]
    public void Movimento_antigo_eh_ignorado()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        world.Handle(ana.Id, new MoveInput { Sequence = 5, X = 2, Z = 0 }, now: 1);

        world.Handle(ana.Id, new MoveInput { Sequence = 4, X = 1, Z = 0 }, now: 1.1);

        Assert.Equal(2f, ana.Movement.X);
    }

    [Fact]
    public void Quem_sai_some_para_os_outros()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);
        var bia = world.AddPlayer("Bia", 0);
        world.Tick(0.05);

        world.RemovePlayer(ana.Id);

        Assert.Equal(ana.Id, Assert.Single(outbox.To<EntityDespawned>(bia.Id)).Id);
        Assert.Single(world.Players);
    }

    [Fact]
    public void Ping_responde_com_o_horario_do_servidor()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, NoMonsters);
        var ana = world.AddPlayer("Ana", 0);

        world.Handle(ana.Id, new Ping { ClientTime = 1.25 }, now: 9.5);

        var pong = Assert.Single(outbox.To<Pong>(ana.Id));
        Assert.Equal((1.25, 9.5), (pong.ClientTime, pong.ServerTime));
    }

    [Fact]
    public void Jogadores_nascem_espalhados_perto_da_vila()
    {
        var points = Enumerable.Range(1, 6).Select(GameWorld.SpawnPointFor).ToList();

        Assert.Equal(points.Count, points.Distinct().Count());
        foreach (var (x, z) in points) Assert.InRange(MathF.Sqrt(x * x + z * z), 1.4f, 3.1f);
    }

    [Fact]
    public void Nome_vazio_ou_longo_eh_ajustado()
    {
        var world = new GameWorld(new FakeOutbox(), NoMonsters);

        Assert.Equal("Aventureiro", world.AddPlayer("   ", 0).Name);
        Assert.Equal(20, world.AddPlayer(new string('x', 50), 0).Name.Length);
    }
}
