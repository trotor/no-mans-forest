using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Content.Tests;

/// <summary>The 1 km real-terrain map (tools/mapgen, Karhumäki on the Lake Onega shore).</summary>
[Collection(Nmf.Content.Tests.TimingCollection.Name)]
public class KarhumakiMapTests
{
    private static readonly Lazy<GridMap> Map = new(() =>
        TmxMapLoader.Load(Path.Combine(CoreContentTests.RepoRoot(), "content", "core", "maps", "karhumaki.tmx")));

    [Fact]
    public void Loads_1000x1000_WithSpawns()
    {
        var map = Map.Value;
        Assert.Equal(1000, map.Width);
        Assert.Equal(1000, map.Height);
        Assert.Contains("water", map.TerrainNames);
        Assert.Equal(4, map.Features.Points.Count(p => p.Type == SkirmishScenario.BluePointType));
        Assert.Equal(5, map.Features.Points.Count(p => p.Type == SkirmishScenario.RedPointType));
        Assert.Single(map.Features.Paths, p => p.Type == SkirmishScenario.PatrolPathType);
    }

    [Fact]
    public void Water_IsImpassableAndSeeThrough_AndTheHillsAreReal()
    {
        var map = Map.Value;
        var water = map.TerrainNames.ToList().IndexOf("water");
        int waterCells = 0, maxHeight = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                maxHeight = Math.Max(maxHeight, cell.GroundHeightCm);
                if (cell.TerrainId != water) continue;
                waterCells++;
                Assert.False(cell.IsPassable);
                Assert.Equal(0, cell.ObstacleHeightCm);
            }
        Assert.True(waterCells > 1000, $"only {waterCells} lake cells");
        Assert.True(maxHeight > 5000, $"relief only {maxHeight} cm");
    }

    [Fact]
    public void SpawnsPassable_AndFinnsCanReachTheSoviets()
    {
        var map = Map.Value;
        var points = map.Features.Points;
        foreach (var p in points)
            Assert.True(map.CellAt(p.Position).IsPassable, $"{p.Name} stands in an impassable cell");
        var blue = points.First(p => p.Type == SkirmishScenario.BluePointType).Position;
        foreach (var red in points.Where(p => p.Type == SkirmishScenario.RedPointType))
            Assert.NotNull(Pathfinder.FindPath(map, blue, red.Position));
        foreach (var node in map.Features.Paths.Single(p => p.Type == SkirmishScenario.PatrolPathType).Points)
            Assert.True(map.CellAt(node).IsPassable);
    }

    [Fact]
    public void TwoMinuteAdvance_IsDeterministic_AndFast()
    {
        var root = CoreContentTests.RepoRoot();
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        (ulong Hash, int Moved) Run()
        {
            var scenario = SkirmishScenario.Create(Map.Value, 1942, weapons, grenades);
            var sim = scenario.Sim;
            int moved = 0;
            var red = sim.Units.First(u => u.Side == Side.Red).Position;
            foreach (var unit in sim.Units.Where(u => u.Side == Side.Blue))
                sim.Submit(Side.Blue, new MoveOrder(unit.Id, red, MoveMode.Auto));
            for (int i = 0; i < 2400; i++)
            {
                scenario.Tick();
                moved += sim.Step().Count(e => e is UnitMoved);
            }
            return (StateHash.Compute(sim), moved);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Moved > 0);
        clock.Stop();
        Assert.True(clock.ElapsedMilliseconds < 20_000, $"two 2-minute runs took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void MapProperties_CarryTheAttribution()
    {
        var props = Map.Value.Features.Properties;
        Assert.Contains("OpenStreetMap", props["source"]);
        Assert.Contains("ODbL", props["source"]);
        Assert.Equal("62.880000", props["origin_lat"]);
    }

    [Fact]
    public void LongestOrders_AcrossTheMap_AreQuick()
    {
        var map = Map.Value;
        (Vec2 From, Vec2 To)[] trips =
        [
            (new(5_050, 50_050), new(94_050, 50_050)), (new(2_050, 98_050), new(90_050, 8_050)),
            (new(2_050, 2_050), new(97_050, 97_050)), (new(98_050, 60_050), new(3_050, 30_050)),
        ];
        Pathfinder.FindPath(map, trips[0].From, trips[0].To); // warm up
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (from, to) in trips)
            Assert.NotNull(Pathfinder.FindPath(map, from, to));
        Assert.True(clock.ElapsedMilliseconds < 450, $"four cross-map orders took {clock.ElapsedMilliseconds} ms");
    }
}
