using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Mission;

/// <summary>Spec 2026-10-10-events-design §5: find, rescue and hidden objectives.</summary>
public class MissionTrackerEventsTests
{
    private static readonly Dictionary<string, WeaponDef> Weapons = new() { ["rifle"] = TestWeapons.Rifle() };
    private static readonly Dictionary<string, GrenadeDef> Grenades = [];

    /// <summary>A 40 m map: the hero at (5, 35) m, the pilot at (30, 30) m, the wreck at (20, 20) m, home the bottom-left corner.</summary>
    internal static MissionSpec Spec(IReadOnlyList<ObjectiveSpec> objectives, IReadOnlyList<EventSpec>? events = null) => new(
        "rescue", new Localized("Rescue"), new Localized("Today"), "map", new Localized("Go."),
        [new SoldierSpec("Alik. Hero", "rifle", null, Leader: true, At: (5, 35)),
         new SoldierSpec("Luutn. Pilot", "rifle", null, At: (30, 30), State: SoldierState.Wounded, Squad: "pilot", Stranded: true)],
        [new SoldierSpec("Serzhant Belov", "rifle", null, Leader: true, At: (35, 5), Squad: "post"),
         new SoldierSpec("Kers. Petrov", "rifle", null, Leader: true, At: (5, 5), Squad: "reserve")],
        objectives, new Dictionary<string, Localized> { ["maps"] = new("Maps") },
        Squads: new Dictionary<string, Localized> { ["strike"] = new("Strike"), ["pilot"] = new("Pilot"), ["post"] = new("Post"), ["reserve"] = new("Reserve") },
        Patrols: false,
        PlaceList: new Dictionary<string, Vec2> { ["wreck"] = new(2050, 2050) },
        ZoneList: [new MapZone("home", "mission", new Vec2(0, 3000), new Vec2(1000, 4000))],
        EventList: events);

    internal static GridMap Map() => new(40, 40, ["none"]);

    internal static readonly ObjectiveSpec FindWreck = new("find_wreck", ObjectiveType.Find, new Localized("Find the wreck"), Place: "wreck");
    internal static readonly ObjectiveSpec BringBack = new("bring_back", ObjectiveType.Rescue, new Localized("Bring him home"),
        Zone: "home", Unit: "Luutn. Pilot");
    internal static readonly ObjectiveSpec HiddenMaps = new("maps", ObjectiveType.PickUp, new Localized("Take the maps"), Item: "maps", Hidden: true);

    private static (Simulation Sim, MissionTracker Tracker, Unit Hero, Unit Pilot) Start(params ObjectiveSpec[] objectives)
    {
        var spec = Spec(objectives);
        var sim = MissionScenario.Create(Map(), spec, Weapons, Grenades, 1).Sim;
        var tracker = new MissionTracker(spec, sim.Map);
        return (sim, tracker, sim.Units[0], sim.Units[1]);
    }

    [Fact]
    public void Find_WithinRadius_Latches()
    {
        var (sim, tracker, hero, _) = Start(FindWreck, BringBack);
        tracker.Update(sim);
        Assert.False(tracker.IsDone("find_wreck"));
        hero.Position = new Vec2(1500, 1500); // 7 m from the wreck
        Assert.Contains(tracker.Update(sim), e => e is ObjectiveChanged { Id: "find_wreck", Done: true });
        hero.Position = new Vec2(500, 3500);
        tracker.Update(sim);
        Assert.True(tracker.IsDone("find_wreck"));
    }

    [Fact]
    public void Find_TheStrandedManHimselfDoesNotFindIt()
    {
        var (sim, tracker, _, pilot) = Start(FindWreck, BringBack);
        pilot.Position = new Vec2(2050, 2050); // he lies at the wreck: he is what is looked for, not a searcher
        tracker.Update(sim);
        Assert.False(tracker.IsDone("find_wreck"));
    }

    [Fact]
    public void Rescue_AliveInZone_Done()
    {
        var (sim, tracker, _, pilot) = Start(BringBack);
        tracker.Update(sim);
        Assert.False(tracker.IsDone("bring_back"));
        pilot.Position = new Vec2(500, 3500);
        tracker.Update(sim);
        Assert.True(tracker.IsDone("bring_back"));
        Assert.True(tracker.Result);
    }

    [Fact]
    public void Rescue_ManDead_Fails()
    {
        var (sim, tracker, _, pilot) = Start(FindWreck, BringBack);
        pilot.Wound = WoundLevel.Dead;
        Assert.Contains(tracker.Update(sim), e => e is MissionEnded { Success: false });
        Assert.False(tracker.Result);
    }

    [Fact]
    public void HiddenObjective_NotRevealed_DoesNotBlock()
    {
        var (sim, tracker, _, pilot) = Start(BringBack, HiddenMaps);
        Assert.False(tracker.IsVisible("maps"));
        pilot.Position = new Vec2(500, 3500);
        tracker.Update(sim);
        Assert.True(tracker.Result);
    }

    [Fact]
    public void Reveal_ThenItCounts()
    {
        var (sim, tracker, _, pilot) = Start(BringBack, HiddenMaps);
        var revealed = tracker.Reveal("maps", sim.Tick);
        Assert.Equal("maps", Assert.IsType<ObjectiveRevealed>(revealed).Id);
        Assert.True(tracker.IsVisible("maps"));
        Assert.Null(tracker.Reveal("maps", sim.Tick)); // once
        pilot.Position = new Vec2(500, 3500);
        tracker.Update(sim);
        Assert.Null(tracker.Result); // the maps are still to take
        Assert.Equal(1, tracker.DoneCount);
        Assert.Equal(2, tracker.VisibleCount);
    }

    [Fact]
    public void End_SetsTheResult()
    {
        var (sim, tracker, _, _) = Start(BringBack);
        Assert.IsType<MissionEnded>(tracker.End(success: false, sim.Tick));
        Assert.False(tracker.Result);
        Assert.Null(tracker.End(success: true, sim.Tick)); // final
    }

    [Theory]
    [InlineData("area")]
    [InlineData("rescue")]
    public void UnknownZone_Throws(string which)
    {
        var bad = which == "area" ? FindWreck with { Area = "nowhere" } : BringBack with { Zone = "nowhere" };
        var spec = Spec([bad]);
        var ex = Assert.Throws<ArgumentException>(() => new MissionTracker(spec, Map()));
        Assert.Contains("nowhere", ex.Message);
    }

    [Fact]
    public void HiddenRescue_ManDead_DoesNotFailUntilRevealed()
    {
        var (sim, tracker, hero, pilot) = Start(FindWreck, BringBack with { Hidden = true });
        pilot.Wound = WoundLevel.Dead;
        tracker.Update(sim);
        Assert.Null(tracker.Result);
        tracker.Reveal("bring_back", sim.Tick);
        tracker.Update(sim);
        Assert.False(tracker.Result);
    }

    [Fact]
    public void Rescue_CarriedHome()
    {
        var (sim, tracker, hero, pilot) = Start(BringBack);
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire; // nobody fights: the carry is what is looked at
        pilot.Wound = WoundLevel.Incapacitated; // he cannot walk: carried
        pilot.Stranded = false;
        hero.Position = pilot.Position + new Vec2(100, 0);
        sim.Submit(Side.Blue, new Nmf.Sim.Orders.CarryOrder(hero.Id, pilot.Id));
        for (int i = 0; i < 100; i++) sim.Step();
        Assert.Equal(pilot.Id, hero.Carrying);
        sim.Submit(Side.Blue, new Nmf.Sim.Orders.MoveOrder(hero.Id, new Vec2(500, 3500), MoveMode.Run));
        for (int i = 0; i < 2_000 && tracker.Result is null; i++)
        {
            sim.Step();
            tracker.Update(sim);
        }
        Assert.True(tracker.IsDone("bring_back"));
        Assert.True(tracker.Result);
    }
}
