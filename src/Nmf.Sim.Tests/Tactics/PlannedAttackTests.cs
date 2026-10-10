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
        f.Sim.Submit(Side.Blue, new PlannedAttackOrder(plan.FirstMan, plan, tactic));
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
    public void Deploy_FireTeamGoes_StrikeTeamWaitsOnTheEdgeOfOpenGround_UntilTheFireBaseFires()
    {
        var f = NewField(); // the strike team starts on the open field the post sees
        var plan = Start(f, FireAndMovement);
        Assert.True(plan.RouteExposed![0]);
        Run(f, 30);
        var attack = Attack(f);
        Assert.Equal(PlannedPhase.Deploy, attack.Phase);
        Assert.All(f.Blues, b => Assert.Equal(attack.Id, b.PlannedAttackId));
        Assert.All(plan.FireTeam, p => Assert.True(Man(f, p.Man).MoveTarget == p.At || (Man(f, p.Man).Position - p.At).LengthSquared < 200 * 200));
        Assert.All(plan.StrikeTeam, id => Assert.Null(Man(f, id).MoveTarget));
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Run(f, 15);
        Assert.All(plan.StrikeTeam, id => Assert.NotNull(Man(f, id).MoveTarget));
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
    public void PositionMovingAway_IsPlannedAgain_ThreeTimes_ThenHalts()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        Run(f, 20);
        for (int move = 1; move <= 3; move++)
        {
            f.Reds[0].Position = move % 2 == 1 ? M(150, 35) : M(150, 60); // 25 m off each time, still seen
            var events = Run(f, 15);
            Assert.DoesNotContain(events, e => e is PlannedAttackHalted);
            Assert.Contains(events, e => e is PlannedAttackPhaseChanged { Phase: PlannedPhase.Deploy });
            Assert.Equal(f.Reds[0].Position, Attack(f).Plan.Aim);
        }
        f.Reds[0].Position = M(150, 60); // a fourth time: enough
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

    [Fact]
    public void AWoundFromBeforeTheOrder_IsNoCasualtyOfTheAttack()
    {
        var f = NewField();
        f.Blues[5].Wound = WoundLevel.Serious; // hit earlier, still in the fight
        Start(f, FireAndMovement);
        var events = Run(f, 30);
        Assert.DoesNotContain(events, e => e is PlannedAttackHalted);
        Assert.Single(f.Sim.PlannedAttacks);
    }

    [Fact]
    public void AManOfThePositionFalling_IsNotThePositionMoving()
    {
        var f = NewField(reds: 2);
        f.Reds[1].Position = M(174, 60); // 24 m east of the first: when he falls the centre jumps 24 m from where the first stood
        f.See(f.Reds[1]);
        Start(f, FireAndMovement);
        Run(f, 20);
        f.Reds[0].Wound = WoundLevel.Dead;
        var events = Run(f, 15);
        Assert.DoesNotContain(events, e => e is PlannedAttackHalted);
    }

    [Fact]
    public void AManPinnedOnTheWay_GoesOnAfterwards_AndIsNotAtHisSpot()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        var striker = Man(f, plan.StrikeTeam[0]);
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300); // the strike team sets off once the fire base fires
        Run(f, 100);
        Assert.NotNull(striker.MoveTarget);
        Run(f, 60, () => striker.Suppression = 900); // pinned: he drops where he is
        Assert.Null(striker.MoveTarget);
        Run(f, 300, () => striker.Suppression = 0);
        Assert.True(striker.MoveTarget is not null || (striker.Position - plan.FormUp[0].At).LengthSquared < 1_000L * 1_000,
            "a man pinned on the way never went on");
    }

    [Fact]
    public void FireBaseThatSeesNothing_HaltsWithNoFire_EvenWithMagazinesForAreaFire()
    {
        var f = NewField();
        for (int x = 0; x < 300; x++)
            for (int y = 64; y <= 66; y++)
            {
                ref var cell = ref f.Sim.Map[new CellCoord(x, y)];
                cell.ObstacleHeightCm = 400; // a tall hedge south of the post: nobody sees it, bullets still fly
                cell.ConcealmentPerM = 255;
            }
        Start(f, LongFire);
        var events = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0, 4_000);
        Assert.Equal(HaltReason.NoFire, Assert.Single(events.OfType<PlannedAttackHalted>()).Reason);
    }

    [Fact]
    public void NobodySeen_NoAssault()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        ToAssault(f, () => Hold(f.Reds, 0));
        var hole = f.Reds[0].Position.ToCell();
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (dx != 0 || dy != 0)
                {
                    ref var cell = ref f.Sim.Map[new CellCoord(hole.X + dx, hole.Y + dy)];
                    cell.ObstacleHeightCm = 300; // a thicket closes round him: lost from sight
                    cell.ConcealmentPerM = 255;
                }
        Run(f, 500, () => Hold(f.Reds, 0));
        Assert.NotEqual(Nmf.Sim.Vision.ContactLevel.Visible, f.Sim.Knowledge(Side.Blue).LevelOf(f.Reds[0].Id));
        Assert.All(plan.StrikeTeam, id => Assert.Null(Man(f, id).AssaultTarget));
    }

    [Fact]
    public void AfterAHalt_NobodyWalksOnToTheFormUp_AndTheStrikeTeamTakesCoverNearBy()
    {
        var f = NewField();
        for (int x = 130; x <= 165; x++)
        {
            ref var cell = ref f.Sim.Map[new CellCoord(x, 147)];
            cell.ObstacleHeightCm = 60; // a low bank 3 m in front of where the strike team waits
            cell.Cover = 200;
        }
        var plan = Start(f, FireAndMovement);
        Run(f, 40);
        f.Blues[1].Wound = WoundLevel.Dead;
        Run(f, 10);
        Assert.Empty(f.Sim.PlannedAttacks);
        var strike = plan.StrikeTeam.Select(id => Man(f, id)).Where(m => !m.IsOutOfAction).ToList();
        Assert.All(strike, m => Assert.DoesNotContain(plan.FormUp, p => p.At == m.MoveTarget));
        Assert.Contains(strike, m => m.MoveTarget is { } goal && goal.ToCell().Y == 148);
    }

    [Fact]
    public void AllMenGivenOtherOrders_EndsQuietly_NotAsTheEnemyGone()
    {
        var f = NewField();
        Start(f, FireAndMovement);
        foreach (var b in f.Blues)
            f.Sim.Submit(Side.Blue, new StopOrder(b.Id));
        var ended = Assert.Single(Run(f, 10).OfType<PlannedAttackEnded>());
        Assert.True(ended.Disbanded);
        Assert.False(ended.Success);
    }

    [Fact]
    public void StartingInTheOpen_TheStrikeTeamWaitsInCoverNearBy()
    {
        var f = NewField();
        for (int x = 130; x <= 165; x++)
        {
            ref var cell = ref f.Sim.Map[new CellCoord(x, 147)];
            cell.ObstacleHeightCm = 60; // a low bank 3 m in front of the strike team
            cell.Cover = 200;
        }
        var plan = Start(f, FireAndMovement);
        Assert.True(plan.RouteExposed![0]);
        Run(f, 30);
        Assert.Contains(plan.StrikeTeam, id => Man(f, id).MoveTarget is { } goal && goal.ToCell().Y == 148
                                                || (Man(f, id).MoveTarget is null && Man(f, id).Position.ToCell().Y == 148));
    }

    [Fact]
    public void APositionMovingAfterTheAssault_CallsTheAssaultOff_AndPlansAgain()
    {
        var f = NewField();
        var plan = Start(f, FireAndMovement);
        ToAssault(f, () => Hold(f.Reds, 0));
        Run(f, 30, () => Hold(f.Reds, 900)); // in they go
        Assert.Contains(plan.StrikeTeam, id => Man(f, id).AssaultTarget is not null);
        f.Reds[0].Position = M(150, 35);
        var events = Run(f, 10);
        Assert.Contains(events, e => e is PlannedAttackPhaseChanged { Phase: PlannedPhase.Deploy });
        Assert.All(Attack(f).Members, id => Assert.Null(Man(f, id).AssaultTarget));
    }

    [Fact]
    public void Replanned_ThePositionIsTheNewOne()
    {
        var f = NewField(reds: 2);
        Start(f, FireAndMovement);
        Run(f, 20);
        f.Reds[0].Position = M(150, 35);
        f.Reds[1].Position = M(158, 35);
        Run(f, 10);
        Assert.Equal(Attack(f).Plan.Position, Attack(f).Position);
    }

    [Fact]
    public void WithoutFire_TheStrikeTeamStaysInCover_EvenOnceTheFireBaseIsSet()
    {
        var f = NewField();
        foreach (var b in f.Blues)
        {
            b.FirePolicy = FirePolicy.HoldFire;
            b.Magazines = 0; // the fire base cannot fire
        }
        var plan = Start(f, FireAndMovement);
        RunUntil(f, () => Attack(f).Phase != PlannedPhase.Deploy, 1_300);
        Assert.Equal(PlannedPhase.Suppress, Attack(f).Phase);
        Run(f, 100);
        Assert.All(plan.StrikeTeam, id => Assert.True((Man(f, id).Position - plan.FormUp[0].At).LengthSquared > 2_000L * 2_000
                                                      && Man(f, id).MoveTarget is null, $"{id} left cover with no fire"));
    }

    [Fact]
    public void HiddenStart_TheStrikeTeamGoesUpToTheLastHiddenPointOfItsWay()
    {
        var f = NewField();
        f.ClearAll();
        f.Forest((x, y) => (x > 140 && y > 115) || x < 105); // woods behind us and far west; an open gap the post sees between them
        foreach (var b in f.Blues)
            b.Position = M(170, 165); // deep in the southern woods, unseen
        var plan = f.Plan(FireAndMovement)!;
        int hold = plan.RouteExposed!.ToList().IndexOf(true);
        Assert.True(hold > 0, "the way starts hidden and crosses open ground");
        Start(f, FireAndMovement, plan);
        Run(f, 600);
        var lastHidden = plan.Route[hold - 1];
        Assert.All(plan.StrikeTeam, id => Assert.True(
            (Man(f, id).Position - lastHidden).LengthSquared <= 800L * 800 || Man(f, id).MoveTarget is { } goal && (goal - lastHidden).LengthSquared <= 800L * 800,
            $"{id} at {Man(f, id).Position} is not waiting by the last hidden point {lastHidden}"));
    }

    // --- Infiltration (spec 2026-10-10-infiltration-enemy-design §1) ---

    private static readonly TacticDef Infiltration = TacticPlannerTests.Infiltration;

    /// <summary>Old hands with two grenades each, the post 90 m off with woods on its flanks.</summary>
    private static Field InfiltrationField()
    {
        var f = NewField();
        foreach (var b in f.Blues)
        {
            b.Experience = 80;
            b.GrenadeType = Nmf.Sim.Tests.Combat.GrenadeDefTests.Test(range: 3_500, lethality: 0); // the post stands: the charge is what is looked at
            b.Grenades = 2;
        }
        return f;
    }

    private static List<Unit> Members(Field f) => Attack(f).Members.Select(id => Man(f, id)).ToList();

    [Fact]
    public void Infiltration_Sneaks_HoldingFire()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        Run(f, 20);
        Assert.Equal(PlannedPhase.Deploy, Attack(f).Phase);
        Assert.All(Members(f), m => Assert.Equal(FirePolicy.HoldFire, m.FirePolicy));
        var moving = Members(f).Where(m => m.MoveTarget is not null).ToList();
        Assert.NotEmpty(moving);
        Assert.All(moving, m => Assert.Equal(MoveMode.Sneak, m.MoveMode));
    }

    [Fact]
    public void Infiltration_AtTheFormUp_GrenadesThenIn()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        var events = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0 || Attack(f).Launched, 12_000);
        var attack = Attack(f);
        Assert.True(attack.Launched, "never went in");
        var thrown = events.OfType<GrenadeThrown>().ToList();
        Assert.NotEmpty(thrown);
        Assert.Empty(events.OfType<PlannedAttackHalted>());
        Assert.All(Members(f).Where(m => m.MoraleState != MoraleState.Pinned), m => Assert.NotNull(m.AssaultTarget));
        Assert.All(Members(f), m => Assert.Equal(FirePolicy.FireAtWill, m.FirePolicy));
    }

    [Fact]
    public void Infiltration_DiscoveredClose_GoesIn()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        var aim = f.Reds[0].Position;
        RunUntil(f, () => Members(f).Any(m => (m.Position - aim).LengthSquared <= 3_800L * 3_800), 12_000);
        Assert.False(Attack(f).Launched);
        Members(f)[0].LastSuppressedTick = f.Sim.Tick; // fired on: they are found
        Assert.Empty(Run(f, 10).OfType<PlannedAttackHalted>());
        Assert.True(Attack(f).Launched);
        Assert.Contains(Members(f), m => m.AssaultTarget is not null);
    }

    [Fact]
    public void Infiltration_DiscoveredFar_Halts_FirePolicyRestored()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        Run(f, 10);
        var men = Members(f);
        men[0].LastSuppressedTick = f.Sim.Tick;
        var halted = Assert.Single(Run(f, 10).OfType<PlannedAttackHalted>());
        Assert.Equal(HaltReason.Discovered, halted.Reason);
        Assert.All(men, m => Assert.Equal(FirePolicy.FireAtWill, m.FirePolicy));
    }

    [Fact]
    public void Infiltration_ManWhoBreaks_GetsHisFirePolicyBack()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        Run(f, 10);
        var man = Members(f)[1];
        man.MoraleState = MoraleState.Broken;
        Run(f, 10);
        Assert.DoesNotContain(man.Id, Attack(f).Members);
        Assert.Equal(FirePolicy.FireAtWill, man.FirePolicy);
    }

    [Fact]
    public void Infiltration_NeverHaltsForNoFire()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        var events = Run(f, PlannedAttackRunner.NoFireTicks + 200);
        Assert.DoesNotContain(events.OfType<PlannedAttackHalted>(), h => h.Reason == HaltReason.NoFire);
    }

    [Fact]
    public void Infiltration_Replan_StillSneaks()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        Run(f, 20);
        f.Reds[0].Position = M(150, 35);
        Run(f, 20);
        Assert.Equal("infiltration", Attack(f).Plan.TacticId);
        Assert.Equal(1, Attack(f).Replans);
        Assert.All(Members(f), m => Assert.Equal(FirePolicy.HoldFire, m.FirePolicy));
        Assert.All(Members(f).Where(m => m.MoveTarget is not null), m => Assert.Equal(MoveMode.Sneak, m.MoveMode));
    }

    private static Field GrenadierField(int reds = 1, int rangeCm = 3_500)
    {
        var f = NewField(reds: reds);
        foreach (var b in f.Blues)
        {
            b.Experience = 80;
            b.GrenadeType = Nmf.Sim.Tests.Combat.GrenadeDefTests.Test(fuse: 80, range: rangeCm, blast: 1_200, lethal: 500, lethality: 0);
            b.Grenades = 2;
        }
        return f;
    }

    [Fact]
    public void Infiltration_ChargeWaitsForItsOwnGrenades()
    {
        var f = GrenadierField();
        Start(f, Infiltration);
        long? launched = null;
        var events = RunUntil(f, () => launched is not null && f.Sim.Tick > launched + 200, 14_000,
            () => launched ??= f.Sim.PlannedAttacks.Count > 0 && Attack(f).Launched ? f.Sim.Tick : null);
        Assert.NotNull(launched);
        // The opening grenades (thrown before the charge; the charge throws more on its way, as any assault does).
        var opening = events.OfType<GrenadeThrown>().Where(t => t.Tick < launched).ToList();
        Assert.NotEmpty(opening);
        Assert.All(opening, t => Assert.True(t.ExplodeTick <= launched, $"own grenade bursts at {t.ExplodeTick}, after the charge at {launched}"));
    }

    [Fact]
    public void Infiltration_OpeningGrenades_SpreadOverThePosition()
    {
        var f = GrenadierField(reds: 2, rangeCm: 5_000); // the second man 8 m further: both within a throw
        Start(f, Infiltration);
        var thrown = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0 || Attack(f).Launched, 14_000).OfType<GrenadeThrown>().ToList();
        Assert.True(thrown.Count >= 2, $"{thrown.Count} thrown");
        foreach (var red in f.Reds)
            Assert.Contains(thrown, t => (t.To - red.Position).LengthSquared <= 300L * 300);
    }

    [Fact]
    public void Infiltration_OpeningGrenade_LandsWhereHeIsBelieved()
    {
        var f = GrenadierField();
        var red = f.Reds[0];
        var believed = red.Position;
        Start(f, Infiltration);
        var thrown = RunUntil(f, () => f.Sim.PlannedAttacks.Count == 0 || Attack(f).Launched, 14_000, () =>
        {
            red.Position = M(185, 62); // slipped off into the eastern wood, out of sight
            var c = f.Sim.Knowledge(Side.Blue).Get(red.Id)!;
            c.Level = Nmf.Sim.Vision.ContactLevel.LastKnown; // they go by where they last put him
            c.Position = believed;
        }).OfType<GrenadeThrown>().ToList();
        Assert.NotEmpty(thrown);
        Assert.All(thrown, t => Assert.True((t.To - believed).LengthSquared <= 100L * 100, $"landed at {t.To}, believed {believed}, true {red.Position}"));
    }

    [Fact]
    public void Infiltration_OrderedAgainMidCreep_TheirOwnFirePolicyStillComesBack()
    {
        var f = InfiltrationField();
        f.Blues[1].FirePolicy = FirePolicy.ReturnFire; // his own, not the default
        Start(f, Infiltration);
        Run(f, 20);
        Start(f, Infiltration);
        Run(f, 10);
        Assert.All(Members(f), m => Assert.Equal(FirePolicy.HoldFire, m.FirePolicy));
        var men = Members(f);
        men[0].LastSuppressedTick = f.Sim.Tick;
        Assert.Contains(Run(f, 10).OfType<PlannedAttackHalted>(), h => h.Reason == HaltReason.Discovered);
        Assert.Equal(FirePolicy.ReturnFire, f.Blues[1].FirePolicy);
        Assert.All(men.Where(m => m != f.Blues[1]), m => Assert.Equal(FirePolicy.FireAtWill, m.FirePolicy));
    }

    [Fact]
    public void Infiltration_PlayerSetsFirePolicyMidCreep_HisChoiceStands()
    {
        var f = InfiltrationField();
        Start(f, Infiltration);
        Run(f, 10);
        var man = Members(f)[2];
        f.Sim.Submit(Side.Blue, new SetFirePolicyOrder(man.Id, FirePolicy.ReturnFire));
        Run(f, 5);
        Assert.Equal(FirePolicy.ReturnFire, man.FirePolicy);
        Members(f)[0].LastSuppressedTick = f.Sim.Tick;
        Run(f, 10);
        Assert.Equal(FirePolicy.ReturnFire, man.FirePolicy);
    }

    [Fact]
    public void Shout_WhenOrderedSo()
    {
        var f = NewField();
        var plan = f.Plan(FireAndMovement)!;
        f.Sim.Submit(Side.Blue, new PlannedAttackOrder(plan.FirstMan, plan, FireAndMovement, Shout: true));
        var shout = Assert.Single(f.Sim.Step().OfType<CounterattackStarted>());
        Assert.Equal(plan.FirstMan, shout.Leader);
        Assert.Equal(plan.Target, shout.Target);
        Assert.Equal(Side.Blue, shout.Side);
        var quiet = NewField();
        Start(quiet, FireAndMovement);
        Assert.Empty(Run(quiet, 5).OfType<CounterattackStarted>());
    }
}
