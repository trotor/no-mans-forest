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

    [Fact]
    public void Patrol_StopsWhenTheUnitIsUnderFireOrOutOfAction()
    {
        var sim = new Simulation(new GridMap(30, 30, ["none"]), 1);
        var unit = sim.SpawnUnit(Side.Red, new Vec2(150, 150), 20);
        var patrol = new PatrolBehavior(unit.Id, [new Vec2(150, 150), new Vec2(950, 150)]);
        unit.Suppression = 100;
        patrol.Tick(sim);
        sim.Step();
        Assert.Empty(sim.OrderLog);

        unit.Suppression = 0;
        unit.Wound = Nmf.Sim.Combat.WoundLevel.Incapacitated;
        for (int i = 0; i < 10; i++) { patrol.Tick(sim); sim.Step(); }
        Assert.Empty(sim.OrderLog);
    }

    [Fact]
    public void Patrol_StopsOnceTheSideSeesAnEnemy()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 1);
        var unit = sim.SpawnUnit(Side.Red, new Vec2(150, 1050), 7);
        sim.SpawnUnit(Side.Blue, new Vec2(2150, 1050), 7);
        var patrol = new PatrolBehavior(unit.Id, [new Vec2(150, 1050), new Vec2(150, 1850)]);
        for (int i = 0; i < 400; i++) { patrol.Tick(sim); sim.Step(); }
        int orders = sim.OrderLog.Count;
        Assert.Contains(sim.Knowledge(Side.Red).Contacts, c => c.Level == Nmf.Sim.Vision.ContactLevel.Visible);
        for (int i = 0; i < 400; i++) { patrol.Tick(sim); sim.Step(); }
        Assert.Equal(orders, sim.OrderLog.Count);
    }
}
