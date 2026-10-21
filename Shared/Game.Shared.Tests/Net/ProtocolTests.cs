using Game.Shared.Net;
using Game.Shared.Text;

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
        var look = RoundTrip(new EntityLook { Id = 4, WeaponId = "espada_astralita", WeaponUpgrade = 10, ArmorGrade = 3, GlovesGrade = 2, BootsGrade = 1, HideHelmet = true });
        Assert.Equal((4, "espada_astralita", (byte)10, (byte)3, (byte)2, (byte)1, true),
            (look.Id, look.WeaponId, look.WeaponUpgrade, look.ArmorGrade, look.GlovesGrade, look.BootsGrade, look.HideHelmet));
        var spawned = RoundTrip(new EntitySpawned
        {
            Id = 3, Kind = EntityKind.Player, TypeId = "blade", WeaponId = "espada_ferro", WeaponUpgrade = 7, ArmorGrade = 2, GlovesGrade = 3,
            BootsGrade = 1, Hair = 4, Cloth = 5, Head = 3, Skin = 2, HideHelmet = true,
        });
        Assert.Equal(("espada_ferro", (byte)7, (byte)2, (byte)3, (byte)1, (byte)4, (byte)5),
            (spawned.WeaponId, spawned.WeaponUpgrade, spawned.ArmorGrade, spawned.GlovesGrade, spawned.BootsGrade, spawned.Hair, spawned.Cloth));
        Assert.Equal(((byte)3, (byte)2, true), (spawned.Head, spawned.Skin, spawned.HideHelmet));
        Assert.True(RoundTrip(new AppearanceCommand { HideHelmet = true }).HideHelmet);
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
            AttackerId = 1, TargetId = 7, Source = CombatSource.Finisher, Damage = 580, Critical = true, Grade = 2, RouteId = "tempestade",
        });
        Assert.Equal((580, true, "tempestade", CombatSource.Finisher), (combat.Damage, combat.Critical, combat.RouteId, combat.Source));

        var combo = RoundTrip(new ComboState
        {
            Active = true, CycleStartServerTime = 10.5, BarDurationSeconds = 1.1f, WindowCenter = 0.75f,
            WindowWidth = 0.2f, ExcellentZoneRatio = 0.35f, HitCount = 4, ChainBonus = 0.24f, CanFinish = true,
        });
        Assert.Equal((true, 10.5, (ushort)4, true), (combo.Active, combo.CycleStartServerTime, combo.HitCount, combo.CanFinish));

        var stats = RoundTrip(new PlayerStats { Level = 12, Hp = 100, MaxHp = 3000, Alz = 9_000_000_000 });
        Assert.Equal((12, 9_000_000_000L), (stats.Level, stats.Alz));

        Assert.Equal("Pele de lobo", RoundTrip(new LootSpawned { LootId = 3, ItemId = "pele_lobo", Quantity = 2 }).Name);
        Assert.Equal(4, RoundTrip(new LootRemoved { LootId = 3, PickedById = 4 }).PickedById);
        Assert.Equal(12f, RoundTrip(new PositionCorrection { X = 12, Z = 1 }).X);
        var notice = RoundTrip(new Notice { Text = new LocalText("upgrade.no_cores", 3, Loc.Ref("item.nucleo_baixo")), Announcement = true });
        Assert.True(notice.Announcement);
        Assert.Equal("Faltam núcleos: precisa de 3x Núcleo de melhoria (baixo).", notice.Text.ToString());
        Assert.Equal(2.0, RoundTrip(new Pong { ClientTime = 1, ServerTime = 2 }).ServerTime);
        Assert.Equal(55, RoundTrip(new Welcome { PlayerId = 55, TickRate = 20 }).PlayerId);
        Assert.Equal("trade.no_alz", RoundTrip(new Reject { Reason = new LocalText("trade.no_alz") }).Reason.Key);
        Assert.Equal(7, RoundTrip(new EntityDespawned { Id = 7 }).Id);
        var bag = RoundTrip(new Inventory { Items = { new InventoryStack { ItemId = "pele_lobo", Rarity = 1, Quantity = 3 } } });
        Assert.Equal(("pele_lobo", 3), (bag.Items[0].ItemId, bag.Items[0].Quantity));
    }

    [Fact]
    public void Mensagens_de_itens_missoes_e_ficha_vao_e_voltam()
    {
        var bag = RoundTrip(new Inventory
        {
            Items =
            {
                new InventoryStack
                {
                    ItemId = "espada_ferro", Rarity = 0, Quantity = 1, Container = ItemContainerKind.Equipment,
                    Slot = 0, Uid = 123456789012, Upgrade = 7, PityFails = 2, PityLevel = 7, RegainGuaranteed = true, OptionId = "pedra_furia",
                },
            },
        }).Items[0];
        Assert.Equal((ItemContainerKind.Equipment, 123456789012L, (byte)7, true, "pedra_furia"),
            (bag.Container, bag.Uid, bag.Upgrade, bag.RegainGuaranteed, bag.OptionId));

        var move = RoundTrip(new ItemCommand
        {
            Action = ItemActionKind.Move, From = ItemContainerKind.Bag, FromSlot = 3, To = ItemContainerKind.Warehouse, ToSlot = 39, Quantity = 2,
        });
        Assert.Equal((ItemActionKind.Move, ItemContainerKind.Warehouse, (ushort)39, 2), (move.Action, move.To, move.ToSlot, move.Quantity));
        Assert.Equal((ushort)4, RoundTrip(new UpgradeItem { Container = ItemContainerKind.Bag, Slot = 4 }).Slot);
        Assert.Equal((ushort)9, RoundTrip(new ApplyOption { Slot = 1, StoneSlot = 9 }).StoneSlot);
        Assert.Equal((ushort)3, RoundTrip(new AllocateAttributes { Strength = 1, Dexterity = 2, Intelligence = 3 }).Intelligence);
        Assert.Equal("ferreira_ilsa", RoundTrip(new TalkToNpc { NpcId = "ferreira_ilsa" }).NpcId);
        Assert.Equal(QuestActionKind.TurnIn, RoundTrip(new QuestCommand { Action = QuestActionKind.TurnIn, QuestId = "x" }).Action);
        RoundTrip(new ActivateAwakening());

        var sheet = RoundTrip(new CharacterSheet
        {
            Class = 1, Strength = 3, FreePoints = 5, MagicPower = 80, CritPercent = 6.5f, ComboUnlocked = true,
            SkillUnlocked = { true, false }, AwakeningGauge = 40, AwakeningUntil = 12.5,
        });
        Assert.Equal((80, 6.5f, true, 2, (byte)40, 12.5),
            (sheet.MagicPower, sheet.CritPercent, sheet.ComboUnlocked, sheet.SkillUnlocked.Count, sheet.AwakeningGauge, sheet.AwakeningUntil));

        var quests = RoundTrip(new QuestState
        {
            Completed = { "primeiros_passos" }, Active = { new QuestEntry { QuestId = "pele_e_presa", Counts = new List<int> { 4, 6 } } },
        });
        Assert.Equal(new[] { 4, 6 }, quests.Active[0].Counts);
        var dialog = RoundTrip(new NpcDialog { NpcId = "anciao_doran", Offered = { "a" }, ReadyToTurnIn = { "b", "c" } });
        Assert.Equal(2, dialog.ReadyToTurnIn.Count);
        var upgrade = RoundTrip(new UpgradeResult { Outcome = 3, Uid = 9, NewLevel = 6, Message = new LocalText("upgrade.max", 10) });
        Assert.Equal(((byte)3, (byte)6, "Já está no máximo (+10)."), (upgrade.Outcome, upgrade.NewLevel, upgrade.Message.ToString()));
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
    public void Barra_de_atalhos_com_20_casas_e_aprender_skill()
    {
        var slots = Enumerable.Range(0, Game.Shared.Progression.SkillTraining.HotbarSize).Select(i => (byte)(i % 3 == 0 ? 255 : i)).ToList();
        var bar = RoundTrip(new SetHotbar { Slots = slots });
        Assert.Equal(slots, bar.Slots);
        Assert.Equal("execucao", RoundTrip(new LearnSkill { SkillId = "execucao" }).SkillId);
        var sheet = RoundTrip(new CharacterSheet { Hotbar = slots });
        Assert.Equal(slots, sheet.Hotbar);
    }

    [Fact]
    public void Tipo_desconhecido_ou_conteudo_errado_eh_erro_de_protocolo()
    {
        Assert.Throws<ProtocolException>(() => NetCodec.Decode((MessageType)999, Array.Empty<byte>()));
        Assert.Throws<ProtocolException>(() => NetCodec.Decode(MessageType.Ping, new byte[3]));      // curto
        Assert.Throws<ProtocolException>(() => NetCodec.Decode(MessageType.Finisher, new byte[6]));  // sobra
    }
}
