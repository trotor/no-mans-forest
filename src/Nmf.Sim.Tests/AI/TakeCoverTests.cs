using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class TakeCoverTests
{
    private static readonly Vec2 ThreatEast = new CellCoord(38, 10).CenterCm;
    private static readonly Vec2 BehindRock = new CellCoord(12, 10).CenterCm;

    private static (Simulation Sim, Unit Man) Setup(bool rock = true, int nerve = 50)
    {
        var map = new GridMap(40, 20, ["none"]);
        if (rock)
            map[new CellCoord(13, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var sim = new Simulation(map, 1);
        var man = sim.SpawnUnit(Side.Blue, new CellCoord(10, 10).CenterCm, 7);
        man.Nerve = nerve;
        return (sim, man);
    }

    private static void ShootAt(Simulation sim, Unit man, Vec2 from, int amount = 40) =>
        MoraleSystem.AddSuppression(sim, man, amount, sim.Tick, [], from);

    private static void StepN(Simulation sim, int n)
    {
        for (int i = 0; i < n; i++) sim.Step();
    }

    [Fact]
    public void ShotAt_RunsToCoverBehindTheRock()
    {
        var (sim, man) = Setup();
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 5);
        Assert.True(man.TakingCover);
        Assert.Equal(BehindRock, man.MoveTarget);
        Assert.Equal(MoveMode.Run, man.MoveMode);
        StepN(sim, 60);
        Assert.Equal(BehindRock, man.Position);
        Assert.False(man.TakingCover);
        Assert.Equal(Stance.Crouching, man.TargetStance ?? man.Stance);
    }

    [Fact]
    public void ShotAtInTheOpen_GoesProne()
    {
        var (sim, man) = Setup(rock: false);
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 40);
        Assert.Equal(Stance.Prone, man.Stance);
        Assert.Null(man.MoveTarget);
    }

    [Fact]
    public void ToughMan_StaysPut()
    {
        var (sim, man) = Setup(nerve: CombatRules.ToughNerve);
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 40);
        Assert.Equal(new CellCoord(10, 10).CenterCm, man.Position);
        Assert.Equal(Stance.Standing, man.Stance);
        Assert.False(man.TakingCover);
    }

    [Fact]
    public void AlreadyBehindCover_CrouchesInPlace()
    {
        var (sim, man) = Setup();
        man.Position = BehindRock;
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 30);
        Assert.Equal(BehindRock, man.Position);
        Assert.Equal(Stance.Crouching, man.Stance);
    }

    [Fact]
    public void SecondShotSoonAfter_NoNewReaction_AndOrdersAreObeyed()
    {
        var (sim, man) = Setup();
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 5);
        var away = new CellCoord(5, 15).CenterCm;
        sim.Submit(Side.Blue, new MoveOrder(man.Id, away, MoveMode.Walk));
        sim.Step();
        Assert.False(man.TakingCover);
        for (int i = 0; i < 10; i++)
        {
            ShootAt(sim, man, ThreatEast);
            StepN(sim, 10);
            Assert.Equal(away, man.MoveTarget ?? away);
            Assert.False(man.TakingCover);
        }
    }

    [Fact]
    public void AutoMover_StopsForCover_ToughOneKeepsGoing()
    {
        var goal = new CellCoord(10, 2).CenterCm;
        var (sim, man) = Setup();
        sim.Submit(Side.Blue, new MoveOrder(man.Id, goal, MoveMode.Auto));
        sim.Step();
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 5);
        Assert.True(man.TakingCover);
        Assert.False(man.AutoPace);

        var (sim2, tough) = Setup(nerve: 90);
        sim2.Submit(Side.Blue, new MoveOrder(tough.Id, goal, MoveMode.Auto));
        sim2.Step();
        ShootAt(sim2, tough, ThreatEast);
        StepN(sim2, 5);
        Assert.False(tough.TakingCover);
        Assert.Equal(goal, tough.MoveTarget);
    }

    [Fact]
    public void AssaultAndRunOrder_DoNotReact()
    {
        var (sim, man) = Setup();
        var red = sim.SpawnUnit(Side.Red, new CellCoord(30, 10).CenterCm, 7);
        sim.Submit(Side.Blue, new AssaultOrder(man.Id, red.Id));
        sim.Step();
        ShootAt(sim, man, red.Position);
        StepN(sim, 5);
        Assert.False(man.TakingCover);
        Assert.Equal(red.Id, man.AssaultTarget);

        var (sim2, runner) = Setup();
        var goal = new CellCoord(10, 2).CenterCm;
        sim2.Submit(Side.Blue, new MoveOrder(runner.Id, goal, MoveMode.Run));
        sim2.Step();
        ShootAt(sim2, runner, ThreatEast);
        StepN(sim2, 5);
        Assert.False(runner.TakingCover);
        Assert.Equal(goal, runner.MoveTarget);
    }

    [Fact]
    public void StanceOrdered_DoesNotReact()
    {
        var (sim, man) = Setup();
        sim.Submit(Side.Blue, new SetStanceOrder(man.Id, Stance.Standing));
        sim.Step();
        ShootAt(sim, man, ThreatEast);
        StepN(sim, 30);
        Assert.False(man.TakingCover);
        Assert.Equal(Stance.Standing, man.Stance);
    }

    [Fact]
    public void GrenadeThreat_CoverShieldsFromTheBlast()
    {
        var (sim, man) = Setup();
        var blast = new CellCoord(17, 10).CenterCm;
        ShootAt(sim, man, blast, 100);
        StepN(sim, 5);
        Assert.NotNull(man.MoveTarget);
        Assert.True(CoverFinder.CoveredAt(sim.Map, man.MoveTarget!.Value.ToCell(), blast) > 0);
    }

    [Fact]
    public void TwoMenSameMoment_DifferentCells()
    {
        var (sim, man) = Setup();
        var other = sim.SpawnUnit(Side.Blue, new CellCoord(10, 11).CenterCm, 7);
        ShootAt(sim, man, ThreatEast);
        ShootAt(sim, other, ThreatEast);
        StepN(sim, 5);
        Assert.True(man.TakingCover && other.TakingCover);
        Assert.NotEqual(man.MoveTarget, other.MoveTarget);
    }

    [Fact]
    public void SpawnedMan_HasDefaultNerve()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        Assert.Equal(CombatRules.DefaultNerve, sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7).Nerve);
    }
}
