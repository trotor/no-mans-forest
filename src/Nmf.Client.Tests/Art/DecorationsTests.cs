using Nmf.Client.Art;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Art;

public class DecorationsTests
{
    private static readonly Dictionary<DecorationKind, int> Counts = new()
    {
        [DecorationKind.Spruce] = 3, [DecorationKind.Birch] = 3, [DecorationKind.Rock] = 4, [DecorationKind.Bush] = 4,
    };

    private static GridMap ForestMap()
    {
        var map = new GridMap(30, 30, ["none", "grass", "forest"]);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                map[new CellCoord(x, y)] = new CellData(0, x < 15 ? (short)1500 : (short)0, 8, 26, x < 15 ? (ushort)2 : (ushort)1);
        map[new CellCoord(20, 20)] = new CellData(0, 120, 255, 230, 1, CellData.Impassable);
        map[new CellCoord(22, 20)] = new CellData(0, 80, 153, 0, 1, 50);
        return map;
    }

    [Fact]
    public void Place_PutsTreesOnlyInForestAboutOnePerBlock()
    {
        var trees = Decorations.Place(ForestMap(), Counts).Where(d => d.Kind is DecorationKind.Spruce or DecorationKind.Birch).ToList();
        Assert.All(trees, t => Assert.True(t.PositionCm.X < 1500 + 40));
        Assert.Equal(50, trees.Count); // 5 x 10 blocks of 3 x 3 cells inside the forest half, one tree each
        Assert.Contains(trees, t => t.Kind == DecorationKind.Spruce);
        Assert.Contains(trees, t => t.Kind == DecorationKind.Birch);
    }

    [Fact]
    public void Place_PutsRockAndBushOnObstacleCells()
    {
        var all = Decorations.Place(ForestMap(), Counts);
        var rock = Assert.Single(all, d => d.Kind == DecorationKind.Rock);
        var bush = Assert.Single(all, d => d.Kind == DecorationKind.Bush);
        Assert.Equal(new CellCoord(20, 20), rock.PositionCm.ToCell());
        Assert.Equal(new CellCoord(22, 20), bush.PositionCm.ToCell());
    }

    [Fact]
    public void Place_IsDeterministicSortedAndVariantsInRange()
    {
        var a = Decorations.Place(ForestMap(), Counts);
        var b = Decorations.Place(ForestMap(), Counts);
        Assert.Equal(a, b);
        Assert.Equal(a.OrderBy(d => d.PositionCm.Y).ThenBy(d => d.PositionCm.X), a);
        Assert.All(a, d => Assert.InRange(d.Variant, 0, Counts[d.Kind] - 1));
    }
}
