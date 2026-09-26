using Nmf.Client.Fog;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Fog;

[Collection(Nmf.Client.Tests.TimingCollection.Name)]
public class FogOfWarTests
{
    private const int Eye = 160;

    private static GridMap Open(int w = 400, int h = 200) => new(w, h, ["none"]);

    private static Vec2 Cell(int x, int y) => new CellCoord(x, y).CenterCm;

    private static FogOfWar Seen(GridMap map, params Vec2[] observers)
    {
        var fog = new FogOfWar(map);
        fog.Update(observers.Select((p, i) => (new UnitId(i + 1), p, map.CellAt(p).GroundHeightCm + Eye)));
        return fog;
    }

    [Fact]
    public void OpenField_SeesWithinRangeOnly()
    {
        var fog = Seen(Open(), Cell(20, 100));
        Assert.True(fog.IsVisible(Cell(20, 100)));
        Assert.True(fog.IsVisible(Cell(120, 100)));
        Assert.False(fog.IsVisible(Cell(200, 100)));
    }

    [Fact]
    public void Ridge_HidesTheFarSide()
    {
        var map = Open();
        for (int y = 0; y < 200; y++)
            for (int x = 100; x < 104; x++)
                map[new CellCoord(x, y)].GroundHeightCm = 400;
        var fog = Seen(map, Cell(20, 100));
        Assert.True(fog.IsVisible(Cell(90, 100)));
        Assert.False(fog.IsVisible(Cell(125, 100)));
    }

    [Fact]
    public void DenseForest_VisibleAtItsEdgeNotInItsDepths()
    {
        var map = Open();
        for (int y = 0; y < 200; y++)
            for (int x = 60; x < 400; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 20, 26, 0);
        var fog = Seen(map, Cell(20, 100));
        Assert.True(fog.IsVisible(Cell(64, 100)));
        Assert.False(fog.IsVisible(Cell(140, 100)));
    }

    [Fact]
    public void InAnEvenForest_TheSeenGroundIsRound_NotSquare()
    {
        var map = Open(200, 200);
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 6, 26, 0);
        var fog = Seen(map, Cell(100, 100));
        long Reach(int dx, int dy)
        {
            long best = 0;
            for (int k = 1; k < 100; k++)
            {
                var p = Cell(100 + dx * k, 100 + dy * k);
                if (fog.IsVisible(p))
                    best = (p - Cell(100, 100)).Length;
            }
            return best;
        }
        long straight = Reach(1, 0), diagonal = Reach(1, 1);
        Assert.InRange(straight, 1000, 12_000); // the forest limits the view well inside the range
        Assert.True(Math.Abs(diagonal - straight) <= 600, $"straight {straight} cm, diagonal {diagonal} cm");
    }

    [Fact]
    public void AgreesWithTheExactViewshed()
    {
        var map = Open(200, 200);
        var rng = new Rng(5);
        for (int i = 0; i < 40; i++) // forest patches and a hill
        {
            int cx = rng.NextInt(200), cy = rng.NextInt(200), r = 5 + rng.NextInt(12);
            for (int y = Math.Max(0, cy - r); y < Math.Min(200, cy + r); y++)
                for (int x = Math.Max(0, cx - r); x < Math.Min(200, cx + r); x++)
                    map[new CellCoord(x, y)] = new CellData(0, 1500, 8, 26, 0);
        }
        for (int y = 0; y < 200; y++)
            for (int x = 0; x < 200; x++)
                map[new CellCoord(x, y)].GroundHeightCm = (short)(300 * Math.Exp(-((x - 140) * (x - 140) + (y - 60) * (y - 60)) / 800.0));
        var observer = Cell(40, 150);
        int eye = map.CellAt(observer).GroundHeightCm + Eye;
        var exact = new bool[200 * 200];
        Viewshed.Compute(map, [(observer, eye)], FogOfWar.RangeCm, exact);
        var fog = Seen(map, observer);

        int agree = 0, total = 0;
        for (int by = 0; by < 50; by++)
            for (int bx = 0; bx < 50; bx++)
            {
                var centre = new Vec2(bx * 400 + 200, by * 400 + 200);
                if ((centre - observer).LengthSquared > (long)FogOfWar.RangeCm * FogOfWar.RangeCm)
                    continue;
                int seen = 0;
                for (int y = by * 4; y < by * 4 + 4; y++)
                    for (int x = bx * 4; x < bx * 4 + 4; x++)
                        if (exact[y * 200 + x]) seen++;
                total++;
                if ((seen >= 8) == fog.IsVisible(centre)) agree++;
            }
        Assert.True(agree * 100 >= total * 85, $"only {agree} of {total} blocks agree with the exact viewshed");
    }

    [Fact]
    public void UnchangedObservers_AreNotRecomputed()
    {
        var map = Open();
        var fog = new FogOfWar(map);
        var a = (new UnitId(1), Cell(20, 100), Eye);
        var b = (new UnitId(2), Cell(40, 60), Eye);
        fog.Update([a, b]);
        Assert.Equal(2, fog.LastRecomputed);
        int version = fog.Version;
        fog.Update([a, b with { Item2 = Cell(41, 61) }]); // a step inside the same 4 m block
        Assert.Equal(0, fog.LastRecomputed);
        Assert.Equal(version, fog.Version);
        fog.Update([a, b with { Item2 = Cell(60, 60) }]);
        Assert.Equal(1, fog.LastRecomputed);
        Assert.True(fog.Version > version);
        Assert.True(fog.IsVisible(Cell(190, 40))); // only b is close enough to see it
        fog.Update([a]); // b fell
        Assert.False(fog.IsVisible(Cell(190, 40)));
    }

    [Fact]
    public void KilometreMap_MovingSquad_UpdatesQuickly()
    {
        var map = new GridMap(1000, 1000, ["none"]);
        var fog = new FogOfWar(map);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int step = 0; step < 20; step++)
            fog.Update(Enumerable.Range(0, 4).Select(i => (new UnitId(i + 1), Cell(300 + i * 5 + step * 5, 500), Eye)));
        Assert.True(clock.ElapsedMilliseconds < Nmf.Client.Tests.TimingCollection.Budget(300), $"20 updates took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void RecomputeWithTheSameResult_KeepsTheVersion()
    {
        var map = Open();
        var fog = new FogOfWar(map);
        fog.Update([(new UnitId(1), Cell(20, 100), Eye)]);
        int version = fog.Version;
        fog.Update([(new UnitId(1), Cell(20, 100), Eye + 25)]); // eye 25 cm higher: recomputed, same open field seen
        Assert.Equal(1, fog.LastRecomputed);
        Assert.Equal(version, fog.Version);
    }

    [Fact]
    public void PointsOutsideTheMap_AreNotVisible()
    {
        var map = Open(203, 200); // the last block column is partial
        var fog = Seen(map, Cell(2, 2), Cell(200, 2));
        Assert.False(fog.IsVisible(new Vec2(-50, 50)));
        Assert.False(fog.IsVisible(new Vec2(20_350, 50)));
        Assert.True(fog.IsVisible(new Vec2(20_250, 50)));
    }
}
