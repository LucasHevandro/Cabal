using System.Collections.Generic;
using System.Linq;
using Game.Client.Combat;
using Game.Client.Loot;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.World;
using UnityEngine;
using UnityEngine.InputSystem;
using Msg = Game.Shared.Net;

namespace Game.Client.UI
{
    /// <summary>
    /// Janelas do jogo online (IMGUI, mesma tela virtual de 1920x1080 do HUD): inventário com arrastar e soltar,
    /// equipamento, tooltip com comparação, armazém, ferreira, conversa de missões, atributos e rastreador de missões.
    /// Só mostra o que o servidor mandou (<see cref="CharacterState"/>) e envia pedidos; o servidor decide tudo.
    /// </summary>
    public sealed partial class GameWindows : MonoBehaviour
    {
        private const float VirtualWidth = 1920f;
        private const float VirtualHeight = 1080f;
        private const float SlotSize = 50f;
        private const float SlotGap = 4f;
        private const int Columns = 8;

        private static readonly Color Panel = new Color(0.03f, 0.03f, 0.05f, 0.82f);
        private static readonly Color SlotBack = new Color(0.16f, 0.16f, 0.19f, 0.95f);
        private static readonly Color SlotHover = new Color(0.3f, 0.3f, 0.36f, 0.95f);
        private static readonly Color Accent = new Color(1f, 0.82f, 0.35f);
        private static readonly Color Good = new Color(0.45f, 0.95f, 0.45f);
        private static readonly Color Bad = new Color(1f, 0.45f, 0.4f);

        private static readonly (EquipSlot Slot, string Label)[] EquipSlots =
        {
            (EquipSlot.Weapon, "Arma"), (EquipSlot.Armor, "Couraça"), (EquipSlot.Gloves, "Luvas"),
            (EquipSlot.Boots, "Botas"), (EquipSlot.Accessory, "Anel"),
        };

        /// <summary>Retângulos (tela virtual) das janelas abertas no último quadro: o clique ali não vai para o mundo.</summary>
        private static readonly List<Rect> OpenRects = new List<Rect>();

        private static float _scale = 1f;
        private static Vector2 _offset;

        private readonly Dictionary<string, string> _monsterNames = new Dictionary<string, string>();
        private GUIStyle _label;
        private GUIStyle _small;
        private GUIStyle _tiny;
        private GUIStyle _button;

        private bool _inventoryOpen;
        private bool _attributesOpen;
        private bool _questLogOpen;
        private Vector2 _questScroll;

        // Arrastar
        private Msg.ItemContainerKind _dragFrom;
        private int _dragSlot = -1;
        private Vector2 _dragStart;
        private bool _dragging;

        private long _discardUid;
        private int _discardSlot = -1;

        // Ferreira: item e pedra escolhidos (pelo id único, que sobrevive às atualizações do inventário)
        private long _smithItemUid;
        private long _smithStoneUid;

        // Atributos ainda não confirmados
        private int _addStr, _addDex, _addInt;

        // Tooltip do quadro atual
        private ItemInstance _hoverItem;
        private bool _hoverWorn;

        /// <summary>O ponteiro está sobre alguma janela (o clique não deve selecionar alvo nem girar a câmera).</summary>
        public static bool PointerOverUi
        {
            get
            {
                var mouse = Mouse.current;
                if (mouse == null) return false;
                var screen = mouse.position.ReadValue();
                var point = new Vector2((screen.x - _offset.x) / _scale, (Screen.height - screen.y - _offset.y) / _scale);
                foreach (var rect in OpenRects) if (rect.Contains(point)) return true;
                return SocialWindows.Contains(point);
            }
        }

        private static PlayerCombat Player => PlayerCombat.Local;

        public static GameWindows Instance { get; private set; }

        // Usados pelo teste automático (DevTools/OnlineSmokeTest) para abrir janelas sem teclado.
        public void SetInventoryOpen(bool open) => _inventoryOpen = open;
        public void SetAttributesOpen(bool open) => _attributesOpen = open;
        public void SetQuestLogOpen(bool open) => _questLogOpen = open;
        public void SelectForSmith(long uid) => _smithItemUid = uid;
        public void TalkToNearestNpc() => TalkToNearest();

        private void Awake()
        {
            if (!GameMode.IsOnline)
            {
                enabled = false;
                return;
            }
            foreach (var spawn in WorldContent.Spawns()) _monsterNames[spawn.Data.Id] = spawn.Data.Name;
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable() => OpenRects.Clear();

        private void Update()
        {
            var keyboard = GameInput.Keyboard;
            if (keyboard == null || ConnectMenu.IsOpen) return;

            if (keyboard.iKey.wasPressedThisFrame) _inventoryOpen = !_inventoryOpen;
            if (keyboard.cKey.wasPressedThisFrame) _attributesOpen = !_attributesOpen;
            if (keyboard.jKey.wasPressedThisFrame) _questLogOpen = !_questLogOpen;
            if (keyboard.tKey.wasPressedThisFrame) TalkToNearest();
            if (keyboard.escapeKey.wasPressedThisFrame) CloseAll();

            // Afastou-se do NPC: a conversa fecha.
            var dialog = CharacterState.Dialog;
            if (dialog != null && Player != null)
            {
                var npc = NpcCatalog.Find(dialog.NpcId);
                if (npc == null || !NearNpc(npc, NpcCatalog.InteractRange + 1.5f)) CharacterState.CloseDialog();
            }
        }

        private void CloseAll()
        {
            _inventoryOpen = _attributesOpen = _questLogOpen = false;
            _discardSlot = -1;
            CharacterState.CloseDialog();
        }

        private void TalkToNearest()
        {
            var npc = NearestNpc();
            if (npc == null)
            {
                if (Player != null) Player.ShowNotice("Nenhum NPC por perto");
                return;
            }
            Send(new Msg.TalkToNpc { NpcId = npc.Id });
        }

        private static NpcDefinition NearestNpc()
        {
            if (Player == null) return null;
            NpcDefinition best = null;
            var bestDistance = float.MaxValue;
            foreach (var npc in NpcCatalog.All)
            {
                var distance = Vector2.Distance(new Vector2(npc.X, npc.Z), new Vector2(Player.transform.position.x, Player.transform.position.z));
                if (distance > NpcCatalog.InteractRange || distance >= bestDistance) continue;
                best = npc;
                bestDistance = distance;
            }
            return best;
        }

        private static bool NearNpc(NpcDefinition npc, float range)
        {
            var p = Player.transform.position;
            return (new Vector2(npc.X - p.x, npc.Z - p.z)).sqrMagnitude <= range * range;
        }

        private static void Send(Msg.INetMessage message)
        {
            if (NetworkSession.Instance != null) NetworkSession.Instance.Send(message);
        }

        // ===== Desenho =====

        private void OnGUI()
        {
            if (Player == null || ConnectMenu.IsOpen) return;
            GUI.depth = -10; // por cima do HUD (plaquinhas dos monstros e barras)
            EnsureStyles();
            OpenRects.Clear();
            _hoverItem = null;

            DrawNpcLabels();

            _scale = Mathf.Min(Screen.width / VirtualWidth, Screen.height / VirtualHeight);
            _offset = new Vector2((Screen.width - VirtualWidth * _scale) / 2, (Screen.height - VirtualHeight * _scale) / 2);
            GUI.matrix = Matrix4x4.TRS(new Vector3(_offset.x, _offset.y, 0), Quaternion.identity, new Vector3(_scale, _scale, 1));

            var dialog = CharacterState.Dialog;
            var npc = dialog != null ? NpcCatalog.Find(dialog.NpcId) : null;
            var warehouseOpen = npc != null && npc.Role == NpcRole.Warehouse;
            var smithOpen = npc != null && npc.Role == NpcRole.Smith;
            var shopOpen = npc != null && npc.Role == NpcRole.Merchant && !Trading;
            var inventoryShown = _inventoryOpen || warehouseOpen || smithOpen || shopOpen || Trading;

            if (!inventoryShown && !_questLogOpen && Dungeon == null) DrawQuestTracker();
            if (inventoryShown) DrawInventory();
            if (warehouseOpen) DrawWarehouse();
            if (smithOpen) DrawSmith(npc);
            if (shopOpen) DrawShop(npc);
            if (Trading) DrawTrade();
            DrawTradeInvite();
            if (npc != null && npc.Role == NpcRole.QuestGiver) DrawQuestDialog(npc, dialog);
            if (npc != null && npc.Role == NpcRole.DungeonPortal) DrawPortal(npc);
            DrawDungeonHud();
            if (_attributesOpen) DrawAttributes();
            if (_questLogOpen) DrawQuestLog();
            DrawTalkPrompt();
            if (_discardSlot >= 0) DrawDiscardConfirm();

            HandleDrop(inventoryShown, warehouseOpen, smithOpen, shopOpen);
            if (_dragging) DrawDragged();
            else if (_hoverItem != null) DrawTooltip(_hoverItem, _hoverWorn);

            GUI.matrix = Matrix4x4.identity;
            GUI.color = Color.white;
        }

        private Rect Window(Rect rect, string title)
        {
            OpenRects.Add(rect);
            Box(rect, Panel);
            GUI.Label(new Rect(rect.x + 14, rect.y + 8, rect.width - 28, 30), $"<b>{title}</b>", _label);
            return rect;
        }

        // ----- Inventário e equipamento -----

        private Rect InventoryRect => new Rect(VirtualWidth - 480, 120, 460, 560);

        private void DrawInventory()
        {
            var rect = Window(InventoryRect, "Inventário  <size=14>(I fecha · botão direito usa/veste)</size>");

            // Equipamento
            var x = rect.x + 14;
            var y = rect.y + 46;
            for (var i = 0; i < EquipSlots.Length; i++)
            {
                var (slot, label) = EquipSlots[i];
                var slotRect = new Rect(x + i * (SlotSize + 34), y + 18, SlotSize + 26, SlotSize + 6);
                GUI.Label(new Rect(slotRect.x, y, slotRect.width, 18), label, _tiny.WithCenter());
                DrawSlot(slotRect, CharacterState.Equipment[slot], Msg.ItemContainerKind.Equipment, (int)slot, worn: true);
            }

            // Bolsa
            y += 92;
            DrawGrid(CharacterState.Bag, Msg.ItemContainerKind.Bag, new Vector2(x, y));

            var sheet = CharacterState.Sheet;
            var footer = rect.yMax - 58;
            var alz = Player.GetComponent<PlayerLoot>();
            GUI.Label(new Rect(x, footer, 220, 24), $"<b>{(alz != null ? alz.Alz : 0):N0}</b> Alz", _small);
            if (sheet != null)
                GUI.Label(new Rect(x, footer + 22, rect.width - 28, 24),
                    $"Ataque {sheet.AttackPower} · Magia {sheet.MagicPower} · Defesa {sheet.Defense} · Crítico {sheet.CritPercent:0.#}%", _tiny);

            // Lixeira: soltar um item aqui pede confirmação.
            var trash = TrashRect;
            Box(trash, new Color(0.35f, 0.08f, 0.08f, _dragging ? 0.9f : 0.5f));
            GUI.Label(trash, "Jogar fora", _tiny.WithCenter());
        }

        private Rect TrashRect => new Rect(InventoryRect.xMax - 110, InventoryRect.yMax - 58, 96, 40);

        private void DrawGrid(ItemContainer container, Msg.ItemContainerKind kind, Vector2 origin)
        {
            for (var slot = 0; slot < container.Size; slot++)
            {
                var rect = SlotRect(origin, slot);
                DrawSlot(rect, container[slot], kind, slot, worn: false);
            }
        }

        private static Rect SlotRect(Vector2 origin, int slot) =>
            new Rect(origin.x + slot % Columns * (SlotSize + SlotGap), origin.y + slot / Columns * (SlotSize + SlotGap), SlotSize, SlotSize);

        private void DrawSlot(Rect rect, ItemInstance item, Msg.ItemContainerKind kind, int slot, bool worn)
        {
            var e = Event.current;
            var hover = rect.Contains(e.mousePosition);
            var dragged = _dragging && _dragFrom == kind && _dragSlot == slot;
            Box(rect, hover ? SlotHover : SlotBack);
            if (item != null && !dragged) DrawItemIcon(rect, item);
            if (item != null && item.Uid == _smithItemUid && _smithItemUid != 0) Outline(rect, Accent);

            if (!hover) return;
            if (item != null && !_dragging)
            {
                _hoverItem = item;
                _hoverWorn = worn;
            }

            if (e.type == EventType.MouseDown && item != null)
            {
                if (e.button == 0)
                {
                    _dragFrom = kind;
                    _dragSlot = slot;
                    _dragStart = e.mousePosition;
                    _dragging = false;
                    e.Use();
                }
                else if (e.button == 1)
                {
                    QuickAction(item, kind, slot);
                    e.Use();
                }
            }
        }

        private void DrawItemIcon(Rect rect, ItemInstance item)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            if (definition == null) return;
            var color = LootPickup.ColorFor(definition.Rarity);
            Box(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), new Color(color.r * 0.35f, color.g * 0.35f, color.b * 0.35f, 1f));
            Outline(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), color);
            GUI.Label(new Rect(rect.x + 2, rect.y + 4, rect.width - 4, rect.height - 18), ShortName(definition), _tiny.WithCenter());
            if (item.Upgrade > 0) GUI.Label(new Rect(rect.x + 4, rect.yMax - 20, rect.width, 18), $"<b>+{item.Upgrade}</b>", _tiny);
            if (item.Quantity > 1)
                GUI.Label(new Rect(rect.x, rect.yMax - 20, rect.width - 5, 18), item.Quantity.ToString(), _tiny.WithAlignment(TextAnchor.UpperRight));
            if (!string.IsNullOrEmpty(item.OptionId)) Box(new Rect(rect.xMax - 10, rect.y + 5, 5, 5), Accent);
        }

        private static string ShortName(ItemDefinition definition)
        {
            var word = definition.Name.Split(' ')[0];
            return word.Length > 7 ? word.Substring(0, 6) + "." : word;
        }

        /// <summary>Botão direito: veste, tira, usa poção; com armazém aberto, guarda/retira; na ferreira, escolhe.</summary>
        private void QuickAction(ItemInstance item, Msg.ItemContainerKind kind, int slot)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            var dialog = CharacterState.Dialog;
            var npc = dialog != null ? NpcCatalog.Find(dialog.NpcId) : null;

            if (Trading && kind == Msg.ItemContainerKind.Bag)
            {
                AddToOffer(slot);
                return;
            }
            if (npc != null && npc.Role == NpcRole.Merchant && kind == Msg.ItemContainerKind.Bag)
            {
                _sellUid = item.Uid;
                return;
            }
            if (npc != null && npc.Role == NpcRole.Smith && kind != Msg.ItemContainerKind.Warehouse)
            {
                if (definition.IsOptionStone) _smithStoneUid = item.Uid;
                else if (definition.Kind == ItemKind.Equipment) _smithItemUid = item.Uid;
                return;
            }
            if (npc != null && npc.Role == NpcRole.Warehouse && kind != Msg.ItemContainerKind.Equipment)
            {
                var to = kind == Msg.ItemContainerKind.Bag ? Msg.ItemContainerKind.Warehouse : Msg.ItemContainerKind.Bag;
                var target = to == Msg.ItemContainerKind.Bag ? CharacterState.Bag : CharacterState.Warehouse;
                var free = FirstFree(target);
                if (free >= 0) SendMove(kind, slot, to, free);
                return;
            }

            switch (kind)
            {
                case Msg.ItemContainerKind.Equipment:
                    var bagSlot = FirstFree(CharacterState.Bag);
                    if (bagSlot >= 0) SendMove(kind, slot, Msg.ItemContainerKind.Bag, bagSlot);
                    else Player.ShowNotice("Bolsa cheia");
                    break;
                case Msg.ItemContainerKind.Bag when definition.Kind == ItemKind.Equipment:
                    SendMove(kind, slot, Msg.ItemContainerKind.Equipment, (int)definition.Slot);
                    break;
                case Msg.ItemContainerKind.Bag when definition.Kind == ItemKind.Consumable:
                    Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Use, From = kind, FromSlot = (ushort)slot });
                    break;
            }
        }

        private static int FirstFree(ItemContainer container)
        {
            for (var i = 0; i < container.Size; i++) if (container[i] == null) return i;
            return -1;
        }

        private static void SendMove(Msg.ItemContainerKind from, int fromSlot, Msg.ItemContainerKind to, int toSlot) =>
            Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Move, From = from, FromSlot = (ushort)fromSlot, To = to, ToSlot = (ushort)toSlot });

        /// <summary>Solta o item arrastado: em outra posição, no equipamento, no armazém, na ferreira ou na lixeira.</summary>
        private void HandleDrop(bool inventoryShown, bool warehouseOpen, bool smithOpen, bool shopOpen)
        {
            var e = Event.current;
            if (_dragSlot < 0) return;
            if (e.type == EventType.MouseDrag && !_dragging && (e.mousePosition - _dragStart).sqrMagnitude > 36) _dragging = true;
            if (e.type != EventType.MouseUp || e.button != 0) return;

            var from = _dragFrom;
            var fromSlot = _dragSlot;
            var wasDragging = _dragging;
            _dragSlot = -1;
            _dragging = false;
            e.Use();
            if (!wasDragging) return;

            var point = e.mousePosition;
            var item = ItemAtSlot(from, fromSlot);
            if (item == null) return;

            if (inventoryShown)
            {
                var bagOrigin = new Vector2(InventoryRect.x + 14, InventoryRect.y + 138);
                for (var slot = 0; slot < CharacterState.Bag.Size; slot++)
                {
                    if (!SlotRect(bagOrigin, slot).Contains(point)) continue;
                    if (from != Msg.ItemContainerKind.Bag || slot != fromSlot) SendMove(from, fromSlot, Msg.ItemContainerKind.Bag, slot);
                    return;
                }
                for (var i = 0; i < EquipSlots.Length; i++)
                {
                    var rect = new Rect(InventoryRect.x + 14 + i * (SlotSize + 34), InventoryRect.y + 64, SlotSize + 26, SlotSize + 6);
                    if (!rect.Contains(point)) continue;
                    if (from == Msg.ItemContainerKind.Bag) SendMove(from, fromSlot, Msg.ItemContainerKind.Equipment, (int)EquipSlots[i].Slot);
                    return;
                }
                if (TrashRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
                {
                    _discardSlot = fromSlot;
                    _discardUid = item.Uid;
                    return;
                }
            }
            if (warehouseOpen && from != Msg.ItemContainerKind.Equipment)
            {
                for (var slot = 0; slot < CharacterState.Warehouse.Size; slot++)
                {
                    if (!SlotRect(WarehouseOrigin, slot).Contains(point)) continue;
                    if (from != Msg.ItemContainerKind.Warehouse || slot != fromSlot) SendMove(from, fromSlot, Msg.ItemContainerKind.Warehouse, slot);
                    return;
                }
            }
            if (Trading && TradeRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
            {
                AddToOffer(fromSlot);
                return;
            }
            if (shopOpen && ShopRect.Contains(point) && from == Msg.ItemContainerKind.Bag)
            {
                _sellUid = item.Uid;
                return;
            }
            if (smithOpen && SmithRect.Contains(point) && from != Msg.ItemContainerKind.Warehouse)
            {
                var definition = ItemCatalog.Get(item.ItemId);
                if (definition.IsOptionStone) _smithStoneUid = item.Uid;
                else if (definition.Kind == ItemKind.Equipment) _smithItemUid = item.Uid;
            }
        }

        private static ItemInstance ItemAtSlot(Msg.ItemContainerKind kind, int slot) => kind switch
        {
            Msg.ItemContainerKind.Bag => CharacterState.Bag[slot],
            Msg.ItemContainerKind.Warehouse => CharacterState.Warehouse[slot],
            _ => CharacterState.Equipment[(EquipSlot)slot],
        };

        private void DrawDragged()
        {
            var item = ItemAtSlot(_dragFrom, _dragSlot);
            if (item == null) return;
            var p = Event.current.mousePosition;
            DrawItemIcon(new Rect(p.x - SlotSize / 2, p.y - SlotSize / 2, SlotSize, SlotSize), item);
        }

        private void DrawDiscardConfirm()
        {
            var item = CharacterState.Bag[_discardSlot];
            if (item == null || item.Uid != _discardUid)
            {
                _discardSlot = -1;
                return;
            }
            var definition = ItemCatalog.Get(item.ItemId);
            var rect = Window(new Rect(VirtualWidth / 2 - 220, VirtualHeight / 2 - 80, 440, 150), "Jogar fora?");
            var name = item.Upgrade > 0 ? $"{definition.Name} +{item.Upgrade}" : definition.Name;
            GUI.Label(new Rect(rect.x + 14, rect.y + 44, rect.width - 28, 50),
                $"{name}{(item.Quantity > 1 ? $" x{item.Quantity}" : "")} some para sempre.", _small);
            if (GUI.Button(new Rect(rect.x + 60, rect.yMax - 50, 140, 36), "Jogar fora", _button))
            {
                Send(new Msg.ItemCommand { Action = Msg.ItemActionKind.Discard, From = Msg.ItemContainerKind.Bag, FromSlot = (ushort)_discardSlot });
                _discardSlot = -1;
            }
            if (GUI.Button(new Rect(rect.xMax - 200, rect.yMax - 50, 140, 36), "Cancelar", _button)) _discardSlot = -1;
        }

        // ----- Tooltip -----

        private void DrawTooltip(ItemInstance item, bool worn)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            if (definition == null) return;
            var lines = new List<string>();
            var color = ColorUtility.ToHtmlStringRGB(LootPickup.ColorFor(definition.Rarity));
            lines.Add($"<b><color=#{color}>{definition.Name}{(item.Upgrade > 0 ? $" +{item.Upgrade}" : "")}</color></b>");

            if (definition.Kind == ItemKind.Equipment)
            {
                var sheetLevel = Player.Level;
                lines.Add($"{ItemCatalog.GradeName(definition.Grade)} · {SlotName(definition.Slot)}");
                var levelColor = sheetLevel >= definition.RequiredLevel ? "ffffff" : "ff7766";
                lines.Add($"<color=#{levelColor}>Nível {definition.RequiredLevel}</color>" +
                          (definition.RequiredClass.HasValue
                              ? $" · <color=#{(definition.RequiredClass.Value == CharacterState.Class ? "ffffff" : "ff7766")}>{AccountRules.ClassDisplayName(definition.RequiredClass.Value)}</color>"
                              : ""));
                lines.Add(StatCalculator.ItemStats(item).Describe());
                if (!string.IsNullOrEmpty(item.OptionId))
                    lines.Add($"<color=#ffd25a>Opção: {ItemCatalog.Find(item.OptionId)?.Name}</color>");

                // Comparação com o que está vestido na mesma posição.
                var current = CharacterState.Equipment[definition.Slot];
                if (!worn && current != null && current.Uid != item.Uid)
                {
                    var delta = StatCalculator.ItemStats(item) + StatCalculator.ItemStats(current) * -1;
                    lines.Add("");
                    lines.Add("<i>Comparado ao vestido:</i>");
                    lines.Add(delta.IsEmpty ? "igual" : ColoredDelta(delta));
                }
                else if (!worn && current == null)
                {
                    lines.Add("");
                    lines.Add("<color=#77ee77>Posição vazia: só ganha.</color>");
                }
            }
            else if (definition.IsOptionStone)
            {
                lines.Add($"Pedra de opção: {definition.OptionStats.Describe()}");
                lines.Add("Use na ferreira (1 por item).");
            }
            else if (!string.IsNullOrEmpty(definition.Description))
            {
                lines.Add(definition.Description);
            }
            if (item.Quantity > 1) lines.Add($"Quantidade: {item.Quantity}");

            var text = string.Join("\n", lines);
            var size = _small.CalcSize(new GUIContent(text.Replace("<b>", "").Replace("</b>", "")));
            var width = Mathf.Clamp(size.x + 30, 220, 460);
            var height = _small.CalcHeight(new GUIContent(text), width - 24) + 20;
            var p = Event.current.mousePosition;
            var rect = new Rect(p.x + 18, p.y + 18, width, height);
            if (rect.xMax > VirtualWidth) rect.x = p.x - width - 12;
            if (rect.yMax > VirtualHeight) rect.y = VirtualHeight - height - 4;
            Box(rect, new Color(0.02f, 0.02f, 0.03f, 0.95f));
            Outline(rect, LootPickup.ColorFor(definition.Rarity));
            GUI.Label(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 16), text, _small);
        }

        private static string ColoredDelta(StatBlock delta)
        {
            var parts = delta.Describe().Split(new[] { ", " }, System.StringSplitOptions.RemoveEmptyEntries);
            return string.Join(", ", parts.Select(p => p.StartsWith("-") ? $"<color=#ff7766>{p}</color>" : $"<color=#77ee77>{p}</color>"));
        }

        private static string SlotName(EquipSlot slot) => EquipSlots.First(s => s.Slot == slot).Label;

        // ----- Armazém -----

        private Vector2 WarehouseOrigin => new Vector2(InventoryRect.x - 470 + 14, InventoryRect.y + 46);

        private void DrawWarehouse()
        {
            Window(new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 340), "Armazém  <size=14>(arraste entre as janelas)</size>");
            DrawGrid(CharacterState.Warehouse, Msg.ItemContainerKind.Warehouse, WarehouseOrigin);
        }

        // ----- Ferreira -----

        private Rect SmithRect => new Rect(InventoryRect.x - 470, InventoryRect.y, 460, 470);

        private void DrawSmith(NpcDefinition npc)
        {
            var rect = Window(SmithRect, $"{npc.Name} — melhoria");
            var x = rect.x + 14;
            var y = rect.y + 44;
            GUI.Label(new Rect(x, y, rect.width - 28, 40), "Arraste um equipamento para cá (ou botão direito nele).", _tiny);

            var (item, container, slot) = FindByUid(_smithItemUid);
            var itemRect = new Rect(x, y + 30, SlotSize + 10, SlotSize + 10);
            Box(itemRect, SlotBack);
            if (item == null)
            {
                _smithItemUid = 0;
                GUI.Label(new Rect(x + 74, y + 40, 300, 40), "Nenhum item escolhido", _small);
            }
            else
            {
                DrawItemIcon(itemRect, item);
                if (itemRect.Contains(Event.current.mousePosition))
                {
                    _hoverItem = item;
                    _hoverWorn = container == Msg.ItemContainerKind.Equipment;
                }
                DrawUpgradeInfo(item, container, slot, new Rect(x + 74, y + 28, rect.width - 102, 260));
            }

            // Pedra de opção
            var stoneY = rect.yMax - 120;
            Box(new Rect(x, stoneY - 8, rect.width - 28, 1), new Color(1, 1, 1, 0.2f));
            var (stone, _, stoneSlot) = FindByUid(_smithStoneUid);
            if (stone == null) _smithStoneUid = 0;
            GUI.Label(new Rect(x, stoneY, rect.width - 28, 24), "<b>Opção</b> (pedra + equipamento sem opção)", _small);
            GUI.Label(new Rect(x, stoneY + 26, 300, 24),
                stone == null ? "Escolha uma pedra (botão direito)" : $"{ItemCatalog.Get(stone.ItemId).Name}: {ItemCatalog.Get(stone.ItemId).OptionStats.Describe()}", _tiny);
            GUI.enabled = stone != null && item != null && string.IsNullOrEmpty(item.OptionId);
            if (GUI.Button(new Rect(rect.xMax - 164, stoneY + 52, 150, 36), "Aplicar pedra", _button) && item != null)
            {
                Send(new Msg.ApplyOption { Container = container, Slot = (ushort)slot, StoneSlot = (ushort)stoneSlot });
                _smithStoneUid = 0;
            }
            GUI.enabled = true;
        }

        private void DrawUpgradeInfo(ItemInstance item, Msg.ItemContainerKind container, int slot, Rect area)
        {
            var definition = ItemCatalog.Get(item.ItemId);
            var y = area.y;
            GUI.Label(new Rect(area.x, y, area.width, 26), $"<b>{definition.Name} +{item.Upgrade}</b>", _label);
            y += 30;
            if (item.Upgrade >= UpgradeRules.MaxLevel)
            {
                GUI.Label(new Rect(area.x, y, area.width, 24), "No máximo (+10).", _small);
                return;
            }

            var guaranteed = UpgradeRules.IsGuaranteed(item);
            var chance = guaranteed ? 1 : UpgradeRules.SuccessChance(item.Upgrade);
            GUI.Label(new Rect(area.x, y, area.width, 24),
                $"+{item.Upgrade} → +{item.Upgrade + 1}: <b>{chance * 100:0}%</b>{(guaranteed ? " <color=#77ee77>(garantida)</color>" : "")}", _small);
            y += 24;
            GUI.Label(new Rect(area.x, y, area.width, 24),
                item.Upgrade >= UpgradeRules.DowngradeFrom ? "<color=#ff9977>Falhar perde 1 nível (recuperar é garantido)</color>" : "Falhar mantém o nível", _tiny);
            y += 22;

            // Barra de proteção contra azar
            var limit = UpgradeRules.PityLimit(item.Upgrade);
            if (limit > 0)
            {
                var progress = UpgradeRules.PityProgress(item);
                var bar = new Rect(area.x, y + 4, area.width, 12);
                Box(bar, SlotBack);
                Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(progress / (float)limit), bar.height), Accent);
                GUI.Label(new Rect(area.x, y + 16, area.width, 20), $"Proteção: {progress}/{limit} falhas (cheia = garantida)", _tiny);
                y += 40;
            }

            // Custo
            var cost = UpgradeRules.CostFor(definition, item.Upgrade);
            var have = CharacterState.Bag.Count(cost.CoreId);
            var alz = Player.GetComponent<PlayerLoot>()?.Alz ?? 0;
            GUI.Label(new Rect(area.x, y, area.width, 22),
                $"<color=#{(have >= cost.Cores ? "ffffff" : "ff7766")}>{cost.Cores}x {ItemCatalog.Get(cost.CoreId).Name} (tem {have})</color>", _tiny);
            y += 20;
            GUI.Label(new Rect(area.x, y, area.width, 22),
                $"<color=#{(alz >= cost.Alz ? "ffffff" : "ff7766")}>{cost.Alz:N0} Alz</color>", _tiny);
            y += 28;

            var next = item.Clone();
            next.Upgrade++;
            GUI.Label(new Rect(area.x, y, area.width, 40), $"Próximo nível: {StatCalculator.ItemStats(next).Describe()}", _tiny);
            y += 40;

            GUI.enabled = have >= cost.Cores && alz >= cost.Alz;
            if (GUI.Button(new Rect(area.x, y, 160, 38), "<b>Melhorar</b>", _button))
                Send(new Msg.UpgradeItem { Container = container, Slot = (ushort)slot });
            GUI.enabled = true;

            var result = CharacterState.LastUpgrade;
            var age = Time.unscaledTime - CharacterState.LastUpgradeAt;
            if (result != null && age < 4f && (result.Uid == item.Uid || result.Uid == 0))
            {
                var resultColor = result.Outcome == (byte)UpgradeOutcome.Success ? Good : result.Outcome == (byte)UpgradeOutcome.Refused ? Color.white : Bad;
                GUI.color = new Color(resultColor.r, resultColor.g, resultColor.b, 1f - Mathf.Clamp01((age - 3f)));
                GUI.Label(new Rect(area.x + 170, y - 4, area.width - 170, 48), $"<b>{result.Message}</b>", _small);
                GUI.color = Color.white;
            }
        }

        private static (ItemInstance Item, Msg.ItemContainerKind Container, int Slot) FindByUid(long uid)
        {
            if (uid == 0) return (null, Msg.ItemContainerKind.Bag, -1);
            var bagSlot = CharacterState.Bag.SlotOf(uid);
            if (bagSlot >= 0) return (CharacterState.Bag[bagSlot], Msg.ItemContainerKind.Bag, bagSlot);
            foreach (var worn in CharacterState.Equipment.Worn)
                if (worn.Value.Uid == uid) return (worn.Value, Msg.ItemContainerKind.Equipment, (int)worn.Key);
            return (null, Msg.ItemContainerKind.Bag, -1);
        }

        // ----- Missões -----

        private void DrawQuestDialog(NpcDefinition npc, Msg.NpcDialog dialog)
        {
            // A altura acompanha o conteúdo (medida no quadro anterior).
            var rect = Window(new Rect(480, 160, 560, Mathf.Clamp(_dialogHeight, 160, 760)), npc.Name);
            var y = rect.y + 46;
            var x = rect.x + 14;
            var width = rect.width - 28;

            if (dialog.Offered.Count == 0 && dialog.ReadyToTurnIn.Count == 0)
            {
                var next = QuestCatalog.All.FirstOrDefault(q => q.GiverNpcId == npc.Id && !CharacterState.QuestCompleted(q.Id)
                    && CharacterState.CountsOf(q.Id) == null);
                var active = CharacterState.Quests.Active.Count > 0;
                var text = active ? "\"Termine o que pedi e volte aqui.\""
                    : next != null ? $"\"Volte quando chegar ao nível {next.MinLevel}, aventureiro.\""
                    : "\"Você já fez tudo o que eu podia pedir. Obrigado!\"";
                GUI.Label(new Rect(x, y, width, 60), text, _small);
                y += 50;
            }

            foreach (var questId in dialog.ReadyToTurnIn)
            {
                var quest = QuestCatalog.Find(questId);
                if (quest == null) continue;
                y = QuestBlock(quest, x, y, width, ready: true);
                if (GUI.Button(new Rect(x, y, 180, 34), "<b>Entregar</b>", _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.TurnIn, QuestId = questId });
                y += 48;
            }
            foreach (var questId in dialog.Offered)
            {
                var quest = QuestCatalog.Find(questId);
                if (quest == null) continue;
                y = QuestBlock(quest, x, y, width, ready: false);
                if (GUI.Button(new Rect(x, y, 180, 34), "Aceitar", _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.Accept, QuestId = questId });
                y += 48;
            }

            if (GUI.Button(new Rect(rect.xMax - 134, rect.yMax - 48, 120, 34), "Fechar", _button)) CharacterState.CloseDialog();
            if (Event.current.type == EventType.Repaint) _dialogHeight = y - rect.y + 60;
        }

        private float _dialogHeight = 300;

        private float QuestBlock(QuestDefinition quest, float x, float y, float width, bool ready)
        {
            GUI.Label(new Rect(x, y, width, 26), $"<b><color=#ffd25a>{quest.Name}</color></b>  <size=14>Nv {quest.MinLevel}</size>", _label);
            y += 28;
            var description = _small.CalcHeight(new GUIContent(quest.Description), width);
            GUI.Label(new Rect(x, y, width, description), quest.Description, _small);
            y += description + 4;
            var counts = CharacterState.CountsOf(quest.Id);
            for (var i = 0; i < quest.Objectives.Count; i++)
            {
                var done = counts != null && i < counts.Count ? counts[i] : 0;
                GUI.Label(new Rect(x + 10, y, width - 10, 22), "· " + ObjectiveText(quest.Objectives[i], done, counts != null), _tiny);
                y += 20;
            }
            GUI.Label(new Rect(x, y, width, 22), $"Recompensa: {RewardText(quest)}", _tiny);
            y += 22;
            if (!string.IsNullOrEmpty(quest.Unlocks))
            {
                GUI.Label(new Rect(x, y, width, 22), $"<color=#77ee77>{quest.Unlocks}</color>", _tiny);
                y += 22;
            }
            return y + 4;
        }

        private string ObjectiveText(QuestObjective objective, int done, bool showProgress)
        {
            var progress = showProgress ? $" {Mathf.Min(done, objective.Count)}/{objective.Count}" : (objective.Count > 1 ? $" x{objective.Count}" : "");
            var text = objective.Kind switch
            {
                QuestObjectiveKind.Kill => $"Derrote {(_monsterNames.TryGetValue(objective.TargetId, out var name) ? name : objective.TargetId)}",
                QuestObjectiveKind.Collect => $"Traga {ItemCatalog.Find(objective.TargetId)?.Name ?? objective.TargetId}",
                _ => $"Fale com {NpcCatalog.Find(objective.TargetId)?.Name ?? objective.TargetId}",
            };
            var complete = showProgress && done >= objective.Count;
            return complete ? $"<color=#77ee77>{text}{progress} (ok)</color>" : text + progress;
        }

        private static string RewardText(QuestDefinition quest)
        {
            var parts = new List<string>();
            if (quest.RewardXp > 0) parts.Add($"{quest.RewardXp:N0} XP");
            if (quest.RewardAlz > 0) parts.Add($"{quest.RewardAlz:N0} Alz");
            parts.AddRange(quest.RewardItems.Select(i => $"{ItemCatalog.Find(i.ItemId)?.Name ?? i.ItemId} x{i.Quantity}"));
            return string.Join(", ", parts);
        }

        /// <summary>Missões aceitas no canto da tela, com o andamento.</summary>
        private void DrawQuestTracker()
        {
            var active = CharacterState.Quests.Active;
            if (active.Count == 0) return;
            var x = VirtualWidth - 470f;
            var y = 104f;
            var height = 40f + active.Sum(q => 28f + 20f * (QuestCatalog.Find(q.QuestId)?.Objectives.Count ?? 0));
            Box(new Rect(x, y, 450, height), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(x + 12, y + 6, 420, 24), "<b>Missões</b>  <size=13>(J abre o diário)</size>", _small);
            y += 34;
            foreach (var entry in active)
            {
                var quest = QuestCatalog.Find(entry.QuestId);
                if (quest == null) continue;
                GUI.Label(new Rect(x + 12, y, 420, 24), $"<color=#ffd25a>{quest.Name}</color>", _small);
                y += 24;
                for (var i = 0; i < quest.Objectives.Count; i++)
                {
                    var done = i < entry.Counts.Count ? entry.Counts[i] : 0;
                    GUI.Label(new Rect(x + 22, y, 410, 20), ObjectiveText(quest.Objectives[i], done, true), _tiny);
                    y += 20;
                }
                y += 4;
            }
        }

        private void DrawQuestLog()
        {
            var rect = Window(new Rect(480, 160, 560, 640), "Diário de missões  <size=14>(J fecha)</size>");
            var x = rect.x + 14;
            var width = rect.width - 44;
            var active = CharacterState.Quests.Active;
            var contentHeight = 60f + active.Count * 200f;
            _questScroll = GUI.BeginScrollView(new Rect(x, rect.y + 44, rect.width - 20, rect.height - 100), _questScroll,
                new Rect(0, 0, width, contentHeight));
            var y = 0f;
            if (active.Count == 0)
            {
                var next = QuestCatalog.All.FirstOrDefault(q => !CharacterState.QuestCompleted(q.Id));
                GUI.Label(new Rect(0, y, width, 60),
                    next == null ? "Todas as missões foram concluídas!" : $"Nenhuma missão aceita. Fale com o {NpcCatalog.Find(next.GiverNpcId)?.Name} na vila (T).", _small);
            }
            foreach (var entry in active)
            {
                var quest = QuestCatalog.Find(entry.QuestId);
                if (quest == null) continue;
                y = QuestBlock(quest, 0, y, width, ready: false);
                if (GUI.Button(new Rect(0, y, 140, 30), "Abandonar", _button))
                    Send(new Msg.QuestCommand { Action = Msg.QuestActionKind.Abandon, QuestId = quest.Id });
                y += 44;
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(x, rect.yMax - 44, width, 30),
                $"Concluídas: {CharacterState.Quests.Completed.Count} de {QuestCatalog.All.Count}", _small);
        }

        // ----- Atributos -----

        private void DrawAttributes()
        {
            var sheet = CharacterState.Sheet;
            var rect = Window(new Rect(20, 340, 420, 400), "Atributos  <size=14>(C fecha)</size>");
            if (sheet == null) return;
            var x = rect.x + 14;
            var y = rect.y + 46;
            var pending = _addStr + _addDex + _addInt;
            var free = sheet.FreePoints - pending;

            GUI.Label(new Rect(x, y, 390, 24), $"{AccountRules.ClassDisplayName((CharacterClass)sheet.Class)} · Nv {Player.Level}", _small);
            y += 28;
            GUI.Label(new Rect(x, y, 390, 24), $"Pontos livres: <b>{free}</b>", _small);
            y += 34;
            y = AttributeRow(x, y, "Força", sheet.Strength, ref _addStr, free, "+2 ataque, +5 HP");
            y = AttributeRow(x, y, "Destreza", sheet.Dexterity, ref _addDex, free, "+1 defesa, +0,2% crítico");
            y = AttributeRow(x, y, "Inteligência", sheet.Intelligence, ref _addInt, free, "+2 magia, +4 MP");

            GUI.enabled = pending > 0;
            if (GUI.Button(new Rect(x, y + 6, 160, 34), "Confirmar", _button))
            {
                Send(new Msg.AllocateAttributes { Strength = (ushort)_addStr, Dexterity = (ushort)_addDex, Intelligence = (ushort)_addInt });
                _addStr = _addDex = _addInt = 0;
            }
            if (GUI.Button(new Rect(x + 172, y + 6, 120, 34), "Limpar", _button)) _addStr = _addDex = _addInt = 0;
            GUI.enabled = true;
            y += 54;

            GUI.Label(new Rect(x, y, 390, 90),
                $"Ataque <b>{sheet.AttackPower}</b> · Magia <b>{sheet.MagicPower}</b>\n" +
                $"Defesa <b>{sheet.Defense}</b> · Crítico <b>{sheet.CritPercent:0.#}%</b>\n" +
                $"HP {Player.Hp.Max} · MP {Player.Mp.Max}", _small);
        }

        private float AttributeRow(float x, float y, string name, int value, ref int add, int free, string hint)
        {
            GUI.Label(new Rect(x, y, 140, 26), name, _small);
            GUI.Label(new Rect(x + 130, y, 90, 26), add > 0 ? $"<b>{value + add}</b> <color=#77ee77>(+{add})</color>" : $"<b>{value}</b>", _small);
            GUI.enabled = free > 0;
            if (GUI.Button(new Rect(x + 250, y, 36, 28), "+", _button)) add++;
            GUI.enabled = add > 0;
            if (GUI.Button(new Rect(x + 292, y, 36, 28), "−", _button)) add--;
            GUI.enabled = true;
            GUI.Label(new Rect(x + 10, y + 26, 380, 20), hint, _tiny);
            return y + 52;
        }

        // ----- NPCs no mundo -----

        private void DrawTalkPrompt()
        {
            if (CharacterState.Dialog != null) return;
            var npc = NearestNpc();
            if (npc == null) return;
            var hasQuest = npc.Role == NpcRole.QuestGiver;
            GUI.Label(new Rect(0, VirtualHeight - 340, VirtualWidth, 30),
                $"<b>T</b>: falar com {npc.Name}{(npc.Role == NpcRole.Smith ? " (melhoria)" : npc.Role == NpcRole.Warehouse ? " (armazém)" : npc.Role == NpcRole.Merchant ? " (loja)" : npc.Role == NpcRole.DungeonPortal ? " (dungeon)" : hasQuest ? " (missões)" : "")}",
                _label.WithCenter());
        }

        /// <summary>Nome em cima de cada NPC (coordenadas de tela, antes da escala).</summary>
        private void DrawNpcLabels()
        {
            var cam = Camera.main;
            var session = NetworkSession.Instance;
            if (cam == null || session == null) return;
            foreach (var npc in NpcCatalog.All)
            {
                if (!session.NpcViews.TryGetValue(npc.Id, out var view) || view == null) continue;
                if ((view.position - Player.transform.position).sqrMagnitude > 40f * 40f) continue;
                var screen = cam.WorldToScreenPoint(view.position + Vector3.up * 2.6f);
                if (screen.z <= 0) continue;
                // Não escreve por cima das janelas do chat e de amigos (coordenadas virtuais do quadro anterior).
                if (SocialWindows.Contains(new Vector2((screen.x - _offset.x) / _scale, (Screen.height - screen.y - _offset.y) / _scale))) continue;
                var role = npc.Role switch { NpcRole.Smith => "Ferreira", NpcRole.Warehouse => "Armazém", NpcRole.Merchant => "Loja", NpcRole.DungeonPortal => "Dungeon", _ => "Missões" };
                var rect = new Rect(screen.x - 150, Screen.height - screen.y - 20, 300, 40);
                GUI.color = new Color(0, 0, 0, 0.8f);
                GUI.Label(new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height), $"<b>{npc.Name}</b>\n<size=13>{role}</size>", _small.WithCenter());
                GUI.color = Accent;
                GUI.Label(rect, $"<b>{npc.Name}</b>\n<size=13>{role}</size>", _small.WithCenter());
                GUI.color = Color.white;
            }
        }

        // ----- Utilidades -----

        private static void Box(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Outline(Rect rect, Color color)
        {
            Box(new Rect(rect.x, rect.y, rect.width, 2), color);
            Box(new Rect(rect.x, rect.yMax - 2, rect.width, 2), color);
            Box(new Rect(rect.x, rect.y, 2, rect.height), color);
            Box(new Rect(rect.xMax - 2, rect.y, 2, rect.height), color);
        }

        private void EnsureStyles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, wordWrap = true };
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(_label) { fontSize = 16 };
            _tiny = new GUIStyle(_label) { fontSize = 13 };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true };
        }
    }
}
