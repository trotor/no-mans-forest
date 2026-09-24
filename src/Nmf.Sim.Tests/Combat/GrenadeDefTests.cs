using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class GrenadeDefTests
{
    internal static GrenadeDef Test(int fuse = 60, int range = 3000, int scatter = 0, int blast = 1000, int lethal = 400, int suppression = 600, int lethality = 100) =>
        new GrenadeDef("test_grenade", "Test grenade", fuse, range, scatter, blast, lethal, suppression, lethality).Validated();

    [Theory]
    [InlineData(5, 400, 1000, 60, "fuse")]
    [InlineData(60, 1200, 1000, 60, "lethal")]
    [InlineData(60, 400, 1000, 101, "lethality")]
    public void Validated_RejectsBadValues(int fuse, int lethal, int blast, int lethality, string field)
    {
        var bad = new GrenadeDef("g", "G", fuse, 3000, 10, blast, lethal, 500, lethality);
        var ex = Assert.Throws<ArgumentException>(() => bad.Validated());
        Assert.Contains(field, ex.Message);
    }

    [Fact]
    public void SpawnUnit_WithGrenadeType_CarriesTwo()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7, TestWeapons.Rifle(), grenade: Test());
        Assert.Equal(CombatRules.GrenadesPerSoldier, u.Grenades);
        Assert.Equal(0, sim.SpawnUnit(Side.Blue, new Vec2(150, 50), 7).Grenades);
    }

    [Fact]
    public void CapturedSoldier_IsOutOfActionButAlive()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7);
        u.IsCaptured = true;
        Assert.True(u.IsOutOfAction);
        Assert.True(u.IsAlive);
    }

    [Fact]
    public void Sneak_MovesCrouchedAtSixtyPercent()
    {
        var sim = new Simulation(new GridMap(30, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(2050, 50), MoveMode.Sneak));
        for (int i = 0; i < 10; i++) sim.Step(); // standing -> crouching takes 10 ticks
        Assert.Equal(Stance.Crouching, u.Stance);
        var at = u.Position;
        sim.Step();
        Assert.Equal(at + new Vec2(6, 0), u.Position);
    }
}
