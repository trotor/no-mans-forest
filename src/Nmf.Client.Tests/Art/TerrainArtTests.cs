using Nmf.Client.Art;

namespace Nmf.Client.Tests.Art;

public class TerrainArtTests
{
    [Theory]
    [InlineData("grass", 0)]
    [InlineData("forest", 1)]
    [InlineData("swamp", 2)]
    [InlineData("road", 3)]
    [InlineData("water", 4)]
    [InlineData("mud", 0)]
    [InlineData("none", 0)]
    public void SlotFor_MapsKnownNamesAndFallsBackToGrass(string name, int slot)
    {
        Assert.Equal(slot, TerrainArt.SlotFor(name));
    }
}
