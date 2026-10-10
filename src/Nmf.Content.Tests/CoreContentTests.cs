using Nmf.Content.Audio;
using Nmf.Content.Tactics;
using Nmf.Content.Tiled;
using Nmf.Sim.World;

namespace Nmf.Content.Tests;

/// <summary>Everything shipped under content/ must load cleanly.</summary>
public class CoreContentTests
{
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NoMansForest.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NoMansForest.slnx) not found.");
    }

    [Fact]
    public void AllCoreMaps_Load()
    {
        var maps = Directory.GetFiles(Path.Combine(RepoRoot(), "content"), "*.tmx", SearchOption.AllDirectories);
        Assert.NotEmpty(maps);
        foreach (var path in maps)
            TmxMapLoader.Load(path);
    }

    [Fact]
    public void CoreSounds_Load_AndEveryListedFileExists()
    {
        string audio = Path.Combine(RepoRoot(), "content", "core", "audio");
        var catalog = AudioCatalogLoader.Load(Path.Combine(audio, "sounds.yaml"));
        Assert.Contains("shot.rifle", catalog.Cues.Keys);
        Assert.Equal("forest", catalog.Surfaces["forest"]);
        Assert.Empty(AudioCatalogLoader.MissingFiles(catalog, audio));
    }

    [Fact]
    public void CoreTactics_Load_FireAndMovementFirst()
    {
        var tactics = TacticLoader.LoadDirectory(Path.Combine(RepoRoot(), "content", "core", "tactics"));
        Assert.Equal(["fire_and_movement", "infiltration", "long_fire"], tactics.Select(t => t.Id));
    }

    [Fact]
    public void SandboxMap_HasExpectedShapeAndFeatures()
    {
        var map = TmxMapLoader.Load(Path.Combine(RepoRoot(), "content", "core", "maps", "sandbox.tmx"));
        Assert.Equal(12, map.Width);
        Assert.Equal(8, map.Height);
        Assert.Equal(new[] { "none", "forest", "grass", "road", "swamp" }, map.TerrainNames);
        Assert.Equal(2, map.Features.Zones.Count);
        Assert.Single(map.Features.Points);
        Assert.Single(map.Features.Paths);
    }

    [Fact]
    public void SkirmishMap_HasSpawnsPatrolAndConnectedPaths()
    {
        var map = TmxMapLoader.Load(Path.Combine(RepoRoot(), "content", "core", "maps", "skirmish.tmx"));
        Assert.Equal(128, map.Width);
        Assert.Equal(96, map.Height);

        var blue = map.Features.Points.Where(p => p.Type == "blue").ToList();
        var red = map.Features.Points.Where(p => p.Type == "red").ToList();
        Assert.Equal(4, blue.Count);
        Assert.Equal(5, red.Count);
        var patrol = Assert.Single(map.Features.Paths, p => p.Type == "patrol");

        foreach (var from in blue)
        {
            Assert.True(map.CellAt(from.Position).IsPassable, $"{from.Name} stands on an impassable cell");
            foreach (var to in red.Select(r => r.Position).Concat(patrol.Points))
                Assert.NotNull(Pathfinder.FindPath(map, from.Position, to));
        }
    }

    [Fact]
    public void MachineGunBurstMissingByTwoMetres_PinsTheManItIsAimedAt()
    {
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(RepoRoot(), "content", "core", "weapons"));
        var lmgs = weapons.Values.Where(w => w.Class == Nmf.Sim.Combat.WeaponClass.Lmg).ToList();
        Assert.NotEmpty(lmgs);
        const int missBy = 200;
        foreach (var lmg in lmgs)
        {
            int perRound = lmg.SuppressionPerRound * (Nmf.Sim.Combat.CombatRules.AimedMissRadiusCm - missBy) / Nmf.Sim.Combat.CombatRules.AimedMissRadiusCm;
            Assert.True(perRound * lmg.RoundsPerBurst >= Nmf.Sim.Combat.CombatRules.PinnedAt, $"{lmg.Id}: a burst gives only {perRound * lmg.RoundsPerBurst}");
        }
    }

    [Fact]
    public void FirstHitFromAnyCoreWeapon_TakesAManOutAtMostAThirdOfTheTime()
    {
        var root = RepoRoot();
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var lethalities = weapons.Values.Select(w => (w.Id, w.LethalityPct)).Concat(grenades.Values.Select(g => (g.Id, g.LethalityPct)));
        foreach (var (id, lethality) in lethalities)
        {
            var sim = new Nmf.Sim.Simulation(new GridMap(10, 10, ["none"]), 42);
            int outOfAction = 0;
            const int hits = 600;
            for (int i = 0; i < hits; i++)
            {
                var man = sim.SpawnUnit(Nmf.Sim.Units.Side.Blue, new Nmf.Sim.Core.Vec2(50, 50), 7);
                Nmf.Sim.Combat.Damage.ApplyHit(sim, man, lethality, 0, []);
                if (man.IsOutOfAction) outOfAction++;
            }
            Assert.True(outOfAction * 3 <= hits, $"{id}: {outOfAction * 100 / hits} % of first hits take a man out");
        }
    }

    [Fact]
    public void WaterTileset_IsImpassableAndSeeThrough()
    {
        var doc = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), "content", "core", "tilesets", "water.tsx"));
        var props = doc.Descendants("property").ToDictionary(p => (string)p.Attribute("name")!, p => (string)p.Attribute("value")!);
        Assert.Equal("water", props["terrain"]);
        Assert.Equal("true", props["impassable"]);
        Assert.False(props.ContainsKey("obstacle_height_cm"));
        Assert.False(props.ContainsKey("concealment_per_m"));
    }

    [Fact]
    public void MissionTactics_Exist()
    {
        var tactics = TacticLoader.LoadDirectory(Path.Combine(RepoRoot(), "content", "core", "tactics"));
        foreach (var dir in Directory.GetDirectories(Path.Combine(RepoRoot(), "content", "core", "missions")))
        {
            var mission = Nmf.Content.Missions.MissionLoader.Load(dir);
            Assert.Empty(Nmf.Sim.Tactics.TacticChoice.Unknown(tactics, mission.Tactics));
            if (mission.EnemyAi.Tactic is { } enemyTactic)
                Assert.Contains(tactics, t => t.Id == enemyTactic);
        }
    }

    [Fact]
    public void EveryMissionsPlacesZonesAndMen_AreOnPassableGround_AndStart()
    {
        var root = RepoRoot();
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var tactics = TacticLoader.LoadDirectory(Path.Combine(root, "content", "core", "tactics"));
        foreach (var dir in Directory.GetDirectories(Path.Combine(root, "content", "core", "missions")))
        {
            var mission = Nmf.Content.Missions.MissionLoader.Load(dir);
            var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
            foreach (var (name, at) in mission.Places)
                Assert.True(map.Contains(at) && map.CellAt(at).IsPassable, $"{mission.Id}: place {name} at {at} is not passable ground");
            foreach (var zone in mission.MissionZones)
            {
                var centre = new Nmf.Sim.Core.Vec2((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);
                Assert.True(map.Contains(centre) && map.CellAt(centre).IsPassable, $"{mission.Id}: zone {zone.Name} centre {centre} is not passable");
            }
            // The whole mission starts: the scenario, its tracker and its event script (every zone name resolved on the map).
            var scenario = Nmf.Sim.Mission.MissionScenario.Create(map, mission, weapons, grenades, 1, tactics);
            var tracker = new Nmf.Sim.Mission.MissionTracker(mission, map);
            _ = new Nmf.Sim.Mission.MissionEvents(mission, scenario, tracker);
            foreach (var man in scenario.Sim.Units)
                Assert.True(map.CellAt(man.Position).IsPassable, $"{mission.Id}: {man.Name} starts on impassable ground at {man.Position}");
        }
    }

    [Fact]
    public void Iskuosasto_HasTheMortar_WithVirtanenOnTheRadio()
    {
        var root = RepoRoot();
        var mission = Nmf.Content.Missions.MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "iskuosasto"));
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var sim = Nmf.Sim.Mission.MissionScenario.Create(map, mission, weapons, grenades, 1).Sim;
        var mortar = Assert.Single(sim.MortarSupports);
        Assert.Equal("Kpl. Virtanen", sim.FindUnit(mortar.Radio)!.Name);
        Assert.Equal(8, mortar.Rounds);
    }

    [Fact]
    public void Hylky_WinterDusk_OnItsOwnMap_WithThePhoneAndTheRace()
    {
        var root = RepoRoot();
        var mission = Nmf.Content.Missions.MissionLoader.Load(Path.Combine(root, "content", "core", "missions", "hylky"));
        Assert.Equal("povenets", mission.Map);
        Assert.Equal(50, mission.SnowCm);
        Assert.Equal(Nmf.Sim.World.LightLevel.Dusk, mission.Light);
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", mission.Map + ".tmx"));
        var weapons = Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));
        var sim = Nmf.Sim.Mission.MissionScenario.Create(map, mission, weapons, grenades, 1).Sim;
        Assert.Single(sim.FieldPhones);
        Assert.True(sim.AlertnessOn(Nmf.Sim.Units.Side.Red));
        Assert.All(sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue && !u.Stranded), u => Assert.True(u.OnSkis));
        Assert.Single(sim.Units, u => u.HasAhkio);
        var pilot = Assert.Single(sim.Units, u => u.Stranded);
        Assert.True(pilot.IsOutOfAction);
        Assert.Contains(mission.Events, e => e.Do.Any(a => a.Kind == Nmf.Sim.Mission.EventActionKind.Search));
        Assert.Contains(mission.Events, e => e.When.Phone == "hq");
    }
}
