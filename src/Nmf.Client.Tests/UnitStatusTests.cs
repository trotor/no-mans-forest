using Nmf.Sim.Combat;
using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class UnitStatusTests
{
    [Fact]
    public void Describe_FollowsStanceAndMovement()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        Assert.Equal("Standing", UnitStatus.Describe(u));

        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(3000, 1000), MoveMode.Run));
        sim.Step();
        Assert.Equal("Running", UnitStatus.Describe(u));

        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Prone));
        sim.Step();
        Assert.Equal("Getting down", UnitStatus.Describe(u));
        for (int i = 0; i < 25; i++) sim.Step();
        Assert.Equal("Prone", UnitStatus.Describe(u));

        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Standing));
        sim.Step();
        Assert.Equal("Getting up", UnitStatus.Describe(u));
    }

    [Fact]
    public void Describe_CombatStatesTakePriority()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        u.Action = CombatAction.Aiming;
        Assert.Equal("Firing", UnitStatus.Describe(u));
        u.Action = CombatAction.Reloading;
        Assert.Equal("Reloading", UnitStatus.Describe(u));
        u.MoraleState = MoraleState.Pinned;
        Assert.Equal("Pinned", UnitStatus.Describe(u));
        u.MoraleState = MoraleState.Broken;
        Assert.Equal("Broken", UnitStatus.Describe(u));
        u.Wound = WoundLevel.Incapacitated;
        Assert.Equal("Down", UnitStatus.Describe(u));
        u.Wound = WoundLevel.Dead;
        Assert.Equal("Dead", UnitStatus.Describe(u));
    }

    [Fact]
    public void Condition_AndPolicyNames()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(100, 100), 7);
        Assert.Equal("Unhurt", UnitStatus.Condition(u));
        u.Wound = WoundLevel.Serious;
        Assert.Equal("Serious wound", UnitStatus.Condition(u));
        Assert.Equal("Return fire", UnitStatus.PolicyName(FirePolicy.ReturnFire));
    }
}
