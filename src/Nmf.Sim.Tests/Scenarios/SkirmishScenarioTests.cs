using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Scenarios;

public class SkirmishScenarioTests
{
    private static GridMap MapWith(IReadOnlyList<MapPoint> points, IReadOnlyList<MapPath> paths) =>
        new(40, 40, ["none"], new MapFeatures([], points, paths));

    [Fact]
    public void Create_SpawnsUnitsFromTypedPoints()
    {
        var map = MapWith(
            [
                new MapPoint("b1", "blue", new Vec2(150, 150)),
                new MapPoint("r1", "red", new Vec2(3500, 3500)),
                new MapPoint("b2", "blue", new Vec2(350, 150)),
                new MapPoint("x", "spawn", new Vec2(50, 50)),
            ],
            []);

        var scenario = SkirmishScenario.Create(map, 3);

        Assert.Equal(new[] { Side.Blue, Side.Red, Side.Blue }, scenario.Sim.Units.Select(u => u.Side));
        Assert.Equal(new Vec2(350, 150), scenario.Sim.Units[2].Position);
        Assert.All(scenario.Sim.Units, u => Assert.Equal(SkirmishScenario.SoldierWalkSpeedCmPerTick, u.SpeedCmPerTick));
        Assert.Equal(8, SkirmishScenario.SoldierWalkSpeedCmPerTick); // 1.6 m/s
        Assert.Equal(3UL, scenario.Sim.Seed);
    }

    [Fact]
    public void Create_AssignsPatrolToNearestRedUnit()
    {
        var map = MapWith(
            [
                new MapPoint("r1", "red", new Vec2(3500, 3500)),
                new MapPoint("r2", "red", new Vec2(1000, 1000)),
                new MapPoint("b1", "blue", new Vec2(1100, 1000)),
            ],
            [new MapPath("p", "patrol", [new Vec2(1200, 1200), new Vec2(2000, 1200)])]);

        var scenario = SkirmishScenario.Create(map, 1);

        var patrol = Assert.Single(scenario.Patrols);
        Assert.Equal(new UnitId(2), patrol.Unit);
    }

    [Fact]
    public void Tick_DrivesPatrols()
    {
        var map = MapWith(
            [new MapPoint("r1", "red", new Vec2(1000, 1000))],
            [new MapPath("p", "patrol", [new Vec2(1000, 1000), new Vec2(2000, 1000)])]);
        var scenario = SkirmishScenario.Create(map, 1);

        for (int i = 0; i < 20; i++)
        {
            scenario.Tick();
            scenario.Sim.Step();
        }

        Assert.True(scenario.Sim.Units[0].Position.X > 1000);
    }

    [Fact]
    public void SovietLeader_CarriesOrders()
    {
        var map = MapWith([new MapPoint("r1", "red", new Vec2(350, 350)), new MapPoint("r2", "red", new Vec2(550, 350)),
            new MapPoint("b1", "blue", new Vec2(150, 150))], []);
        var units = SkirmishScenario.Create(map, 3).Sim.Units;
        Assert.Equal(new[] { SkirmishScenario.SovietOrders }, units[0].Items);
        Assert.Empty(units[1].Items);
        Assert.Empty(units[2].Items);
        Assert.Equal("soviet_orders", SkirmishScenario.SovietOrders.Id);
    }
}
