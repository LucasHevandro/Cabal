using System.Collections.Generic;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Progression;
using UnityEngine;
using Msg = Game.Shared.Net;

namespace Game.Client.UI
{
    /// <summary>Loja da mercadora e troca entre jogadores (as janelas ficam à esquerda do inventário).</summary>
    public sealed partial class GameWindows
    {
        // Loja: item da bolsa escolhido para vender (pelo id único)
        private long _sellUid;
        private Vector2 _shopScroll;

        private Rect ShopRect => new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 560);
        private Rect TradeRect => new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 520);

        private static bool Trading => CharacterState.Trade != null && CharacterState.Trade.Active;

        // Usados pelo teste automático.
        public void SelectForSale(long uid) => _sellUid = uid;
        public void OfferInTrade(int bagSlot) => AddToOffer(bagSlot);

        // ----- Loja -----

        private void DrawShop(NpcDefinition npc)
        {
            var rect = Window(ShopRect, $"{npc.Name} — loja");
            var x = rect.x + 14;
            var y = rect.y + 44;
            var alz = Player.GetComponent<PlayerLoot>()?.Alz ?? 0;

            var listRect = new Rect(x, y, rect.width - 28, 300);
            var rowHeight = 42f;
            _shopScroll = GUI.BeginScrollView(listRect, _shopScroll, new Rect(0, 0, listRect.width - 20, ShopCatalog.Stock.Count * rowHeight));
            for (var i = 0; i < ShopCatalog.Stock.Count; i++)
            {
                var entry = ShopCatalog.Stock[i];
                var definition = ItemCatalog.Get(entry.ItemId);
                var row = new Rect(0, i * rowHeight, listRect.width - 20, rowHeight - 4);
                var icon = new Rect(row.x, row.y, 38, 38);
                DrawItemIcon(icon, new ItemInstance { ItemId = entry.ItemId });
                if (icon.Contains(Event.current.mousePosition)) _hoverItem = new ItemInstance { ItemId = entry.ItemId };
                var affordable = alz >= entry.Price;
                GUI.Label(new Rect(row.x + 46, row.y + 2, 200, 22), definition.Name, _small);
                GUI.Label(new Rect(row.x + 46, row.y + 20, 200, 18),
                    $"<color=#{(affordable ? "ffd25a" : "ff7766")}>{entry.Price:N0} Alz</color>", _tiny);
                GUI.enabled = affordable;
                if (GUI.Button(new Rect(row.xMax - 140, row.y + 4, 64, 30), "x1", _button)) Buy(entry, 1);
                GUI.enabled = definition.MaxStack > 1 && alz >= entry.Price * 10;
                if (GUI.Button(new Rect(row.xMax - 70, row.y + 4, 64, 30), "x10", _button)) Buy(entry, 10);
                GUI.enabled = true;
            }
            GUI.EndScrollView();

            // Vender
            y = listRect.yMax + 12;
            Box(new Rect(x, y - 6, rect.width - 28, 1), new Color(1, 1, 1, 0.2f));
            GUI.Label(new Rect(x, y, rect.width - 28, 24), "<b>Vender</b>  <size=13>(botão direito ou arraste um item da bolsa)</size>", _small);
            var slot = _sellUid != 0 ? CharacterState.Bag.SlotOf(_sellUid) : -1;
            var item = slot >= 0 ? CharacterState.Bag[slot] : null;
            if (item == null)
            {
                _sellUid = 0;
                GUI.Label(new Rect(x, y + 32, rect.width - 28, 24), "Nenhum item escolhido", _tiny);
                return;
            }
            var sold = ItemCatalog.Get(item.ItemId);
            var iconRect = new Rect(x, y + 30, SlotSize, SlotSize);
            DrawItemIcon(iconRect, item);
            if (iconRect.Contains(Event.current.mousePosition))
            {
                _hoverItem = item;
                _hoverWorn = false;
            }
            var unit = EconomyRules.SellPrice(sold, item.Upgrade);
            GUI.Label(new Rect(x + 62, y + 30, rect.width - 90, 24),
                $"{sold.Name}{(item.Upgrade > 0 ? $" +{item.Upgrade}" : "")}{(item.Quantity > 1 ? $" x{item.Quantity}" : "")}", _small);
            GUI.Label(new Rect(x + 62, y + 52, rect.width - 90, 22), $"{unit:N0} Alz cada", _tiny);
            if (item.Upgrade > 0 || !string.IsNullOrEmpty(item.OptionId) || sold.Rarity >= Game.Shared.Loot.ItemRarity.Epic)
                GUI.Label(new Rect(x, y + 86, rect.width - 28, 22), "<color=#ff7766>Atenção: item valioso, não dá para desfazer.</color>", _tiny);
            if (item.Quantity > 1 && GUI.Button(new Rect(x, y + 112, 150, 34), "Vender 1", _button)) Sell(slot, 1);
            if (GUI.Button(new Rect(rect.xMax - 214, y + 112, 200, 34), $"Vender tudo ({unit * item.Quantity:N0})", _button)) Sell(slot, 0);
        }

        private static void Buy(ShopEntry entry, int quantity) =>
            Send(new Msg.ShopCommand { Action = Msg.ShopActionKind.Buy, ItemId = entry.ItemId, Quantity = quantity });

        private void Sell(int slot, int quantity)
        {
            Send(new Msg.ShopCommand { Action = Msg.ShopActionKind.Sell, Slot = (ushort)slot, Quantity = quantity });
            if (quantity == 0) _sellUid = 0;
        }

        // ----- Troca -----

        private void DrawTrade()
        {
            var state = CharacterState.Trade;
            var rect = Window(TradeRect, $"Troca com {state.PartnerName}");
            var x = rect.x + 14;
            var y = rect.y + 44;

            // Do outro lado
            GUI.Label(new Rect(x, y, 300, 24), $"<b>{state.PartnerName} oferece</b>  {Status(state.TheirLocked, state.TheirConfirmed)}", _small);
            DrawOfferRow(state.TheirItems, new Vector2(x, y + 28), mine: false);
            GUI.Label(new Rect(x, y + 28 + SlotSize + 6, 300, 22), $"<b>{state.TheirAlz:N0}</b> Alz", _small);

            // Do meu lado
            y += 140;
            Box(new Rect(x, y - 8, rect.width - 28, 1), new Color(1, 1, 1, 0.2f));
            GUI.Label(new Rect(x, y, 420, 24), $"<b>Você oferece</b>  {Status(state.MyLocked, state.MyConfirmed)}", _small);
            GUI.Label(new Rect(x, y + 22, 420, 20), "<size=12>Botão direito ou arraste da bolsa; clique num item seu para tirar.</size>", _tiny);
            DrawOfferRow(state.MyItems, new Vector2(x, y + 46), mine: true);

            y += 46 + SlotSize + 10;
            GUI.Label(new Rect(x, y, 200, 26), $"<b>{state.MyAlz:N0}</b> Alz", _small);
            var bx = x + 130;
            foreach (var step in new long[] { 100, 1000, 10000 })
            {
                if (GUI.Button(new Rect(bx, y, 70, 28), $"+{step:N0}", _button)) SetAlz(state.MyAlz + step);
                bx += 74;
            }
            if (GUI.Button(new Rect(bx, y, 66, 28), "Zerar", _button)) SetAlz(0);

            // Confirmações
            y = rect.yMax - 96;
            GUI.Label(new Rect(x, y, rect.width - 28, 40),
                !state.BothLocked() ? "<size=13>1) Os dois apertam <b>Pronto</b>. Mudar a oferta destrava os dois.</size>"
                                    : "<size=13>2) Confira tudo e aperte <b>Confirmar</b>. A troca acontece quando os dois confirmam.</size>", _tiny);
            GUI.enabled = !state.MyLocked;
            if (GUI.Button(new Rect(x, rect.yMax - 50, 130, 36), "Pronto", _button)) Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.Lock });
            GUI.enabled = state.BothLocked() && !state.MyConfirmed;
            if (GUI.Button(new Rect(x + 140, rect.yMax - 50, 150, 36), "<b>Confirmar</b>", _button))
                Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.Confirm });
            GUI.enabled = true;
            if (GUI.Button(new Rect(rect.xMax - 134, rect.yMax - 50, 120, 36), "Cancelar", _button))
                Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.Cancel });
        }

        private static string Status(bool locked, bool confirmed) =>
            confirmed ? "<color=#77ee77>✔ confirmou</color>" : locked ? "<color=#ffd25a>pronto</color>" : "<color=#aaaaaa>montando</color>";

        private void DrawOfferRow(List<Msg.TradeItemInfo> items, Vector2 origin, bool mine)
        {
            for (var i = 0; i < TradeRules.MaxItems; i++)
            {
                var rect = new Rect(origin.x + i * (SlotSize + SlotGap), origin.y, SlotSize, SlotSize);
                Box(rect, SlotBack);
                if (i >= items.Count) continue;
                var info = items[i];
                var item = new ItemInstance
                {
                    Uid = info.Uid, ItemId = info.ItemId, Quantity = info.Quantity, Upgrade = info.Upgrade, OptionId = info.OptionId ?? "",
                };
                DrawItemIcon(rect, item);
                if (!rect.Contains(Event.current.mousePosition)) continue;
                _hoverItem = item;
                _hoverWorn = false;
                if (mine && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    RemoveFromOffer(info.Slot);
                    Event.current.Use();
                }
            }
        }

        private static void AddToOffer(int bagSlot)
        {
            var state = CharacterState.Trade;
            var item = CharacterState.Bag[bagSlot];
            if (state == null || !state.Active || item == null) return;
            var items = new List<TradeItem>();
            foreach (var mine in state.MyItems)
            {
                if (mine.Slot == bagSlot) return; // já está na oferta
                items.Add(new TradeItem(mine.Slot, mine.Uid, mine.Quantity));
            }
            if (items.Count >= TradeRules.MaxItems) return;
            items.Add(new TradeItem(bagSlot, item.Uid, item.Quantity));
            Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.SetOffer, Items = items, Alz = state.MyAlz });
        }

        private static void RemoveFromOffer(int bagSlot)
        {
            var state = CharacterState.Trade;
            var items = new List<TradeItem>();
            foreach (var mine in state.MyItems)
                if (mine.Slot != bagSlot) items.Add(new TradeItem(mine.Slot, mine.Uid, mine.Quantity));
            Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.SetOffer, Items = items, Alz = state.MyAlz });
        }

        private void SetAlz(long alz)
        {
            var state = CharacterState.Trade;
            var have = Player.GetComponent<PlayerLoot>()?.Alz ?? 0;
            var items = new List<TradeItem>();
            foreach (var mine in state.MyItems) items.Add(new TradeItem(mine.Slot, mine.Uid, mine.Quantity));
            Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.SetOffer, Items = items, Alz = System.Math.Min(alz, have) });
        }

        private void DrawTradeInvite()
        {
            var invite = CharacterState.PendingTradeInvite;
            if (invite == null) return;
            var rect = Window(new Rect(VirtualWidth / 2 - 230, 440, 460, 130), "Pedido de troca");
            GUI.Label(new Rect(rect.x, rect.y + 40, rect.width, 26), $"<b>{invite.FromName}</b> quer trocar com você", _label.WithCenter());
            if (GUI.Button(new Rect(rect.x + 60, rect.y + 80, 150, 36), "<b>Aceitar</b>", _button))
            {
                Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.Accept, TargetId = invite.FromId });
                CharacterState.ClearTradeInvite();
                _inventoryOpen = true;
            }
            if (GUI.Button(new Rect(rect.xMax - 210, rect.y + 80, 150, 36), "Recusar", _button))
            {
                Send(new Msg.TradeCommand { Action = Msg.TradeActionKind.Decline, TargetId = invite.FromId });
                CharacterState.ClearTradeInvite();
            }
        }
    }

    internal static class TradeStateExtensions
    {
        public static bool BothLocked(this Msg.TradeState state) => state.MyLocked && state.TheirLocked;
    }
}
