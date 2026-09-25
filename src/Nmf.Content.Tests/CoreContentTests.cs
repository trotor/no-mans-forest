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
}
