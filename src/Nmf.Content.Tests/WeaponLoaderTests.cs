using Nmf.Content;
using Nmf.Content.Weapons;
using Nmf.Sim.Combat;

namespace Nmf.Content.Tests;

public class WeaponLoaderTests
{
    private const string Rifle = """
        id: test_rifle
        name: "Test Rifle"
        class: rifle
        magazine: 5
        aim_ticks: 30
        burst: 1
        round_interval_ticks: 0
        recover_ticks: 24
        reload_ticks: 80
        spread_mrad: 6
        range_m: 300
        lethality_pct: 70
        suppression: 80
        noise_m: 300
        """;

    private static string Write(string dir, string name, string yaml)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void Load_ReadsAllFieldsAndConvertsMetres()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            var w = WeaponLoader.Load(Write(dir, "rifle.yaml", Rifle));
            Assert.Equal(new WeaponDef("test_rifle", "Test Rifle", WeaponClass.Rifle, 5, 30, 1, 0, 24, 80, 6, 30_000, 70, 80, 30_000), w);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("magazine: 5\n", "", "magazine")]
    [InlineData("class: rifle", "class: cannon", "class")]
    [InlineData("magazine: 5", "magazine: lots", "rifle.yaml")]
    [InlineData("noise_m: 300", "noise_m: 300\ncolour: red", "colour")]
    [InlineData("lethality_pct: 70", "lethality_pct: 170", "lethality")]
    public void Load_BadFiles_ThrowWithFileAndProblem(string find, string replace, string expected)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            var path = Write(dir, "rifle.yaml", Rifle.Replace(find, replace));
            var ex = Assert.Throws<ContentLoadException>(() => WeaponLoader.Load(path));
            Assert.Contains("rifle.yaml", ex.Message);
            Assert.Contains(expected, ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadDirectory_RejectsDuplicateIds()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            Write(dir, "a.yaml", Rifle);
            Write(dir, "b.yaml", Rifle);
            var ex = Assert.Throws<ContentLoadException>(() => WeaponLoader.LoadDirectory(dir));
            Assert.Contains("duplicate", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CoreWeapons_LoadAndCoverTheSkirmishLoadouts()
    {
        var root = CoreContentTests.RepoRoot();
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        foreach (var id in new[] { "mosin_m39", "mosin_9130", "suomi_kp31", "ppsh41", "lahti_saloranta", "dp27" })
            Assert.True(weapons.ContainsKey(id), id);
    }

    [Fact]
    public void SpareMagazines_OptionalDefaultsToFour()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try { Assert.Equal(4, WeaponLoader.Load(Write(dir, "rifle.yaml", Rifle)).SpareMagazines); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SpareMagazines_Read()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try { Assert.Equal(12, WeaponLoader.Load(Write(dir, "rifle.yaml", Rifle + "\nspare_magazines: 12")).SpareMagazines); }
        finally { Directory.Delete(dir, true); }
    }
}
