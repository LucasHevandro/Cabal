using Game.Shared.Net;
using Game.Shared.Pvp;
using Game.Shared.Social;

namespace Game.Shared.Tests.Pvp;

public class PvpTests
{
    [Fact]
    public void Dano_pvp_e_reduzido_e_o_despertar_vale_metade()
    {
        Assert.Equal(350, PvpRules.Scale(1000));
        Assert.Equal(1, PvpRules.Scale(1));
        Assert.Equal(1.15, PvpRules.AwakeningMultiplier(1.3), 6);
        Assert.Equal(1.0, PvpRules.AwakeningMultiplier(1.0), 6);
    }

    [Fact]
    public void Times_ficam_equilibrados_por_nivel()
    {
        var players = Enumerable.Range(1, 10).Select(i => (i, Level: i * 3)).ToList();
        var (blue, red) = ArenaRules.Balance(players);
        Assert.Equal(5, blue.Count);
        Assert.Equal(5, red.Count);
        var level = players.ToDictionary(p => p.i, p => p.Level);
        Assert.InRange(Math.Abs(blue.Sum(id => level[id]) - red.Sum(id => level[id])), 0, 6);
        Assert.Empty(blue.Intersect(red));
    }

    [Fact]
    public void Captura_anda_so_com_um_time_e_trocar_de_dono_leva_o_dobro()
    {
        var progress = 0.0;
        var owner = ArenaTeam.None;
        // Disputado: não anda.
        owner = ArenaRules.Advance(ref progress, owner, 2, 1, 1);
        Assert.Equal((0.0, ArenaTeam.None), (progress, owner));

        // Azul sozinho toma em 6 s.
        for (var i = 0; i < 6; i++) owner = ArenaRules.Advance(ref progress, owner, 1, 0, 1);
        Assert.Equal((-1.0, ArenaTeam.Blue), (progress, owner));

        // Vermelho precisa de 6 s para neutralizar e mais 6 para tomar.
        for (var i = 0; i < 6; i++) owner = ArenaRules.Advance(ref progress, owner, 0, 3, 1);
        Assert.Equal(ArenaTeam.Blue, owner); // chegou ao meio, ainda não passou
        owner = ArenaRules.Advance(ref progress, owner, 0, 3, 0.1);
        Assert.Equal(ArenaTeam.None, owner);
        for (var i = 0; i < 6; i++) owner = ArenaRules.Advance(ref progress, owner, 0, 3, 1);
        Assert.Equal(ArenaTeam.Red, owner);
    }

    [Fact]
    public void Comandos_de_duelo_e_arena_no_chat()
    {
        var duel = ChatParser.Parse("/duelo Bia", ChatChannel.Map);
        Assert.Equal((ChatInputKind.DuelRequest, "Bia"), (duel.Kind, duel.Target));
        Assert.Equal(ChatInputKind.ArenaQueue, ChatParser.Parse("/arena", ChatChannel.Map).Kind);
        Assert.Equal(ChatInputKind.Invalid, ChatParser.Parse("/duelo", ChatChannel.Map).Kind);
    }

    [Fact]
    public void Mensagens_de_pvp_ida_e_volta()
    {
        static T RoundTrip<T>(T message) where T : INetMessage
        {
            var frame = NetCodec.Encode(message);
            var buffer = new FrameBuffer();
            buffer.Append(frame, 0, frame.Length);
            Assert.True(buffer.TryRead(out var type, out var payload));
            return Assert.IsType<T>(NetCodec.Decode(type, payload));
        }

        var duel = RoundTrip(new DuelCommand { Action = DuelActionKind.Accept, TargetId = 4, TargetName = "Bia" });
        Assert.Equal((DuelActionKind.Accept, 4, "Bia"), (duel.Action, duel.TargetId, duel.TargetName));
        Assert.Equal(ArenaActionKind.LeaveQueue, RoundTrip(new ArenaCommand { Action = ArenaActionKind.LeaveQueue }).Action);
        Assert.Equal("Caio", RoundTrip(new DuelInvite { FromId = 1, FromName = "Caio", Seconds = 20 }).FromName);

        var state = RoundTrip(new PvpState
        {
            Mode = PvpMode.Arena, InQueue = false, QueueSize = 3, QueueNeeded = 10, StartsAt = 5, EndsAt = 605, OriginZ = -1000,
            Team = (sbyte)ArenaTeam.Red, ScoreBlue = 120, ScoreRed = 99, Finished = true, Winner = (sbyte)ArenaTeam.Blue,
            Points = { new ArenaPointInfo { Owner = -1, Progress = 0.5f, Contested = true } },
            Players = { new ArenaPlayerInfo { Id = 7, Name = "Ana", Team = 0, Level = 20, Kills = 3, Deaths = 1, Captures = 2 } },
        });
        Assert.Equal((PvpMode.Arena, (sbyte)1, (ushort)120, (ushort)99, true, (sbyte)0),
            (state.Mode, state.Team, state.ScoreBlue, state.ScoreRed, state.Finished, state.Winner));
        Assert.Equal((-1, 0.5f, true), ((int)state.Points[0].Owner, state.Points[0].Progress, state.Points[0].Contested));
        Assert.Equal(("Ana", (ushort)3, (ushort)2), (state.Players[0].Name, state.Players[0].Kills, state.Players[0].Captures));
    }
}
