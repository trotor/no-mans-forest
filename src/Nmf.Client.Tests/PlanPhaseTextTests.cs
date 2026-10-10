using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class PlanPhaseTextTests
{
    [Fact]
    public void FireTeam_DeployThenSuppress()
    {
        Assert.Equal("Tuli: matkalla", PlanPhaseText.Of(strike: false, PlannedPhase.Deploy, atSpot: false, launched: false, "fi"));
        Assert.Equal("Tuli: lamauttaa", PlanPhaseText.Of(strike: false, PlannedPhase.Deploy, atSpot: true, launched: false, "fi"));
        Assert.Equal("Tuli: lamauttaa", PlanPhaseText.Of(strike: false, PlannedPhase.Assault, atSpot: true, launched: true, "fi"));
        Assert.Equal("Fire: covering", PlanPhaseText.Of(strike: false, PlannedPhase.Suppress, atSpot: true, launched: false, "en"));
    }

    [Fact]
    public void StrikeTeam_WaitsThenAssaults()
    {
        Assert.Equal("Isku: matkalla", PlanPhaseText.Of(strike: true, PlannedPhase.Deploy, atSpot: false, launched: false, "fi"));
        Assert.Equal("Isku: odottaa", PlanPhaseText.Of(strike: true, PlannedPhase.Suppress, atSpot: true, launched: false, "fi"));
        Assert.Equal("Isku: rynnäkkö", PlanPhaseText.Of(strike: true, PlannedPhase.Assault, atSpot: true, launched: true, "fi"));
        Assert.Equal("Strike: assault", PlanPhaseText.Of(strike: true, PlannedPhase.Assault, atSpot: true, launched: true, "en"));
    }

    [Fact]
    public void InAnAttackUnderWay_SaysHisPart_OthersNull()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(9050 + i * 300, 18_050)))
            .Append(new MapPoint("r1", "red", new Vec2(10_050, 6_050))).ToList();
        var map = new GridMap(200, 200, ["none"], new MapFeatures([], points, []));
        var session = new GameSession(SkirmishScenario.Create(map, 1)) { Tactics = [new TacticDef("long_fire", "Long fire", "Pitkä tuli", 5_000, 15_000, null, null, 30, 200)] };
        var red = session.Sim.Units.Single(u => u.Side == Side.Red);
        var contact = session.Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
        contact.Level = Nmf.Sim.Vision.ContactLevel.Visible;
        contact.Position = red.Position;
        var men = session.OwnUnits.Take(3).Select(u => u.Id).ToList();
        foreach (var id in men)
            session.Selection.Add(id);
        session.OrderAttack(red.Id);
        session.StepOnce();
        Assert.StartsWith("Tuli:", PlanPhaseText.For(session.Sim, Side.Blue, men[0], "fi"));
        Assert.Null(PlanPhaseText.For(session.Sim, Side.Blue, session.OwnUnits.Last().Id, "fi"));
    }
}
