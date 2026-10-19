namespace Game.Shared.Items
{
    /// <summary>
    /// Aparência do personagem: a cor do cabelo e a da roupa escolhidas na criação (índices nas paletas do cliente) e o
    /// grau da armadura vestida (0 = sem armadura; 1 Ferro, 2 Aço Negro, 3 Astralita), que muda a cor do metal.
    /// </summary>
    public static class Appearance
    {
        public const int HairColors = 6;
        public const int ClothColors = 6;

        /// <summary>Índice válido de cor de cabelo (fora da paleta vira a primeira).</summary>
        public static int Hair(int index) => index >= 0 && index < HairColors ? index : 0;

        /// <summary>Índice válido de cor de roupa (fora da paleta vira a primeira).</summary>
        public static int Cloth(int index) => index >= 0 && index < ClothColors ? index : 0;

        /// <summary>Grau da armadura vestida (0 sem armadura).</summary>
        public static int ArmorGrade(string? armorId) =>
            ItemCatalog.Find(armorId ?? "") is { Slot: EquipSlot.Armor } armor ? armor.Grade : 0;
    }
}
