using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-10-10-carrying-design: carrying a man who cannot move.</summary>
public class CarryTests
{
    /// <summary>A 100 m open strip: the carrier at (5, 10) m, the wounded man down at (15, 10) m, an enemy far off.</summary>
    private static (Simulation Sim, Unit Carrier, Unit Wounded) Setup()
    {
        var sim = new Simulation(new GridMap(100, 20, ["none"]), 1);
        var carrier = sim.SpawnUnit(Side.Blue, new Vec2(550, 1050), 8, TestWeapons.Rifle());
        var wounded = sim.SpawnUnit(Side.Blue, new Vec2(1550, 1050), 8, TestWeapons.Rifle());
        wounded.Wound = WoundLevel.Incapacitated;
        wounded.Stance = Stance.Prone;
        return (sim, carrier, wounded);
    }

    private static List<SimEvent> Run(Simulation sim, int ticks, Action? everyTick = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks; i++)
        {
            everyTick?.Invoke();
            events.AddRange(sim.Step());
        }
        return events;
    }

    private static void Lift(Simulation sim, Unit carrier, Unit wounded)
    {
        sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        Run(sim, 400);
        Assert.Equal(wounded.Id, carrier.Carrying);
    }

    [Fact]
    public void WalksOver_LiftsHim_InThreeSeconds()
    {
        var (sim, carrier, wounded) = Setup();
        sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        long started = -1;
        var events = new List<SimEvent>();
        for (int i = 0; i < 400 && carrier.Carrying is null; i++)
        {
            events.AddRange(sim.Step());
            if (started < 0 && carrier.Action == CombatAction.Lifting)
                started = sim.Tick;
        }
        Assert.True(started > 0);
        var lifted = Assert.Single(events.OfType<UnitLifted>());
        Assert.InRange(lifted.Tick - started, CombatRules.LiftTicks - 2, CombatRules.LiftTicks);
        Assert.Equal(carrier.Id, wounded.CarriedBy);
        Assert.Equal(wounded.Id, carrier.Carrying);
    }

    [Fact]
    public void Carried_FollowsExactly()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        int startX = carrier.Position.X;
        sim.Submit(Side.Blue, new MoveOrder(carrier.Id, new Vec2(8050, 1050)));
        Run(sim, 100, () => Assert.Equal(carrier.Position, wounded.Position));
        Run(sim, 1);
        Assert.Equal(carrier.Position, wounded.Position);
        Assert.InRange(carrier.Position.X - startX, 300, 600); // about 4 m in 5 s at 55 % of 1.6 m/s
    }

    [Fact]
    public void Carrier_WalksAt55Percent_RunsAt80()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        Assert.Equal(carrier.SpeedCmPerTick * 55 / 100, StanceRules.SpeedCmPerTick(carrier, MoveMode.Walk));
        Assert.Equal(carrier.SpeedCmPerTick * 80 / 100, StanceRules.SpeedCmPerTick(carrier, MoveMode.Run));
    }

    [Fact]
    public void WithAHelper_75Percent_AndTheHelperKeepsBeside()
    {
        var (sim, carrier, wounded) = Setup();
        var helper = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1550), 8, TestWeapons.Rifle());
        Lift(sim, carrier, wounded);
        sim.Submit(Side.Blue, new CarryOrder(helper.Id, wounded.Id));
        Run(sim, 200);
        Assert.Equal(wounded.Id, helper.HelpingCarry);
        Assert.Equal(carrier.SpeedCmPerTick * 75 / 100, StanceRules.SpeedCmPerTick(carrier, MoveMode.Walk));
        sim.Submit(Side.Blue, new MoveOrder(carrier.Id, new Vec2(8050, 1050)));
        Run(sim, 200);
        Assert.True((helper.Position - carrier.Position).LengthSquared <= 300L * 300, $"helper {helper.Position} carrier {carrier.Position}");
    }

    [Fact]
    public void Carriers_DoNotFire()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(4050, 1050), 8, TestWeapons.Rifle(lethality: 0));
        enemy.FirePolicy = FirePolicy.HoldFire;
        var shots = Run(sim, 300).OfType<ShotFired>().Where(s => s.Shooter == carrier.Id);
        Assert.Empty(shots);
        Assert.Equal(wounded.Id, carrier.Carrying);
    }

    [Fact]
    public void DropOrder_LaysHimDown_WhereTheCarrierIs()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        sim.Submit(Side.Blue, new MoveOrder(carrier.Id, new Vec2(5050, 1050)));
        Run(sim, 60);
        sim.Submit(Side.Blue, new DropOrder(carrier.Id));
        Run(sim, 2);
        Assert.Null(carrier.Carrying);
        Assert.Null(wounded.CarriedBy);
        var where = wounded.Position;
        Run(sim, 40);
        Assert.Equal(where, wounded.Position);
    }

    [Fact]
    public void AnOrderNeedingHands_DropsHimFirst()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(4050, 1050), 8, TestWeapons.Rifle(lethality: 0));
        sim.Submit(Side.Blue, new FireAtOrder(carrier.Id, enemy.Id));
        Run(sim, 2);
        Assert.Null(carrier.Carrying);
    }

    [Fact]
    public void CarrierFalls_HeIsDropped_TheHelperCarriesOn()
    {
        var (sim, carrier, wounded) = Setup();
        var helper = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1550), 8, TestWeapons.Rifle());
        Lift(sim, carrier, wounded);
        sim.Submit(Side.Blue, new CarryOrder(helper.Id, wounded.Id));
        Run(sim, 200);
        carrier.Wound = WoundLevel.Dead;
        Run(sim, 2);
        Assert.Equal(helper.Id, wounded.CarriedBy);
        Assert.Equal(wounded.Id, helper.Carrying);
        Assert.Null(helper.HelpingCarry);
    }

    [Fact]
    public void CarrierPinned_HeIsDropped()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        carrier.MoraleState = MoraleState.Pinned;
        Run(sim, 2);
        Assert.Null(carrier.Carrying);
        Assert.Null(wounded.CarriedBy);
    }

    [Fact]
    public void Carried_Dies_Dropped()
    {
        var (sim, carrier, wounded) = Setup();
        Lift(sim, carrier, wounded);
        wounded.Wound = WoundLevel.Dead;
        Run(sim, 2);
        Assert.Null(carrier.Carrying);
    }

    [Theory]
    [InlineData(WoundLevel.Light)]
    [InlineData(WoundLevel.Dead)]
    public void OnlyAManWhoCannotMove_IsCarried(WoundLevel wound)
    {
        var (sim, carrier, wounded) = Setup();
        wounded.Wound = wound;
        sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        Assert.Contains(sim.Step(), e => e is OrderRejected);
        Assert.Null(carrier.CarryTarget);
    }

    [Fact]
    public void Carrying_SameRun_SameState()
    {
        static ulong Hash()
        {
            var (sim, carrier, wounded) = Setup();
            sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
            Run(sim, 200);
            sim.Submit(Side.Blue, new MoveOrder(carrier.Id, new Vec2(8050, 1050)));
            Run(sim, 200);
            Assert.NotNull(carrier.Carrying);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
    }

    [Fact]
    public void StateHash_IncludesCarry()
    {
        var (sim, carrier, wounded) = Setup();
        ulong before = StateHash.Compute(sim);
        carrier.CarryTarget = wounded.Id;
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void Lift_WaitsForWhatHeIsDoing_DoesNotOverwriteIt()
    {
        var (sim, carrier, wounded) = Setup();
        carrier.Position = wounded.Position + new Vec2(100, 0); // right beside him …
        carrier.Ammo = 0;
        carrier.Action = CombatAction.Reloading;                 // … in the middle of a reload
        carrier.ActionTicksLeft = 30;
        sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        Run(sim, 5);
        Assert.Equal(CombatAction.Reloading, carrier.Action);
        Run(sim, 200);
        Assert.Equal(wounded.Id, carrier.Carrying); // he lifted once the reload was done …
        Assert.True(carrier.Ammo > 0, "the reload was cut short by the lift"); // … and the reload was not lost
    }

    [Fact]
    public void CarrierFalls_AFarHelper_WalksOverToLiftHim_HeDoesNotJump()
    {
        var (sim, carrier, wounded) = Setup();
        var helper = sim.SpawnUnit(Side.Blue, new Vec2(8050, 1050), 8, TestWeapons.Rifle());
        Lift(sim, carrier, wounded);
        sim.Submit(Side.Blue, new CarryOrder(helper.Id, wounded.Id));
        Run(sim, 2);
        Assert.Equal(wounded.Id, helper.HelpingCarry);
        var where = wounded.Position;
        carrier.Wound = WoundLevel.Dead;
        Run(sim, 2);
        Assert.Equal(where, wounded.Position);
        Assert.Null(wounded.CarriedBy);
        Assert.Equal(wounded.Id, helper.CarryTarget);
    }

    [Fact]
    public void TwoLiftingAtOnce_TheSecondHelps()
    {
        var (sim, carrier, wounded) = Setup();
        var second = sim.SpawnUnit(Side.Blue, new Vec2(1650, 1050), 8, TestWeapons.Rifle());
        carrier.Position = new Vec2(1450, 1050);
        sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        sim.Step();
        sim.Submit(Side.Blue, new CarryOrder(second.Id, wounded.Id));
        Run(sim, 200);
        Assert.Equal(wounded.Id, carrier.Carrying);
        Assert.Equal(wounded.Id, second.HelpingCarry);
    }

    [Fact]
    public void ACarrierCannotThrow()
    {
        var (sim, carrier, wounded) = Setup();
        carrier.GrenadeType = GrenadeDefTests.Test();
        carrier.Grenades = 2;
        Lift(sim, carrier, wounded);
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(carrier.Position.X + 1_200, 1050), 8, TestWeapons.Rifle(lethality: 0));
        enemy.Stance = Stance.Prone;
        Assert.False(GrenadeSystem.CanThrowAt(sim, carrier, enemy, sim.Tick));
        Assert.False(GrenadeSystem.CanOpenWith(sim, carrier, enemy, enemy.Position, sim.Tick));
        GrenadeSystem.StartThrow(carrier, enemy);
        Run(sim, 40);
        Assert.Equal(2, carrier.Grenades);
    }

    [Fact]
    public void APlannedAttack_LaysTheManDownFirst()
    {
        var f = new Nmf.Sim.Tests.Tactics.TacticPlannerTests.Field(blues: 4);
        var carrier = f.Blues[0];
        var wounded = f.Sim.SpawnUnit(Side.Blue, carrier.Position + new Vec2(100, 0), 8, TestWeapons.Rifle());
        wounded.Wound = WoundLevel.Incapacitated;
        f.Sim.Submit(Side.Blue, new CarryOrder(carrier.Id, wounded.Id));
        Run(f.Sim, 100);
        Assert.Equal(wounded.Id, carrier.Carrying);
        var plan = f.Plan(Nmf.Sim.Tests.Tactics.TacticPlannerTests.LongFire)!;
        f.Sim.Submit(Side.Blue, new PlannedAttackOrder(plan.FirstMan, plan, Nmf.Sim.Tests.Tactics.TacticPlannerTests.LongFire));
        Run(f.Sim, 2);
        Assert.Null(carrier.Carrying);
        Assert.Null(wounded.CarriedBy);
    }
}
