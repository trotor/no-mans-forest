using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

public class PathfinderTests
{
    private static GridMap OpenMap(int w, int h) => new(w, h, ["none"]);

    private static void Block(GridMap map, int x, int y) => map[new CellCoord(x, y)].ExtraMoveCost = CellData.Impassable;

    /// <summary>Samples every path segment each 10 cm and fails if any sample lies in an impassable cell.</summary>
    private static void AssertWalkable(GridMap map, Vec2 start, IReadOnlyList<Vec2> path)
    {
        var from = start;
        foreach (var to in path)
        {
            var d = to - from;
            int steps = Math.Max(1, d.Length / 10);
            for (int i = 0; i <= steps; i++)
            {
                var p = new Vec2(from.X + (int)((long)d.X * i / steps), from.Y + (int)((long)d.Y * i / steps));
                Assert.True(map.CellAt(p).IsPassable || p.ToCell() == start.ToCell(), $"path crosses impassable cell at {p}");
            }
            from = to;
        }
    }

    [Fact]
    public void SameCell_ReturnsTargetOnly()
    {
        var path = Pathfinder.FindPath(OpenMap(5, 5), new Vec2(10, 10), new Vec2(90, 60));
        Assert.Equal(new[] { new Vec2(90, 60) }, path);
    }

    [Fact]
    public void StraightLine_CollapsesToTarget()
    {
        var path = Pathfinder.FindPath(OpenMap(20, 5), new Vec2(50, 250), new Vec2(1850, 250));
        Assert.Equal(new[] { new Vec2(1850, 250) }, path);
    }

    [Fact]
    public void WallWithGap_RoutesThroughGap()
    {
        var map = OpenMap(12, 12);
        for (int y = 0; y <= 10; y++) Block(map, 5, y);
        var start = new Vec2(150, 150);
        var target = new Vec2(950, 150);

        var path = Pathfinder.FindPath(map, start, target);

        Assert.NotNull(path);
        Assert.Equal(target, path[^1]);
        Assert.Contains(path, p => p.ToCell().Y == 11);
        AssertWalkable(map, start, path);
    }

    [Fact]
    public void EnclosedTarget_IsUnreachable()
    {
        var map = OpenMap(10, 10);
        for (int x = 3; x <= 7; x++) { Block(map, x, 3); Block(map, x, 7); }
        for (int y = 3; y <= 7; y++) { Block(map, 3, y); Block(map, 7, y); }
        Assert.Null(Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 550)));
    }

    [Fact]
    public void ImpassableTargetCell_IsUnreachable()
    {
        var map = OpenMap(10, 10);
        Block(map, 5, 5);
        Assert.Null(Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 550)));
    }

    [Fact]
    public void TargetOutsideMap_IsUnreachable()
    {
        Assert.Null(Pathfinder.FindPath(OpenMap(10, 10), new Vec2(50, 50), new Vec2(5000, 50)));
    }

    [Fact]
    public void StartOnImpassableCell_CanStillLeave()
    {
        var map = OpenMap(10, 10);
        Block(map, 0, 0);
        var path = Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 50));
        Assert.NotNull(path);
        Assert.Equal(new Vec2(550, 50), path[^1]);
    }

    [Fact]
    public void ExpensiveTerrain_IsAvoided()
    {
        var map = OpenMap(20, 11);
        for (int x = 5; x <= 14; x++)
            for (int y = 2; y <= 8; y++)
                map[new CellCoord(x, y)].ExtraMoveCost = 200;
        var start = new Vec2(150, 550);
        var path = Pathfinder.FindPath(map, start, new Vec2(1850, 550));

        Assert.NotNull(path);
        var from = start;
        foreach (var to in path)
        {
            var d = to - from;
            int steps = Math.Max(1, d.Length / 10);
            for (int i = 0; i <= steps; i++)
            {
                var p = new Vec2(from.X + (int)((long)d.X * i / steps), from.Y + (int)((long)d.Y * i / steps));
                Assert.True(map.CellAt(p).ExtraMoveCost == 0, $"path enters swamp at {p}");
            }
            from = to;
        }
    }

    [Fact]
    public void RandomRockFields_PathsNeverCutThroughRocks()
    {
        var rng = new Rng(5);
        var map = OpenMap(20, 20);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                if (rng.Chance(150)) Block(map, x, y);

        int found = 0;
        for (int i = 0; i < 60; i++)
        {
            var start = new Vec2(rng.NextInt(2000), rng.NextInt(2000));
            var target = new Vec2(rng.NextInt(2000), rng.NextInt(2000));
            if (!map.CellAt(start).IsPassable) continue;
            var path = Pathfinder.FindPath(map, start, target);
            if (path is null) continue;
            found++;
            Assert.Equal(target, path[^1]);
            AssertWalkable(map, start, path);
        }
        Assert.True(found > 20, $"only {found} paths found; the fixture is too dense");
    }

    [Fact]
    public void SameInput_GivesSamePath()
    {
        var map = OpenMap(30, 30);
        for (int y = 0; y < 25; y++) Block(map, 15, y);
        var a = Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(2850, 150));
        var b = Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(2850, 150));
        Assert.Equal(a, b);
    }
}
