using Nmf.Content.Tactics;

namespace Nmf.Content.Tests;

public sealed class TacticLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nmf-tactics-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string FireAndMovement = """
        id: fire_and_movement
        name: { en: "Fire and movement", fi: "Tuli ja liike" }
        fire_base: { min_m: 35, max_m: 100 }
        maneuver:
          flank_deg: 60
          form_up: { min_m: 25, max_m: 45 }
        assault: { suppression: 250, wait_s: 20, give_up_s: 60 }
        abort: { casualties_pct: 30, pinned_s: 10 }
        """;

    private const string LongFire = """
        id: long_fire
        name: { en: "Long fire", fi: "Pitkä tuli" }
        fire_base: { min_m: 50, max_m: 150 }
        abort: { casualties_pct: 30, pinned_s: 10 }
        """;

    private void Write(string name, string yaml) => File.WriteAllText(Path.Combine(_dir, name), yaml);

    [Fact]
    public void ValidFile_Loads()
    {
        Write("10_fm.yaml", FireAndMovement);
        var t = Assert.Single(TacticLoader.LoadDirectory(_dir));
        Assert.Equal("fire_and_movement", t.Id);
        Assert.Equal("Tuli ja liike", t.Name("fi"));
        Assert.Equal("Fire and movement", t.Name("en"));
        Assert.Equal(3_500, t.FireBaseMinCm);
        Assert.Equal(10_000, t.FireBaseMaxCm);
        Assert.Equal(new Nmf.Sim.Tactics.TacticManeuver(60, 2_500, 4_500), t.Maneuver);
        Assert.Equal(new Nmf.Sim.Tactics.TacticAssault(250, 400, 1_200), t.Assault);
        Assert.Equal(30, t.AbortCasualtiesPct);
        Assert.Equal(200, t.AbortPinnedTicks);
    }

    [Fact]
    public void LongFire_HasNoManeuverNorAssault()
    {
        Write("20_lf.yaml", LongFire);
        var t = Assert.Single(TacticLoader.LoadDirectory(_dir));
        Assert.Null(t.Maneuver);
        Assert.Null(t.Assault);
    }

    [Fact]
    public void Tactics_AreInFileNameOrder()
    {
        Write("20_lf.yaml", LongFire);
        Write("10_fm.yaml", FireAndMovement);
        Assert.Equal(["fire_and_movement", "long_fire"], TacticLoader.LoadDirectory(_dir).Select(t => t.Id));
    }

    [Theory]
    [InlineData("fire_base: { min_m: 35, max_m: 100 }", "fire_base: { min_m: 0, max_m: 100 }", "min_m")]
    [InlineData("fire_base: { min_m: 35, max_m: 100 }", "fire_base: { min_m: 35, max_m: 30 }", "max_m")]
    [InlineData("fire_base: { min_m: 35, max_m: 100 }", "fire_base: { min_m: 35, max_m: 301 }", "max_m")]
    [InlineData("flank_deg: 60", "flank_deg: 151", "flank_deg")]
    [InlineData("form_up: { min_m: 25, max_m: 45 }", "form_up: { min_m: 25, max_m: 120 }", "form_up")]
    [InlineData("casualties_pct: 30", "casualties_pct: 0", "casualties_pct")]
    public void BadField_IsAClearError(string good, string bad, string mentions)
    {
        Write("x.yaml", FireAndMovement.Replace(good, bad));
        var ex = Assert.Throws<ContentLoadException>(() => TacticLoader.LoadDirectory(_dir));
        Assert.Contains(mentions, ex.Message);
    }

    [Fact]
    public void AssaultWithoutManeuver_IsAnError()
    {
        Write("x.yaml", LongFire + "\nassault: { suppression: 250, wait_s: 20, give_up_s: 60 }\n");
        Assert.Contains("assault", Assert.Throws<ContentLoadException>(() => TacticLoader.LoadDirectory(_dir)).Message);
    }

    [Fact]
    public void DuplicateId_IsAnError()
    {
        Write("a.yaml", LongFire);
        Write("b.yaml", LongFire);
        Assert.Contains("duplicate", Assert.Throws<ContentLoadException>(() => TacticLoader.LoadDirectory(_dir)).Message);
    }
}
