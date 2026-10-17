using System;
using System.Collections.Generic;
using Game.Shared.Combat;
using Game.Shared.Text;

namespace Game.Shared.Loot
{
    /// <summary>Raridade de item, do mais comum ao mais raro.</summary>
    public enum ItemRarity
    {
        Common = 0,
        Uncommon = 1,
        Rare = 2,
        Epic = 3,
    }

    /// <summary>Uma linha da tabela de drop.</summary>
    [Serializable]
    public sealed class DropEntry
    {
        public string ItemId = "";

        /// <summary>Nome do item no idioma do jogador.</summary>
        public string Name => Loc.T("item." + ItemId);

        public ItemRarity Rarity = ItemRarity.Common;

        /// <summary>Chance de cair, de 0 a 1.</summary>
        public double Chance = 0.5;

        public int MinQuantity = 1;
        public int MaxQuantity = 1;

        public DropEntry() { }

        public DropEntry(string itemId, ItemRarity rarity, double chance, int minQuantity = 1, int maxQuantity = 1)
        {
            ItemId = itemId;
            Rarity = rarity;
            Chance = chance;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
        }
    }

    /// <summary>Item que caiu, com quantidade.</summary>
    public readonly struct ItemDrop
    {
        public string ItemId { get; }
        public string Name { get; }
        public ItemRarity Rarity { get; }
        public int Quantity { get; }

        public ItemDrop(string itemId, string name, ItemRarity rarity, int quantity)
        {
            ItemId = itemId;
            Name = name;
            Rarity = rarity;
            Quantity = quantity;
        }
    }

    /// <summary>Resultado de um sorteio de drop.</summary>
    public sealed class LootResult
    {
        public List<ItemDrop> Items { get; } = new List<ItemDrop>();
        public int Alz { get; set; }
    }

    /// <summary>
    /// Tabela de drop de um monstro: cada linha é sorteada de forma independente, mais uma quantia de Alz.
    /// Roda no servidor (decisão) e nos testes; o cliente só mostra o resultado.
    /// </summary>
    [Serializable]
    public sealed class DropTable
    {
        public List<DropEntry> Entries = new List<DropEntry>();
        public int AlzMin;
        public int AlzMax;

        public DropTable() { }

        public DropTable(int alzMin, int alzMax, params DropEntry[] entries)
        {
            AlzMin = alzMin;
            AlzMax = alzMax;
            Entries.AddRange(entries);
        }

        /// <summary>Sorteia o drop.</summary>
        public LootResult Roll(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var result = new LootResult();
            foreach (var entry in Entries)
            {
                if (random.NextDouble() >= entry.Chance) continue;
                var quantity = RollRange(random, entry.MinQuantity, entry.MaxQuantity);
                if (quantity > 0) result.Items.Add(new ItemDrop(entry.ItemId, entry.Name, entry.Rarity, quantity));
            }
            result.Alz = RollRange(random, AlzMin, AlzMax);
            return result;
        }

        /// <summary>Lança <see cref="ArgumentException"/> se algum valor for inválido.</summary>
        public void Validate()
        {
            if (AlzMin < 0 || AlzMax < AlzMin) throw new ArgumentException("Faixa de Alz inválida.", nameof(AlzMin));
            foreach (var entry in Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ItemId)) throw new ArgumentException("Drop sem ItemId.", nameof(Entries));
                if (entry.Chance < 0 || entry.Chance > 1) throw new ArgumentException($"{entry.ItemId}: chance fora de [0, 1].", nameof(Entries));
                if (entry.MinQuantity < 0 || entry.MaxQuantity < entry.MinQuantity)
                    throw new ArgumentException($"{entry.ItemId}: quantidade inválida.", nameof(Entries));
            }
        }

        /// <summary>Inteiro uniforme em [min, max].</summary>
        private static int RollRange(IRandomSource random, int min, int max)
        {
            if (max <= min) return min;
            return min + (int)Math.Floor(random.NextDouble() * (max - min + 1));
        }
    }
}
