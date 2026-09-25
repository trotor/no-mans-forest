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

    private static GridMap Maze(int w, int h)
    {
        var map = OpenMap(w, h);
        for (int x = 5; x < w - 5; x += 40)
            for (int y = (x / 40) % 2 == 0 ? 0 : 10; y < h - ((x / 40) % 2 == 0 ? 10 : 0); y++)
                Block(map, x, y);
        return map;
    }

    [Fact]
    public void RepeatedCalls_SameResult()
    {
        var map = Maze(200, 120);
        var a = Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(19_950, 11_950));
        var b = Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(19_950, 11_950));
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void AlternatingMapSizes_NoStaleState()
    {
        var big = Maze(300, 200);
        var small = OpenMap(10, 10);
        Block(small, 5, 4); Block(small, 5, 5); Block(small, 5, 6);
        var smallFirst = Pathfinder.FindPath(small, new Vec2(250, 550), new Vec2(850, 550));
        var bigPath = Pathfinder.FindPath(big, new Vec2(50, 50), new Vec2(29_950, 19_950));
        var smallAgain = Pathfinder.FindPath(small, new Vec2(250, 550), new Vec2(850, 550));
        Assert.NotNull(bigPath);
        Assert.Equal(smallFirst, smallAgain);
        AssertWalkable(small, new Vec2(250, 550), smallAgain!);
        var walled = OpenMap(10, 10);
        for (int y = 0; y < 10; y++) Block(walled, 5, y);
        Assert.Null(Pathfinder.FindPath(walled, new Vec2(250, 550), new Vec2(850, 550)));
    }

    [Fact]
    public void KilometreMaze_LongPathStillFound()
    {
        var map = Maze(1000, 1000);
        Assert.NotNull(Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(99_950, 99_950)));
    }

    /// <summary>Open ground with scattered rocks, slow patches and a long wall with a gap, like a real 1 km map.</summary>
    private static GridMap Countryside(int size)
    {
        var map = OpenMap(size, size);
        var rng = new Rng(7);
        for (int i = 0; i < size * size / 50; i++)
            Block(map, rng.NextInt(size), rng.NextInt(size));
        for (int i = 0; i < size * size / 4; i++)
            map[new CellCoord(rng.NextInt(size), rng.NextInt(size))].ExtraMoveCost = 120;
        for (int x = 0; x < size - 60; x++)
            Block(map, x, size / 2);
        return map;
    }

    [Fact]
    public void KilometreCountryside_LongPaths_AreFastAndAllocateLittle()
    {
        var map = Countryside(1000);
        Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(99_850, 99_850)); // warm up
        long before = GC.GetAllocatedBytesForCurrentThread();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        int found = 0;
        for (int i = 0; i < 20; i++)
            if (Pathfinder.FindPath(map, new Vec2(150 + i * 1000, 150), new Vec2(99_850 - i * 1000, 99_850)) is not null)
                found++;
        clock.Stop();
        long allocatedPerCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;
        Assert.True(found >= 18, $"only {found} of 20 paths found");
        Assert.True(allocatedPerCall < 2_000_000, $"{allocatedPerCall / 1024} KiB allocated per path");
        Assert.True(clock.ElapsedMilliseconds < 8000, $"20 kilometre paths took {clock.ElapsedMilliseconds} ms"); // catches pathological regressions, not a benchmark
    }

    [Fact]
    public void KilometreMap_TargetInAWalledPocket_RejectedQuickly()
    {
        var map = Countryside(1000);
        for (int i = 600; i <= 610; i++) { Block(map, i, 200); Block(map, i, 210); Block(map, 600, i - 400); Block(map, 610, i - 400); }
        Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(60_550, 20_550)); // warm up
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 5; i++)
            Assert.Null(Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(60_550, 20_550)));
        Assert.True(clock.ElapsedMilliseconds < 200, $"5 unreachable targets took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Reachable_AnswersFromRegions_AndNoticesMapChanges()
    {
        var map = OpenMap(50, 50);
        Assert.True(Pathfinder.Reachable(map, new Vec2(150, 150), new Vec2(4850, 4850)));
        for (int y = 0; y < 50; y++) Block(map, 25, y);
        Assert.False(Pathfinder.Reachable(map, new Vec2(150, 150), new Vec2(4850, 4850)));
        Assert.True(Pathfinder.Reachable(map, new Vec2(150, 150), new Vec2(2350, 4850)));
        Assert.False(Pathfinder.Reachable(map, new Vec2(150, 150), new Vec2(2550, 2550))); // the wall itself
    }

    [Fact]
    public void Reachable_IsQuickOnAKilometreMap()
    {
        var map = Countryside(1000);
        Pathfinder.Reachable(map, new Vec2(150, 150), new Vec2(99_850, 99_850));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            Pathfinder.Reachable(map, new Vec2(150 + i * 100, 150), new Vec2(99_850, 99_850 - i * 100));
        Assert.True(clock.ElapsedMilliseconds < 500, $"50 reachability checks took {clock.ElapsedMilliseconds} ms");
    }
}
