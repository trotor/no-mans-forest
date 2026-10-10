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
    private static GameSession Session(params EventSpec[] events) => Session(null, events);

    private static GameSession Session(Action<GridMap>? terrain, params EventSpec[] events)
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
        var map = new GridMap(100, 100, ["none"]);
        terrain?.Invoke(map);
        var scenario = MissionScenario.Create(map, spec, weapons, new Dictionary<string, GrenadeDef>(), 1);
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

    [Fact]
    public void WhatOnlyTheStrandedManSees_IsNotShown()
    {
        var s = Session(map =>
        {
            for (int x = 0; x < 100; x++)
                for (int y = 60; y <= 63; y++)
                {
                    ref var cell = ref map[new CellCoord(x, y)];
                    cell.ObstacleHeightCm = 400; // a thicket between the searchers and the wreck
                    cell.ConcealmentPerM = 255;
                }
        });
        var belov = s.Sim.Units[2];
        belov.Position = Pilot(s).Position + new Vec2(2_000, 0); // 20 m from the pilot, beyond the thicket from the hero
        for (int i = 0; i < 20; i++) s.StepOnce(); // the fog: the hero's side of the thicket only
        var contact = s.Sim.Knowledge(Side.Blue).GetOrAdd(belov.Id);
        contact.Level = Nmf.Sim.Vision.ContactLevel.Visible;                           // the side knows (the pilot sees him) …
        Assert.False(s.IsShownToPlayer(belov, revealAll: false));                      // … but the player is not told
        Assert.Contains("0 nähty", HudText.Status(s, "fi", ""));
    }

    [Fact]
    public void SquadKeys_Ignore_TheStrandedMansSquad()
    {
        var s = Session();
        Assert.Equal(1, s.SquadCount);
        s.SelectSquad(1);
        Assert.Empty(s.Selection.Ids); // nobody to select there yet
    }

    [Fact]
    public void MapMarks_OnlyVisibleObjectives_FindAtItsArea_NeverAtThePlace()
    {
        var s = Session();
        var marks = MissionPaper.MapObjectives(s);
        Assert.Equal([1, 2], marks.Select(m => m.Number));
        Assert.Equal(new Vec2(6000, 3000), marks[0].At); // the search area, not the wreck nor the outpost
        Assert.Null(marks[1].At); // the way home: nothing to mark until he is with them
        s.Tracker!.Reveal("maps", 0);
        Assert.Equal(3, MissionPaper.MapObjectives(s).Count);
        Assert.Null(MissionPaper.MapObjectives(s)[2].At); // the maps are with the stranded pilot: not shown
    }
}
