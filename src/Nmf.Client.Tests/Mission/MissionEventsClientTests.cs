using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Mission;

/// <summary>Spec 2026-10-10-events-design §5–§6: what the player sees of events, find and rescue objectives and the stranded man.</summary>
public class MissionEventsClientTests
{
    private static readonly WeaponDef Rifle = new("rifle", "Test M/39", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000);

    private static readonly ObjectiveSpec FindWreck = new("find_wreck", ObjectiveType.Find, new Localized("Find the wreck", "Etsi hylky"),
        Place: "wreck", Area: "search");
    private static readonly ObjectiveSpec BringBack = new("bring_back", ObjectiveType.Rescue, new Localized("Bring him home", "Tuo hänet kotiin"),
        Zone: "home", Unit: "Luutn. Pilot", Requires: ["find_wreck"]);
    private static readonly ObjectiveSpec HiddenMaps = new("maps", ObjectiveType.PickUp, new Localized("Take the maps", "Ota kartat"), Item: "maps", Hidden: true);

    /// <summary>100 m map: the hero at (5, 95) m, the pilot stranded at the wreck (60, 30) m, the search zone round it.</summary>
    private static GameSession Session(params EventSpec[] events)
    {
        var spec = new MissionSpec("rescue", new Localized("Rescue"), new Localized("Today"), "map", new Localized("Go."),
            [new SoldierSpec("Alik. Hero", "rifle", null, Leader: true, At: (5, 95)),
             new SoldierSpec("Luutn. Pilot", "rifle", null, At: (60, 30), Squad: "pilot", Stranded: true)],
            [new SoldierSpec("Serzhant Belov", "rifle", null, Leader: true, At: (95, 5))],
            [FindWreck, BringBack, HiddenMaps], new Dictionary<string, Localized> { ["maps"] = new("Maps") },
            Squads: new Dictionary<string, Localized> { ["strike"] = new("Strike"), ["pilot"] = new("Pilot") },
            Patrols: false,
            PlaceList: new Dictionary<string, Vec2> { ["wreck"] = new(6050, 3050) },
            ZoneList: [new MapZone("home", "mission", new Vec2(0, 9000), new Vec2(1000, 10000)),
                       new MapZone("search", "mission", new Vec2(4000, 1000), new Vec2(8000, 5000))],
            EventList: events);
        var weapons = new Dictionary<string, WeaponDef> { ["rifle"] = Rifle };
        var scenario = MissionScenario.Create(new GridMap(100, 100, ["none"]), spec, weapons, new Dictionary<string, GrenadeDef>(), 1);
        return new GameSession(scenario, spec, "fi");
    }

    private static Unit Hero(GameSession s) => s.Sim.Units[0];
    private static Unit Pilot(GameSession s) => s.Sim.Units[1];

    [Fact]
    public void StrandedMan_NotCommanded_NotShown_UntilFound()
    {
        var s = Session();
        Assert.DoesNotContain(Pilot(s).Id, s.CommandedIds);
        Assert.False(s.IsShownToPlayer(Pilot(s), revealAll: false));
        Assert.DoesNotContain(Pilot(s), s.CardUnits);
        Hero(s).Position = Pilot(s).Position + new Vec2(2_500, 0); // 25 m: they can make him out
        Assert.True(s.IsShownToPlayer(Pilot(s), revealAll: false));
        Assert.DoesNotContain(Pilot(s).Id, s.CommandedIds);
        Hero(s).Position = Pilot(s).Position + new Vec2(300, 0);
        for (int i = 0; i < 20; i++) s.StepOnce();
        Assert.Contains(Pilot(s).Id, s.CommandedIds);
        Assert.Contains(Pilot(s), s.CardUnits);
        var found = Assert.Single(s.TakeEvents().OfType<StrandedFound>());
        Assert.Equal("Löytyi: Luutn. Pilot", Alerts.Found(found, s));
    }

    [Fact]
    public void Script_RunsInTheSession_MessagesComeThrough()
    {
        var s = Session(new EventSpec("hello", new EventCondition(AfterTicks: 5),
            [new EventAction(EventActionKind.Message, Text: new Localized("Radio!", "Radio!")), new EventAction(EventActionKind.Reveal, Objective: "maps")]));
        for (int i = 0; i < 30; i++) s.StepOnce();
        var events = s.TakeEvents();
        Assert.Equal("Radio!", Assert.Single(events.OfType<MissionMessage>()).Text.In("fi"));
        Assert.Equal(["Radio!"], s.Messages.Select(m => m.Text.In("fi")));
        var revealed = Assert.Single(events.OfType<ObjectiveRevealed>());
        Assert.Equal("Uusi tavoite: Ota kartat", Alerts.Revealed(revealed, s));
    }

    [Fact]
    public void Paper_ShowsOnlyVisibleObjectives()
    {
        var s = Session();
        Assert.Equal(["Etsi hylky", "Tuo hänet kotiin"], MissionPaper.Objectives(s.Tracker!, "fi").Select(o => o.Text));
        s.Tracker!.Reveal("maps", 0);
        Assert.Equal(3, MissionPaper.Objectives(s.Tracker!, "fi").Count);
    }

    [Fact]
    public void Guide_Find_ShowsTheAreaNotThePlace()
    {
        var s = Session();
        var target = ObjectiveGuide.Next(s)!.Value;
        Assert.Equal(new Vec2(6000, 3000), target.At); // the middle of the search zone, not the wreck (60.5, 30.5) m
        Assert.Equal("search", target.Zone!.Name);
    }

    [Fact]
    public void Guide_Find_WithoutAnArea_ShowsNothing()
    {
        var s = Session();
        var spec = s.Mission! with { Objectives = [FindWreck with { Area = null }, BringBack, HiddenMaps] };
        var bare = new GameSession(s.Scenario, spec, "fi");
        Assert.Null(ObjectiveGuide.Next(bare));
    }

    [Fact]
    public void Guide_Rescue_ToHomeOnceHeIsWithThem()
    {
        var s = Session();
        Hero(s).Position = Pilot(s).Position + new Vec2(300, 0);
        for (int i = 0; i < 20; i++) s.StepOnce();
        var target = ObjectiveGuide.Next(s)!.Value;
        Assert.Equal("home", target.Zone!.Name);
    }
}
