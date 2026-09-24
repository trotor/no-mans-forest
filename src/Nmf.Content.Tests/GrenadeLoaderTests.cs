using Nmf.Content;
using Nmf.Content.Weapons;
using Nmf.Sim.Combat;

namespace Nmf.Content.Tests;

public class GrenadeLoaderTests
{
    private const string Yaml = """
        id: test_grenade
        name: "Test"
        fuse_ticks: 80
        throw_range_m: 30
        scatter_pct: 15
        blast_radius_m: 10
        lethal_radius_m: 4
        suppression: 600
        lethality_pct: 60
        """;

    [Fact]
    public void Load_ReadsFieldsAndConvertsMetres()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-grenades-").FullName;
        try
        {
            var path = Path.Combine(dir, "g.yaml");
            File.WriteAllText(path, Yaml);
            Assert.Equal(new GrenadeDef("test_grenade", "Test", 80, 3000, 15, 1000, 400, 600, 60), GrenadeLoader.Load(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("fuse_ticks: 80\n", "", "fuse_ticks")]
    [InlineData("scatter_pct: 15", "scatter_pct: 15\nbounce: 2", "bounce")]
    [InlineData("lethal_radius_m: 4", "lethal_radius_m: 40", "lethal")]
    public void Load_BadFiles_ThrowWithFileAndProblem(string find, string replace, string expected)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-grenades-").FullName;
        try
        {
            var path = Path.Combine(dir, "g.yaml");
            File.WriteAllText(path, Yaml.Replace(find, replace));
            var ex = Assert.Throws<ContentLoadException>(() => GrenadeLoader.Load(path));
            Assert.Contains("g.yaml", ex.Message);
            Assert.Contains(expected, ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CoreGrenades_Load()
    {
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(CoreContentTests.RepoRoot(), "content", "core", "grenades"));
        Assert.True(grenades.ContainsKey("m32"));
        Assert.True(grenades.ContainsKey("rgd33"));
    }
}
