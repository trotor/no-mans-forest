using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class CombatOrderTests
{
    private static (Simulation Sim, Unit Blue, Unit Red) Setup()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 1);
        return (sim, sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle()), sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7));
    }

    private static string? RejectionOf(Simulation sim, Side side, Order order)
    {
        sim.Submit(side, order);
        return sim.Step().OfType<OrderRejected>().FirstOrDefault(r => r.Order == order)?.Reason;
    }

    [Fact]
    public void OutOfAction_RejectsEverything()
    {
        var (sim, blue, _) = Setup();
        Damage.SetWound(sim, blue, WoundLevel.Incapacitated, 0, []);
        Assert.Equal("unit is out of action", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
        Assert.Equal("unit is out of action", RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Standing)));
    }

    [Fact]
    public void Broken_RejectsOrders()
    {
        var (sim, blue, _) = Setup();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        Assert.Equal("unit is broken", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
    }

    [Fact]
    public void Pinned_RejectsMovingAndStandingUpButAllowsProneAndFireOrders()
    {
        var (sim, blue, red) = Setup();
        blue.Suppression = 600;
        blue.MoraleState = MoraleState.Pinned;
        Assert.Equal("unit is pinned", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
        Assert.Equal("unit is pinned", RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Standing)));
        Assert.Null(RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Prone)));
        Assert.Null(RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, red.Id)));
        Assert.Equal(red.Id, blue.OrderedTarget);
    }

    [Fact]
    public void FireAt_OwnSideOrUnknown_IsRejected()
    {
        var (sim, blue, _) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        Assert.Equal("invalid target", RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, friend.Id)));
        Assert.Equal("invalid target", RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, new UnitId(99))));
    }

    [Fact]
    public void FirePolicyOrder_SetsPolicy()
    {
        var (sim, blue, _) = Setup();
        Assert.Null(RejectionOf(sim, Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.ReturnFire)));
        Assert.Equal(FirePolicy.ReturnFire, blue.FirePolicy);
    }
}
