using System.Collections.Generic;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Economy;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.Text;
using Msg = Game.Shared.Net;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Mercado da escrivã (quadro de ofertas entre jogadores): comprar o que os outros puseram, pôr um item da bolsa à
    /// venda (botão direito no item) e acompanhar as próprias ofertas, recebendo o Alz das vendas e o que venceu.
    /// O servidor decide tudo; a janela só mostra o último <see cref="Msg.MarketState"/>.
    /// </summary>
    public sealed partial class GameWindows
    {
        private enum MarketTab
        {
            Buy,
            Sell,
            Mine,
        }

        private MarketTab _marketTab;
        private Vector2 _marketScroll;
        private string _marketSearch = "";
        private long _marketSellUid;
        private int _marketQuantity = 1;
        private string _marketPrice = "";
        private GUIStyle _marketInput;

        private Rect MarketRect => new Rect(InventoryRect.x - 520, InventoryRect.y, 510, 600);

        // Usados pelo teste automático.
        public void ShowMarketOffers()
        {
            _marketTab = MarketTab.Buy;
            _marketScroll = Vector2.zero;
        }

        public void SelectForMarket(long uid, long price)
        {
            _marketSellUid = uid;
            _marketTab = MarketTab.Sell;
            _marketQuantity = 1;
            _marketPrice = price.ToString();
        }

        private void DrawMarket(NpcDefinition npc)
        {
            _marketInput ??= new GUIStyle(GUI.skin.textField) { fontSize = 16, alignment = TextAnchor.MiddleLeft };
            var rect = Window(MarketRect, Loc.T("market.title", npc.Name));
            var state = CharacterState.Market;
            var x = rect.x + 14;
            var y = rect.y + 48;
            var tabWidth = (rect.width - 28 - 12) / 3f;
            var tabs = new[] { (MarketTab.Buy, Loc.T("market.tab_buy")), (MarketTab.Sell, Loc.T("market.tab_sell")),
                (MarketTab.Mine, Loc.T("market.tab_mine", state != null ? state.Mine.Count : 0)) };
            for (var i = 0; i < tabs.Length; i++)
            {
                var (tab, label) = tabs[i];
                if (GUI.Toggle(new Rect(x + i * (tabWidth + 6), y, tabWidth, 32), _marketTab == tab, label, _button) && _marketTab != tab)
                {
                    _marketTab = tab;
                    _marketScroll = Vector2.zero;
                }
            }
            var body = new Rect(x, y + 44, rect.width - 28, rect.yMax - y - 58);
            switch (_marketTab)
            {
                case MarketTab.Buy:
                    DrawMarketOffers(body, state);
                    break;
                case MarketTab.Sell:
                    DrawMarketSell(body, state);
                    break;
                default:
                    DrawMarketMine(body, state);
                    break;
            }
        }

        private void DrawMarketOffers(Rect body, Msg.MarketState state)
        {
            GUI.Label(new Rect(body.x, body.y + 2, 100, 26), Loc.T("market.search"), _small);
            _marketSearch = GUI.TextField(new Rect(body.x + 100, body.y, body.width - 100, 30), _marketSearch ?? "", 30, _marketInput);
            var offers = new List<Msg.MarketEntry>();
            var search = (_marketSearch ?? "").Trim().ToLowerInvariant();
            if (state != null)
                foreach (var offer in state.Offers)
                    if (search.Length == 0 || ItemCatalog.Find(offer.ItemId)?.Name.ToLowerInvariant().Contains(search) == true) offers.Add(offer);

            var list = new Rect(body.x, body.y + 40, body.width, body.height - 40);
            if (offers.Count == 0)
            {
                GUI.Label(new Rect(list.x, list.y + 10, list.width, 26), search.Length == 0 ? Loc.T("market.empty") : Loc.T("market.empty_search"), _tiny);
                return;
            }
            var alz = Player.GetComponent<PlayerLoot>()?.Alz ?? 0;
            const float row = 50f;
            _marketScroll = GUI.BeginScrollView(list, _marketScroll, new Rect(0, 0, list.width - 20, offers.Count * row));
            for (var i = 0; i < offers.Count; i++)
            {
                var offer = offers[i];
                var r = new Rect(0, i * row, list.width - 20, row - 4);
                DrawMarketItem(r, offer);
                var time = offer.Expired ? Loc.T("market.expired") : Loc.T("market.hours_left", offer.HoursLeft);
                GUI.Label(new Rect(r.x + 46, r.y + 24, 220, 20), Loc.T("market.seller", offer.Seller, time), _tiny);
                var affordable = alz >= offer.Price;
                GUI.Label(new Rect(r.xMax - 210, r.y + 2, 110, 22),
                    $"<color=#{(affordable ? "ffd25a" : "ff7766")}>{offer.Price:N0}</color>", _small.WithAlignment(TextAnchor.UpperRight));
                var own = IsMine(offer, state);
                GUI.enabled = affordable && !own;
                if (GUI.Button(new Rect(r.xMax - 92, r.y + 6, 90, 32), own ? Loc.T("market.own_offer") : Loc.T("market.buy"), _button))
                    Send(new Msg.MarketCommand { Action = Msg.MarketActionKind.Buy, ListingId = offer.Id });
                GUI.enabled = true;
            }
            GUI.EndScrollView();
        }

        private static bool IsMine(Msg.MarketEntry offer, Msg.MarketState state)
        {
            foreach (var mine in state.Mine) if (mine.Id == offer.Id) return true;
            return false;
        }

        /// <summary>Ícone, nome (com melhoria) e quantidade de uma oferta, com o tooltip do item.</summary>
        private void DrawMarketItem(Rect row, Msg.MarketEntry entry)
        {
            var item = new ItemInstance { ItemId = entry.ItemId, Quantity = entry.Quantity, Upgrade = entry.Upgrade, OptionId = entry.OptionId ?? "" };
            var icon = new Rect(row.x, row.y, 40, 40);
            DrawItemIcon(icon, item);
            if (icon.Contains(Event.current.mousePosition))
            {
                _hoverItem = item;
                _hoverWorn = false;
            }
            var definition = ItemCatalog.Find(entry.ItemId);
            var name = definition == null ? entry.ItemId : entry.Upgrade > 0 ? Loc.T("item.with_upgrade", definition.Name, entry.Upgrade) : definition.Name;
            GUI.Label(new Rect(row.x + 46, row.y + 2, 260, 22), name + (entry.Quantity > 1 ? $" x{entry.Quantity}" : ""), _small);
        }

        private void DrawMarketSell(Rect body, Msg.MarketState state)
        {
            var count = state != null ? state.Mine.Count : 0;
            GUI.Label(new Rect(body.x, body.y, body.width, 60), Loc.T("market.sell_hint", MarketRules.ListingHours), _tiny);
            GUI.Label(new Rect(body.x, body.y + 58, body.width, 22), Loc.T("market.limit_hint", count, MarketRules.MaxListings), _tiny);

            var slot = _marketSellUid != 0 ? CharacterState.Bag.SlotOf(_marketSellUid) : -1;
            var item = slot >= 0 ? CharacterState.Bag[slot] : null;
            var y = body.y + 92;
            if (item == null)
            {
                _marketSellUid = 0;
                GUI.Label(new Rect(body.x, y, body.width, 24), Loc.T("smith.no_item"), _tiny);
                return;
            }
            var definition = ItemCatalog.Get(item.ItemId);
            var iconRect = new Rect(body.x, y, SlotSize, SlotSize);
            DrawItemIcon(iconRect, item);
            if (iconRect.Contains(Event.current.mousePosition))
            {
                _hoverItem = item;
                _hoverWorn = false;
            }
            GUI.Label(new Rect(body.x + 62, y, body.width - 62, 24),
                item.Upgrade > 0 ? Loc.T("item.with_upgrade", definition.Name, item.Upgrade) : definition.Name, _small);

            // Quantidade (só pilhas) e preço total.
            _marketQuantity = Mathf.Clamp(_marketQuantity, 1, item.Quantity);
            if (item.Quantity > 1)
            {
                GUI.Label(new Rect(body.x + 62, y + 26, 200, 24), Loc.T("market.quantity", _marketQuantity, item.Quantity), _tiny);
                if (GUI.Button(new Rect(body.x + 230, y + 24, 34, 26), "−", _button)) _marketQuantity--;
                if (GUI.Button(new Rect(body.x + 268, y + 24, 34, 26), "+", _button)) _marketQuantity++;
                if (GUI.Button(new Rect(body.x + 306, y + 24, 60, 26), Loc.T("market.max"), _button)) _marketQuantity = item.Quantity;
            }

            y += SlotSize + 20;
            GUI.Label(new Rect(body.x, y + 4, 140, 26), Loc.T("market.price"), _small);
            var typed = GUI.TextField(new Rect(body.x + 140, y, 200, 32), _marketPrice ?? "", 11, _marketInput);
            _marketPrice = Digits(typed);
            GUI.Label(new Rect(body.x + 346, y + 4, 60, 26), "Alz", _small);
            long.TryParse(_marketPrice, out var price);

            y += 44;
            GUI.Label(new Rect(body.x, y, body.width, 40),
                Loc.T("market.fee", MarketRules.FeePercent, MarketRules.Fee(price), MarketRules.SellerGets(price)), _tiny);
            GUI.enabled = MarketRules.ValidPrice(price) && count < MarketRules.MaxListings;
            if (GUI.Button(new Rect(body.x, y + 48, 220, 38), Loc.T("market.put"), _button))
            {
                Send(new Msg.MarketCommand { Action = Msg.MarketActionKind.Sell, Slot = (ushort)slot, Quantity = _marketQuantity, Price = price });
                _marketSellUid = 0;
                _marketPrice = "";
                _marketTab = MarketTab.Mine;
            }
            GUI.enabled = true;
        }

        private static string Digits(string text)
        {
            var digits = new System.Text.StringBuilder();
            foreach (var c in text ?? "") if (c >= '0' && c <= '9') digits.Append(c);
            return digits.ToString();
        }

        private void DrawMarketMine(Rect body, Msg.MarketState state)
        {
            var collectRect = new Rect(body.x, body.yMax - 40, body.width, 38);
            var list = new Rect(body.x, body.y, body.width, body.height - 52);
            if (state == null || state.Mine.Count == 0)
                GUI.Label(new Rect(list.x, list.y + 10, list.width, 26), Loc.T("market.mine_empty"), _tiny);
            else
            {
                const float row = 52f;
                _marketScroll = GUI.BeginScrollView(list, _marketScroll, new Rect(0, 0, list.width - 20, state.Mine.Count * row));
                for (var i = 0; i < state.Mine.Count; i++)
                {
                    var entry = state.Mine[i];
                    var r = new Rect(0, i * row, list.width - 20, row - 4);
                    DrawMarketItem(r, entry);
                    string status;
                    if (entry.State == MarketListingState.Sold)
                        status = $"<color=#7fe07f>{Loc.T("market.state_sold", entry.Buyer, MarketRules.SellerGets(entry.Price))}</color>";
                    else if (entry.Expired) status = $"<color=#ff9966>{Loc.T("market.state_expired")}</color>";
                    else status = Loc.T("market.state_active", Loc.T("market.hours_left", entry.HoursLeft));
                    GUI.Label(new Rect(r.x + 46, r.y + 24, 300, 22), status, _tiny);
                    GUI.Label(new Rect(r.xMax - 210, r.y + 2, 110, 22), $"<color=#ffd25a>{entry.Price:N0}</color>", _small.WithAlignment(TextAnchor.UpperRight));
                    if (entry.State == MarketListingState.Active &&
                        GUI.Button(new Rect(r.xMax - 92, r.y + 6, 90, 32), Loc.T("market.cancel"), _button))
                        Send(new Msg.MarketCommand { Action = Msg.MarketActionKind.Cancel, ListingId = entry.Id });
                }
                GUI.EndScrollView();
            }

            var pending = state != null && (state.ToCollectAlz > 0 || state.ToCollectItems > 0);
            GUI.enabled = pending;
            if (GUI.Button(collectRect, pending ? Loc.T("market.collect", state.ToCollectAlz, state.ToCollectItems) : Loc.T("market.nothing_to_collect"), _button))
                Send(new Msg.MarketCommand { Action = Msg.MarketActionKind.Collect });
            GUI.enabled = true;
        }
    }
}
