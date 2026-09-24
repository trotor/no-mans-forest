using Nmf.Sim.AI;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class PatrolBehaviorTests
{
    [Fact]
    public void Patrol_WalksPingPongAlongPoints()
    {
        var sim = new Simulation(new GridMap(30, 30, ["none"]), 1);
        var unit = sim.SpawnUnit(Side.Red, new Vec2(150, 150), 20);
        Vec2[] points = [new(150, 150), new(950, 150), new(950, 950)];
        var patrol = new PatrolBehavior(unit.Id, points);

        var arrivals = new List<Vec2>();
        for (int i = 0; i < 2000 && arrivals.Count < 5; i++)
        {
            patrol.Tick(sim);
            foreach (var e in sim.Step())
                if (e is UnitArrived arrived) arrivals.Add(arrived.Position);
        }

        Assert.Equal(new[] { points[0], points[1], points[2], points[1], points[0] }, arrivals);
    }

    [Fact]
    public void Patrol_NeedsTwoPoints()
    {
        Assert.Throws<ArgumentException>(() => new PatrolBehavior(new UnitId(1), [new Vec2(0, 0)]));
    }

    [Fact]
    public void Patrol_ForMissingUnit_DoesNothing()
    {
        var sim = new Simulation(new GridMap(5, 5, ["none"]), 1);
        new PatrolBehavior(new UnitId(9), [new Vec2(50, 50), new Vec2(150, 50)]).Tick(sim);
        sim.Step();
        Assert.Empty(sim.OrderLog);
    }
}
