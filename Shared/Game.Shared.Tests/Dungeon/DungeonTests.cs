using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Net;

namespace Game.Shared.Tests.Dungeon;

public class DungeonTests
{
    [Theory]
    [InlineData(400, 15, DungeonMedal.S)]
    [InlineData(400, 5, DungeonMedal.A)]   // rápido, mas sem combo: A
    [InlineData(600, 30, DungeonMedal.A)]
    [InlineData(800, 30, DungeonMedal.B)]
    [InlineData(900, 0, DungeonMedal.B)]
    [InlineData(901, 99, DungeonMedal.None)]
    public void Medalha_por_tempo_e_combo(double seconds, int combo, DungeonMedal expected) =>
        Assert.Equal(expected, DungeonRules.Medal(seconds, combo));

    [Fact]
    public void Cripta_tem_tres_salas_e_o_chefe_e_tudo_valido()
    {
        var crypt = DungeonCatalog.Crypt();
        crypt.Validate();
        Assert.Equal(4, crypt.Rooms.Count);
        Assert.Equal(3, crypt.Rooms.Count(r => !r.IsBossRoom));
        Assert.True(crypt.Rooms[^1].IsBossRoom);
        Assert.Equal(crypt.BossId, crypt.Rooms[^1].Spawns.Single().Data.Id);
        Assert.NotNull(ItemCatalog.Find(crypt.KeyItemId));
        // Salas em sequência, sem sobrepor (o portão de uma fica antes do começo da próxima).
        for (var i = 1; i < crypt.Rooms.Count; i++) Assert.True(crypt.Rooms[i - 1].GateZ < crypt.Rooms[i].StartZ);
        foreach (var entry in crypt.Chest.Entries) Assert.NotNull(ItemCatalog.Find(entry.ItemId));
        Assert.Equal(new[] { 3, 2, 1, 0 }, new[] { DungeonMedal.S, DungeonMedal.A, DungeonMedal.B, DungeonMedal.None }.Select(DungeonRules.ChestRolls));
        Assert.Equal("7:05", DungeonRules.FormatTime(425.9));
    }

    [Fact]
    public void Grupo_de_quatro_termina_em_menos_de_15_minutos_e_um_grupo_bom_tira_S()
    {
        var crypt = DungeonCatalog.Crypt();
        (Game.Shared.Accounts.CharacterClass, int)[] Group(int level) =>
        [
            (Game.Shared.Accounts.CharacterClass.Blade, level), (Game.Shared.Accounts.CharacterClass.Blade, level),
            (Game.Shared.Accounts.CharacterClass.Arcanist, level), (Game.Shared.Accounts.CharacterClass.Arcanist, level),
        ];

        var typical = DungeonEstimate.TotalSeconds(DungeonEstimate.Run(crypt, Group(22)));
        Assert.InRange(typical, 8 * 60, 13 * 60);
        Assert.True(DungeonEstimate.TotalSeconds(DungeonEstimate.Run(crypt, Group(crypt.MinLevel))) < DungeonRules.TimeLimitSeconds);

        // Quem joga bem (mais combo e mais rápido nas skills), com equipamento +7, consegue a S.
        var fast = new Game.Shared.Progression.LevelingAssumptions { ComboBonus = 0.35, SecondsPerAction = 0.8, UpgradeByGrade = new[] { 2, 4, 7 } };
        Assert.True(DungeonEstimate.TotalSeconds(DungeonEstimate.Run(crypt, Group(28), fast)) <= DungeonRules.MedalSSeconds);
    }

    [Fact]
    public void Santuario_pede_grupo_mais_forte_e_cabe_no_limite()
    {
        var sanctuary = DungeonCatalog.Sanctuary();
        (Game.Shared.Accounts.CharacterClass, int)[] Group(int level) =>
        [
            (Game.Shared.Accounts.CharacterClass.Blade, level), (Game.Shared.Accounts.CharacterClass.Blade, level),
            (Game.Shared.Accounts.CharacterClass.Arcanist, level), (Game.Shared.Accounts.CharacterClass.Arcanist, level),
        ];
        var typical = DungeonEstimate.TotalSeconds(DungeonEstimate.Run(sanctuary, Group(28)));
        var minimum = DungeonEstimate.TotalSeconds(DungeonEstimate.Run(sanctuary, Group(sanctuary.MinLevel)));
        System.Console.WriteLine($"santuario: 28 = {DungeonRules.FormatTime(typical)}, 26 = {DungeonRules.FormatTime(minimum)}, 30 = {DungeonRules.FormatTime(DungeonEstimate.TotalSeconds(DungeonEstimate.Run(sanctuary, Group(30))))}, 2x28 = {DungeonRules.FormatTime(DungeonEstimate.TotalSeconds(DungeonEstimate.Run(sanctuary, new[] { Group(28)[0], Group(28)[2] })))}");
        Assert.InRange(typical, 9 * 60, 13 * 60);
        Assert.True(minimum < DungeonRules.TimeLimitSeconds);
        Assert.Equal(new[] { DungeonCatalog.CryptId, DungeonCatalog.SanctuaryId }, DungeonCatalog.All().Select(d => d.Id));
        Assert.All(DungeonCatalog.All(), d => Assert.NotNull(Game.Shared.Items.ItemCatalog.Find(d.KeyItemId)));
    }

    [Fact]
    public void Mensagens_da_dungeon_ida_e_volta()
    {
        static T RoundTrip<T>(T message) where T : INetMessage
        {
            var frame = NetCodec.Encode(message);
            var buffer = new FrameBuffer();
            buffer.Append(frame, 0, frame.Length);
            Assert.True(buffer.TryRead(out var type, out var payload));
            return Assert.IsType<T>(NetCodec.Decode(type, payload));
        }

        var command = RoundTrip(new DungeonCommand { Action = DungeonActionKind.Leave, DungeonId = "x" });
        Assert.Equal((DungeonActionKind.Leave, "x"), (command.Action, command.DungeonId));

        var state = RoundTrip(new DungeonState
        {
            Active = true, DungeonId = "cripta", OriginX = 0, OriginZ = 1400, Room = 2, Rooms = 4, MonstersLeft = 5, StartedAt = 12.5,
            LimitSeconds = 900, BossShield = true, Completed = true, ElapsedSeconds = 420, Medal = (byte)DungeonMedal.S, MaxCombo = 14,
        });
        Assert.Equal((1400f, (byte)2, (ushort)5, 12.5, true, (byte)3, (ushort)14),
            (state.OriginZ, state.Room, state.MonstersLeft, state.StartedAt, state.BossShield, state.Medal, state.MaxCombo));

        var info = RoundTrip(new DungeonInfo { DungeonId = "cripta", Ranking = { new DungeonRankEntry { Seconds = 400, Medal = 3, MaxCombo = 12, Members = "Ana,Bia" } } });
        Assert.Equal(("Ana,Bia", 400f), (info.Ranking[0].Members, info.Ranking[0].Seconds));
    }
}
