using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Replays;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.World;
using static Nmf.Sim.Tests.Tactics.TacticPlannerTests;

namespace Nmf.Sim.Tests.Tactics;

public class PlannedAttackTests
{
    /// <summary>Six Finns 90 m south of the post, rifles that cannot hurt, firing at will; the reds hold their fire.</summary>
    private static Field NewField(int reds = 1, int blueY = 150, int blues = 6)
    {
        var f = new Field(blues: blues, reds: reds, blueY: blueY);
        f.Blues[0].Weapon = Lmg;
        foreach (var b in f.Blues)
        {
            b.FirePolicy = FirePolicy.FireAtWill;
            b.Magazines = 12; // as a Finnish rifleman carries; the test rifle comes with none, and area fire needs spares
        }
        foreach (var r in f.Reds)
            r.FirePolicy = FirePolicy.HoldFire;
        return f;
    }

    private static TacticPlan Start(Field f, TacticDef tactic, TacticPlan? plan = null)
    {
        plan ??= f.Plan(tactic)!;
        f.Sim.Submit(Side.Blue, new PlannedAttackOrder(plan.FireTeam[0].Man, plan, tactic));
        f.Sim.Step();
        return plan;
    }

    /// <summary>
    /// Steps until the condition holds (checked before each step); the events on the way. The test rifles cannot kill but
    /// still wound and break: the reds are patched up and kept in place every tick (a dead one stays dead), so the position
    /// stands while the attack runs.
    /// </summary>
    private static List<SimEvent> RunUntil(Field f, Func<bool> done, int maxTicks, Action? everyTick = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < maxTicks && !done(); i++)
        {
            foreach (var red in f.Sim.Units.Where(u => u.Side == Side.Red && u.Wound < WoundLevel.Dead))
            {
                red.Wound = WoundLevel.None;
                red.Morale = red.BaseMorale;
                if (red.MoraleState == MoraleState.Broken)
                    red.MoraleState = MoraleState.Steady;
                Movement.ClearPath(red); // they hold the position: a broken man would run off with it
            }
            everyTick?.Invoke();
            events.AddRange(f.Sim.Step());
        }
        return events;
    }

    private static List<SimEvent> Run(Field f, int ticks, Action? everyTick = null) => RunUntil(f, () => false, ticks, everyTick);

    private static PlannedAttack Attack(Field f) => Assert.Single(f.Sim.PlannedAttacks);

    private static void Hold(IEnumerable<Unit> reds, int suppression)
    {
        foreach (var r in reds)
            r.Suppression = suppression;
    }

    private static Unit Man(Field f, UnitId id) => f.Sim.FindUnit(id)!;

    /// <summary>Into the assault phase: the fire team set and firing, the strike team at the form-up.</summary>
    private static void ToAssault(Field f, Action? everyTick = null) =>
        RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0 || Attack(f).Phase == PlannedPhase.Assault, 8_000, everyTick);

    [Fact]
    public void Order_StartsBothTeamsAtOnce()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        Run(f, 5);
        var attack = Attack(f);
        Assert.Equal(PlannedPhase.Deploy, attack.Phase);
        Assert.All(f.Blues, b => Assert.Equal(attack.Id, b.PlannedAttackId));
        Assert.All(plan.StrikeTeam, id => Assert.NotNull(Man(f, id).MoveTarget));
        Assert.All(plan.FireTeam, p => Assert.True(Man(f, p.Man).MoveTarget == p.At || (Man(f, p.Man).Position - p.At).LengthSquared < 200 * 200));
    }

    [Fact]
    public void FireTeamSet_GoesToSuppress_AndFires()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        var events = RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Assert.Equal(PlannedPhase.Suppress, Attack(f).Phase);
        Assert.Contains(events, e => e is PlannedAttackPhaseChanged { Phase: PlannedPhase.Suppress });
        var fireTeam = plan.FireTeam.Select(p => p.Man).ToHashSet();
        var shots = Run(f, 200).OfType<ShotFired>().Where(s => fireTeam.Contains(s.Shooter)).ToList();
        Assert.NotEmpty(shots);
    }

    [Fact]
    public void CoveringMen_AreaFireAtTheirMansPlace_EvenWhenTheSideSeesHim()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Run(f, 5);
        Assert.All(plan.FireTeam, p => Assert.Equal(f.Reds[0].Position, Man(f, p.Man).AreaTarget));
        Assert.All(plan.FireTeam, p => Assert.Equal(f.Reds[0].Id, Man(f, p.Man).OrderedTarget));
    }

    [Fact]
    public void FireIsSharedAcrossThePosition_LmgOnTheLmg()
    {
        var f = NewField(reds: 2);
        f.Reds[1].Weapon = Lmg;
        var plan = Start(f, FireAndMovement);
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Run(f, 5);
        Assert.Equal(f.Reds[1].Id, f.Blues[0].OrderedTarget); // our machine gun on theirs
        var others = plan.FireTeam.Select(p => Man(f, p.Man)).Where(m => m != f.Blues[0]).ToList();
        Assert.Contains(others, m => m.OrderedTarget == f.Reds[0].Id);
    }

    [Fact]
    public void Assault_WaitsUntilEverySeenManOfThePositionIsSuppressed()
    {
        var f = NewField(reds: 2);
        var plan = Start(f, FireAndMovement);
        ToAssault(f, () => Hold(f.Reds, 0));
        Assert.Equal(PlannedPhase.Assault, Attack(f).Phase);
        Run(f, 300, () => { f.Reds[0].Suppression = 900; f.Reds[1].Suppression = 0; });
        Assert.All(plan.StrikeTeam, id => Assert.Null(Man(f, id).AssaultTarget));
        Run(f, 10, () => Hold(f.Reds, 900));
        Assert.All(plan.StrikeTeam, id => Assert.NotNull(Man(f, id).AssaultTarget));
    }

    [Fact]
    public void Assault_AfterWait_GoesWhenHalfSuppressed()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        ToAssault(f, () => Hold(f.Reds, 0));
        Run(f, 300, () => Hold(f.Reds, 130));
        Assert.All(plan.StrikeTeam, id => Assert.Null(Man(f, id).AssaultTarget));
        Run(f, 120, () => Hold(f.Reds, 130));
        Assert.All(plan.StrikeTeam, id => Assert.NotNull(Man(f, id).AssaultTarget));
    }

    [Fact]
    public void PositionTaken_EndsWithSuccess_NoNewTarget()
    {
        var f = NewField();
        var beyond = f.Sim.SpawnUnit(Side.Red, M(150, 20), 8);
        beyond.FirePolicy = FirePolicy.HoldFire;
        f.See(beyond);
        Start(f, FireAndMovement);
        Run(f, 50);
        f.Reds[0].Wound = WoundLevel.Dead;
        var events = Run(f, 10);
        Assert.Contains(events, e => e is PlannedAttackEnded { Success: true });
        Assert.Empty(f.Sim.PlannedAttacks);
        Assert.All(f.Blues, b => Assert.Null(b.PlannedAttackId));
        Assert.All(f.Blues, b => Assert.Null(b.AssaultTarget));
        Assert.All(f.Blues, b => Assert.NotEqual(beyond.Id, b.OrderedTarget));
    }

    [Fact]
    public void Casualties_Halt_FireTeamKeepsAreaFire()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Man(f, plan.StrikeTeam[0]).Wound = WoundLevel.Serious;
        var events = Run(f, 10);
        var halt = Assert.Single(events.OfType<PlannedAttackHalted>());
        Assert.Equal(HaltReason.Casualties, halt.Reason);
        Assert.Equal(1, halt.Casualties);
        Assert.Empty(f.Sim.PlannedAttacks);
        Assert.All(plan.FireTeam, p => Assert.NotNull(Man(f, p.Man).AreaTarget));
        Assert.All(f.Blues, b => Assert.Null(b.PlannedAttackId));
    }

    [Fact]
    public void LeaderDown_Halts()
    {
        var f = NewField();
        f.Blues[2].IsLeader = true;
        Start(f, FireAndMovement);
        f.Blues[2].Wound = WoundLevel.Dead;
        Assert.Equal(HaltReason.LeaderDown, Assert.Single(Run(f, 10).OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void StrikeTeamPinned_Halts()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        var events = Run(f, 260, () =>
        {
            foreach (var id in plan.StrikeTeam)
                Man(f, id).Suppression = 900;
        });
        Assert.Equal(HaltReason.StrikePinned, Assert.Single(events.OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void NotSuppressedByGiveUp_Halts()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        ToAssault(f, () => Hold(f.Reds, 0));
        var events = Run(f, 1_300, () => Hold(f.Reds, 0));
        Assert.Equal(HaltReason.NotSuppressed, Assert.Single(events.OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void NoFireAtAll_HaltsWithNoFire()
    {
        var f = NewField();
        foreach (var b in f.Blues)
        {
            b.FirePolicy = FirePolicy.HoldFire;
            b.Magazines = 0; // nothing to spare for area fire, and told to hold fire
        }
        Start(f, FireAndMovement);
        var events = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0, 3_000);
        Assert.Equal(HaltReason.NoFire, Assert.Single(events.OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void PositionMovingAway_HaltsWithTargetMoved()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        Run(f, 20);
        f.Reds[0].Position = M(150, 30); // 30 m further off, still seen
        Assert.Equal(HaltReason.TargetMoved, Assert.Single(Run(f, 15).OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void ForestContactAt26m_FireTeamFallsBackAndActuallyFires()
    {
        var f = NewField(blueY: 86);
        var plan = Start(f, FireAndMovement);
        Assert.All(plan.FireTeam, p => Assert.True((p.At - f.Reds[0].Position).LengthSquared >= 3_300L * 3_300));
        var fireTeam = plan.FireTeam.Select(p => p.Man).ToHashSet();
        var events = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0 || Attack(f).Phase == PlannedPhase.Assault, 3_000);
        Assert.Contains(events.OfType<ShotFired>(), s => fireTeam.Contains(s.Shooter));
    }

    [Fact]
    public void PinnedManInThePlan_IsLeftOut_OthersGo()
    {
        var f = NewField();
        var plan = f.Plan(FireAndMovement)!;
        f.Blues[3].Suppression = 900;
        f.Blues[3].MoraleState = MoraleState.Pinned;
        Start(f, FireAndMovement, plan);
        Assert.Null(f.Blues[3].PlannedAttackId);
        Assert.All(f.Blues.Where(b => b != f.Blues[3]), b => Assert.NotNull(b.PlannedAttackId));
    }

    [Fact]
    public void AnotherOrder_TakesAManOut()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        f.Sim.Submit(Side.Blue, new MoveOrder(f.Blues[4].Id, M(10, 290)));
        Run(f, 6);
        Assert.Null(f.Blues[4].PlannedAttackId);
        Assert.DoesNotContain(f.Blues[4].Id, Attack(f).Members);
        Assert.Equal(5, Attack(f).Members.Count);
    }

    [Fact]
    public void LongFire_NeverAssaults()
    {
        var f = NewField();
        Start(f, LongFire);
        var events = Run(f, 2_500, () => Hold(f.Reds, 900));
        Assert.DoesNotContain(events, e => e is PlannedAttackHalted or PlannedAttackEnded);
        Assert.Equal(PlannedPhase.Suppress, Attack(f).Phase);
        Assert.All(f.Blues, b => Assert.Null(b.AssaultTarget));
    }

    [Fact]
    public void Replay_ReproducesThePlannedAttack()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        for (int i = 0; i < 600; i++)
            f.Sim.Step(); // as it falls out, without patching anyone up: the replay does not either
        var replay = ReplayRunner.Capture(f.Sim);

        var g = NewField();
        ReplayRunner.Run(g.Sim, replay);
        Assert.Equal(StateHash.Compute(f.Sim), StateHash.Compute(g.Sim));
    }
}
