using Nmf.Client.Mission;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Mission;

public class AlertsTests
{
    private static readonly CounterattackStarted Shout = new(100, Side.Red, new UnitId(5), new Vec2(10_000, 10_000), new UnitId(1));

    [Fact]
    public void TheShout_IsHeardWithin300Metres()
    {
        Assert.Equal("Kuuluu huuto: \"Urraa!\" — vihollinen hyökkää!", Alerts.Counterattack(Shout, [new Vec2(10_000, 39_000)], "fi"));
        Assert.Equal("A shout: \"Urraa!\" — the enemy is attacking!", Alerts.Counterattack(Shout, [new Vec2(10_000, 39_000)], "en"));
        Assert.Null(Alerts.Counterattack(Shout, [new Vec2(10_000, 41_000)], "fi"));
        Assert.Null(Alerts.Counterattack(Shout, [], "fi"));
    }
}

public class TacticAlertsTests
{
    private static readonly Nmf.Sim.Tactics.TacticDef FireAndMovement = new("fire_and_movement", "Fire and movement", "Tuli ja liike", 3_500, 10_000,
        new Nmf.Sim.Tactics.TacticManeuver(60, 2_500, 4_500), new Nmf.Sim.Tactics.TacticAssault(250, 400, 1_200), 30, 200);
    private static readonly Nmf.Sim.Tactics.TacticDef LongFire = new("long_fire", "Long fire", "Pitkä tuli", 5_000, 15_000, null, null, 30, 200);

    private static Nmf.Sim.Tactics.TacticPlan Plan(string tactic, int metres, int side, bool blind = false) =>
        new(tactic, new UnitId(1), Vec2.Zero, [], [], [], [], [], blind, 0, new Nmf.Sim.Tactics.PlanReason(metres, side, blind));

    [Fact]
    public void PlanSummary_SaysTacticDistanceAndSide()
    {
        Assert.Equal("Tuli ja liike: tuliasema 72 m, koukkaus vasemmalta", Alerts.PlanSummary(Plan("fire_and_movement", 72, -1), FireAndMovement, "fi"));
        Assert.Equal("Fire and movement: fire base 72 m, flanking right", Alerts.PlanSummary(Plan("fire_and_movement", 72, 1), FireAndMovement, "en"));
    }

    [Fact]
    public void LongFire_Summary_HasNoFlank()
    {
        Assert.Equal("Pitkä tuli: tuliasema 120 m", Alerts.PlanSummary(Plan("long_fire", 120, 0), LongFire, "fi"));
        Assert.Equal("Long fire: fire base 120 m (no sight of the position)", Alerts.PlanSummary(Plan("long_fire", 120, 0, blind: true), LongFire, "en"));
    }

    [Theory]
    [InlineData(Nmf.Sim.Tactics.HaltReason.Casualties, "Hyökkäys pysähtyi: tappioita 2", "Attack halted: 2 casualties")]
    [InlineData(Nmf.Sim.Tactics.HaltReason.LeaderDown, "Hyökkäys pysähtyi: johtaja kaatui", "Attack halted: the leader is down")]
    [InlineData(Nmf.Sim.Tactics.HaltReason.StrikePinned, "Hyökkäys pysähtyi: iskuryhmä on maassa", "Attack halted: the strike team is pinned")]
    [InlineData(Nmf.Sim.Tactics.HaltReason.NotSuppressed, "Hyökkäys pysähtyi: asema ei lamaudu", "Attack halted: the position will not go down")]
    [InlineData(Nmf.Sim.Tactics.HaltReason.NoFire, "Hyökkäys pysähtyi: tuliasemasta ei näe asemaa", "Attack halted: the fire base cannot see the position")]
    [InlineData(Nmf.Sim.Tactics.HaltReason.TargetMoved, "Hyökkäys pysähtyi: kohde siirtyi", "Attack halted: the enemy has moved")]
    public void Halted_SaysWhyAndHowMany(Nmf.Sim.Tactics.HaltReason reason, string fi, string en)
    {
        var halted = new PlannedAttackHalted(10, Side.Blue, 1, reason, 2);
        Assert.Equal(fi, Alerts.Halted(halted, "fi"));
        Assert.Equal(en, Alerts.Halted(halted, "en"));
    }

    [Fact]
    public void Assault_And_Ending_AreAnnounced()
    {
        Assert.Equal("Isku lähtee!", Alerts.Phase(new PlannedAttackPhaseChanged(1, Side.Blue, 1, Nmf.Sim.Tactics.PlannedPhase.Assault), "fi"));
        Assert.Null(Alerts.Phase(new PlannedAttackPhaseChanged(1, Side.Blue, 1, Nmf.Sim.Tactics.PlannedPhase.Suppress), "fi"));
        Assert.Equal("Asema vallattu", Alerts.Ended(new PlannedAttackEnded(1, Side.Blue, 1, true), "fi"));
        Assert.Equal("Attack over: the enemy is gone", Alerts.Ended(new PlannedAttackEnded(1, Side.Blue, 1, false), "en"));
    }
}
