using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

public class GridMapTests
{
    private static GridMap NewMap(int w = 4, int h = 3) => new(w, h, ["none", "grass"]);

    [Fact]
    public void NewMap_HasSizeAndZeroedCells()
    {
        var map = NewMap();
        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal(400, map.WidthCm);
        Assert.Equal(300, map.HeightCm);
        Assert.Equal(default(CellData), map[new CellCoord(3, 2)]);
        Assert.Empty(map.Features.Zones);
    }

    [Fact]
    public void Indexer_ReturnsWritableReference()
    {
        var map = NewMap();
        map[new CellCoord(1, 2)] = new CellData(100, 50, 10, 20, 1);
        map[new CellCoord(1, 2)].Cover = 99;

        Assert.Equal(new CellData(100, 50, 10, 99, 1), map[new CellCoord(1, 2)]);
        Assert.Equal(default(CellData), map[new CellCoord(2, 1)]);
    }

    [Fact]
    public void CellAt_ConvertsCentimetresToCell()
    {
        var map = NewMap();
        map[new CellCoord(2, 1)].TerrainId = 1;
        Assert.Equal(1, map.CellAt(new Vec2(250, 199)).TerrainId);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(3, 2, true)]
    [InlineData(4, 2, false)]
    [InlineData(3, 3, false)]
    [InlineData(-1, 0, false)]
    public void InBounds_ChecksCellRange(int x, int y, bool expected)
    {
        Assert.Equal(expected, NewMap().InBounds(new CellCoord(x, y)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(399, 299, true)]
    [InlineData(400, 0, false)]
    [InlineData(0, 300, false)]
    [InlineData(-1, 0, false)]
    public void Contains_ChecksCentimetreRange(int x, int y, bool expected)
    {
        Assert.Equal(expected, NewMap().Contains(new Vec2(x, y)));
    }

    [Fact]
    public void Indexer_OutsideMap_ThrowsWithCoordinates()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewMap()[new CellCoord(4, 0)]);
        Assert.Contains("4", ex.Message);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(4097, 5)]
    public void Constructor_InvalidSize_Throws(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridMap(w, h, ["none"]));
    }

    [Fact]
    public void Constructor_NoTerrainNames_Throws()
    {
        Assert.Throws<ArgumentException>(() => new GridMap(2, 2, []));
    }

    [Fact]
    public void Features_AreKept()
    {
        var zone = new MapZone("start", "zone", new Vec2(0, 0), new Vec2(100, 100));
        var map = new GridMap(2, 2, ["none"], new MapFeatures([zone], [], []));
        Assert.Same(zone, Assert.Single(map.Features.Zones));
    }

    [Fact]
    public void CellData_DefaultIsPassableAtNormalCost()
    {
        var cell = default(CellData);
        Assert.True(cell.IsPassable);
        Assert.Equal(100, cell.MoveCostPct);
    }

    [Fact]
    public void CellData_ImpassableValue_BlocksMovement()
    {
        var cell = new CellData(0, 0, 0, 0, 0, CellData.Impassable);
        Assert.False(cell.IsPassable);
    }
}
