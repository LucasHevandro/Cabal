using System;
using System.Collections.Generic;
using Game.Shared.Accounts;
using Game.Shared.Items;
using UnityEngine;
using Msg = Game.Shared.Net;

namespace Game.Client.Net
{
    /// <summary>
    /// Modo online: o personagem como o servidor descreveu por último — itens, ficha, missões, a conversa aberta com
    /// um NPC e o último resultado da ferreira. As janelas só leem daqui e mandam pedidos; quem decide é o servidor.
    /// </summary>
    public static class CharacterState
    {
        /// <summary>Algo mudou (as janelas podem redesenhar).</summary>
        public static event Action Changed;

        public static ItemContainer Bag { get; private set; } = new ItemContainer();
        public static ItemContainer Warehouse { get; private set; } = new ItemContainer();
        public static Equipment Equipment { get; private set; } = new Equipment();

        public static Msg.CharacterSheet Sheet { get; private set; }
        public static Msg.QuestState Quests { get; private set; } = new Msg.QuestState();

        /// <summary>Conversa aberta com um NPC (nulo = nenhuma).</summary>
        public static Msg.NpcDialog Dialog { get; private set; }

        public static Msg.UpgradeResult LastUpgrade { get; private set; }

        /// <summary>Troca aberta (nulo ou Active falso = nenhuma) e pedido de troca esperando resposta.</summary>
        public static Msg.TradeState Trade { get; private set; }

        private static Msg.TradeInvite _tradeInvite;
        private static float _tradeInviteUntil;

        /// <summary>PvP (fila, duelo ou arena) e o desafio de duelo esperando resposta.</summary>
        public static Msg.PvpState Pvp { get; private set; }

        private static Msg.DuelInvite _duelInvite;
        private static float _duelInviteUntil;

        public static Msg.DuelInvite PendingDuelInvite => Time.unscaledTime < _duelInviteUntil ? _duelInvite : null;

        public static void Apply(Msg.PvpState state)
        {
            Pvp = state;
            Changed?.Invoke();
        }

        public static void Apply(Msg.DuelInvite invite)
        {
            _duelInvite = invite;
            _duelInviteUntil = Time.unscaledTime + invite.Seconds;
            Changed?.Invoke();
        }

        public static void ClearDuelInvite() => _duelInvite = null;

        /// <summary>Dungeon em que está (nulo ou inativo = fora) e o último ranking recebido.</summary>
        public static Msg.DungeonState Dungeon { get; private set; }
        public static Msg.DungeonInfo DungeonRanking { get; private set; }

        public static void Apply(Msg.DungeonState state)
        {
            Dungeon = state.Active ? state : null;
            Changed?.Invoke();
        }

        public static void Apply(Msg.DungeonInfo info)
        {
            DungeonRanking = info;
            Changed?.Invoke();
        }

        public static Msg.TradeInvite PendingTradeInvite => Time.unscaledTime < _tradeInviteUntil ? _tradeInvite : null;
        public static float LastUpgradeAt { get; private set; } = float.NegativeInfinity;

        public static CharacterClass Class => Sheet != null ? (CharacterClass)Sheet.Class : CharacterClass.Blade;
        public static bool HasSheet => Sheet != null;

        /// <summary>Volta ao começo (entrar de novo no mundo).</summary>
        public static void Reset()
        {
            Bag = new ItemContainer();
            Warehouse = new ItemContainer();
            Equipment = new Equipment();
            Sheet = null;
            Quests = new Msg.QuestState();
            Dialog = null;
            LastUpgrade = null;
            LastUpgradeAt = float.NegativeInfinity;
            Trade = null;
            _tradeInvite = null;
            Dungeon = null;
            DungeonRanking = null;
            Pvp = null;
            _duelInvite = null;
        }

        public static void Apply(Msg.TradeState trade)
        {
            Trade = trade;
            Changed?.Invoke();
        }

        public static void Apply(Msg.TradeInvite invite)
        {
            _tradeInvite = invite;
            _tradeInviteUntil = Time.unscaledTime + invite.Seconds;
            Changed?.Invoke();
        }

        public static void ClearTradeInvite() => _tradeInvite = null;

        public static void Apply(Msg.Inventory inventory)
        {
            var bag = new ItemContainer();
            var warehouse = new ItemContainer();
            var equipment = new Equipment();
            foreach (var stack in inventory.Items)
            {
                if (ItemCatalog.Find(stack.ItemId) == null) continue;
                var item = new ItemInstance
                {
                    Uid = stack.Uid, ItemId = stack.ItemId, Quantity = stack.Quantity, Upgrade = stack.Upgrade,
                    PityFails = stack.PityFails, PityLevel = stack.PityLevel, RegainGuaranteed = stack.RegainGuaranteed,
                    OptionId = stack.OptionId ?? "",
                };
                switch (stack.Container)
                {
                    case Msg.ItemContainerKind.Bag: bag.TryPut(stack.Slot, item); break;
                    case Msg.ItemContainerKind.Warehouse: warehouse.TryPut(stack.Slot, item); break;
                    case Msg.ItemContainerKind.Equipment: equipment.Put((EquipSlot)stack.Slot, item); break;
                }
            }
            Bag = bag;
            Warehouse = warehouse;
            Equipment = equipment;
            Changed?.Invoke();
        }

        public static void Apply(Msg.CharacterSheet sheet)
        {
            Sheet = sheet;
            Changed?.Invoke();
        }

        public static void Apply(Msg.QuestState quests)
        {
            Quests = quests;
            Changed?.Invoke();
        }

        public static void Apply(Msg.NpcDialog dialog)
        {
            Dialog = dialog;
            Changed?.Invoke();
        }

        public static void Apply(Msg.UpgradeResult result)
        {
            LastUpgrade = result;
            LastUpgradeAt = Time.unscaledTime;
            Changed?.Invoke();
        }

        public static void CloseDialog()
        {
            Dialog = null;
            Changed?.Invoke();
        }

        public static bool SkillUnlocked(int slot) =>
            Sheet == null || (slot >= 0 && slot < Sheet.SkillUnlocked.Count && Sheet.SkillUnlocked[slot]);

        /// <summary>Contadores de uma missão aceita (nulo se não está aceita).</summary>
        public static List<int> CountsOf(string questId)
        {
            foreach (var quest in Quests.Active)
                if (quest.QuestId == questId) return quest.Counts;
            return null;
        }

        public static bool QuestCompleted(string questId) => Quests.Completed.Contains(questId);
    }
}
