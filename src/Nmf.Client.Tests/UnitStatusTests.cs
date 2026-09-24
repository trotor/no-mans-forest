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
}
