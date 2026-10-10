using Nmf.Content;
using Nmf.Content.Missions;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Mission;

namespace Nmf.Content.Tests;

/// <summary>Spec 2026-10-10-events-design §2–§5: places, zones, events, find and rescue objectives, stranded men.</summary>
public class MissionEventsLoaderTests
{
    private const string Rescue = """
        id: rescue
        title: { en: "Rescue" }
        date: { en: "Winter 1942" }
        map: karhumaki
        briefing: { en: briefing.en.md }
        squads:
          strike: { en: "Strike squad" }
          pilot: { en: "Pilot" }
          post: { en: "Post" }
          reserve: { en: "Reserve" }
        places:
          wreck: [612, 340]
        zones:
          home: { min: [400, 560], max: [520, 640] }
          search: { min: [560, 280], max: [680, 400] }
        forces:
          player:
            - { name: "Alik. Hero", weapon: suomi_kp31, squad: strike, leader: true }
            - { name: "Luutn. Saarinen", weapon: mosin_m39, squad: pilot, stranded: true, state: wounded, at: [612, 344] }
          enemy:
            - { name: "Serzhant Belov", weapon: ppsh41, squad: post, leader: true }
            - { name: "Kers. Petrov", weapon: ppsh41, squad: reserve, leader: true, at: [700, 120] }
        items:
          maps: { en: "Maps" }
        objectives:
          - { id: find_wreck, type: find, place: wreck, area: search, text: { en: "Find the wreck" } }
          - { id: bring_back, type: rescue, unit: "Luutn. Saarinen", zone: home, requires: [find_wreck], text: { en: "Bring him home" } }
          - { id: papers, type: pick_up, item: maps, hidden: true, text: { en: "Take the maps" } }
        events:
          - id: wreck_found
            when: { objective: find_wreck }
            do:
              - { message: { en: "The wreck!", fi: "Hylky!" } }
              - { wait_s: 120 }
              - { send: { squad: reserve, to: wreck, mode: run } }
              - { release: reserve }
          - id: late
            when: { after_s: 900, event: wreck_found }
            do:
              - { reveal: papers }
              - { end: failure }
          - id: spotted
            when: { contact: enemy, zone: { name: home, side: player }, losses: { side: player, at_least: 2 } }
            do:
              - { send: { squad: reserve, to: home } }
        """;

    private static string Dir(string yaml)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-events-").FullName;
        File.WriteAllText(Path.Combine(dir, "mission.yaml"), yaml);
        File.WriteAllText(Path.Combine(dir, "briefing.en.md"), "# Orders");
        return dir;
    }

    private static string Error(string yaml) => Assert.Throws<ContentLoadException>(() => MissionLoader.Load(Dir(yaml))).Message;

    [Fact]
    public void PlacesAndZones_InCentimetres()
    {
        var m = MissionLoader.Load(Dir(Rescue));
        Assert.Equal(new Vec2(61_250, 34_050), m.Places["wreck"]);
        var home = Assert.Single(m.MissionZones, z => z.Name == "home");
        Assert.Equal(new Vec2(40_000, 56_000), home.Min);
        Assert.Equal(new Vec2(52_000, 64_000), home.Max);
    }

    [Fact]
    public void Objectives_FindRescueHidden()
    {
        var m = MissionLoader.Load(Dir(Rescue));
        var find = m.Objectives[0];
        Assert.Equal(ObjectiveType.Find, find.Type);
        Assert.Equal("wreck", find.Place);
        Assert.Equal("search", find.Area);
        Assert.Equal(1_500, find.RadiusCm);
        var rescue = m.Objectives[1];
        Assert.Equal(ObjectiveType.Rescue, rescue.Type);
        Assert.Equal("Luutn. Saarinen", rescue.Unit);
        Assert.Equal("home", rescue.Zone);
        Assert.True(m.Objectives[2].Hidden);
        Assert.False(find.Hidden);
    }

    [Fact]
    public void Stranded_Read()
    {
        var m = MissionLoader.Load(Dir(Rescue));
        Assert.True(m.Player[1].Stranded);
        Assert.False(m.Player[0].Stranded);
    }

    [Fact]
    public void Events_ConditionsAndActions()
    {
        var m = MissionLoader.Load(Dir(Rescue));
        Assert.Equal(["wreck_found", "late", "spotted"], m.Events.Select(e => e.Id));
        var found = m.Events[0];
        Assert.Equal("find_wreck", found.When.Objective);
        Assert.Equal([EventActionKind.Message, EventActionKind.Wait, EventActionKind.Send, EventActionKind.Release], found.Do.Select(a => a.Kind));
        Assert.Equal(new Localized("The wreck!", "Hylky!"), found.Do[0].Text);
        Assert.Equal(120 * SimConstants.TicksPerSecond, found.Do[1].Ticks);
        Assert.Equal("reserve", found.Do[2].Squad);
        Assert.Equal("wreck", found.Do[2].To);
        Assert.Equal(Nmf.Sim.Units.MoveMode.Run, found.Do[2].Mode);
        Assert.Equal("reserve", found.Do[3].Squad);
        var late = m.Events[1];
        Assert.Equal(900 * SimConstants.TicksPerSecond, late.When.AfterTicks);
        Assert.Equal("wreck_found", late.When.Event);
        Assert.Equal("papers", late.Do[0].Objective);
        Assert.Equal(EventActionKind.End, late.Do[1].Kind);
        Assert.False(late.Do[1].Success);
        var spotted = m.Events[2].When;
        Assert.Equal(Nmf.Sim.Units.Side.Red, spotted.ContactBy);
        Assert.Equal("home", spotted.ZoneName);
        Assert.Equal(Nmf.Sim.Units.Side.Blue, spotted.ZoneSide);
        Assert.Equal(Nmf.Sim.Units.Side.Blue, spotted.LossesSide);
        Assert.Equal(2, spotted.LossesAtLeast);
        Assert.Equal(Nmf.Sim.Units.MoveMode.Auto, m.Events[2].Do[0].Mode); // walking pace unless told
    }

    [Fact]
    public void OldMissions_StillLoad_NothingNew()
    {
        var m = MissionLoader.Load(Path.Combine(CoreContentTests.RepoRoot(), "content", "core", "missions", "iskuosasto"));
        Assert.Empty(m.Events);
        Assert.Empty(m.Places);
        Assert.Empty(m.MissionZones);
        Assert.All(m.Objectives, o => Assert.False(o.Hidden));
    }

    [Theory]
    [InlineData("place: wreck, area", "place: crash, area", "place 'crash'")]
    [InlineData("unit: \"Luutn. Saarinen\"", "unit: \"Luutn. Ghost\"", "'Luutn. Ghost'")]
    [InlineData("unit: \"Luutn. Saarinen\"", "unit: \"Serzhant Belov\"", "'Serzhant Belov'")]
    [InlineData("squad: reserve, to: wreck", "squad: reinforcements, to: wreck", "squad 'reinforcements'")]
    [InlineData("mode: run", "mode: fly", "mode")]
    [InlineData("{ release: reserve }", "{ release: ghosts }", "squad 'ghosts'")]
    [InlineData("{ reveal: papers }", "{ reveal: treasure }", "objective 'treasure'")]
    [InlineData("when: { objective: find_wreck }", "when: { objective: find_treasure }", "objective 'find_treasure'")]
    [InlineData("event: wreck_found", "event: nothing_happened", "event 'nothing_happened'")]
    [InlineData("{ end: failure }", "{ end: maybe }", "end")]
    [InlineData("contact: enemy", "contact: aliens", "contact")]
    [InlineData("{ reveal: papers }", "{ reveal: papers, wait_s: 3 }", "one action")]
    [InlineData("when: { objective: find_wreck }", "when: { }", "when")]
    [InlineData("when: { objective: find_wreck }", "when: { objective: find_wreck, weather: snow }", "weather")]
    public void BadEvents_AreClearErrors(string good, string bad, string mentions)
    {
        Assert.Contains(good, Rescue);
        Assert.Contains(mentions, Error(Rescue.Replace(good, bad)));
    }

    [Fact]
    public void DuplicateEventIds_AreAnError() =>
        Assert.Contains("duplicate event", Error(Rescue.Replace("id: late", "id: wreck_found").Replace("event: wreck_found", "after_s: 1")));
}
