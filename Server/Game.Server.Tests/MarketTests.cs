using Game.Server.Accounts;
using Game.Server.Persistence;
using Game.Server.World;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.World;

namespace Game.Server.Tests;

/// <summary>Mercado da vila: pôr à venda, comprar, retirar, receber, vencer e salvar sem duplicar.</summary>
public class MarketTests
{
    private sealed class FakeOutbox : IOutbox
    {
        public List<(int PlayerId, INetMessage Message)> Sent { get; } = new();
        public void Send(int playerId, INetMessage message) => Sent.Add((playerId, message));

        public List<T> To<T>(int playerId) where T : INetMessage =>
            Sent.Where(s => s.PlayerId == playerId).Select(s => s.Message).OfType<T>().ToList();

        public List<string> Notices(int playerId) => To<Notice>(playerId).Select(n => n.Text.ToString()).ToList();
    }

    private sealed class FixedRandom : IRandomSource
    {
        public double NextDouble() => 0.5;
    }

    private long _uid = 1000;
    private long NewUid() => ++_uid;
    private DateTimeOffset _clock = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private (GameWorld World, FakeOutbox Outbox) NewWorld()
    {
        var outbox = new FakeOutbox();
        var world = new GameWorld(outbox, Array.Empty<SpawnDefinition>(), new FixedRandom(), new ItemUidAllocator(5000)) { Clock = () => _clock };
        return (world, outbox);
    }

    /// <summary>Personagem de verdade (salvo no banco), já perto da escrivã.</summary>
    private ServerPlayer Join(GameWorld world, int id, string name, long alz = 0)
    {
        var record = AuthService.InitialCharacter(CharacterClass.Blade, NewUid) with { Id = id, AccountId = id, Name = name, Alz = alz };
        var player = world.AddPlayer("", 0, null, record);
        var npc = NpcCatalog.Find(NpcCatalog.Market)!;
        player.Movement.Teleport(npc.X - 1.5f, npc.Z + 1.5f, 0);
        return player;
    }

    private static void Market(GameWorld world, ServerPlayer player, MarketActionKind action, int slot = 0, int quantity = 0, long price = 0,
        long listing = 0) =>
        world.Handle(player.Id, new MarketCommand { Action = action, Slot = (ushort)slot, Quantity = quantity, Price = price, ListingId = listing }, 1);

    [Fact]
    public void Vender_parte_da_pilha_comprar_e_receber_com_taxa()
    {
        var (world, outbox) = NewWorld();
        var ana = Join(world, 7, "Ana");
        var bia = Join(world, 8, "Bia", alz: 5000);
        ana.Bag.TryAdd("pele_lobo", 10, NewUid);
        var slot = ana.Bag.Items().Single(i => i.Item.ItemId == "pele_lobo").Slot;

        Market(world, ana, MarketActionKind.Sell, slot, 4, 1000);
        Assert.Equal(6, ana.Bag.Count("pele_lobo"));
        var listing = Assert.Single(world.MarketListings);
        Assert.Equal(4, listing.Item.Quantity);

        Market(world, bia, MarketActionKind.Open);
        var offer = Assert.Single(outbox.To<MarketState>(bia.Id).Last().Offers);
        Assert.Equal(("pele_lobo", 4, 1000L, "Ana"), (offer.ItemId, offer.Quantity, offer.Price, offer.Seller));

        Market(world, bia, MarketActionKind.Buy, listing: offer.Id);
        Assert.Equal(4, bia.Bag.Count("pele_lobo"));
        Assert.Equal(4000, bia.Alz);
        Assert.Contains(outbox.Notices(ana.Id), n => n.Contains("vendida para Bia"));
        Market(world, ana, MarketActionKind.Open);
        Assert.Equal(950, outbox.To<MarketState>(ana.Id).Last().ToCollectAlz);

        var before = ana.Alz;
        Market(world, ana, MarketActionKind.Collect);
        Assert.Equal(before + 950, ana.Alz); // 5% de taxa
        Assert.Empty(world.MarketListings);
    }

    [Fact]
    public void Equipamento_vai_inteiro_com_melhoria_e_opcao()
    {
        var (world, _) = NewWorld();
        var ana = Join(world, 7, "Ana");
        var bia = Join(world, 8, "Bia", alz: 100_000);
        ana.Bag.TryPlace(new ItemInstance { Uid = NewUid(), ItemId = "espada_aco_negro", Upgrade = 5, OptionId = "pedra_furia" }, out var slot);

        Market(world, ana, MarketActionKind.Sell, slot, 1, 50_000);
        Market(world, bia, MarketActionKind.Buy, listing: world.MarketListings.Single().Id);

        var bought = bia.Bag.Items().Single(i => i.Item.ItemId == "espada_aco_negro").Item;
        Assert.Equal((5, "pedra_furia"), (bought.Upgrade, bought.OptionId));
    }

    [Fact]
    public void Nao_compra_a_propria_oferta_sem_alz_nem_longe_da_escriva()
    {
        var (world, outbox) = NewWorld();
        var ana = Join(world, 7, "Ana");
        var bia = Join(world, 8, "Bia", alz: 10);
        ana.Bag.TryAdd("pele_lobo", 1, NewUid);

        Market(world, ana, MarketActionKind.Sell, ana.Bag.SlotOf(ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Item.Uid), 1, 0);
        Assert.Empty(world.MarketListings); // preço inválido
        Market(world, ana, MarketActionKind.Sell, ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot, 1, 500);
        var id = world.MarketListings.Single().Id;

        Market(world, ana, MarketActionKind.Buy, listing: id);
        Assert.Contains(outbox.Notices(ana.Id), n => n.Contains("é sua"));
        Market(world, bia, MarketActionKind.Buy, listing: id);
        Assert.Contains(outbox.Notices(bia.Id), n => n.Contains("Alz insuficiente"));

        bia.Alz = 1000;
        bia.Movement.Teleport(0, 0, 0);
        Market(world, bia, MarketActionKind.Buy, listing: id);
        Assert.Contains(outbox.Notices(bia.Id), n => n.Contains("Fale com"));
        Assert.Equal(MarketListingState.Active, world.MarketListings.Single().State);
    }

    [Fact]
    public void Oferta_vencida_nao_vende_e_volta_ao_receber()
    {
        var (world, _) = NewWorld();
        var ana = Join(world, 7, "Ana");
        var bia = Join(world, 8, "Bia", alz: 10_000);
        ana.Bag.TryAdd("pele_lobo", 3, NewUid);
        Market(world, ana, MarketActionKind.Sell, ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot, 3, 300);
        var id = world.MarketListings.Single().Id;

        _clock = _clock.AddHours(MarketRules.ListingHours + 1);
        Market(world, bia, MarketActionKind.Buy, listing: id);
        Assert.Equal(0, bia.Bag.Count("pele_lobo"));

        Market(world, ana, MarketActionKind.Collect);
        Assert.Equal(3, ana.Bag.Count("pele_lobo"));
        Assert.Empty(world.MarketListings);
    }

    [Fact]
    public void Limite_de_ofertas_por_personagem()
    {
        var (world, outbox) = NewWorld();
        var ana = Join(world, 7, "Ana");
        ana.Bag.TryAdd("pele_lobo", MarketRules.MaxListings + 1, NewUid);
        var slot = ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot;
        for (var i = 0; i <= MarketRules.MaxListings; i++) Market(world, ana, MarketActionKind.Sell, slot, 1, 10);

        Assert.Equal(MarketRules.MaxListings, world.MarketListings.Count);
        Assert.Contains(outbox.Notices(ana.Id), n => n.Contains("já tem"));
    }

    [Fact]
    public void Cada_acao_salva_na_hora_e_o_banco_nunca_volta_a_situacao()
    {
        var (world, _) = NewWorld();
        var ana = Join(world, 7, "Ana");
        var bia = Join(world, 8, "Bia", alz: 5000);
        var saves = new List<CharacterSnapshot>();
        world.SaveRequested += p => saves.Add(world.CaptureForSave(p).Snapshot);
        ana.Bag.TryAdd("pele_lobo", 2, NewUid);

        Market(world, ana, MarketActionKind.Sell, ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot, 2, 800);
        Market(world, bia, MarketActionKind.Buy, listing: world.MarketListings.Single().Id);

        Assert.Equal(new[] { 7, 8 }, saves.Select(s => s.CharacterId));
        // O salvamento da Ana já vai sem o item na bolsa e com a oferta; o da Bia, com o item e a oferta vendida.
        Assert.DoesNotContain(saves[0].Items, i => i.ItemId == "pele_lobo");
        Assert.Equal(MarketListingState.Active, Assert.Single(saves[0].Market).State);
        Assert.Contains(saves[1].Items, i => i.ItemId == "pele_lobo");
        Assert.Equal(MarketListingState.Sold, Assert.Single(saves[1].Market).State);

        // Mesmo que cheguem fora de ordem, a oferta salva fica como vendida.
        var store = new InMemoryGameStore();
        var account = store.CreateAccountAsync("ana", "x", "1").Result;
        var created = store.CreateCharacterAsync(account.Id, "Ana", CharacterClass.Blade, AuthService.InitialCharacter(CharacterClass.Blade, null)).Result;
        var sold = saves[1].Market[0] with { SellerCharacterId = created.Id };
        var active = saves[0].Market[0] with { SellerCharacterId = created.Id };
        store.SaveCharacterAsync(saves[1] with { CharacterId = created.Id, Market = new[] { sold } }, Array.Empty<ItemLogEntry>()).Wait();
        store.SaveCharacterAsync(saves[0] with { CharacterId = created.Id, Market = new[] { active } }, Array.Empty<ItemLogEntry>()).Wait();
        Assert.Equal(MarketListingState.Sold, store.AllMarketListings().Single().State);
    }

    [Fact]
    public void Ofertas_voltam_do_banco_ao_ligar_o_servidor()
    {
        var (world, _) = NewWorld();
        var ana = Join(world, 7, "Ana");
        ana.Bag.TryPlace(new ItemInstance { Uid = NewUid(), ItemId = "arco_ferro", Upgrade = 3 }, out var slot);
        Market(world, ana, MarketActionKind.Sell, slot, 1, 2500);
        var record = world.CaptureForSave(ana).Snapshot.Market.Single();

        var (restarted, outbox) = NewWorld();
        restarted.SeedMarket(new[] { record });
        var bia = Join(restarted, 8, "Bia", alz: 3000);
        Market(restarted, bia, MarketActionKind.Buy, listing: record.Id);

        var bought = bia.Bag.Items().Single(i => i.Item.ItemId == "arco_ferro").Item;
        Assert.Equal(3, bought.Upgrade);
        Assert.Equal(500, bia.Alz);
        Assert.Contains(outbox.Notices(bia.Id), n => n.Contains("comprou"));
    }

    [Fact]
    public void Retirar_devolve_o_item()
    {
        var (world, _) = NewWorld();
        var ana = Join(world, 7, "Ana");
        ana.Bag.TryAdd("pele_lobo", 5, NewUid);
        Market(world, ana, MarketActionKind.Sell, ana.Bag.Items().First(i => i.Item.ItemId == "pele_lobo").Slot, 5, 100);
        Assert.Equal(0, ana.Bag.Count("pele_lobo"));

        Market(world, ana, MarketActionKind.Cancel, listing: world.MarketListings.Single().Id);
        Assert.Equal(5, ana.Bag.Count("pele_lobo"));
        Assert.Empty(world.MarketListings);
    }
}
