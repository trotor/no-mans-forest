using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Mission;

public class MissionClientTests
{
    private static WeaponDef Rifle => new("rifle", "Test M/39", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000);

    private static (GridMap Map, MissionSpec Spec) Setup()
    {
        var points = new List<MapPoint> { new("b1", "blue", new Vec2(550, 3550)), new("r1", "red", new Vec2(550, 550)) };
        var zones = new List<MapZone> { new("start_zone", "zone", new Vec2(0, 3000), new Vec2(2000, 4000)) };
        var map = new GridMap(40, 40, ["none"], new MapFeatures(zones, points, []));
        var spec = new MissionSpec("t", new Localized("Raid", "Isku"), new Localized("Today", "Tänään"), "m",
            new Localized("# Orders\n## Situation\nGo **now**.\n- one\n- [two]", "# Käsky"),
            [new SoldierSpec("Alik. Hero", "rifle", null, Leader: true, Nerve: 95, Morale: 950, Marksmanship: 70, Leadership: 90)],
            [new SoldierSpec("Serž. Belov", "rifle", null, Leader: true, Items: ["orders"])],
            [new ObjectiveSpec("grab", ObjectiveType.PickUp, new Localized("Take the orders", "Ota käskyt"), Item: "orders"),
             new ObjectiveSpec("back", ObjectiveType.ReachZone, new Localized("Bring them back"), Zone: "start_zone", Carrying: "orders", Requires: ["grab"])],
            new Dictionary<string, Localized> { ["orders"] = new("Soviet orders", "Käskyt") });
        return (map, spec);
    }

    private static GameSession Session(string language = "en")
    {
        var (map, spec) = Setup();
        var scenario = MissionScenario.Create(map, spec, new Dictionary<string, WeaponDef> { ["rifle"] = Rifle }, new Dictionary<string, GrenadeDef>(), 1);
        return new GameSession(scenario, spec, language);
    }

    [Fact]
    public void Markdown_HeadingsListsBoldAndBrackets()
    {
        var bb = MarkdownLite.ToBbcode("# Orders\n## Situation\nGo **now**.\n- one\n- [two]");
        Assert.Contains("[b]Orders[/b]", bb);
        Assert.Contains("[font_size=", bb);
        Assert.Contains("Go [b]now[/b].", bb);
        Assert.Contains("• one", bb);
        Assert.Contains("[lb]two]", bb);
    }

    [Fact]
    public void Paper_ListsObjectivesWithTheirState_InTheChosenLanguage()
    {
        var session = Session("fi");
        var lines = MissionPaper.Objectives(session.Tracker!, "fi");
        Assert.Equal(new[] { ("Ota käskyt", false), ("Bring them back", false) }, lines);
        Assert.Equal("Isku", session.Mission!.Title.In(session.Language));
    }

    [Fact]
    public void Paper_RosterLineShowsTheMansAttributes()
    {
        var session = Session();
        var line = MissionPaper.RosterLine(session.Sim.Units[0], w => w.Name);
        Assert.Contains("Alik. Hero", line);
        Assert.Contains("Test M/39", line);
        Assert.Contains("nerve 95", line);
        Assert.Contains("morale 950", line);
        Assert.Contains("shooting 70", line);
        Assert.Contains("leadership 90", line);
        var fi = MissionPaper.RosterLine(session.Sim.Units[0], w => w.Name, "fi");
        Assert.Contains("sisu 95", fi);
        Assert.Contains("moraali 950", fi);
        Assert.Contains("ampumataito 70", fi);
        Assert.Contains("johtamiskyky 90", fi);
    }

    [Fact]
    public void Session_TracksTheMission_AndReportsItsEvents()
    {
        var session = Session();
        var hero = session.Sim.Units[0];
        hero.AddItem(new Item("orders", "Soviet orders"));
        session.StepOnce();
        var events = session.TakeEvents();
        Assert.Contains(events, e => e is ObjectiveChanged { Id: "grab", Done: true });
        Assert.Contains(events, e => e is MissionEnded { Success: true });
        Assert.True(session.Tracker!.Result);
    }

    [Fact]
    public void CarriedPapers_UseTheMissionLanguage()
    {
        var session = Session("fi");
        session.Sim.Units[0].AddItem(new Item("orders", "Soviet orders"));
        Assert.Equal(new[] { "Käskyt" }, session.CarriedPapers);
    }

    [Fact]
    public void UnitNames_PreferTheRosterName()
    {
        var session = Session();
        Assert.Equal("Alik. Hero", UnitNames.Of(session.Sim.Units[0], 0));
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        Assert.Equal(UnitNames.For(Side.Blue, 0), UnitNames.Of(sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7), 0));
    }
}
