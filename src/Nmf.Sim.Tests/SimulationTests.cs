using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests;

public class SimulationTests
{
    private static Simulation NewSim() => new(new GridMap(20, 20, ["none"]), seed: 1);

    [Fact]
    public void Step_AdvancesTick()
    {
        var sim = NewSim();
        sim.Step();
        sim.Step();
        Assert.Equal(2, sim.Tick);
    }

    [Fact]
    public void SpawnUnit_AssignsIncreasingIdsInOrder()
    {
        var sim = NewSim();
        var a = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7);
        var b = sim.SpawnUnit(Side.Red, new Vec2(150, 50), 7);
        Assert.Equal(new UnitId(1), a.Id);
        Assert.Equal(new UnitId(2), b.Id);
        Assert.Equal(new[] { a, b }, sim.Units);
        Assert.Same(b, sim.FindUnit(b.Id));
        Assert.Null(sim.FindUnit(new UnitId(99)));
    }

    [Fact]
    public void SpawnUnit_OutsideMap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSim().SpawnUnit(Side.Blue, new Vec2(2000, 0), 7));
    }

    [Fact]
    public void SpawnUnit_NonPositiveSpeed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSim().SpawnUnit(Side.Blue, new Vec2(50, 50), 0));
    }

    [Fact]
    public void Submit_TakesEffectOnNextStep()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(80, 50)));
        Assert.Null(u.MoveTarget);
        sim.Step();
        Assert.NotNull(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_MovesAtSpeedAndArrives()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(80, 50)));

        var first = sim.Step();
        Assert.Equal(new Vec2(60, 50), u.Position);
        Assert.Contains<SimEvent>(new UnitMoved(0, u.Id, new Vec2(50, 50), new Vec2(60, 50)), first);

        sim.Step();
        Assert.Equal(new Vec2(70, 50), u.Position);

        var third = sim.Step();
        Assert.Equal(new Vec2(80, 50), u.Position);
        Assert.Contains<SimEvent>(new UnitArrived(2, u.Id, new Vec2(80, 50)), third);
        Assert.Null(u.MoveTarget);

        Assert.Empty(sim.Step());
    }

    [Fact]
    public void MoveOrder_SlowDiagonal_StillArrives()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), speedCmPerTick: 1);
        var target = new Vec2(350, 250);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, target));

        for (int i = 0; i < 1000 && (i == 0 || u.MoveTarget is not null); i++)
            sim.Step();

        Assert.Equal(target, u.Position);
        Assert.Null(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_ToOwnPosition_ArrivesWithoutMoving()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(50, 50)));
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new UnitArrived(0, u.Id, new Vec2(50, 50)) }, events);
    }

    [Fact]
    public void StopOrder_ClearsTarget()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(500, 50)));
        sim.Step();
        sim.Submit(Side.Blue, new StopOrder(u.Id));
        sim.Step();
        var stoppedAt = u.Position;
        sim.Step();
        Assert.Null(u.MoveTarget);
        Assert.Equal(stoppedAt, u.Position);
    }

    [Fact]
    public void Order_ForUnknownUnit_IsRejected()
    {
        var sim = NewSim();
        var order = new MoveOrder(new UnitId(42), new Vec2(10, 10));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "unknown unit") }, events);
    }

    [Fact]
    public void Order_ForOtherSidesUnit_IsRejected()
    {
        var sim = NewSim();
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(50, 50), 10);
        var order = new MoveOrder(enemy.Id, new Vec2(100, 100));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "unit belongs to another side") }, events);
        Assert.Null(enemy.MoveTarget);
    }

    [Fact]
    public void MoveOrder_OutsideMap_IsRejectedAndSimKeepsRunning()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var bad = new MoveOrder(u.Id, new Vec2(5000, 50));
        sim.Submit(Side.Blue, bad);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(100, 50)));
        var events = sim.Step();
        Assert.Contains<SimEvent>(new OrderRejected(0, bad, "target outside map"), events);
        Assert.Equal(new Vec2(60, 50), u.Position);
    }

    [Fact]
    public void OrderLog_RecordsTickAndIssuer()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var first = new MoveOrder(u.Id, new Vec2(100, 50));
        var second = new StopOrder(u.Id);
        sim.Submit(Side.Blue, first);
        sim.Step();
        sim.Step();
        sim.Step();
        sim.Submit(Side.Blue, second);
        sim.Step();
        Assert.Equal(new[] { new LoggedOrder(0, Side.Blue, first), new LoggedOrder(3, Side.Blue, second) }, sim.OrderLog);
    }

    [Fact]
    public void Submit_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewSim().Submit(Side.Blue, null!));
    }
}
