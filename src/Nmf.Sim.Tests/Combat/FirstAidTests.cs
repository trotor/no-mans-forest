using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-10-11-campaign-design §9b, "Ensiapu": the medic binds a bad wound so it no longer bleeds.</summary>
public class FirstAidTests
{
    private const int Second = SimConstants.TicksPerSecond;

    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    /// <summary>An open field; our men at the given metres, the first a medic (or not), the rest badly wounded.</summary>
    private static (Simulation Sim, Unit Medic, Unit[] Wounded) Field(Specialty medic, (int X, int Y) at, params (int X, int Y)[] wounded)
    {
        var sim = new Simulation(new GridMap(120, 80, ["none"]), 7);
        var man = sim.SpawnUnit(Side.Blue, M(at.X, at.Y), 8, TestWeapons.Rifle());
        sim.SetSpecialty(man, medic);
        var hurt = wounded.Select(w =>
        {
            var u = sim.SpawnUnit(Side.Blue, M(w.X, w.Y), 8, TestWeapons.Rifle());
            u.Wound = WoundLevel.Serious;
            return u;
        }).ToArray();
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        return (sim, man, hurt);
    }

    private static List<SimEvent> Run(Simulation sim, int ticks, Func<bool>? until = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
            events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void Order_ByAManWhoIsNoMedic_IsRejected()
    {
        var (sim, man, hurt) = Field(Specialty.Scout, (10, 30), (12, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(man.Id, hurt[0].Id));
        Assert.Single(Run(sim, 2).OfType<OrderRejected>());
        Assert.Null(man.FirstAidTarget);
    }

    [Theory]
    [InlineData(WoundLevel.None)]
    [InlineData(WoundLevel.Light)]
    [InlineData(WoundLevel.Incapacitated)]
    public void Order_ForAManWhoDoesNotBleed_IsRejected(WoundLevel wound)
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (12, 30));
        hurt[0].Wound = wound;
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Assert.Single(Run(sim, 2).OfType<OrderRejected>());
    }

    [Fact]
    public void Medic_GoesToHim_BindsTheWound_AndHeBleedsNoMore()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (50, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        var bound = Assert.Single(Run(sim, 60 * Second, () => hurt[0].Bandaged).OfType<UnitBandaged>());
        Assert.Equal(medic.Id, bound.Medic);
        Assert.Equal(hurt[0].Id, bound.Patient);
        Assert.True((medic.Position - hurt[0].Position).LengthSquared <= (long)CombatRules.FirstAidRangeCm * CombatRules.FirstAidRangeCm);
        Assert.Null(medic.FirstAidTarget);
        Assert.Equal(CombatAction.None, medic.Action);
        Run(sim, CombatRules.SeriousBleedTicks + Second);
        Assert.Equal(WoundLevel.Serious, hurt[0].Wound);
    }

    [Fact]
    public void WithoutAMedic_TheBadlyWoundedGoesDown()
    {
        var (sim, _, hurt) = Field(Specialty.None, (10, 30), (12, 30));
        Run(sim, CombatRules.SeriousBleedTicks + Second);
        Assert.Equal(WoundLevel.Incapacitated, hurt[0].Wound);
    }

    [Fact]
    public void Binding_TakesFifteenSeconds_BesideHim()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        long start = sim.Tick;
        var bound = Assert.Single(Run(sim, 30 * Second).OfType<UnitBandaged>());
        Assert.InRange(bound.Tick - start, CombatRules.FirstAidTicks, CombatRules.FirstAidTicks + 3);
    }

    [Fact]
    public void Medic_BindsHisOwnWound()
    {
        var (sim, medic, _) = Field(Specialty.Medic, (10, 30));
        medic.Wound = WoundLevel.Serious;
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, medic.Id));
        Assert.Single(Run(sim, 20 * Second).OfType<UnitBandaged>());
        Assert.True(medic.Bandaged);
    }

    [Fact]
    public void ANewWound_TearsTheBandageAway()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 20 * Second);
        Assert.True(hurt[0].Bandaged);
        Damage.SetWound(sim, hurt[0], WoundLevel.Serious, sim.Tick, []);
        Assert.False(hurt[0].Bandaged);
        Run(sim, 5 * Second, () => medic.FirstAidTarget is not null);
        Assert.Equal(hurt[0].Id, medic.FirstAidTarget); // he sees to it again
    }

    [Fact]
    public void PatientWalksOff_TheMedicFollows_AndStartsAgainWhenHeStops()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 10 * Second);
        sim.Submit(Side.Blue, new MoveOrder(hurt[0].Id, M(25, 30), MoveMode.Walk));
        Run(sim, 2);
        Assert.NotNull(hurt[0].MoveTarget);
        Run(sim, 90 * Second, () => hurt[0].MoveTarget is null && !hurt[0].IsMoving);
        Assert.Null(hurt[0].MoveTarget);
        Run(sim, 2);
        long stopped = sim.Tick;
        Assert.False(hurt[0].Bandaged); // ten seconds of it before he left did not count
        var bound = Assert.Single(Run(sim, 60 * Second).OfType<UnitBandaged>());
        Assert.True(bound.Tick - stopped >= CombatRules.FirstAidTicks - 2 * Second, $"bound {bound.Tick - stopped} ticks after he stopped");
        Assert.True((medic.Position - hurt[0].Position).LengthSquared <= (long)CombatRules.FirstAidRangeCm * CombatRules.FirstAidRangeCm);
    }

    [Fact]
    public void PatientGoesDown_TheMedicGivesUp()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 5 * Second);
        Assert.Equal(CombatAction.FirstAid, medic.Action);
        Damage.SetWound(sim, hurt[0], WoundLevel.Incapacitated, sim.Tick, []);
        var events = Run(sim, 20 * Second);
        Assert.Empty(events.OfType<UnitBandaged>());
        Assert.Null(medic.FirstAidTarget);
        Assert.Equal(CombatAction.None, medic.Action);
    }

    [Fact]
    public void AnyOtherOrder_StopsTheBinding()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 5 * Second);
        sim.Submit(Side.Blue, new MoveOrder(medic.Id, M(100, 30), MoveMode.Walk));
        var events = Run(sim, 20 * Second);
        Assert.Empty(events.OfType<UnitBandaged>());
        Assert.False(hurt[0].Bandaged);
    }

    [Fact]
    public void MedicGoesDown_TheBindingStops()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 5 * Second);
        Damage.SetWound(sim, medic, WoundLevel.Incapacitated, sim.Tick, []);
        Assert.Null(medic.FirstAidTarget);
        Assert.Equal(CombatAction.None, medic.Action);
    }

    [Fact]
    public void IdleMedic_BindsTheNearestBadlyWoundedWithin20m_OnHisOwn()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (22, 30), (10, 40), (50, 60));
        var events = Run(sim, 120 * Second, () => hurt[0].Bandaged && hurt[1].Bandaged);
        var bound = events.OfType<UnitBandaged>().ToList();
        Assert.Equal([hurt[1].Id, hurt[0].Id], bound.Select(b => b.Patient)); // 10 m first, then 12 m; 50 m is too far
        Assert.False(hurt[2].Bandaged);
    }

    [Fact]
    public void IdleMedic_TwoAtTheSameDistance_TheLowerIdFirst()
    {
        var (sim, _, hurt) = Field(Specialty.Medic, (10, 30), (10, 40), (10, 20));
        var first = Run(sim, 60 * Second).OfType<UnitBandaged>().First();
        Assert.Equal(hurt[0].Id, first.Patient);
    }

    [Fact]
    public void Medic_UnderFireOrBusy_DoesNotGoOnHisOwn()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (15, 30));
        medic.LastSuppressedTick = sim.Tick + 10 * Second; // fire around him a while yet
        Run(sim, 5 * Second);
        Assert.Null(medic.FirstAidTarget);

        var (sim2, medic2, _) = Field(Specialty.Medic, (10, 30), (15, 30));
        sim2.Submit(Side.Blue, new MoveOrder(medic2.Id, M(100, 30), MoveMode.Walk));
        Run(sim2, 10 * Second);
        Assert.Null(medic2.FirstAidTarget);

        var (sim3, medic3, _) = Field(Specialty.Medic, (10, 30), (15, 30));
        medic3.PlannedAttackId = 1;
        Run(sim3, 5 * Second);
        Assert.Null(medic3.FirstAidTarget);
    }

    [Fact]
    public void NoMedic_NobodyBindsOnHisOwn()
    {
        var (sim, _, hurt) = Field(Specialty.Sapper, (10, 30), (12, 30));
        Assert.Empty(Run(sim, 30 * Second).OfType<UnitBandaged>());
    }

    [Fact]
    public void FirstAid_IsDeterministic_AndInTheStateHash()
    {
        ulong Hash(int seconds)
        {
            var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (25, 30));
            Run(sim, seconds * Second);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(10), Hash(10));
        var (a, ma, ha) = Field(Specialty.Medic, (10, 30), (11, 30));
        var (b, mb, hb) = Field(Specialty.Medic, (10, 30), (11, 30));
        ha[0].Bandaged = true;
        Assert.NotEqual(StateHash.Compute(a), StateHash.Compute(b));
    }

    [Fact]
    public void AnotherPatient_TheBindingStartsOver()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30), (10, 31));
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 14 * Second, () => medic.Action == CombatAction.FirstAid && medic.ActionTicksLeft < 2 * Second);
        Assert.Equal(CombatAction.FirstAid, medic.Action);
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[1].Id));
        long switched = sim.Tick;
        var bound = Run(sim, 30 * Second).OfType<UnitBandaged>().First(); // then he goes back to the first on his own
        Assert.Equal(hurt[1].Id, bound.Patient);
        Assert.True(bound.Tick - switched >= CombatRules.FirstAidTicks, $"bound {bound.Tick - switched} ticks after the switch");
    }

    [Fact]
    public void AnAssault_StopsTheBinding()
    {
        var (sim, medic, hurt) = Field(Specialty.Medic, (10, 30), (11, 30));
        var enemy = sim.SpawnUnit(Side.Red, M(60, 30), 8, TestWeapons.Rifle());
        enemy.FirePolicy = FirePolicy.HoldFire;
        sim.Submit(Side.Blue, new FirstAidOrder(medic.Id, hurt[0].Id));
        Run(sim, 5 * Second);
        Assert.True(sim.StartAssault(medic, enemy));
        Assert.Null(medic.FirstAidTarget);
        Assert.NotEqual(CombatAction.FirstAid, medic.Action);
    }
}
