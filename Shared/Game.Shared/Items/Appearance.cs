namespace Game.Shared.Items
{
    /// <summary>
    /// Aparência do personagem: a cor do cabelo e a da roupa, o penteado (a cabeça de um dos modelos) e o tom de pele
    /// escolhidos na criação (índices nas paletas do cliente), se o elmo aparece (muda a qualquer hora) e o grau da
    /// couraça, das luvas e das botas vestidas (0 = nada; 1 Ferro, 2 Aço Negro, 3 Astralita), que muda a cor do metal do
    /// corpo, dos braços e das pernas.
    /// </summary>
    public static class Appearance
    {
        public const int HairColors = 6;
        public const int ClothColors = 6;

        /// <summary>Penteados: 0 = o da classe; os outros são as cabeças dos modelos de aventureiro.</summary>
        public const int Heads = 6;

        public const int SkinTones = 5;

        /// <summary>Índice válido de penteado (fora da lista vira o da classe).</summary>
        public static int Head(int index) => index >= 0 && index < Heads ? index : 0;

        /// <summary>Índice válido de tom de pele (fora da paleta vira o original).</summary>
        public static int Skin(int index) => index >= 0 && index < SkinTones ? index : 0;

        /// <summary>Índice válido de cor de cabelo (fora da paleta vira a primeira).</summary>
        public static int Hair(int index) => index >= 0 && index < HairColors ? index : 0;

        /// <summary>Índice válido de cor de roupa (fora da paleta vira a primeira).</summary>
        public static int Cloth(int index) => index >= 0 && index < ClothColors ? index : 0;

        /// <summary>Grau da couraça vestida (0 sem couraça).</summary>
        public static int ArmorGrade(string? armorId) => GradeOf(armorId, EquipSlot.Armor);

        /// <summary>Grau da peça se ela for do tipo da casa (0 se vazia ou de outro tipo).</summary>
        public static int GradeOf(string? itemId, EquipSlot slot) =>
            ItemCatalog.Find(itemId ?? "") is { } item && item.Slot == slot && IsWearable(item) ? item.Grade : 0;

        private static bool IsWearable(ItemDefinition item) =>
            item.Slot == EquipSlot.Armor || item.Slot == EquipSlot.Gloves || item.Slot == EquipSlot.Boots;
    }
}
