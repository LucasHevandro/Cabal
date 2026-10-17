using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;

namespace Game.Server.World;

/// <summary>
/// Loja da mercadora (comprar consumíveis e equipamento básico, vender drops) e troca direta entre jogadores com dupla
/// confirmação. Tudo com registro de Alz e itens (item_log).
/// </summary>
public sealed partial class GameWorld
{
    private readonly Dictionary<int, TradeSession> _trades = new();
    private int _nextTradeId = 1;

    public TradeSession? FindTrade(int tradeId) => _trades.TryGetValue(tradeId, out var trade) ? trade : null;

    // ===== Loja =====

    private void HandleShop(ServerPlayer player, ShopCommand command)
    {
        if (player.TradeId != 0)
        {
            SystemChat(player, "Termine a troca antes de usar a loja");
            return;
        }
        if (player.IsDead || !NearNpc(player, NpcRole.Merchant))
        {
            Notify(player, "Vá até a mercadora da vila.");
            return;
        }
        if (command.Action == ShopActionKind.Buy) Buy(player, command.ItemId, command.Quantity);
        else Sell(player, command.Slot, command.Quantity);
    }

    private void Buy(ServerPlayer player, string itemId, int quantity)
    {
        var entry = ShopCatalog.Find(itemId);
        if (entry == null) return;
        var definition = ItemCatalog.Get(entry.ItemId);
        quantity = Math.Clamp(quantity, 1, definition.MaxStack);
        var cost = entry.Price * quantity;
        if (player.Alz < cost)
        {
            Notify(player, $"Faltam Alz: {definition.Name} x{quantity} custa {cost:N0}");
            return;
        }
        if (!player.Bag.TryAdd(entry.ItemId, quantity, _uids.Next))
        {
            Notify(player, "Bolsa cheia");
            return;
        }
        player.Alz -= cost;
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "compra_loja", entry.ItemId, quantity, -cost, $"{quantity} x {entry.Price}");
        Notify(player, $"Comprou {definition.Name} x{quantity} por {cost:N0} Alz");
    }

    private void Sell(ServerPlayer player, int slot, int quantity)
    {
        var item = player.Bag[slot];
        if (item == null) return;
        var definition = ItemCatalog.Get(item.ItemId);
        quantity = Math.Clamp(quantity <= 0 ? item.Quantity : quantity, 1, item.Quantity);
        var price = EconomyRules.SellPrice(definition, item.Upgrade) * quantity;
        if (quantity == item.Quantity) player.Bag.Take(slot);
        else item.Quantity -= quantity;
        player.Alz += price;
        player.InventoryDirty = true;
        player.StatsDirty = true;
        player.QuestsDirty |= player.Quests.Active.Any();
        Log(player, "venda_loja", item.ItemId, quantity, price, $"uid {item.Uid}{(item.Upgrade > 0 ? $" +{item.Upgrade}" : "")}");
        Notify(player, $"Vendeu {definition.Name}{(item.Upgrade > 0 ? $" +{item.Upgrade}" : "")} x{quantity} por {price:N0} Alz");
    }

    // ===== Troca =====

    private void HandleTrade(ServerPlayer player, TradeCommand command, double now)
    {
        var trade = FindTrade(player.TradeId);
        switch (command.Action)
        {
            case TradeActionKind.Request:
                RequestTrade(player, command.TargetName, now);
                break;
            case TradeActionKind.Accept:
                AcceptTrade(player, command.TargetId, now);
                break;
            case TradeActionKind.Decline:
                if (player.PendingTradeInvite is { } invite && _players.TryGetValue(invite.FromId, out var from))
                    SystemChat(from, $"{player.Name} recusou a troca");
                player.PendingTradeInvite = null;
                break;
            case TradeActionKind.Cancel:
                if (trade != null) CancelTrade(trade, $"{player.Name} cancelou a troca");
                break;
            case TradeActionKind.SetOffer when trade != null:
                var offer = new TradeOffer { Alz = command.Alz, Items = command.Items.ToList() };
                var problem = TradeExecutor.CheckOffer(offer, player.Bag, player.Alz);
                if (problem != null)
                {
                    SystemChat(player, problem);
                    return;
                }
                trade.SetOffer(player.Id, offer);
                MarkTrade(trade);
                break;
            case TradeActionKind.Lock when trade != null:
                var invalid = TradeExecutor.CheckOffer(trade.OfferOf(player.Id), player.Bag, player.Alz);
                if (invalid != null)
                {
                    SystemChat(player, invalid);
                    return;
                }
                trade.Lock(player.Id);
                MarkTrade(trade);
                break;
            case TradeActionKind.Confirm when trade != null:
                if (!trade.Confirm(player.Id))
                {
                    SystemChat(player, "Os dois precisam apertar Pronto antes de confirmar");
                    return;
                }
                MarkTrade(trade);
                if (trade.BothConfirmed) CompleteTrade(trade);
                break;
        }
    }

    private void RequestTrade(ServerPlayer player, string name, double now)
    {
        var target = FindPlayerByName(name);
        string? error =
            target == null ? $"{name} não está online" :
            target == player ? "Você não pode trocar consigo mesmo" :
            player.TradeId != 0 ? "Você já está numa troca" :
            target.TradeId != 0 ? $"{target.Name} já está numa troca" :
            player.IsDead || target.IsDead ? "Ninguém troca morto" :
            player.IsGuest != target.IsGuest ? "Convidados só trocam com convidados" :
            target.HasBlocked(player) ? $"{target.Name} não está aceitando trocas" :
            player.HasBlocked(target) ? $"Desbloqueie {target.Name} para trocar" :
            Distance(player.X, player.Z, target.X, target.Z) > TradeRules.Range ? $"Chegue perto de {target.Name} para trocar" :
            target.PendingTradeInvite is { } pending && pending.ExpiresAt > now && pending.FromId != player.Id
                ? $"{target.Name} já tem um pedido de troca esperando" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        target!.PendingTradeInvite = (player.Id, now + TradeRules.InviteSeconds);
        _outbox.Send(target.Id, new TradeInvite { FromId = player.Id, FromName = player.Name, Seconds = (float)TradeRules.InviteSeconds });
        SystemChat(player, $"Pedido de troca enviado para {target.Name}");
    }

    private void AcceptTrade(ServerPlayer player, int fromId, double now)
    {
        var pending = player.PendingTradeInvite;
        player.PendingTradeInvite = null;
        if (pending is not { } invite || invite.FromId != fromId || invite.ExpiresAt < now || !_players.TryGetValue(fromId, out var from))
        {
            SystemChat(player, "O pedido de troca expirou");
            return;
        }
        string? error =
            player.TradeId != 0 || from.TradeId != 0 ? "Alguém já está numa troca" :
            player.IsDead || from.IsDead ? "Ninguém troca morto" :
            Distance(player.X, player.Z, from.X, from.Z) > TradeRules.Range ? $"Chegue perto de {from.Name} para trocar" :
            null;
        if (error != null)
        {
            SystemChat(player, error);
            return;
        }
        var trade = new TradeSession(_nextTradeId++, from.Id, player.Id);
        _trades.Add(trade.Id, trade);
        from.TradeId = player.TradeId = trade.Id;
        MarkTrade(trade);
    }

    private void CompleteTrade(TradeSession trade)
    {
        var a = _players[trade.PlayerA];
        var b = _players[trade.PlayerB];
        if (Distance(a.X, a.Z, b.X, b.Z) > TradeRules.Range)
        {
            Unlock(trade, "Cheguem mais perto para concluir a troca");
            return;
        }
        // Guarda o que sai antes de mexer (para os registros).
        var outA = Describe(a, trade.OfferA);
        var outB = Describe(b, trade.OfferB);
        var alzA = a.Alz;
        var alzB = b.Alz;
        var error = TradeExecutor.Execute(trade, a.Bag, ref alzA, b.Bag, ref alzB, _uids.Next);
        if (error != null)
        {
            Unlock(trade, $"A troca não aconteceu: {error}");
            return;
        }
        a.Alz = alzA;
        b.Alz = alzB;

        foreach (var (player, partner, given, received, offer, partnerOffer) in new[]
                 {
                     (a, b, outA, outB, trade.OfferA, trade.OfferB),
                     (b, a, outB, outA, trade.OfferB, trade.OfferA),
                 })
        {
            player.InventoryDirty = true;
            player.StatsDirty = true;
            player.QuestsDirty |= player.Quests.Active.Any();
            var details = $"troca {trade.Id} com {partner.Name} (personagem {partner.CharacterId})";
            foreach (var (itemId, quantity, uid) in given) Log(player, "troca_saida", itemId, quantity, 0, $"{details}, uid {uid}");
            foreach (var (itemId, quantity, uid) in received) Log(player, "troca_entrada", itemId, quantity, 0, $"{details}, uid {uid}");
            var alzDelta = partnerOffer.Alz - offer.Alz;
            if (alzDelta != 0) Log(player, "troca_alz", null, 0, alzDelta, details);
            SystemChat(player, $"Troca com {partner.Name} concluída");
        }
        CloseTrade(trade);
    }

    private static List<(string ItemId, int Quantity, long Uid)> Describe(ServerPlayer player, TradeOffer offer) =>
        offer.Items.Select(i => (player.Bag[i.Slot]?.ItemId ?? "?", i.Quantity, i.Uid)).ToList();

    /// <summary>A troca não pôde acontecer: destrava os dois (as ofertas continuam) e explica.</summary>
    private void Unlock(TradeSession trade, string message)
    {
        trade.SetOffer(trade.PlayerA, trade.OfferA);
        MarkTrade(trade);
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) SystemChat(p, message);
    }

    private void CancelTrade(TradeSession trade, string message)
    {
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) SystemChat(p, message);
        CloseTrade(trade);
    }

    private void CloseTrade(TradeSession trade)
    {
        _trades.Remove(trade.Id);
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
        {
            if (!_players.TryGetValue(id, out var p)) continue;
            p.TradeId = 0;
            _outbox.Send(p.Id, new TradeState { Active = false });
        }
    }

    private void MarkTrade(TradeSession trade)
    {
        foreach (var id in new[] { trade.PlayerA, trade.PlayerB })
            if (_players.TryGetValue(id, out var p)) p.TradeDirty = true;
    }

    /// <summary>Cancela trocas de quem saiu, morreu ou se afastou.</summary>
    private void TickTrades()
    {
        foreach (var trade in _trades.Values.ToList())
        {
            var hasA = _players.TryGetValue(trade.PlayerA, out var a);
            var hasB = _players.TryGetValue(trade.PlayerB, out var b);
            if (!hasA || !hasB) CancelTrade(trade, "A troca foi cancelada: o outro jogador saiu");
            else if (a!.IsDead || b!.IsDead) CancelTrade(trade, "A troca foi cancelada: alguém morreu");
            else if (Distance(a.X, a.Z, b.X, b.Z) > TradeRules.KeepRange) CancelTrade(trade, "A troca foi cancelada: vocês se afastaram");
        }
    }

    private void SendTradeState(ServerPlayer player)
    {
        if (!player.TradeDirty) return;
        player.TradeDirty = false;
        var trade = FindTrade(player.TradeId);
        if (trade == null) return;
        var partner = _players[trade.PartnerOf(player.Id)];
        _outbox.Send(player.Id, new TradeState
        {
            Active = true, PartnerId = partner.Id, PartnerName = partner.Name,
            MyItems = Infos(player, trade.OfferOf(player.Id)), MyAlz = trade.OfferOf(player.Id).Alz,
            TheirItems = Infos(partner, trade.OfferOf(partner.Id)), TheirAlz = trade.OfferOf(partner.Id).Alz,
            MyLocked = trade.LockedOf(player.Id), TheirLocked = trade.LockedOf(partner.Id),
            MyConfirmed = trade.ConfirmedOf(player.Id), TheirConfirmed = trade.ConfirmedOf(partner.Id),
        });
    }

    private static List<TradeItemInfo> Infos(ServerPlayer owner, TradeOffer offer) => offer.Items
        .Select(i => (Entry: i, Item: owner.Bag[i.Slot]))
        .Where(x => x.Item != null && x.Item.Uid == x.Entry.Uid)
        .Select(x => new TradeItemInfo
        {
            Slot = x.Entry.Slot, Uid = x.Entry.Uid, ItemId = x.Item!.ItemId, Quantity = x.Entry.Quantity,
            Upgrade = (byte)x.Item.Upgrade, OptionId = x.Item.OptionId,
        })
        .ToList();
}
