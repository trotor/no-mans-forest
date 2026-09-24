using Nmf.Client;
using Nmf.Sim.Core;

namespace Nmf.Client.Tests;

public class FormationTests
{
    [Fact]
    public void Offsets_StartAtCentreThenNearestRing()
    {
        Assert.Equal(
            new[] { new Vec2(0, 0), new Vec2(0, -200), new Vec2(-200, 0), new Vec2(200, 0), new Vec2(0, 200) },
            Formation.Offsets(5));
    }

    [Fact]
    public void Offsets_ZeroCount_IsEmpty()
    {
        Assert.Empty(Formation.Offsets(0));
    }

    [Fact]
    public void Offsets_AreDistinctForLargeGroups()
    {
        var offsets = Formation.Offsets(30);
        Assert.Equal(30, offsets.Count);
        Assert.Equal(30, offsets.Distinct().Count());
    }
}
