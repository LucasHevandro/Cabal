using Game.Shared.Net;

namespace Game.Shared.Tests.Net;

public class ProtocolTests
{
    /// <summary>Codifica, passa pelo FrameBuffer como se viesse do TCP e decodifica.</summary>
    private static T RoundTrip<T>(T message) where T : INetMessage
    {
        var frame = NetCodec.Encode(message);
        var buffer = new FrameBuffer();
        buffer.Append(frame, 0, frame.Length);

        Assert.True(buffer.TryRead(out var type, out var payload));
        Assert.Equal(message.Type, type);
        Assert.Equal(0, buffer.Pending);
        return Assert.IsType<T>(NetCodec.Decode(type, payload));
    }

    [Fact]
    public void Hello_com_acentos_volta_igual()
    {
        var back = RoundTrip(new Hello { Name = "Guerreiro Ação ⚔" });

        Assert.Equal(Protocol.Version, back.ProtocolVersion);
        Assert.Equal("Guerreiro Ação ⚔", back.Name);
    }

    [Fact]
    public void Mensagens_do_cliente_voltam_iguais()
    {
        var move = RoundTrip(new MoveInput { Sequence = 42, X = 1.5f, Z = -3.25f, Yaw = 270f, Dodging = true });
        Assert.Equal((42u, 1.5f, -3.25f, 270f, true), (move.Sequence, move.X, move.Z, move.Yaw, move.Dodging));

        var area = RoundTrip(new AreaWarning { SourceId = 3, X = 1, Z = 2, Radius = 4, Seconds = 1.6f });
        Assert.Equal((3, 4f, 1.6f), (area.SourceId, area.Radius, area.Seconds));

        var skill = RoundTrip(new UseSkill { Slot = 2, TargetId = 77, ClientTime = 123.456 });
        Assert.Equal((2, 77, 123.456), (skill.Slot, skill.TargetId, skill.ClientTime));

        Assert.Equal(9, RoundTrip(new StartCombo { TargetId = 9, ClientTime = 1 }).TargetId);
        Assert.Equal(9, RoundTrip(new Finisher { TargetId = 9 }).TargetId);
        Assert.Equal(5, RoundTrip(new PickupLoot { LootId = 5 }).LootId);
        Assert.Equal(3.5, RoundTrip(new Ping { ClientTime = 3.5 }).ClientTime);
    }

    [Fact]
    public void Snapshot_com_varias_entidades_volta_igual()
    {
        var snapshot = new Snapshot { Tick = 1000, ServerTime = 50.25 };
        for (var i = 0; i < 100; i++)
            snapshot.Entities.Add(new EntityState { Id = i, X = i * 0.5f, Z = -i, Yaw = i, Hp = i * 10, Flags = EntityState.FlagWindUp });

        var back = RoundTrip(snapshot);

        Assert.Equal(1000u, back.Tick);
        Assert.Equal(100, back.Entities.Count);
        Assert.Equal(99 * 10, back.Entities[99].Hp);
        Assert.Equal(-99f, back.Entities[99].Z);
        Assert.Equal(EntityState.FlagWindUp, back.Entities[0].Flags);
    }

    [Fact]
    public void Mensagens_do_servidor_voltam_iguais()
    {
        var spawned = RoundTrip(new EntitySpawned
        {
            Id = 7, Kind = EntityKind.Monster, TypeId = "lobo_cinzento", Name = "Lobo Cinzento", Level = 6, MaxHp = 900, X = 3, Z = 4,
        });
        Assert.Equal(("lobo_cinzento", 900, EntityKind.Monster), (spawned.TypeId, spawned.MaxHp, spawned.Kind));

        var combat = RoundTrip(new CombatEvent
        {
            AttackerId = 1, TargetId = 7, Source = CombatSource.Finisher, Damage = 580, Critical = true, Grade = 2, RouteName = "Tempestade",
        });
        Assert.Equal((580, true, "Tempestade", CombatSource.Finisher), (combat.Damage, combat.Critical, combat.RouteName, combat.Source));

        var combo = RoundTrip(new ComboState
        {
            Active = true, CycleStartServerTime = 10.5, BarDurationSeconds = 1.1f, WindowCenter = 0.75f,
            WindowWidth = 0.2f, ExcellentZoneRatio = 0.35f, HitCount = 4, ChainBonus = 0.24f, CanFinish = true,
        });
        Assert.Equal((true, 10.5, (ushort)4, true), (combo.Active, combo.CycleStartServerTime, combo.HitCount, combo.CanFinish));

        var stats = RoundTrip(new PlayerStats { Level = 12, Hp = 100, MaxHp = 3000, Alz = 9_000_000_000 });
        Assert.Equal((12, 9_000_000_000L), (stats.Level, stats.Alz));

        Assert.Equal("Pele de lobo", RoundTrip(new LootSpawned { LootId = 3, Name = "Pele de lobo", Quantity = 2 }).Name);
        Assert.Equal(4, RoundTrip(new LootRemoved { LootId = 3, PickedById = 4 }).PickedById);
        Assert.Equal(12f, RoundTrip(new PositionCorrection { X = 12, Z = 1 }).X);
        Assert.True(RoundTrip(new Notice { Text = "O chefe ficou furioso!", Announcement = true }).Announcement);
        Assert.Equal(2.0, RoundTrip(new Pong { ClientTime = 1, ServerTime = 2 }).ServerTime);
        Assert.Equal(55, RoundTrip(new Welcome { PlayerId = 55, TickRate = 20 }).PlayerId);
        Assert.Equal("Versão antiga", RoundTrip(new Reject { Reason = "Versão antiga" }).Reason);
        Assert.Equal(7, RoundTrip(new EntityDespawned { Id = 7 }).Id);
    }

    [Fact]
    public void Mensagem_chegando_byte_a_byte_so_sai_quando_completa()
    {
        var frame = NetCodec.Encode(new Hello { Name = "Lento" });
        var buffer = new FrameBuffer();

        for (var i = 0; i < frame.Length - 1; i++)
        {
            buffer.Append(frame, i, 1);
            Assert.False(buffer.TryRead(out _, out _));
        }
        buffer.Append(frame, frame.Length - 1, 1);

        Assert.True(buffer.TryRead(out var type, out var payload));
        Assert.Equal("Lento", ((Hello)NetCodec.Decode(type, payload)).Name);
    }

    [Fact]
    public void Varias_mensagens_num_pedaco_saem_na_ordem()
    {
        var a = NetCodec.Encode(new Ping { ClientTime = 1 });
        var b = NetCodec.Encode(new Finisher { TargetId = 2 });
        var c = NetCodec.Encode(new Ping { ClientTime = 3 });
        var all = a.Concat(b).Concat(c).ToArray();
        var buffer = new FrameBuffer();

        // Os três chegam juntos, mais metade de um quarto.
        var d = NetCodec.Encode(new PickupLoot { LootId = 4 });
        buffer.Append(all, 0, all.Length);
        buffer.Append(d, 0, 3);

        var received = new List<MessageType>();
        while (buffer.TryRead(out var type, out _)) received.Add(type);

        Assert.Equal(new[] { MessageType.Ping, MessageType.Finisher, MessageType.Ping }, received);
        Assert.Equal(3, buffer.Pending);
    }

    [Fact]
    public void Tamanho_invalido_eh_erro_de_protocolo()
    {
        var buffer = new FrameBuffer();
        var huge = BitConverter.GetBytes(Framing.MaxBodySize + 1);
        buffer.Append(huge, 0, huge.Length);

        Assert.Throws<ProtocolException>(() => buffer.TryRead(out _, out _));
    }

    [Fact]
    public void Tipo_desconhecido_ou_conteudo_errado_eh_erro_de_protocolo()
    {
        Assert.Throws<ProtocolException>(() => NetCodec.Decode((MessageType)999, Array.Empty<byte>()));
        Assert.Throws<ProtocolException>(() => NetCodec.Decode(MessageType.Ping, new byte[3]));      // curto
        Assert.Throws<ProtocolException>(() => NetCodec.Decode(MessageType.Finisher, new byte[6]));  // sobra
    }
}
