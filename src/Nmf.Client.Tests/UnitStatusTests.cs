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

    [Fact]
    public void Describe_NewCloseCombatStates()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        u.Action = CombatAction.Throwing;
        Assert.Equal("Throwing", UnitStatus.Describe(u));
        u.Action = CombatAction.Melee;
        Assert.Equal("Melee", UnitStatus.Describe(u));
        u.Action = CombatAction.None;
        u.AssaultTarget = new UnitId(9);
        u.MoveTarget = new Vec2(2000, 1000);
        Assert.Equal("Assaulting", UnitStatus.Describe(u));
        u.AssaultTarget = null;
        u.MoveMode = MoveMode.Sneak;
        u.Stance = Stance.Crouching;
        Assert.Equal("Sneaking", UnitStatus.Describe(u));
        u.IsCaptured = true;
        Assert.Equal("Captured", UnitStatus.Describe(u));
    }

    [Fact]
    public void Looting_AndOutOfAmmo()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7, new WeaponDef("r", "R", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000));
        u.Action = CombatAction.Looting;
        Assert.Equal("Looting", UnitStatus.Describe(u));
        u.Action = CombatAction.None;
        u.Ammo = 0;
        u.Magazines = 0;
        Assert.Equal("Out of ammo", UnitStatus.Describe(u));
        Assert.Equal("Ammo 0+0", UnitStatus.AmmoText(u));
        u.Ammo = 3;
        u.Magazines = 12;
        Assert.Equal("Ammo 3+12", UnitStatus.AmmoText(u));
    }

    [Fact]
    public void TakingCover()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(3000, 1000), MoveMode.Run));
        sim.Step();
        u.TakingCover = true;
        Assert.Equal("Taking cover", UnitStatus.Describe(u));
    }

    [Fact]
    public void AttackRoles()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        u.AttackRole = AttackRole.Covering;
        Assert.Equal("Covering fire", UnitStatus.Describe(u));
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(3000, 1000), MoveMode.Run));
        sim.Step();
        u.AttackRole = AttackRole.Bounding;
        Assert.Equal("Bounding", UnitStatus.Describe(u));
    }

    [Fact]
    public void Describe_AreaFire()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7, new WeaponDef("r", "R", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000));
        u.AreaTarget = new Vec2(3000, 1000);
        Assert.Equal("Area fire", UnitStatus.Describe(u));
        u.Action = CombatAction.Aiming;
        Assert.Equal("Area fire", UnitStatus.Describe(u));
        u.Target = new UnitId(9); // a close enemy first
        Assert.Equal("Firing", UnitStatus.Describe(u));
    }

    [Fact]
    public void CardLine_IsShortEnoughForSevenCards()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7, new WeaponDef("r", "R", WeaponClass.Smg, 71, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000));
        u.Magazines = 2;
        u.Grenades = 2;
        Assert.Equal("71+2 · 2 gr · Free fire", UnitStatus.CardLine(u));
        u.FirePolicy = FirePolicy.HoldFire;
        Assert.Equal("71+2 · 2 gr · Hold fire", UnitStatus.CardLine(u));
    }
}
