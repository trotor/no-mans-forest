using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Units;

public class MovementTests
{
    private static GridMap OpenMap(int w = 30, int h = 30) => new(w, h, ["none"]);

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < n; i++) events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void CostlyCell_SlowsMovement()
    {
        var map = OpenMap();
        map[new CellCoord(0, 0)].ExtraMoveCost = 100;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(10, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50)));
        sim.Step();
        Assert.Equal(new Vec2(15, 50), u.Position);
        Assert.True(u.IsMoving);
    }

    [Fact]
    public void RunMode_DoublesSpeed()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(10, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50), MoveMode.Run));
        sim.Step();
        Assert.Equal(new Vec2(30, 50), u.Position);
    }

    [Fact]
    public void SetStance_StandingToProne_TakesTwentyFiveTicksViaCrouch()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Prone));

        StepN(sim, 10);
        Assert.Equal(Stance.Crouching, u.Stance);
        StepN(sim, 14);
        Assert.Equal(Stance.Crouching, u.Stance);
        var last = sim.Step();
        Assert.Equal(Stance.Prone, u.Stance);
        Assert.Null(u.TargetStance);
        Assert.Contains<SimEvent>(new StanceChanged(24, u.Id, Stance.Prone), last);
    }

    [Fact]
    public void CrawlOrder_GoesProneFirstThenCrawlsSlowly()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(2050, 50), MoveMode.Crawl));

        StepN(sim, 25);
        Assert.Equal(Stance.Prone, u.Stance);
        Assert.Equal(new Vec2(50, 50), u.Position);

        sim.Step();
        Assert.Equal(new Vec2(52, 50), u.Position);
    }

    [Fact]
    public void WalkOrderWhileProne_StandsUpBeforeMoving()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Prone));
        StepN(sim, 25);

        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50)));
        StepN(sim, 25);
        Assert.Equal(Stance.Standing, u.Stance);
        Assert.Equal(new Vec2(50, 50), u.Position);

        sim.Step();
        Assert.Equal(new Vec2(60, 50), u.Position);
    }

    [Fact]
    public void MoveOrder_ToImpassableCell_IsRejected()
    {
        var map = OpenMap();
        map[new CellCoord(5, 0)].ExtraMoveCost = CellData.Impassable;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var order = new MoveOrder(u.Id, new Vec2(550, 50));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "target not reachable") }, events);
        Assert.Null(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_AroundWall_NeverEntersImpassableCells()
    {
        var map = OpenMap(20, 20);
        for (int y = 0; y <= 18; y++) map[new CellCoord(10, y)].ExtraMoveCost = CellData.Impassable;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(150, 150), 7);
        var target = new Vec2(1850, 150);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, target));

        for (int i = 0; i < 2000 && (i == 0 || u.MoveTarget is not null); i++)
        {
            sim.Step();
            Assert.True(map.CellAt(u.Position).IsPassable, $"unit entered impassable cell at {u.Position}");
        }
        Assert.Equal(target, u.Position);
    }

    [Fact]
    public void StopOrder_ClearsPath()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(2550, 2550)));
        sim.Step();
        sim.Submit(Side.Blue, new StopOrder(u.Id));
        sim.Step();
        Assert.Null(u.MoveTarget);
        Assert.Empty(u.Path);
        Assert.False(u.IsMoving);
    }
}
