using Nmf.Client;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests;

public class UnitNamesTests
{
    [Fact]
    public void For_GivesLeaderRankFirstAndDistinctNames()
    {
        Assert.StartsWith("Alik.", UnitNames.For(Side.Blue, 0));
        var names = Enumerable.Range(0, 8).Select(i => UnitNames.For(Side.Blue, i)).ToList();
        Assert.Equal(8, names.Distinct().Count());
        Assert.NotEqual(UnitNames.For(Side.Blue, 1), UnitNames.For(Side.Red, 1));
    }

    [Fact]
    public void For_BeyondListStillUnique()
    {
        var names = Enumerable.Range(0, 30).Select(i => UnitNames.For(Side.Red, i)).ToList();
        Assert.Equal(30, names.Distinct().Count());
        Assert.Equal(3, UnitNames.PortraitIndex(11));
    }
}
