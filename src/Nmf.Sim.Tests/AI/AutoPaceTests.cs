using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Events;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class AutoPaceTests
{
    private static (Simulation Sim, Unit Blue) Setup(bool withEnemy)
    {
        var sim = new Simulation(new GridMap(80, 20, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        if (withEnemy)
            sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        return (sim, blue);
    }

    [Fact]
    public void AutoMove_WithNoEnemyInSight_Walks()
    {
        var (sim, blue) = Setup(withEnemy: false);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(5050, 1850), MoveMode.Auto));
        sim.Step();
        Assert.Equal(MoveMode.Walk, blue.MoveMode);
        Assert.True(blue.AutoPace);
    }

    [Fact]
    public void AutoMove_WithAnEnemyInSightNearby_Sneaks()
    {
        var (sim, blue) = Setup(withEnemy: true);
        for (int i = 0; i < 25; i++) sim.Step();
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(1050, 1850), MoveMode.Auto));
        sim.Step();
        Assert.Equal(MoveMode.Sneak, blue.MoveMode);
        Assert.Equal(Stance.Crouching, blue.TargetStance ?? blue.Stance);
    }

    [Fact]
    public void AutoMove_UnderFire_Runs()
    {
        var (sim, blue) = Setup(withEnemy: false);
        MoraleSystem.AddSuppression(sim, blue, 200, sim.Tick, []);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(5050, 1850), MoveMode.Auto));
        sim.Step();
        Assert.Equal(MoveMode.Run, blue.MoveMode);
    }

    [Fact]
    public void IdleStandingSoldier_CrouchesWhenAnEnemyIsInSightNearby()
    {
        var (sim, blue) = Setup(withEnemy: true);
        for (int i = 0; i < 40; i++) sim.Step();
        Assert.Equal(Stance.Crouching, blue.TargetStance ?? blue.Stance);
    }

    [Fact]
    public void PlayerStanceOrder_IsNotUndoneByTheAutoCrouch()
    {
        var (sim, blue) = Setup(withEnemy: true);
        sim.Submit(Side.Blue, new SetStanceOrder(blue.Id, Stance.Standing));
        for (int i = 0; i < 60; i++) sim.Step();
        Assert.Equal(Stance.Standing, blue.Stance);
        Assert.Null(blue.TargetStance);
    }

    [Fact]
    public void AimingSoldier_IsNotMadeToCrouch()
    {
        var (sim, blue) = Setup(withEnemy: true);
        for (int i = 0; i < 21; i++) sim.Step();
        blue.TargetStance = null;
        blue.Stance = Stance.Standing;
        blue.Action = Nmf.Sim.Combat.CombatAction.Aiming;
        blue.ActionTicksLeft = 50;
        for (int i = 0; i < 5; i++) sim.Step();
        Assert.Null(blue.TargetStance);
    }

    [Fact]
    public void ShotAtWhileSneaking_Runs_ThenSneaksAgainWhenTheFireStops()
    {
        var (sim, blue) = Setup(withEnemy: true);
        blue.Nerve = 90; // a tough man keeps going instead of taking cover
        for (int i = 0; i < 25; i++) sim.Step();
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(1050, 1850), MoveMode.Auto));
        sim.Step();
        MoraleSystem.AddSuppression(sim, blue, 40, sim.Tick, []); // a single rifle miss
        for (int i = 0; i < 10; i++) sim.Step();
        Assert.Equal(MoveMode.Run, blue.MoveMode);
        blue.Suppression = 0;
        for (int i = 0; i < CombatRules.UnderFireTicks + 10; i++) sim.Step();
        if (blue.MoveTarget is not null)
            Assert.Equal(MoveMode.Sneak, blue.MoveMode);
    }

    [Fact]
    public void AutoMovingRifleman_FiresBackWithoutStopping()
    {
        var sim = new Simulation(new GridMap(80, 40, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle(spread: 6));
        sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7, TestWeapons.Rifle(spread: 6));
        for (int i = 0; i < 25; i++) sim.Step();
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(1050, 3850), MoveMode.Auto));
        bool firedOnTheMove = false;
        for (int i = 0; i < 200 && !firedOnTheMove; i++)
            firedOnTheMove = sim.Step().Any(e => e is ShotFired s && s.Shooter == blue.Id) && blue.MoveTarget is not null;
        Assert.True(firedOnTheMove);
    }

    [Fact]
    public void RunOrder_IsASprintWithoutFiring()
    {
        var sim = new Simulation(new GridMap(80, 40, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle(spread: 6));
        sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        for (int i = 0; i < 25; i++) sim.Step();
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(1050, 3850), MoveMode.Run));
        for (int i = 0; i < 60; i++)
            Assert.DoesNotContain(sim.Step(), e => e is ShotFired);
    }
}
