using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Social;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Loja da mercadora e troca direta com dupla confirmação, decididas pelo servidor.</summary>
public class EconomyWorldTests
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

    private long _uid = 1000;
    private long NewUid() => ++_uid;

    private CharacterRecord Record(int id, string name) =>
        AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with { Id = id, AccountId = id, Name = name };

    private (GameWorld World, FakeOutbox Outbox, ServerPlayer A, ServerPlayer B) Setup()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>(), uids: new ItemUidAllocator(5000));
        var a = world.AddPlayer("", 0, null, Record(1, "Ana"));
        var b = world.AddPlayer("", 0, null, Record(2, "Bia"));
        var merchant = NpcCatalog.Find(NpcCatalog.Merchant)!;
        a.Movement.Teleport(merchant.X, merchant.Z - 1, 0);
        b.Movement.Teleport(merchant.X + 1, merchant.Z - 1, 0);
        return (world, outbox, a, b);
    }

    private static void Trade(GameWorld world, ServerPlayer player, TradeActionKind action, int targetId = 0, string name = "",
        long alz = 0, params TradeItem[] items) =>
        world.Handle(player.Id, new TradeCommand { Action = action, TargetId = targetId, TargetName = name, Alz = alz, Items = items.ToList() }, 1);

    private static TradeItem Offer(ServerPlayer player, string itemId, int quantity)
    {
        var (slot, item) = player.Bag.Items().First(i => i.Item.ItemId == itemId);
        return new TradeItem(slot, item.Uid, quantity);
    }

    [Fact]
    public void Comprar_e_vender_na_mercadora_mexe_no_alz_e_registra()
    {
        var (world, outbox, ana, _) = Setup();
        ana.Alz = 1000;
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Buy, ItemId = "pocao_hp_p", Quantity = 10 }, 1);
        Assert.Equal(600, ana.Alz);
        Assert.Equal(15, ana.Bag.Count("pocao_hp_p")); // 5 iniciais + 10

        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Buy, ItemId = ItemCatalog.CoreLow, Quantity = 1 }, 1);
        Assert.Contains(outbox.Texts(ana.Id), t => t.StartsWith("Faltam Alz"));
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Buy, ItemId = "espada_astralita", Quantity = 1 }, 1);
        Assert.Equal(0, ana.Bag.Count("espada_astralita")); // não está à venda

        ana.Bag.TryAdd("pele_lobo", 7, NewUid);
        var slot = ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot;
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Sell, Slot = (ushort)slot, Quantity = 3 }, 1);
        Assert.Equal(4, ana.Bag.Count("pele_lobo"));
        Assert.Equal(600 + 3 * 12, ana.Alz);
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Sell, Slot = (ushort)slot, Quantity = 0 }, 1); // 0 = tudo
        Assert.Equal(0, ana.Bag.Count("pele_lobo"));

        var (_, logs) = world.CaptureForSave(ana);
        Assert.Contains(logs, l => l.Kind == "compra_loja" && l.AlzDelta == -400);
        Assert.Equal(2, logs.Count(l => l.Kind == "venda_loja"));

        // Longe da mercadora, nada feito.
        ana.Movement.Teleport(50, 50, 2);
        world.Handle(ana.Id, new ShopCommand { Action = ShopActionKind.Buy, ItemId = "pocao_hp_p", Quantity = 1 }, 2);
        Assert.Contains("Vá até a mercadora da vila.", outbox.Texts(ana.Id));
    }

    [Fact]
    public void Troca_completa_com_dupla_confirmacao_e_registros()
    {
        var (world, outbox, ana, bia) = Setup();
        ana.Alz = 500;
        Trade(world, ana, TradeActionKind.Request, name: "bia");
        Assert.Single(outbox.To<TradeInvite>(bia.Id));
        Trade(world, bia, TradeActionKind.Accept, ana.Id);
        Assert.NotEqual(0, ana.TradeId);

        var sword = ana.Equipment[EquipSlot.Weapon]!;
        world.Handle(ana.Id, new ItemCommand { Action = ItemActionKind.Move, From = ItemContainerKind.Equipment, FromSlot = (ushort)EquipSlot.Weapon, To = ItemContainerKind.Bag, ToSlot = 30 }, 1);
        Assert.Contains("Termine a troca antes de mexer na bolsa", outbox.Texts(ana.Id));

        Trade(world, ana, TradeActionKind.SetOffer, alz: 200, items: Offer(ana, "pocao_hp_p", 2));
        Trade(world, bia, TradeActionKind.SetOffer, items: Offer(bia, "pocao_hp_p", 5));
        Trade(world, ana, TradeActionKind.Confirm);
        Assert.Contains("Os dois precisam apertar Pronto antes de confirmar", outbox.Texts(ana.Id));

        Trade(world, ana, TradeActionKind.Lock);
        Trade(world, bia, TradeActionKind.Lock);
        world.Tick(1.05);
        var state = outbox.To<TradeState>(bia.Id).Last();
        Assert.True(state.Active && state.MyLocked && state.TheirLocked);
        Assert.Equal((200L, "pocao_hp_p", 2), (state.TheirAlz, state.TheirItems[0].ItemId, state.TheirItems[0].Quantity));

        // Mudar a oferta depois de travar destrava os dois.
        Trade(world, bia, TradeActionKind.SetOffer, items: Offer(bia, "pocao_hp_p", 4));
        var trade = world.FindTrade(ana.TradeId)!;
        Assert.False(trade.LockedA || trade.LockedB);
        Trade(world, ana, TradeActionKind.Lock);
        Trade(world, bia, TradeActionKind.Lock);
        Trade(world, ana, TradeActionKind.Confirm);
        Assert.Equal(5, ana.Bag.Count("pocao_hp_p")); // ainda não trocou
        Trade(world, bia, TradeActionKind.Confirm);

        Assert.Equal(0, ana.TradeId);
        Assert.Equal((300L, 200L), (ana.Alz, bia.Alz));
        Assert.Equal(5 - 2 + 4, ana.Bag.Count("pocao_hp_p"));
        Assert.Equal(5 - 4 + 2, bia.Bag.Count("pocao_hp_p"));
        Assert.Same(sword, ana.Equipment[EquipSlot.Weapon]);
        Assert.False(outbox.To<TradeState>(ana.Id).Last().Active);
        Assert.Contains("Troca com Bia concluída", outbox.Texts(ana.Id));

        var (_, logs) = world.CaptureForSave(ana);
        Assert.Contains(logs, l => l.Kind == "troca_saida" && l.Quantity == 2);
        Assert.Contains(logs, l => l.Kind == "troca_entrada" && l.Quantity == 4);
        Assert.Contains(logs, l => l.Kind == "troca_alz" && l.AlzDelta == -200);
    }

    [Fact]
    public void Troca_cancela_ao_se_afastar_e_exige_estar_perto()
    {
        var (world, outbox, ana, bia) = Setup();
        bia.Movement.Teleport(ana.X + 20, ana.Z, 0);
        Trade(world, ana, TradeActionKind.Request, name: "Bia");
        Assert.Contains("Chegue perto de Bia para trocar", outbox.Texts(ana.Id));

        bia.Movement.Teleport(ana.X + 1, ana.Z, 0);
        Trade(world, ana, TradeActionKind.Request, name: "Bia");
        Trade(world, bia, TradeActionKind.Accept, ana.Id);
        Trade(world, ana, TradeActionKind.SetOffer, items: Offer(ana, "pocao_hp_p", 5));
        bia.Movement.Teleport(ana.X + TradeRules.KeepRange + 5, ana.Z, 1);
        world.Tick(1.05);
        Assert.Equal(0, ana.TradeId);
        Assert.Contains("A troca foi cancelada: vocês se afastaram", outbox.Texts(bia.Id));
        Assert.Equal(5, ana.Bag.Count("pocao_hp_p"));
    }

    [Fact]
    public void Desconectar_cancela_a_troca_e_convidado_nao_troca_com_conta()
    {
        var (world, outbox, ana, bia) = Setup();
        Trade(world, ana, TradeActionKind.Request, name: "Bia");
        Trade(world, bia, TradeActionKind.Accept, ana.Id);
        world.RemovePlayer(bia.Id);
        world.Tick(1.05);
        Assert.Equal(0, ana.TradeId);
        Assert.Contains("A troca foi cancelada: o outro jogador saiu", outbox.Texts(ana.Id));

        var guest = world.AddPlayer("Bot01", 2);
        guest.Movement.Teleport(ana.X + 1, ana.Z, 2);
        Trade(world, guest, TradeActionKind.Request, name: "Ana");
        Assert.Contains("Convidados só trocam com convidados", outbox.Texts(guest.Id));
    }

    [Fact]
    public void Oferta_invalida_e_recusada_e_bloqueado_nao_pede_troca()
    {
        var (world, outbox, ana, bia) = Setup();
        Trade(world, ana, TradeActionKind.Request, name: "Bia");
        Trade(world, bia, TradeActionKind.Accept, ana.Id);
        Trade(world, ana, TradeActionKind.SetOffer, alz: 999_999);
        Assert.Contains("Alz insuficiente", outbox.Texts(ana.Id));
        Trade(world, ana, TradeActionKind.SetOffer, items: new TradeItem(0, 424242, 1));
        Assert.Contains("Um item da oferta não está mais na bolsa", outbox.Texts(ana.Id));
        Trade(world, ana, TradeActionKind.Cancel);
        Assert.Equal(0, bia.TradeId);

        world.Handle(bia.Id, new SocialCommand { Action = RelationActionKind.Block, TargetName = "Ana" }, 2);
        Trade(world, ana, TradeActionKind.Request, name: "Bia");
        Assert.Contains("Bia não está aceitando trocas", outbox.Texts(ana.Id));
    }
}
