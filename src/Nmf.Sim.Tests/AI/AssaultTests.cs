using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class AssaultTests
{
    private static (Simulation Sim, Unit Blue, Unit Red) Setup(GridMap? map = null, Vec2? redPos = null)
    {
        var sim = new Simulation(map ?? new GridMap(80, 30, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var red = sim.SpawnUnit(Side.Red, redPos ?? new Vec2(3050, 1050), 7);
        for (int i = 0; i < 25; i++) sim.Step(); // both see each other
        return (sim, blue, red);
    }

    private static string? RejectionOf(Simulation sim, Order order)
    {
        sim.Submit(Side.Blue, order);
        return sim.Step().OfType<OrderRejected>().FirstOrDefault(r => r.Order == order)?.Reason;
    }

    [Fact]
    public void Assault_RunsAtTheTargetAndEndsInMelee()
    {
        var (sim, blue, red) = Setup();
        sim.Submit(Side.Blue, new AssaultOrder(blue.Id, red.Id));
        sim.Step();
        Assert.Equal(red.Id, blue.AssaultTarget);
        Assert.Equal(MoveMode.Run, blue.MoveMode);
        var events = new List<SimEvent>();
        for (int i = 0; i < 400 && !events.OfType<MeleeStarted>().Any(); i++) events.AddRange(sim.Step());
        Assert.Contains(events, e => e is MeleeStarted);
    }

    [Fact]
    public void Assault_FollowsAMovingTarget()
    {
        var (sim, blue, red) = Setup();
        sim.Submit(Side.Blue, new AssaultOrder(blue.Id, red.Id));
        sim.Step();
        red.Position = new Vec2(3050, 2050);
        for (int i = 0; i < 6; i++) sim.Step();
        Assert.Equal(new Vec2(3050, 2050), blue.MoveTarget);
    }

    [Fact]
    public void Assault_EndsWhenTheTargetFalls()
    {
        var (sim, blue, red) = Setup();
        sim.Submit(Side.Blue, new AssaultOrder(blue.Id, red.Id));
        sim.Step();
        Damage.SetWound(sim, red, WoundLevel.Dead, sim.Tick, []);
        for (int i = 0; i < 6; i++) sim.Step();
        Assert.Null(blue.AssaultTarget);
        Assert.Null(blue.MoveTarget);
    }

    [Fact]
    public void Assault_OnAnUnreachableTarget_IsRejected()
    {
        var map = new GridMap(80, 30, ["none"]);
        for (int x = 28; x <= 32; x++) { map[new CellCoord(x, 8)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(x, 12)].ExtraMoveCost = CellData.Impassable; }
        for (int y = 8; y <= 12; y++) { map[new CellCoord(28, y)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(32, y)].ExtraMoveCost = CellData.Impassable; }
        var (sim, blue, red) = Setup(map, new CellCoord(30, 10).CenterCm);
        Assert.Equal("target not reachable", RejectionOf(sim, new AssaultOrder(blue.Id, red.Id)));
        Assert.Null(blue.AssaultTarget);
    }

    [Fact]
    public void Assault_OnAFriendOrWhilePinned_IsRejected()
    {
        var (sim, blue, red) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        Assert.Equal("invalid target", RejectionOf(sim, new AssaultOrder(blue.Id, friend.Id)));
        blue.Suppression = 600;
        blue.MoraleState = MoraleState.Pinned;
        Assert.Equal("unit is pinned", RejectionOf(sim, new AssaultOrder(blue.Id, red.Id)));
    }

    [Fact]
    public void SoldierWithGrenades_ThrowsAtAProneEnemyWithinRange()
    {
        var sim = new Simulation(new GridMap(80, 30, ["none"]), 1);
        sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, grenade: GrenadeDefTests.Test());
        var red = sim.SpawnUnit(Side.Red, new Vec2(2550, 1050), 7);
        red.Stance = Stance.Prone;
        var events = new List<SimEvent>();
        for (int i = 0; i < 160; i++) events.AddRange(sim.Step()); // a prone man takes a while to spot
        Assert.Contains(events, e => e is GrenadeThrown);
    }

    [Fact]
    public void Assault_HoldsBackFromAFriendlyLiveGrenade()
    {
        var (sim, blue, red) = Setup();
        var thrower = sim.SpawnUnit(Side.Blue, new Vec2(1050, 250), 7, null, grenade: GrenadeDefTests.Test(fuse: 200));
        sim.Submit(Side.Blue, new AssaultOrder(blue.Id, red.Id));
        sim.Step();
        sim.AddGrenade(thrower, new Vec2(2050, 1050), sim.Tick); // live grenade on the assault path, 10 m ahead
        bool held = false;
        for (int i = 0; i < 150; i++)
        {
            sim.Step();
            Assert.True((blue.Position - new Vec2(2050, 1050)).Length > 400 || sim.Grenades.Count == 0, $"ran into the live grenade at {blue.Position}");
            held |= blue.MoveTarget is null && sim.Grenades.Count > 0;
        }
        Assert.True(held);
    }

    [Fact]
    public void BrokenAssaulter_DropsTheAssault()
    {
        var (sim, blue, red) = Setup();
        sim.Submit(Side.Blue, new AssaultOrder(blue.Id, red.Id));
        sim.Step();
        blue.Morale = 0;
        MoraleSystem.Check(sim, blue, sim.Tick, []);
        Assert.Null(blue.AssaultTarget);
    }
}
