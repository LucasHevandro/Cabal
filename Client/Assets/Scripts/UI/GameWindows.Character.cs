using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Text;
using Msg = Game.Shared.Net;
using UnityEngine;

namespace Game.Client.UI
{
    /// <summary>
    /// Janela do personagem (P, e junto com o inventário): o modelo 3D com a armadura, as cores e a arma vestidas, as
    /// cinco peças de equipamento em volta, mostrar o elmo, ataque, magia, defesa, crítico e os atributos.
    /// Arrastar uma peça da bolsa para a janela veste; arrastar uma vestida para a bolsa tira.
    /// </summary>
    public sealed partial class GameWindows
    {
        private const float CharacterWidth = 420f;
        private const float CharacterHeight = 500f;
        private const float EquipWidth = SlotSize + 26;
        private const float EquipHeight = SlotSize + 6;

        // Peças em volta do modelo: arma, couraça e luvas à esquerda; botas e anel à direita.
        private static readonly (int Column, int Row)[] EquipLayout = { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1) };

        private bool _characterOpen;
        private float _characterX;
        private CharacterPreview _characterPreview;

        public void SetCharacterOpen(bool open) => _characterOpen = open;

        /// <summary>Ao lado do inventário ou, com uma janela de NPC aberta (loja, ferreira...), ao lado dela.</summary>
        private Rect CharacterRect => new Rect(_characterX, InventoryRect.y, CharacterWidth, CharacterHeight);

        private Rect EquipSlotRect(int index)
        {
            var (column, row) = EquipLayout[index];
            var rect = CharacterRect;
            var x = column == 0 ? rect.x + 14 : rect.xMax - 14 - EquipWidth;
            return new Rect(x, rect.y + 74 + row * (EquipHeight + 30), EquipWidth, EquipHeight);
        }

        /// <summary>A janela mais à esquerda já aberta ao lado do inventário (a do personagem vai para a esquerda dela).</summary>
        private float LeftOfOpenWindows(bool npcWindowOpen, float npcWindowX) =>
            (npcWindowOpen ? npcWindowX : InventoryRect.x) - CharacterWidth - 10;

        private void DrawCharacter()
        {
            var rect = Window(CharacterRect, Loc.T("char.title", GameMode.PlayerName, KeyBindings.Label(GameAction.Character)));
            var sheet = CharacterState.Sheet;
            var combat = Player;
            if (sheet != null)
                GUI.Label(new Rect(rect.x, rect.y + 44, rect.width, 22),
                    Loc.T("char.subtitle", AccountRules.ClassDisplayName((CharacterClass)sheet.Class), combat != null ? combat.Level : 1), _tiny.WithCenter());

            // O modelo no meio, girando devagar, com o que está vestido.
            var preview = new Rect(rect.x + 14 + EquipWidth + 10, rect.y + 70, rect.width - 2 * (14 + EquipWidth + 10), 300);
            if (combat != null)
            {
                _characterPreview ??= new CharacterPreview(new Vector3(0f, 420f, 60f));
                _characterPreview.Show(CharacterState.Class, combat.LookStyle, combat.LookGear, combat.LookWeapon, combat.LookUpgrade);
                if (_characterPreview.Texture != null) GUI.DrawTexture(preview, _characterPreview.Texture, ScaleMode.ScaleAndCrop, false);
                UiTheme.Frame(preview);
            }

            for (var i = 0; i < EquipSlots.Length; i++)
            {
                var (slot, label) = EquipSlots[i];
                var slotRect = EquipSlotRect(i);
                GUI.Label(new Rect(slotRect.x, slotRect.y - 20, slotRect.width, 18), Loc.T(label), _tiny.WithCenter());
                DrawSlot(slotRect, CharacterState.Equipment[slot], Msg.ItemContainerKind.Equipment, (int)slot, worn: true);
            }

            var x = rect.x + 18;
            var y = preview.yMax + 14;
            // Mostrar o elmo (o chapéu, no Arcanista): online o servidor guarda e avisa quem vê; offline troca na hora.
            if (combat != null)
            {
                var show = UiTheme.Toggle(new Rect(preview.x, y, preview.width, 24), !combat.HelmetHidden, " " + Loc.T("inv.show_helmet"), _tiny);
                if (show == combat.HelmetHidden)
                {
                    if (NetworkSession.Instance != null) Send(new Msg.AppearanceCommand { HideHelmet = !show });
                    else combat.SetHelmetHidden(!show);
                }
            }
            y += 36;
            UiTheme.Divider(new Rect(rect.x + 10, y, rect.width - 20, 2));
            y += 10;
            if (sheet == null) return;
            GUI.Label(new Rect(x, y, rect.width - 36, 24), Loc.T("inv.stats", sheet.AttackPower, sheet.MagicPower, sheet.Defense, sheet.CritPercent), _small);
            GUI.Label(new Rect(x, y + 28, rect.width - 36, 24), Loc.T("char.attributes", sheet.Strength, sheet.Dexterity, sheet.Intelligence), _small);
            if (sheet.FreePoints > 0)
                GUI.Label(new Rect(x, y + 56, rect.width - 36, 24),
                    $"<color=#ffd25a>{Loc.T("char.free_points", sheet.FreePoints, KeyBindings.Label(GameAction.Attributes))}</color>", _tiny);
        }

        /// <summary>Soltar uma peça da bolsa em qualquer lugar da janela do personagem: veste no lugar dela.</summary>
        private bool DropOnCharacter(Vector2 point, Msg.ItemContainerKind from, int fromSlot, ItemInstance item)
        {
            if (!CharacterRect.Contains(point)) return false;
            for (var i = 0; i < EquipSlots.Length; i++)
            {
                if (!EquipSlotRect(i).Contains(point)) continue;
                if (from == Msg.ItemContainerKind.Bag) SendMove(from, fromSlot, Msg.ItemContainerKind.Equipment, (int)EquipSlots[i].Slot);
                return true;
            }
            var definition = ItemCatalog.Find(item.ItemId);
            if (from == Msg.ItemContainerKind.Bag && definition != null && definition.Kind == ItemKind.Equipment)
                SendMove(from, fromSlot, Msg.ItemContainerKind.Equipment, (int)definition.Slot);
            return true;
        }

        private void HideCharacterPreview()
        {
            _characterPreview?.Hide();
            _characterPreview = null;
        }
    }
}
