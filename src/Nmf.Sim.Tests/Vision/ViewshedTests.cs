using Nmf.Sim.Core;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class ViewshedTests
{
    private static readonly Vec2 Center = new(1050, 1050); // cell (10,10)

    private static bool[] Compute(GridMap map, int rangeCm = 500)
    {
        var visible = new bool[map.Width * map.Height];
        Viewshed.Compute(map, [(Center, 160)], rangeCm, visible);
        return visible;
    }

    private static bool At(bool[] visible, GridMap map, int x, int y) => visible[y * map.Width + x];

    [Fact]
    public void OpenGround_IsVisibleWithinRangeOnly()
    {
        var map = new GridMap(21, 21, ["none"]);
        var visible = Compute(map);
        Assert.True(At(visible, map, 10, 10));
        Assert.True(At(visible, map, 14, 10));
        Assert.True(At(visible, map, 15, 10));
        Assert.False(At(visible, map, 16, 10));
        Assert.False(At(visible, map, 14, 14)); // about 5.7 m away
    }

    [Fact]
    public void OpaqueWall_HidesCellsBehindIt()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)] = new CellData(0, 300, 255, 230, 0);
        var visible = Compute(map);
        Assert.True(At(visible, map, 11, 10));
        Assert.True(At(visible, map, 12, 10));
        Assert.False(At(visible, map, 13, 10));
    }

    [Fact]
    public void Hill_HidesLowGroundBehindIt()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)].GroundHeightCm = 300;
        var visible = Compute(map);
        Assert.True(At(visible, map, 12, 10));
        Assert.False(At(visible, map, 14, 10));
    }

    [Fact]
    public void LowBushes_DoNotBlockView()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)] = new CellData(0, 80, 255, 0, 0);
        Assert.True(At(Compute(map), map, 14, 10));
    }

    [Fact]
    public void WrongBufferSize_Throws()
    {
        var map = new GridMap(5, 5, ["none"]);
        Assert.Throws<ArgumentException>(() => Viewshed.Compute(map, [], 500, new bool[3]));
    }

    [Fact]
    public void ObserverNearEdge_DoesNotThrow()
    {
        var map = new GridMap(5, 5, ["none"]);
        var visible = new bool[25];
        Viewshed.Compute(map, [(new Vec2(10, 10), 160)], 2000, visible);
        Assert.True(visible[0]);
        Assert.True(visible[24]);
    }

    [Fact]
    public void ChestHighRock_DoesNotHideStandingManBehindIt()
    {
        var map = new GridMap(21, 21, ["none"]);
        map[new CellCoord(12, 10)] = new CellData(0, 120, 255, 230, 0, CellData.Impassable);
        Assert.True(At(Compute(map), map, 13, 10));
    }
}
