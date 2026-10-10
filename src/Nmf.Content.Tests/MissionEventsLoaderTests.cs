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

    [Fact]
    public void Support_Read()
    {
        var m = MissionLoader.Load(Dir(Rescue + "\nsupport: { mortar: { he: 8, delay_s: [60, 120], radio: \"Alik. Hero\" } }\n"));
        Assert.Equal(new MortarSupportSpec(8, 60, 120, "Alik. Hero"), m.Support?.Mortar);
        Assert.Null(MissionLoader.Load(Dir(Rescue)).Support);
    }

    [Theory]
    [InlineData("radio: \"Serzhant Belov\"", "radio")]
    [InlineData("radio: \"Nobody\"", "radio")]
    [InlineData("he: 0, delay_s: [60, 120], radio: \"Alik. Hero\"", "he")]
    [InlineData("he: 8, delay_s: [120, 60], radio: \"Alik. Hero\"", "delay_s")]
    public void Support_BadFields(string mortar, string mentions)
    {
        string body = mortar.StartsWith("radio") ? "he: 8, delay_s: [60, 120], " + mortar : mortar;
        Assert.Contains(mentions, Error(Rescue + "\nsupport: { mortar: { " + body + " } }\n"));
    }

    private const string Phones = """

        enemy_ai: { counterattack: true, alertness: calm, flares: 2 }
        phones:
          - { id: hq, place: wreck, line: [[612, 340], [700, 300], [799, 300]] }
          - { id: post, at: [500, 100], line: [[500, 100], [500, 0]] }
        """;

    [Fact]
    public void Alertness_Flares_Phones_Read()
    {
        var events = Rescue.Replace("when: { after_s: 900, event: wreck_found }", "when: { phone: hq }");
        var m = MissionLoader.Load(Dir(events + Phones));
        Assert.True(m.EnemyAi.Calm);
        Assert.Equal(2, m.EnemyAi.Flares);
        Assert.Equal(2, m.Phones.Count);
        Assert.Equal(new Vec2(61_250, 34_050), m.Phones[0].At); // at the place
        Assert.Equal(new Vec2(79_950, 30_050), m.Phones[0].Line[^1]);
        Assert.Equal(new Vec2(50_050, 10_050), m.Phones[1].At);
        Assert.Equal("hq", m.Events.Single(e => e.Id == "late").When.Phone);
        var plain = MissionLoader.Load(Dir(Rescue));
        Assert.False(plain.EnemyAi.Calm);
        Assert.Empty(plain.Phones);
    }

    [Theory]
    [InlineData("line: [[500, 100]]", "two points")]
    [InlineData("line: [[500, 100], [500]]", "[x, y]")]
    public void Phone_BadLine(string line, string mentions) =>
        Assert.Contains(mentions, Error(Rescue + Phones.Replace("line: [[500, 100], [500, 0]]", line)));

    [Fact]
    public void Phone_NeedsTheEnemyCalm_AndKnownIds()
    {
        Assert.Contains("alertness", Error(Rescue + Phones.Replace("alertness: calm, ", "")));
        Assert.Contains("place", Error(Rescue + Phones.Replace("place: wreck", "place: nowhere")));
        Assert.Contains("duplicate", Error(Rescue + Phones.Replace("id: post", "id: hq")));
        Assert.Contains("phone 'nope'", Error(Rescue.Replace("when: { after_s: 900, event: wreck_found }", "when: { phone: nope }") + Phones));
        Assert.Contains("alertness", Error(Rescue + "\nenemy_ai: { alertness: sleepy }\n"));
    }

    [Fact]
    public void Search_Read_AndChecked()
    {
        var yaml = Rescue.Replace("- { release: reserve }", "- { search: { squad: reserve, zone: search } }");
        var search = MissionLoader.Load(Dir(yaml)).Events[0].Do[^1];
        Assert.Equal(new EventAction(EventActionKind.Search, Squad: "reserve", To: "search"), search);
        Assert.Contains("squad", Error(yaml.Replace("squad: reserve, zone", "squad: nobody, zone")));
        Assert.Contains("zone", Error(yaml.Replace(", zone: search }", " }")));
    }

    [Fact]
    public void Winter_Light_Skis_Ahkio_Read()
    {
        var yaml = Rescue.Replace("{ name: \"Alik. Hero\", weapon: suomi_kp31, squad: strike, leader: true }",
                       "{ name: \"Alik. Hero\", weapon: suomi_kp31, squad: strike, leader: true, skis: true, ahkio: true }")
                   + "\nwinter: { snow_cm: 40 }\nlight: dusk\n";
        var m = MissionLoader.Load(Dir(yaml));
        Assert.Equal(40, m.SnowCm);
        Assert.Equal(Nmf.Sim.World.LightLevel.Dusk, m.Light);
        Assert.True(m.Player[0].Skis);
        Assert.True(m.Player[0].Ahkio);
        Assert.False(m.Player[1].Skis);
        var summer = MissionLoader.Load(Dir(Rescue));
        Assert.Null(summer.SnowCm);
        Assert.Equal(Nmf.Sim.World.LightLevel.Day, summer.Light);
    }

    [Theory]
    [InlineData("winter: { snow_cm: 0 }", "snow_cm")]
    [InlineData("winter: { snow_cm: 400 }", "snow_cm")]
    [InlineData("winter: {}", "snow_cm")]
    [InlineData("light: twilight", "light")]
    public void Winter_Light_BadValues(string line, string mentions) =>
        Assert.Contains(mentions, Error(Rescue + "\n" + line + "\n"));
}
