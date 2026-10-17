using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Combat;
using Game.Shared.Net;
using Game.Shared.Pvp;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>PvP decidido pelo servidor: duelo combinado e arena de captura de pontos.</summary>
public class PvpWorldTests
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
        public double NextDouble() => 0.5; // sem crítico
    }

    private double _now = 1;

    private (GameWorld World, FakeOutbox Outbox) NewWorld(int teamSize = 2)
    {
        var outbox = new FakeOutbox();
        return (new GameWorld(outbox, Array.Empty<SpawnDefinition>(), new FixedRandom(), new ItemUidAllocator(5000), arenaTeamSize: teamSize), outbox);
    }

    private ServerPlayer Add(GameWorld world, string name, float x = 0, float z = 0)
    {
        var player = world.AddPlayer(name, _now);
        player.Movement.Teleport(x, z, _now);
        return player;
    }

    private void Tick(GameWorld world, double seconds = 0.05)
    {
        // Passos de no máximo 0,25 s (o mundo limita o dt).
        var end = _now + seconds;
        while (_now < end - 1e-9)
        {
            _now = Math.Min(end, _now + 0.25);
            world.Tick(_now);
        }
    }

    private void Hit(GameWorld world, ServerPlayer attacker, ServerPlayer target)
    {
        _now += 2; // recarga
        world.Tick(_now);
        world.Handle(attacker.Id, new UseSkill { Slot = 0, TargetId = target.Id }, _now);
    }

    private void StartDuel(GameWorld world, ServerPlayer a, ServerPlayer b)
    {
        world.Handle(a.Id, new DuelCommand { Action = DuelActionKind.Request, TargetName = b.Name }, _now);
        world.Handle(b.Id, new DuelCommand { Action = DuelActionKind.Accept, TargetId = a.Id }, _now);
    }

    [Fact]
    public void Sem_duelo_ninguem_bate_em_ninguem()
    {
        var (world, outbox) = NewWorld();
        var ana = Add(world, "Ana");
        var bia = Add(world, "Bia", 1);
        Hit(world, ana, bia);
        Assert.Equal(bia.Hp.Max, bia.Hp.Current);
        Assert.Contains("Selecione um alvo", outbox.Texts(ana.Id));
    }

    [Fact]
    public void Duelo_espera_a_contagem_reduz_o_dano_e_acaba_em_1_de_vida()
    {
        var (world, outbox) = NewWorld();
        var ana = Add(world, "Ana");
        var bia = Add(world, "Bia", 1);
        StartDuel(world, ana, bia);
        Assert.Single(outbox.To<DuelInvite>(bia.Id));
        var duel = world.FindDuel(ana.DuelId)!;
        Assert.Equal(duel.Id, bia.DuelId);

        // Na contagem, ainda não vale.
        world.Handle(ana.Id, new UseSkill { Slot = 0, TargetId = bia.Id }, _now);
        Assert.Equal(bia.Hp.Max, bia.Hp.Current);

        Tick(world, DuelRules.CountdownSeconds + 0.1);
        Hit(world, ana, bia);
        var dealt = bia.Hp.Max - bia.Hp.Current;
        Assert.True(dealt > 0);
        var hit = outbox.To<CombatEvent>(bia.Id).Last(e => e.TargetId == bia.Id);
        Assert.Equal(dealt, hit.Damage);

        // O mesmo golpe num monstro de defesa igual daria cerca de 1/0,35 vezes mais.
        var full = DamageFormula.Calculate(new DamageInput(ana.Skills[0].BaseDamage, ana.Stats.AttackPower, 1, 0, PlayerRules.CritMultiplier,
            bia.Stats.Defense), new FixedRandom()).Amount;
        Assert.Equal(PvpRules.Scale(full), dealt);

        bia.Hp.Add(-(bia.Hp.Current - 2));
        Hit(world, ana, bia);
        Assert.Equal(1, bia.Hp.Current);
        Assert.False(bia.IsDead);
        Assert.Equal(0, ana.DuelId);
        Assert.Contains("Ana venceu o duelo contra Bia!", outbox.Texts(bia.Id));
    }

    [Fact]
    public void Fugir_do_duelo_e_perder_e_quem_esta_ocupado_nao_duela()
    {
        var (world, outbox) = NewWorld();
        var ana = Add(world, "Ana");
        var bia = Add(world, "Bia", 1);
        var caio = Add(world, "Caio", 2);
        StartDuel(world, ana, bia);
        world.Handle(caio.Id, new DuelCommand { Action = DuelActionKind.Request, TargetName = "Ana" }, _now);
        Assert.Contains("Ana já está num duelo", outbox.Texts(caio.Id));

        Tick(world, DuelRules.CountdownSeconds + 0.1);
        bia.Movement.Teleport(DuelRules.BoundaryRadius + 5, 0, _now);
        Tick(world);
        Assert.Equal(0, ana.DuelId);
        Assert.Contains("Bia fugiu do duelo: Ana venceu!", outbox.Texts(ana.Id));
    }

    [Fact]
    public void Arena_forma_times_equilibrados_e_leva_para_as_bases()
    {
        var (world, outbox) = NewWorld();
        var players = new[] { "Ana", "Bia", "Caio", "Davi" }.Select((n, i) => Add(world, n, i)).ToList();
        players[0].Progress = new Game.Shared.Progression.CharacterProgress(20, 0);
        foreach (var p in players.Take(3)) world.Handle(p.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        Tick(world);
        Assert.Empty(world.Arenas);
        Assert.Equal(3, outbox.To<PvpState>(players[0].Id).Last().QueueSize);

        world.Handle(players[3].Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        Tick(world);
        var match = Assert.Single(world.Arenas);
        Assert.Empty(world.ArenaQueue);
        Assert.Equal(2, match.Teams.Values.Count(t => t == ArenaTeam.Blue));
        Assert.Equal(ArenaTeam.Blue, match.Teams[players[0].Id]); // o de nível mais alto abre o azul
        foreach (var p in players)
        {
            Assert.Equal(match.Id, p.ArenaId);
            Assert.InRange(p.Z, match.OriginZ - ArenaRules.HalfLength, match.OriginZ + ArenaRules.HalfLength);
            var mine = outbox.Sent.Where(s => s.PlayerId == p.Id).Select(s => s.Message).ToList();
            Assert.True(mine.FindIndex(m => m is PvpState { Mode: PvpMode.Arena }) < mine.FindLastIndex(m => m is PositionCorrection));
        }
    }

    [Fact]
    public void Captura_pontua_abate_soma_e_a_vitoria_paga_e_volta_para_a_vila()
    {
        var (world, outbox) = NewWorld(teamSize: 1);
        var ana = Add(world, "Ana");
        var bia = Add(world, "Bia", 1);
        world.Handle(ana.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        world.Handle(bia.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        Tick(world);
        var match = world.Arenas.Single();
        var blue = match.Teams[ana.Id] == ArenaTeam.Blue ? ana : bia;
        var red = blue == ana ? bia : ana;

        // Antes de começar: sem dano e sem captura.
        var b = ArenaRules.Points[1];
        blue.Movement.Teleport(match.OriginX + b.X, match.OriginZ + b.Z, _now);
        Tick(world, 2);
        Assert.Equal(ArenaTeam.None, match.Owner[1]);

        Tick(world, ArenaRules.CountdownSeconds);
        Tick(world, ArenaRules.CaptureSeconds + 0.5);
        Assert.Equal(ArenaTeam.Blue, match.Owner[1]);
        Assert.Equal(1, match.Scores[blue.Id].Captures);
        Assert.Contains(outbox.Texts(red.Id), t => t == "Time Azul tomou o ponto B!");
        var before = match.ScoreBlue;
        Tick(world, 3);
        Assert.InRange(match.ScoreBlue - before, 2, 4);

        // Vermelho chega: disputado, o ponto para.
        red.Movement.Teleport(match.OriginX + b.X + 1, match.OriginZ + b.Z, _now);
        var progress = match.Progress[1];
        Tick(world, 1);
        Assert.True(match.Contested[1]);
        Assert.Equal(progress, match.Progress[1]);

        // Abate: +3 e renasce na base.
        red.Hp.Add(-(red.Hp.Current - 1));
        var score = match.ScoreBlue;
        Hit(world, blue, red);
        Assert.True(red.IsDead);
        Assert.Equal(1, match.Scores[red.Id].Deaths);
        Assert.True(match.ScoreBlue >= score + ArenaRules.ScorePerKill);
        Tick(world, ArenaRules.RespawnSeconds + 0.5);
        Assert.False(red.IsDead);
        Assert.True(Math.Abs(red.Z - (match.OriginZ + ArenaRules.BaseZ)) < 1);

        // Chegar à pontuação de vitória encerra e paga.
        match.ScoreBlue = ArenaRules.WinScore;
        Tick(world);
        Assert.True(match.Finished);
        Assert.Equal(ArenaTeam.Blue, match.Winner);
        Assert.Equal(ArenaRules.WinAlz, blue.Alz);
        Assert.Equal(ArenaRules.LossAlz, red.Alz);
        Assert.True(outbox.To<PvpState>(red.Id).Last().Finished);

        Tick(world, ArenaRules.ResultSeconds + 0.5);
        Assert.Empty(world.Arenas);
        Assert.Equal(0, blue.ArenaId);
        Assert.InRange(blue.Z, -10, 10);
        Assert.Equal(PvpMode.None, outbox.To<PvpState>(blue.Id).Last().Mode);
    }

    [Fact]
    public void Time_que_fica_sem_ninguem_perde_e_sair_nao_paga()
    {
        var (world, outbox) = NewWorld(teamSize: 1);
        var ana = Add(world, "Ana");
        var bia = Add(world, "Bia", 1);
        world.Handle(ana.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        world.Handle(bia.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        Tick(world);
        var match = world.Arenas.Single();
        world.Handle(ana.Id, new ArenaCommand { Action = ArenaActionKind.Leave }, _now);
        Tick(world);
        Assert.True(match.Finished);
        Assert.Equal(match.Teams[bia.Id], match.Winner);
        Assert.Equal(0, ana.Alz);
        Assert.Equal(ArenaRules.WinAlz, bia.Alz);
        Assert.Equal(0, ana.ArenaId);
    }

    [Fact]
    public void Duas_partidas_ao_mesmo_tempo_ficam_separadas()
    {
        var (world, _) = NewWorld(teamSize: 1);
        var players = Enumerable.Range(0, 4).Select(i => Add(world, $"Jogador{i}", i)).ToList();
        foreach (var p in players) world.Handle(p.Id, new ArenaCommand { Action = ArenaActionKind.Join }, _now);
        Tick(world);
        Assert.Equal(2, world.Arenas.Count);
        var origins = world.Arenas.Select(a => a.OriginZ).Distinct().Count();
        Assert.Equal(2, origins);
        // Ninguém de uma partida pode bater em alguém da outra.
        var first = world.Arenas.First();
        var outsider = players.First(p => p.ArenaId != first.Id);
        var insider = players.First(p => p.ArenaId == first.Id);
        Tick(world, ArenaRules.CountdownSeconds + 0.5);
        outsider.Movement.Teleport(insider.X + 1, insider.Z, _now);
        Hit(world, outsider, insider);
        Assert.Equal(insider.Hp.Max, insider.Hp.Current);
    }
}
