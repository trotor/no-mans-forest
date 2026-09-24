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
        blue.Suppression = 200;
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
}
