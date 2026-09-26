using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Mission;

public class MissionTests
{
    private static readonly Dictionary<string, WeaponDef> Weapons = new() { ["rifle"] = TestWeapons.Rifle(), ["smg"] = TestWeapons.Smg() };
    private static readonly Dictionary<string, GrenadeDef> Grenades = new() { ["g"] = GrenadeDefTests.Test() };

    private static GridMap Map(int blue = 2, int red = 2)
    {
        var points = new List<MapPoint>();
        for (int i = 0; i < blue; i++) points.Add(new MapPoint($"b{i}", "blue", new Vec2(550 + i * 200, 3550)));
        for (int i = 0; i < red; i++) points.Add(new MapPoint($"r{i}", "red", new Vec2(550 + i * 200, 550)));
        var zones = new List<MapZone> { new("start_zone", "zone", new Vec2(0, 3000), new Vec2(2000, 4000)) };
        return new GridMap(40, 40, ["none"], new MapFeatures(zones, points, []));
    }

    private static MissionSpec Spec(params ObjectiveSpec[] objectives) => new(
        "test", new Localized("Test"), new Localized("Today"), "map", new Localized("Go."),
        [new SoldierSpec("Alik. Hero", "smg", "g", Leader: true, Nerve: 95, Morale: 950, Marksmanship: 80, Leadership: 90),
         new SoldierSpec("Sotm. Brave", "rifle", null, Nerve: 85)],
        [new SoldierSpec("Serzhant Belov", "smg", null, Leader: true, Items: ["orders"]), new SoldierSpec("Ryadovoy", "rifle", null)],
        objectives.Length > 0 ? objectives : [PickUp, BringBack],
        new Dictionary<string, Localized> { ["orders"] = new("Soviet orders", "Käskyt") });

    private static readonly ObjectiveSpec PickUp = new("grab", ObjectiveType.PickUp, new Localized("Take the orders"), Item: "orders");
    private static readonly ObjectiveSpec BringBack = new("back", ObjectiveType.ReachZone, new Localized("Bring them back"),
        Zone: "start_zone", Carrying: "orders", Requires: ["grab"]);

    [Fact]
    public void Scenario_AppliesTheRosterInPointOrder()
    {
        var sim = MissionScenario.Create(Map(), Spec(), Weapons, Grenades, 3).Sim;
        var hero = sim.Units[0];
        Assert.Equal("Alik. Hero", hero.Name);
        Assert.True(hero.IsLeader);
        Assert.Equal(95, hero.Nerve);
        Assert.Equal(950, hero.BaseMorale);
        Assert.Equal(950, hero.Morale);
        Assert.Equal(80, hero.Marksmanship);
        Assert.Equal(90, hero.LeaderQualityPct);
        Assert.Same(Weapons["smg"], hero.Weapon);
        Assert.Equal(2, hero.Grenades);
        Assert.Equal(new Vec2(550, 3550), hero.Position);
        Assert.Equal(CombatRules.BaseMorale, sim.Units[1].BaseMorale);
        var belov = sim.Units.First(u => u.Side == Side.Red);
        Assert.Equal("orders", Assert.Single(belov.Items).Id);
        Assert.Equal("Soviet orders", belov.Items[0].Name);
    }

    [Fact]
    public void Scenario_TooFewPoints_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => MissionScenario.Create(Map(blue: 1), Spec(), Weapons, Grenades, 3));
        Assert.Contains("blue", ex.Message);
    }

    [Fact]
    public void Tracker_UnknownZone_Throws()
    {
        var bad = BringBack with { Zone = "nowhere" };
        Assert.Throws<ArgumentException>(() => new MissionTracker(Spec(PickUp, bad), Map()));
    }

    [Fact]
    public void PickUp_DoneWhileCarried_RevertsWhenTheCarrierFalls()
    {
        var sim = MissionScenario.Create(Map(), Spec(), Weapons, Grenades, 3).Sim;
        var tracker = new MissionTracker(Spec(), Map());
        Assert.Empty(tracker.Update(sim));
        var hero = sim.Units[0];
        hero.Position = new Vec2(550, 550); // at the enemy position, out of the start zone
        hero.AddItem(new Item("orders", "Soviet orders"));
        Assert.Contains(tracker.Update(sim), e => e is ObjectiveChanged { Id: "grab", Done: true });
        Assert.Null(tracker.Result);
        Assert.True(tracker.IsDone("grab"));
        Damage.SetWound(sim, hero, WoundLevel.Dead, sim.Tick, []);
        Assert.Contains(tracker.Update(sim), e => e is ObjectiveChanged { Id: "grab", Done: false });
    }

    [Fact]
    public void ReachZone_NeedsTheRequiredObjectiveAndTheCarrier_ThenTheMissionSucceeds()
    {
        var sim = MissionScenario.Create(Map(), Spec(), Weapons, Grenades, 3).Sim;
        var tracker = new MissionTracker(Spec(), Map());
        var brave = sim.Units[1];            // in the start zone, carrying nothing
        tracker.Update(sim);
        Assert.False(tracker.IsDone("back"));
        brave.Position = new Vec2(550, 550); // out of the zone, takes the orders
        brave.AddItem(new Item("orders", "Soviet orders"));
        tracker.Update(sim);
        Assert.True(tracker.IsDone("grab"));
        Assert.False(tracker.IsDone("back"));
        brave.Position = new Vec2(1050, 3550); // back in the zone with them
        var events = tracker.Update(sim);
        Assert.True(tracker.IsDone("back"));
        Assert.Contains(events, e => e is MissionEnded { Success: true });
        Assert.True(tracker.Result);
    }

    [Fact]
    public void AllMenDown_MissionFails_AndTheResultIsFinal()
    {
        var sim = MissionScenario.Create(Map(), Spec(), Weapons, Grenades, 3).Sim;
        var tracker = new MissionTracker(Spec(), Map());
        foreach (var unit in sim.Units.Where(u => u.Side == Side.Blue))
            Damage.SetWound(sim, unit, WoundLevel.Dead, sim.Tick, []);
        Assert.Contains(tracker.Update(sim), e => e is MissionEnded { Success: false });
        Assert.False(tracker.Result);
        Assert.Empty(tracker.Update(sim));
        Assert.False(tracker.Result);
    }

    [Fact]
    public void Localized_FallsBackToEnglish()
    {
        Assert.Equal("Käskyt", new Localized("Orders", "Käskyt").In("fi"));
        Assert.Equal("Orders", new Localized("Orders").In("fi"));
        Assert.Equal("Orders", new Localized("Orders", "Käskyt").In("en"));
    }

    [Fact]
    public void ReachZone_StaysDoneWhenTheManLeaves()
    {
        var scout = new ObjectiveSpec("scout", ObjectiveType.ReachZone, new Localized("Scout the start"), Zone: "start_zone");
        var sim = MissionScenario.Create(Map(), Spec(scout, PickUp), Weapons, Grenades, 3).Sim;
        var tracker = new MissionTracker(Spec(scout, PickUp), Map());
        tracker.Update(sim);
        Assert.True(tracker.IsDone("scout"));
        foreach (var unit in sim.Units.Where(u => u.Side == Side.Blue))
            unit.Position = new Vec2(550, 550);
        Assert.DoesNotContain(tracker.Update(sim), e => e is ObjectiveChanged { Id: "scout" });
        Assert.True(tracker.IsDone("scout"));
    }

    [Fact]
    public void Success_IsFinalToo()
    {
        var sim = MissionScenario.Create(Map(), Spec(), Weapons, Grenades, 3).Sim;
        var tracker = new MissionTracker(Spec(), Map());
        sim.Units[1].AddItem(new Item("orders", "Soviet orders")); // Brave is in the start zone
        Assert.Contains(tracker.Update(sim), e => e is MissionEnded { Success: true });
        foreach (var unit in sim.Units.Where(u => u.Side == Side.Blue))
            Damage.SetWound(sim, unit, WoundLevel.Dead, sim.Tick, []);
        Assert.Empty(tracker.Update(sim));
        Assert.True(tracker.Result);
    }

    [Fact]
    public void Tracker_PlanPointOffTheMap_Throws()
    {
        var spec = Spec() with { PlanArrows = [new PlanArrow(PlanKind.Attack, [new Vec2(100, 100), new Vec2(900_000, 100)])] };
        Assert.Contains("plan", Assert.Throws<ArgumentException>(() => new MissionTracker(spec, Map())).Message);
    }
}
