using Nmf.Content;
using Nmf.Content.Missions;
using Nmf.Sim.Mission;

namespace Nmf.Content.Tests;

public class MissionLoaderTests
{
    private const string Valid = """
        id: raid
        title: { en: "Raid", fi: "Isku" }
        date: { en: "July 1942" }
        map: karhumaki
        briefing: { en: briefing.en.md, fi: briefing.fi.md }
        squads:
          first: { en: "Strike squad", fi: "Iskuryhmä" }
          second: { en: "Support squad" }
        forces:
          player:
            - { name: "Alik. Hero", weapon: suomi_kp31, grenade: m32, leader: true, nerve: 95, morale: 950, marksmanship: 70, leadership: 90, experience: 85 }
            - { name: "Sotm. Brave", weapon: mosin_m39, squad: second }
          enemy:
            - { name: "Serzhant Belov", weapon: ppsh41, leader: true, items: [orders] }
        objectives:
          - { id: grab, type: pick_up, item: orders, text: { en: "Take the orders", fi: "Ota käskyt" } }
          - { id: back, type: reach_zone, zone: start_zone, carrying: orders, requires: [grab], text: { en: "Bring them back" } }
        items:
          orders: { en: "Soviet orders", fi: "Käskyt" }
        enemy_ai: { counterattack: true }
        plan:
          - { kind: attack, points: [[250, 818], [330, 700], [466, 556]] }
          - { kind: withdraw, points: [[466, 556], [252, 816]] }
        """;

    private static string Dir(string yaml, bool briefings = true)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-mission-").FullName;
        File.WriteAllText(Path.Combine(dir, "mission.yaml"), yaml);
        if (briefings)
        {
            File.WriteAllText(Path.Combine(dir, "briefing.en.md"), "# Orders\nGo.");
            File.WriteAllText(Path.Combine(dir, "briefing.fi.md"), "# Käsky\nMene.");
        }
        return dir;
    }

    private static ContentLoadException Fails(string yaml) => Assert.Throws<ContentLoadException>(() => MissionLoader.Load(Dir(yaml)));

    [Fact]
    public void Load_ReadsAllFields()
    {
        var m = MissionLoader.Load(Dir(Valid));
        Assert.Equal("raid", m.Id);
        Assert.Equal(new Localized("Raid", "Isku"), m.Title);
        Assert.Equal("karhumaki", m.Map);
        Assert.Equal("# Orders\nGo.", m.Briefing.En);
        Assert.Equal("# Käsky\nMene.", m.Briefing.Fi);
        var hero = m.Player[0];
        Assert.Equal(new SoldierSpec("Alik. Hero", "suomi_kp31", "m32", true, 95, 950, 70, 90, [], Experience: 85), hero with { Items = [] });
        Assert.Equal(new SoldierSpec("Sotm. Brave", "mosin_m39", null, Squad: "second"), m.Player[1] with { Items = null });
        Assert.Equal("Iskuryhmä", m.SquadNames["first"].In("fi"));
        Assert.Equal([0, 1], m.Player.Select(m.SquadIndex).ToArray());
        Assert.Equal([0], m.Enemy.Select(m.SquadIndex).ToArray());
        Assert.Equal(["orders"], m.Enemy[0].Items);
        Assert.Equal(ObjectiveType.ReachZone, m.Objectives[1].Type);
        Assert.Equal(["grab"], m.Objectives[1].Requires);
        Assert.Equal("Soviet orders", m.Items["orders"].En);
        Assert.Equal(new EnemyAiSpec(Counterattack: true, Investigate: false), m.EnemyAi);
        Assert.Equal(EnemyAiSpec.None, MissionLoader.Load(Dir(Valid.Replace("enemy_ai: { counterattack: true }\n", ""))).EnemyAi);
    }

    [Fact]
    public void Load_ReadsTheTestbedFields_WithTheirDefaults()
    {
        var plain = MissionLoader.Load(Dir(Valid));
        Assert.False(plain.Debug);
        Assert.True(plain.Patrols);
        Assert.Equal((null, SoldierState.Fit, false), (plain.Enemy[0].At, plain.Enemy[0].State, plain.Enemy[0].Searched));

        var yaml = Valid.Replace("map: karhumaki\n", "map: karhumaki\ndebug: true\npatrols: false\n")
            .Replace("leader: true, items: [orders] }", "items: [orders], at: [120, 340], state: dead, searched: true }");
        var test = MissionLoader.Load(Dir(yaml));
        Assert.True(test.Debug);
        Assert.False(test.Patrols);
        Assert.Equal(((int, int)?)(120, 340), test.Enemy[0].At);
        Assert.Equal(SoldierState.Dead, test.Enemy[0].State);
        Assert.True(test.Enemy[0].Searched);
        foreach (var (state, expected) in new[] { ("wounded", SoldierState.Wounded), ("incapacitated", SoldierState.Incapacitated) })
            Assert.Equal(expected, MissionLoader.Load(Dir(Valid.Replace("leader: true, items: [orders] }", $"items: [orders], state: {state} }}"))).Enemy[0].State);
    }

    [Theory]
    [InlineData("state: sleeping", "state")]
    [InlineData("at: [1]", "at")]
    [InlineData("at: [-5, 10]", "at")]
    [InlineData("state: dead", "leader")] // Belov leads: a squad must not start without its leader
    public void Load_BadTestbedField_Throws(string bad, string expected)
    {
        Assert.Contains(expected, Fails(Valid.Replace("items: [orders] }", $"items: [orders], {bad} }}")).Message);
    }

    [Theory]
    [InlineData("type: pick_up, item: orders", "type: fly", "type")]
    [InlineData("text: { en: \"Take the orders\", fi: \"Ota käskyt\" }", "text: { fi: \"Ota käskyt\" }", "en")]
    [InlineData("requires: [grab]", "requires: [back]", "requires")]
    [InlineData("requires: [grab]", "requires: [nobody]", "nobody")]
    [InlineData("nerve: 95", "nerve: 150", "nerve")]
    [InlineData("morale: 950", "morale: 1200", "morale")]
    [InlineData("experience: 85", "experience: -5", "experience")]
    [InlineData("squad: second", "squad: third", "third")]
    [InlineData("items: [orders]", "items: [money]", "money")]
    [InlineData("map: karhumaki", "map: \"\"", "map")]
    public void Load_BadField_Throws(string good, string bad, string expected)
    {
        Assert.Contains(good, Valid);
        Assert.Contains(expected, Fails(Valid.Replace(good, bad)).Message);
    }

    [Fact]
    public void SquadNumbers_FollowTheOrderTheSquadsAreDeclared_NotTheRoster()
    {
        var yaml = Valid.Replace("leadership: 90, experience: 85 }", "leadership: 90, experience: 85, squad: second }")
                        .Replace("weapon: mosin_m39, squad: second }", "weapon: mosin_m39, squad: first }");
        var m = MissionLoader.Load(Dir(yaml));
        Assert.Equal([1, 0], m.Player.Select(m.SquadIndex).ToArray());
        Assert.Equal("Iskuryhmä", m.PlayerSquadName(0)!.In("fi"));
    }

    [Fact]
    public void LoadAll_ListsEveryMissionFolder_ABrokenOneWithItsError()
    {
        var root = Directory.CreateTempSubdirectory("nmf-missions-").FullName;
        Directory.Move(Dir(Valid), Path.Combine(root, "raid"));
        Directory.Move(Dir(Valid.Replace("map: karhumaki", "map: \"\"")), Path.Combine(root, "broken"));
        var all = MissionLoader.LoadAll(root);
        Assert.Equal(["broken", "raid"], all.Select(m => m.Id));
        Assert.Null(all[0].Spec);
        Assert.Contains("map", all[0].Error);
        Assert.Equal("Raid", all[1].Spec!.Title.En);
        Assert.Empty(MissionLoader.LoadAll(Path.Combine(root, "nowhere")));
        Directory.CreateDirectory(Path.Combine(root, "notes")); // no mission.yaml: not a mission
        Assert.Equal(2, MissionLoader.LoadAll(root).Count);
    }

    [Fact]
    public void LoadAll_TheCoreMissions_IncludeIskuosasto()
    {
        var all = MissionLoader.LoadAll(Path.Combine(CoreContentTests.RepoRoot(), "content", "core", "missions"));
        Assert.Contains(all, m => m.Id == "iskuosasto" && m.Spec is not null);
    }

    [Fact]
    public void Load_MissingBriefing_Throws()
    {
        Assert.Contains("briefing", Assert.Throws<ContentLoadException>(() => MissionLoader.Load(Dir(Valid, briefings: false))).Message);
    }

    [Fact]
    public void TheTestbed_IsADebugMission_WithEveryManOnOpenGroundAndTheSceneSetUp()
    {
        var root = CoreContentTests.RepoRoot();
        var mission = MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "testikentta"));
        Assert.True(mission.Debug);
        Assert.False(mission.Patrols);
        Assert.False(MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "iskuosasto")).Debug);
        var map = Nmf.Content.Tiled.TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var scenario = MissionScenario.Create(map, mission, weapons, grenades, 1942);
        Assert.Empty(scenario.Patrols);
        Assert.All(scenario.Sim.Units, u => Assert.True(map.CellAt(u.Position).IsPassable, $"{u.Name} stands on a boulder"));
        var reds = scenario.Sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Red).ToList();
        Assert.Contains(reds, u => u.IsOutOfAction && u.WasSearchedBy(Nmf.Sim.Units.Side.Blue));
        Assert.Contains(reds, u => u.IsOutOfAction && !u.WasSearchedBy(Nmf.Sim.Units.Side.Blue));
        Assert.Contains(reds, u => !u.IsOutOfAction && u.Items.Any(i => i.Id == "soviet_orders"));
    }

    [Fact]
    public void Iskuosasto_LoadsAndMatchesItsMap()
    {
        var root = CoreContentTests.RepoRoot();
        var mission = MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "iskuosasto"));
        Assert.Equal("iskuosasto", mission.Id);
        Assert.Equal("Iskuosasto", mission.Title.In("fi"));
        Assert.NotNull(mission.Briefing.Fi);
        var map = Nmf.Content.Tiled.TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var sim = MissionScenario.Create(map, mission, weapons, grenades, 1942).Sim;
        var tracker = new MissionTracker(mission, map);
        Assert.Equal(7, sim.Units.Count(u => u.Side == Nmf.Sim.Units.Side.Blue));
        Assert.Equal(9, sim.Units.Count(u => u.Side == Nmf.Sim.Units.Side.Red));
        foreach (var side in new[] { Nmf.Sim.Units.Side.Blue, Nmf.Sim.Units.Side.Red })
        {
            var squads = sim.Units.Where(u => u.Side == side).GroupBy(u => u.Squad).ToList();
            Assert.Equal(2, squads.Count);
            Assert.All(squads, g => Assert.Single(g, u => u.IsLeader));
        }
        Assert.All(sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue), u => Assert.True(u.Nerve >= 75, $"{u.Name} is no hero"));
        Assert.All(sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue), u => Assert.True(u.Experience >= 70, $"{u.Name} is no veteran"));
        Assert.Equal(new EnemyAiSpec(true, true), mission.EnemyAi);
        Assert.Single(MissionScenario.Create(map, mission, weapons, grenades, 1942).Commanders);
        Assert.All(sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Red), u => Assert.True(u.Experience < 75, $"{u.Name} is too good"));
        Assert.Contains(sim.Units, u => u.Items.Any(i => i.Id == "soviet_orders"));
        Assert.Empty(tracker.Update(sim));
    }

    [Fact]
    public void Load_NoObjectives_Throws()
    {
        var yaml = Valid[..Valid.IndexOf("objectives:", StringComparison.Ordinal)] + "objectives: []\nitems:\n  orders: { en: \"Soviet orders\" }\n";
        Assert.Contains("objective", Fails(yaml).Message);
    }

    [Theory]
    [InlineData("objectives:\n", "objectives:\n  - ~\n")]
    [InlineData("  player:\n", "  player:\n    - ~\n")]
    public void Load_EmptyListEntry_Throws(string anchor, string replacement)
    {
        Assert.Contains(anchor, Valid);
        Assert.Contains("empty", Fails(Valid.Replace(anchor, replacement)).Message);
    }

    [Fact]
    public void Load_DuplicateObjectiveId_Throws()
    {
        Assert.Contains("duplicate", Fails(Valid.Replace("id: back", "id: grab")).Message);
    }

    [Fact]
    public void Load_ReadsThePlan_InCentimetres()
    {
        var plan = MissionLoader.Load(Dir(Valid)).Plan;
        Assert.Equal(2, plan.Count);
        Assert.Equal(PlanKind.Attack, plan[0].Kind);
        Assert.Equal(new Nmf.Sim.Core.Vec2(25_050, 81_850), plan[0].Points[0]);
        Assert.Equal(PlanKind.Withdraw, plan[1].Kind);
    }

    [Theory]
    [InlineData("kind: attack", "kind: dance", "kind")]
    [InlineData("points: [[466, 556], [252, 816]]", "points: [[466, 556]]", "points")]
    public void Load_BadPlan_Throws(string good, string bad, string expected)
    {
        Assert.Contains(expected, Fails(Valid.Replace(good, bad)).Message);
    }

    [Fact]
    public void Iskuosasto_PlanRunsThroughPassableGround()
    {
        var root = CoreContentTests.RepoRoot();
        var mission = MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "iskuosasto"));
        var map = Nmf.Content.Tiled.TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        Assert.Contains(mission.Plan, a => a.Kind == PlanKind.Attack);
        Assert.Contains(mission.Plan, a => a.Kind == PlanKind.Withdraw);
        foreach (var point in mission.Plan.SelectMany(a => a.Points))
            Assert.True(map.Contains(point) && map.CellAt(point).IsPassable, $"plan point {point} is off the map or blocked");
    }

    [Fact]
    public void Iskuosasto_AttackOnTheSovietLeader_MakesProgress_Deterministically()
    {
        var root = CoreContentTests.RepoRoot();
        var mission = MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "iskuosasto"));
        var map = Nmf.Content.Tiled.TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));

        (ulong Hash, long Closest, int BlueShots, string Outcome) Run()
        {
            var scenario = MissionScenario.Create(map, mission, weapons, grenades, 1942);
            var sim = scenario.Sim;
            var blues = sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue).ToList();
            var belov = sim.Units.First(u => u.Side == Nmf.Sim.Units.Side.Red);
            foreach (var b in blues)
                sim.Submit(Nmf.Sim.Units.Side.Blue, new Nmf.Sim.Orders.MoveOrder(b.Id, belov.Position, Nmf.Sim.Units.MoveMode.Auto));
            // Advance until the first enemy is seen, then attack him (as a player would, by double-clicking him).
            Nmf.Sim.Units.Unit? target = null;
            int i = 0;
            for (; i < 20 * 300 && target is null; i++)
            {
                scenario.Tick();
                sim.Step();
                target = sim.Units.FirstOrDefault(u => u.Side == Nmf.Sim.Units.Side.Red && !u.IsOutOfAction
                    && sim.Knowledge(Nmf.Sim.Units.Side.Blue).LevelOf(u.Id) == Nmf.Sim.Vision.ContactLevel.Visible);
            }
            Assert.NotNull(target);
            long start = blues.Where(b => !b.IsOutOfAction).Min(b => (long)(b.Position - target!.Position).Length);
            foreach (var b in blues.Where(b => !b.IsOutOfAction))
                sim.Submit(Nmf.Sim.Units.Side.Blue, new Nmf.Sim.Orders.AttackOrder(b.Id, target!.Id));
            long closest = start;
            int shots = 0;
            for (int k = 0; k < 20 * 180; k++)
            {
                scenario.Tick();
                shots += sim.Step().Count(e => e is Nmf.Sim.Events.ShotFired s && sim.FindUnit(s.Shooter)!.Side == Nmf.Sim.Units.Side.Blue);
                var alive = blues.Where(b => !b.IsOutOfAction).ToList();
                if (alive.Count > 0)
                    closest = Math.Min(closest, alive.Min(b => (long)(b.Position - target!.Position).Length));
            }
            string outcome = $"target {(target!.IsOutOfAction ? "down" : "standing")}, blue down {blues.Count(b => b.IsOutOfAction)}";
            Assert.True(shots > 0, "the covering half never fired");
            Assert.True(closest < start - 1500 || target.IsOutOfAction, $"the attack got no closer than {closest / 100} m from {start / 100} m ({outcome})");
            return (Nmf.Sim.Core.StateHash.Compute(sim), closest, shots, outcome);
        }

        var a = Run();
        Assert.Equal(a, Run());
    }
}
