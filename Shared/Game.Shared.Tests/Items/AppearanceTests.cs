using Game.Shared.Items;
using Xunit;

namespace Game.Shared.Tests.Items;

public class AppearanceTests
{
    [Theory]
    [InlineData("armadura_ferro", 1)]
    [InlineData("armadura_aco_negro", 2)]
    [InlineData("armadura_astralita", 3)]
    [InlineData("luvas_astralita", 0)]
    [InlineData("espada_ferro", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Grau_da_armadura_vem_so_da_couraca(string? itemId, int grade) =>
        Assert.Equal(grade, Appearance.ArmorGrade(itemId));

    [Fact]
    public void Cor_fora_da_paleta_vira_a_primeira()
    {
        Assert.Equal(3, Appearance.Hair(3));
        Assert.Equal(0, Appearance.Hair(Appearance.HairColors));
        Assert.Equal(0, Appearance.Cloth(-1));
        Assert.Equal(Appearance.ClothColors - 1, Appearance.Cloth(Appearance.ClothColors - 1));
    }
}
