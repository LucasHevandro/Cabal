using System;
using System.Collections.Generic;
using Game.Shared.Accounts;
using Game.Shared.Loot;

namespace Game.Shared.Items
{
    public enum ItemKind
    {
        Material = 0,
        Consumable = 1,
        Equipment = 2,
    }

    public enum EquipSlot
    {
        Weapon = 0,
        Armor = 1,
        Gloves = 2,
        Boots = 3,
        Accessory = 4,
    }

    /// <summary>Atributos de combate somáveis (base do nível + atributos + equipamento + opção).</summary>
    public struct StatBlock
    {
        /// <summary>Ataque físico (skills físicas).</summary>
        public int AttackPower;

        /// <summary>Ataque mágico (skills mágicas).</summary>
        public int MagicPower;

        public int Defense;
        public int MaxHp;
        public int MaxMp;

        /// <summary>Chance de crítico em pontos percentuais (10 = 10%).</summary>
        public float CritPercent;

        public static StatBlock operator +(StatBlock a, StatBlock b) => new StatBlock
        {
            AttackPower = a.AttackPower + b.AttackPower,
            MagicPower = a.MagicPower + b.MagicPower,
            Defense = a.Defense + b.Defense,
            MaxHp = a.MaxHp + b.MaxHp,
            MaxMp = a.MaxMp + b.MaxMp,
            CritPercent = a.CritPercent + b.CritPercent,
        };

        public static StatBlock operator *(StatBlock a, int n) => new StatBlock
        {
            AttackPower = a.AttackPower * n,
            MagicPower = a.MagicPower * n,
            Defense = a.Defense * n,
            MaxHp = a.MaxHp * n,
            MaxMp = a.MaxMp * n,
            CritPercent = a.CritPercent * n,
        };

        public bool IsEmpty => AttackPower == 0 && MagicPower == 0 && Defense == 0 && MaxHp == 0 && MaxMp == 0 && CritPercent == 0;

        /// <summary>Texto curto para tooltips: "+25 ataque, +100 HP".</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (AttackPower != 0) parts.Add($"{AttackPower:+0;-0} ataque");
            if (MagicPower != 0) parts.Add($"{MagicPower:+0;-0} magia");
            if (Defense != 0) parts.Add($"{Defense:+0;-0} defesa");
            if (MaxHp != 0) parts.Add($"{MaxHp:+0;-0} HP");
            if (MaxMp != 0) parts.Add($"{MaxMp:+0;-0} MP");
            if (CritPercent != 0) parts.Add($"{CritPercent:+0.#;-0.#}% crítico");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Definição de um tipo de item (os dados; o item que o jogador tem é um <see cref="ItemInstance"/>).</summary>
    public sealed class ItemDefinition
    {
        public string Id = "";
        public string Name = "";
        public ItemKind Kind;
        public ItemRarity Rarity;
        public int MaxStack = 1;

        // Equipamento
        public EquipSlot Slot;

        /// <summary>Grau do material: 1 Ferro, 2 Aço Negro, 3 Astralita. Define o núcleo de melhoria.</summary>
        public int Grade = 1;

        public int RequiredLevel = 1;

        /// <summary>Só esta classe pode usar (nulo = todas).</summary>
        public CharacterClass? RequiredClass;

        public StatBlock Stats;

        /// <summary>Quanto cada +1 de melhoria soma.</summary>
        public StatBlock UpgradeStats;

        // Materiais especiais
        /// <summary>Núcleo de melhoria: o grau de equipamento que ele melhora (0 = não é núcleo).</summary>
        public int UpgradeCoreGrade;

        /// <summary>Pedra de opção: o que soma ao ser colocada num item.</summary>
        public StatBlock OptionStats;

        public bool IsOptionStone => !OptionStats.IsEmpty;

        // Consumíveis
        public int HealHp;

        public string Description = "";
    }

    /// <summary>Um item que existe no jogo (no inventário, equipado ou no armazém).</summary>
    [Serializable]
    public sealed class ItemInstance
    {
        /// <summary>Identificador único dado pelo servidor (rastreia o item nos registros e impede duplicação).</summary>
        public long Uid;

        public string ItemId = "";
        public int Quantity = 1;

        /// <summary>Nível de melhoria (+0 a +10).</summary>
        public int Upgrade;

        /// <summary>Falhas tentando passar do nível <see cref="PityLevel"/>: ao chegar no limite, a próxima é garantida.</summary>
        public int PityFails;

        /// <summary>Nível em que as falhas de <see cref="PityFails"/> aconteceram.</summary>
        public int PityLevel;

        /// <summary>Caiu de nível numa falha: a próxima tentativa (recuperar o nível) é garantida.</summary>
        public bool RegainGuaranteed;

        /// <summary>Pedra de opção colocada (vazio = slot livre).</summary>
        public string OptionId = "";

        public ItemInstance Clone() => (ItemInstance)MemberwiseClone();
    }

    /// <summary>Todos os itens do MVP. Fonte única para servidor e cliente; números provisórios até a planilha.</summary>
    public static class ItemCatalog
    {
        public const string CoreLow = "nucleo_baixo";
        public const string CoreMedium = "nucleo_medio";
        public const string CoreHigh = "nucleo_alto";

        private static readonly Dictionary<string, ItemDefinition> Items = Build();

        public static IEnumerable<ItemDefinition> All => Items.Values;

        public static ItemDefinition? Find(string id) => Items.TryGetValue(id ?? "", out var item) ? item : null;

        public static ItemDefinition Get(string id) =>
            Find(id) ?? throw new KeyNotFoundException($"Item desconhecido: {id}");

        public static string GradeName(int grade) => grade switch
        {
            1 => "Ferro",
            2 => "Aço Negro",
            3 => "Astralita",
            _ => "?",
        };

        /// <summary>Núcleo que melhora equipamento do grau informado.</summary>
        public static string CoreForGrade(int grade) => grade switch
        {
            1 => CoreLow,
            2 => CoreMedium,
            _ => CoreHigh,
        };

        private static Dictionary<string, ItemDefinition> Build()
        {
            var list = new List<ItemDefinition>();

            // ===== Armas da Lâmina =====
            list.Add(Weapon("espada_ferro", "Espada de Ferro", 1, 1, CharacterClass.Blade, atk: 25, perLevel: 4));
            list.Add(Weapon("espada_aco_negro", "Espada de Aço Negro", 2, 10, CharacterClass.Blade, atk: 60, perLevel: 8));
            list.Add(Weapon("espada_astralita", "Espada de Astralita", 3, 20, CharacterClass.Blade, atk: 110, perLevel: 14));

            // ===== Armas do Arcanista =====
            list.Add(Weapon("cajado_ferro", "Cajado de Ferro", 1, 1, CharacterClass.Arcanist, magic: 25, perLevel: 4));
            list.Add(Weapon("cajado_aco_negro", "Cajado de Aço Negro", 2, 10, CharacterClass.Arcanist, magic: 60, perLevel: 8));
            list.Add(Weapon("cajado_astralita", "Cajado de Astralita", 3, 20, CharacterClass.Arcanist, magic: 110, perLevel: 14));

            // ===== Armaduras e acessórios (todas as classes) =====
            var grades = new[] { (1, 1, "ferro", "de Ferro"), (2, 10, "aco_negro", "de Aço Negro"), (3, 20, "astralita", "de Astralita") };
            foreach (var (grade, level, key, suffix) in grades)
            {
                var g = grade;
                list.Add(Gear($"armadura_{key}", $"Couraça {suffix}", EquipSlot.Armor, g, level,
                    new StatBlock { Defense = 20 * g * g, MaxHp = 100 * g * g },
                    new StatBlock { Defense = 3 * g, MaxHp = 20 * g }));
                list.Add(Gear($"luvas_{key}", $"Luvas {suffix}", EquipSlot.Gloves, g, level,
                    new StatBlock { Defense = 6 * g * g, CritPercent = 1 * g },
                    new StatBlock { Defense = 1 * g, CritPercent = 0.2f }));
                list.Add(Gear($"botas_{key}", $"Botas {suffix}", EquipSlot.Boots, g, level,
                    new StatBlock { Defense = 6 * g * g, MaxHp = 40 * g * g },
                    new StatBlock { Defense = 1 * g, MaxHp = 10 * g }));
                list.Add(Gear($"anel_{key}", $"Anel {suffix}", EquipSlot.Accessory, g, level,
                    new StatBlock { MaxHp = 60 * g * g, MaxMp = 40 * g * g },
                    new StatBlock { MaxHp = 15 * g, MaxMp = 10 * g }));
            }

            // ===== Núcleos de melhoria =====
            list.Add(Core(CoreLow, "Núcleo de melhoria (baixo)", 1, ItemRarity.Rare));
            list.Add(Core(CoreMedium, "Núcleo de melhoria (médio)", 2, ItemRarity.Rare));
            list.Add(Core(CoreHigh, "Núcleo de melhoria (alto)", 3, ItemRarity.Rare));

            // ===== Pedras de opção (1 por item) =====
            list.Add(Stone("pedra_vigor", "Pedra do Vigor", new StatBlock { MaxHp = 150 }));
            list.Add(Stone("pedra_furia", "Pedra da Fúria", new StatBlock { AttackPower = 15, MagicPower = 15 }));
            list.Add(Stone("pedra_precisao", "Pedra da Precisão", new StatBlock { CritPercent = 3 }));

            // ===== Consumíveis =====
            list.Add(new ItemDefinition
            {
                Id = "pocao_hp_p", Name = "Poção de vida (P)", Kind = ItemKind.Consumable, Rarity = ItemRarity.Common,
                MaxStack = 50, HealHp = 400, Description = "Recupera 400 de vida.",
            });
            list.Add(new ItemDefinition
            {
                Id = "pocao_hp_m", Name = "Poção de vida (M)", Kind = ItemKind.Consumable, Rarity = ItemRarity.Common,
                MaxStack = 50, HealHp = 1200, Description = "Recupera 1.200 de vida.",
            });

            // ===== Materiais dos monstros (vendáveis e para missões) =====
            foreach (var (id, name, rarity) in new[]
                     {
                         ("pelo_macio", "Pelo macio", ItemRarity.Common), ("pele_lobo", "Pele de lobo", ItemRarity.Common),
                         ("presa_lobo", "Presa de lobo", ItemRarity.Uncommon), ("couro_javali", "Couro de javali", ItemRarity.Common),
                         ("seda", "Seda de aranha", ItemRarity.Common), ("veneno", "Glândula de veneno", ItemRarity.Uncommon),
                         ("fragmento_pedra", "Fragmento de pedra viva", ItemRarity.Common), ("essencia", "Essência espectral", ItemRarity.Uncommon),
                         ("fragmento_guardiao", "Fragmento do Guardião", ItemRarity.Epic),
                     })
            {
                list.Add(new ItemDefinition { Id = id, Name = name, Kind = ItemKind.Material, Rarity = rarity, MaxStack = 99 });
            }

            var result = new Dictionary<string, ItemDefinition>();
            foreach (var item in list) result.Add(item.Id, item);
            return result;
        }

        private static ItemDefinition Weapon(string id, string name, int grade, int level, CharacterClass characterClass,
            int atk = 0, int magic = 0, int perLevel = 0) => new ItemDefinition
        {
            Id = id, Name = name, Kind = ItemKind.Equipment, Slot = EquipSlot.Weapon, Grade = grade, RequiredLevel = level,
            RequiredClass = characterClass, Rarity = grade == 3 ? ItemRarity.Rare : grade == 2 ? ItemRarity.Uncommon : ItemRarity.Common,
            Stats = new StatBlock { AttackPower = atk, MagicPower = magic },
            UpgradeStats = new StatBlock { AttackPower = atk > 0 ? perLevel : 0, MagicPower = magic > 0 ? perLevel : 0 },
        };

        private static ItemDefinition Gear(string id, string name, EquipSlot slot, int grade, int level, StatBlock stats, StatBlock perLevel) =>
            new ItemDefinition
            {
                Id = id, Name = name, Kind = ItemKind.Equipment, Slot = slot, Grade = grade, RequiredLevel = level,
                Rarity = grade == 3 ? ItemRarity.Rare : grade == 2 ? ItemRarity.Uncommon : ItemRarity.Common,
                Stats = stats, UpgradeStats = perLevel,
            };

        private static ItemDefinition Core(string id, string name, int grade, ItemRarity rarity) => new ItemDefinition
        {
            Id = id, Name = name, Kind = ItemKind.Material, Rarity = rarity, MaxStack = 99, UpgradeCoreGrade = grade,
            Description = $"Melhora equipamentos de {GradeName(grade)}.",
        };

        private static ItemDefinition Stone(string id, string name, StatBlock stats) => new ItemDefinition
        {
            Id = id, Name = name, Kind = ItemKind.Material, Rarity = ItemRarity.Rare, MaxStack = 99, OptionStats = stats,
            Description = $"Coloque num equipamento sem opção: {stats.Describe()}.",
        };
    }
}
