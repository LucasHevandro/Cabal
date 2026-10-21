using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Aparência dos jogadores (arma e brilho da melhoria) para quem os vê.</summary>
public class LookTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));
    }

    [Fact]
    public void Quem_aparece_leva_a_arma_e_a_melhoria_avisa_quem_ja_via()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>());
        var uid = 1000L;
        var ana = world.AddPlayer("Ana", 0, character: AuthService.InitialCharacter(newUid: () => uid++) with { Id = 1, AccountId = 1, Name = "Ana" });
        var bia = world.AddPlayer("Bia", 0, character: AuthService.InitialCharacter(newUid: () => uid++) with { Id = 2, AccountId = 2, Name = "Bia" });
        world.Tick(0.05);

        var spawned = outbox.Sent.Where(s => s.PlayerId == bia.Id).Select(s => s.Message).OfType<EntitySpawned>().Single(m => m.Id == ana.Id);
        Assert.Equal(("espada_ferro", (byte)0), (spawned.WeaponId, spawned.WeaponUpgrade));
        Assert.DoesNotContain(outbox.Sent, s => s.Message is EntityLook);

        ana.Equipment[EquipSlot.Weapon]!.Upgrade = 7;
        outbox.Sent.Clear();
        world.Tick(0.1);
        world.Tick(0.15);

        var looks = outbox.Sent.Where(s => s.PlayerId == bia.Id).Select(s => s.Message).OfType<EntityLook>().ToList();
        var look = Assert.Single(looks); // uma vez só, não a cada tick
        Assert.Equal((ana.Id, "espada_ferro", (byte)7), (look.Id, look.WeaponId, look.WeaponUpgrade));
        // A própria Ana também recebe (o modelo dela no cliente usa o mesmo caminho).
        Assert.Contains(outbox.Sent, s => s.PlayerId == ana.Id && s.Message is EntityLook { Id: var id } && id == ana.Id);
    }

    [Fact]
    public void Quem_aparece_leva_o_grau_das_pecas_vestidas_e_as_cores_da_criacao()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>());
        var uid = 1000L;
        var initial = AuthService.InitialCharacter(newUid: () => uid++);
        var armored = initial with
        {
            Id = 1, AccountId = 1, Name = "Ana", Hair = 4, Cloth = 2, Head = 5, Skin = 3,
            Items = initial.Items
                .Append(new ItemRecord(uid++, ItemContainerKind.Equipment, (int)EquipSlot.Armor, "armadura_aco_negro", 1))
                .Append(new ItemRecord(uid++, ItemContainerKind.Equipment, (int)EquipSlot.Gloves, "luvas_astralita", 1))
                .Append(new ItemRecord(uid++, ItemContainerKind.Equipment, (int)EquipSlot.Boots, "botas_ferro", 1)).ToList(),
        };
        var ana = world.AddPlayer("Ana", 0, character: armored);
        var bia = world.AddPlayer("Bia", 0, character: AuthService.InitialCharacter(newUid: () => uid++) with { Id = 2, AccountId = 2, Name = "Bia" });
        world.Tick(0.05);

        var spawned = outbox.Sent.Where(s => s.PlayerId == bia.Id).Select(s => s.Message).OfType<EntitySpawned>().Single(m => m.Id == ana.Id);
        Assert.Equal(((byte)2, (byte)3, (byte)1, (byte)4, (byte)2),
            (spawned.ArmorGrade, spawned.GlovesGrade, spawned.BootsGrade, spawned.Hair, spawned.Cloth));
        Assert.Equal(((byte)5, (byte)3, false), (spawned.Head, spawned.Skin, spawned.HideHelmet));
    }

    [Fact]
    public void Esconder_o_elmo_avisa_quem_ve_e_fica_salvo()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>());
        var uid = 1000L;
        var ana = world.AddPlayer("Ana", 0, character: AuthService.InitialCharacter(newUid: () => uid++) with { Id = 1, AccountId = 1, Name = "Ana" });
        var bia = world.AddPlayer("Bia", 0, character: AuthService.InitialCharacter(newUid: () => uid++) with { Id = 2, AccountId = 2, Name = "Bia" });
        world.Tick(0.05);
        outbox.Sent.Clear();

        world.Handle(ana.Id, new AppearanceCommand { HideHelmet = true }, 0.06);
        world.Tick(0.1);
        Assert.True(ana.HideHelmet);
        var look = Assert.Single(outbox.Sent.Where(s => s.PlayerId == bia.Id).Select(s => s.Message).OfType<EntityLook>());
        Assert.True(look.HideHelmet);
        Assert.Contains(outbox.Sent, s => s.PlayerId == ana.Id && s.Message is EntityLook { HideHelmet: true });

        var (snapshot, _) = world.CaptureForSave(ana);
        Assert.True(snapshot.HideHelmet);
        var back = world.AddPlayer("Ana", 1, character: AuthService.InitialCharacter(newUid: () => uid++) with
        {
            Id = 3, AccountId = 3, Name = "Ana2", HideHelmet = true, Head = 99, Skin = -1,
        });
        Assert.True(back.HideHelmet);
        Assert.Equal((0, 0), (back.Head, back.Skin)); // fora da lista vira o padrão
    }
}
