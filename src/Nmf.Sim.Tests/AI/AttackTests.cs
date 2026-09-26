using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class AttackTests
{
    /// <summary>Four armed Finns in a row 150 m south of an unarmed Soviet, open ground; everyone sees everyone.</summary>
    private static (Simulation Sim, List<Unit> Blues, Unit Red) Setup(int redDistanceM = 150)
    {
        var sim = new Simulation(new GridMap(100, 220, ["none"]), 1);
        var blues = Enumerable.Range(0, 4).Select(i => sim.SpawnUnit(Side.Blue, new Vec2(4050 + i * 300, 20050), 8, TestWeapons.Rifle(lethality: 0))).ToList();
        var red = sim.SpawnUnit(Side.Red, new Vec2(4500, 20050 - redDistanceM * 100), 8);
        for (int i = 0; i < 30; i++) sim.Step();
        return (sim, blues, red);
    }

    private static void Attack(Simulation sim, IEnumerable<Unit> men, Unit target)
    {
        foreach (var man in men)
            sim.Submit(Side.Blue, new AttackOrder(man.Id, target.Id));
    }

    private static void StepN(Simulation sim, int n)
    {
        for (int i = 0; i < n; i++) sim.Step();
    }

    [Fact]
    public void Order_FormsOneGroup_HalfBoundsWhileHalfCovers()
    {
        var (sim, blues, red) = Setup();
        Attack(sim, blues, red);
        StepN(sim, 6);
        Assert.Single(sim.AttackGroups);
        Assert.All(new[] { blues[0], blues[2] }, b => Assert.Equal(AttackRole.Bounding, b.AttackRole));
        Assert.All(new[] { blues[1], blues[3] }, b => Assert.Equal(AttackRole.Covering, b.AttackRole));
        Assert.All(new[] { blues[0], blues[2] }, b => Assert.NotNull(b.MoveTarget));
        Assert.All(new[] { blues[1], blues[3] }, b => Assert.Null(b.MoveTarget));
        Assert.All(blues, b => Assert.Equal(red.Id, b.OrderedTarget));
    }

    [Fact]
    public void Bound_GoesAboutTwentyFiveMetresCloser_ButNotPastTheAssaultLine()
    {
        var (sim, blues, red) = Setup();
        long start = (blues[0].Position - red.Position).Length;
        Attack(sim, blues, red);
        StepN(sim, 6);
        long goal = (blues[0].MoveTarget!.Value - red.Position).Length;
        Assert.InRange(start - goal, 1500, 3500);

        var (sim2, blues2, red2) = Setup(redDistanceM: 32);
        Attack(sim2, blues2, red2);
        StepN(sim2, 6);
        if (blues2[0].MoveTarget is { } near)
            Assert.True((near - red2.Position).Length >= 1500, "bounded too close before the final assault");
    }

    [Fact]
    public void Halves_Swap_WhenTheBoundersAreDown_InCover()
    {
        var (sim, blues, red) = Setup();
        foreach (var b in blues) // no shooting, so the target lasts long enough to watch the dash
            sim.Submit(Side.Blue, new SetFirePolicyOrder(b.Id, FirePolicy.HoldFire));
        sim.Step();
        Attack(sim, blues, red);
        StepN(sim, 6);
        for (int i = 0; i < 400 && blues[0].AttackRole == AttackRole.Bounding; i++) sim.Step();
        Assert.Equal(AttackRole.Covering, blues[0].AttackRole);
        Assert.Equal(AttackRole.Bounding, blues[1].AttackRole);
        Assert.NotNull(blues[1].MoveTarget);
    }

    [Fact]
    public void CloseToASuppressedTarget_TheFinalAssaultBegins()
    {
        var (sim, blues, red) = Setup(redDistanceM: 22);
        red.Suppression = 400;
        Attack(sim, blues, red);
        StepN(sim, 6);
        Assert.All(blues, b => Assert.Equal(red.Id, b.AssaultTarget));
        Assert.Empty(sim.AttackGroups);
    }

    [Fact]
    public void TargetDown_EndsTheAttack()
    {
        var (sim, blues, red) = Setup();
        Attack(sim, blues, red);
        StepN(sim, 6);
        Damage.SetWound(sim, red, WoundLevel.Dead, sim.Tick, []);
        StepN(sim, 6);
        Assert.Empty(sim.AttackGroups);
        Assert.All(blues, b => Assert.Equal(AttackRole.None, b.AttackRole));
    }

    [Fact]
    public void AnotherOrder_TakesTheManOutOfTheAttack()
    {
        var (sim, blues, red) = Setup();
        Attack(sim, blues, red);
        StepN(sim, 6);
        sim.Submit(Side.Blue, new StopOrder(blues[0].Id));
        StepN(sim, 6);
        Assert.Equal(AttackRole.None, blues[0].AttackRole);
        Assert.Equal(3, sim.AttackGroups[0].Members.Count);
    }

    [Fact]
    public void Rejections()
    {
        var (sim, blues, red) = Setup();
        sim.Submit(Side.Blue, new AttackOrder(blues[0].Id, blues[1].Id));
        Assert.Contains(sim.Step(), e => e is OrderRejected { Reason: "invalid target" });
        blues[2].Morale = CombatRules.MaxMorale;
        MoraleSystem.AddSuppression(sim, blues[2], 700, sim.Tick, []);
        sim.Submit(Side.Blue, new AttackOrder(blues[2].Id, red.Id));
        Assert.Contains(sim.Step(), e => e is OrderRejected { Reason: "unit is pinned" });
    }

    [Fact]
    public void Attackers_DoNotRunOffForCoverWhenFiredOn()
    {
        var (sim, blues, red) = Setup();
        Attack(sim, blues, red);
        StepN(sim, 6);
        MoraleSystem.AddSuppression(sim, blues[1], 40, sim.Tick, [], red.Position);
        StepN(sim, 6);
        Assert.False(blues[1].TakingCover);
        Assert.Equal(AttackRole.Covering, blues[1].AttackRole);
    }

    [Fact]
    public void TargetDown_TheAttackMovesOnToTheNextSeenEnemyOfThePosition()
    {
        var (sim, blues, red) = Setup();
        foreach (var b in blues)
            sim.Submit(Side.Blue, new SetFirePolicyOrder(b.Id, FirePolicy.HoldFire));
        var second = sim.SpawnUnit(Side.Red, red.Position + new Vec2(1500, 0), 8);
        sim.SpawnUnit(Side.Red, new Vec2(500, 500), 8); // seen too, but 60 m off: not part of the position
        StepN(sim, 30); // see the new men
        Attack(sim, blues, red);
        StepN(sim, 6);
        Damage.SetWound(sim, red, WoundLevel.Dead, sim.Tick, []);
        StepN(sim, 6);
        var group = Assert.Single(sim.AttackGroups);
        Assert.Equal(second.Id, group.Target);
        Assert.All(blues, b => Assert.Equal(second.Id, b.OrderedTarget));
    }
}
