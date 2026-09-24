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
}
