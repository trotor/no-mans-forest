using Nmf.Content.Audio;

namespace Nmf.Content.Tests;

public sealed class AudioCatalogLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("nmf-audio-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string yaml)
    {
        var path = Path.Combine(_dir, "sounds.yaml");
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void ValidFile_Loads_WithDefaults()
    {
        var catalog = AudioCatalogLoader.Load(Write("""
            cues:
              shot.rifle: { bus: sfx, files: [sfx/a.ogg], loud: true }
              ui.click:   { bus: ui, files: [] }
            surfaces: { forest: forest }
            """));
        var shot = catalog.Cues["shot.rifle"];
        Assert.Equal(AudioBus.Sfx, shot.Bus);
        Assert.True(shot.Loud);
        Assert.Equal(50, shot.Priority);
        Assert.Equal(6, shot.MaxVoices);
        Assert.Equal(0, shot.VolumeDb);
        Assert.Equal(["sfx/a.ogg"], shot.Files);
        Assert.Empty(catalog.Cues["ui.click"].Files);
        Assert.Equal(AudioBus.Ui, catalog.Cues["ui.click"].Bus);
        Assert.Equal("forest", catalog.Surfaces["forest"]);
    }

    [Fact]
    public void Find_SkipsCuesWithoutFiles()
    {
        var catalog = AudioCatalogLoader.Load(Write("""
            cues:
              shot.mosin_m39: { bus: sfx, files: [] }
              shot.rifle:     { bus: sfx, files: [sfx/x.ogg] }
            """));
        Assert.Equal("shot.rifle", catalog.Find("shot.mosin_m39", "shot.rifle")!.Id);
        Assert.Null(catalog.Find("nope"));
        Assert.Null(catalog.Find("shot.mosin_m39"));
    }

    [Theory]
    [InlineData("{ bus: loud }", "bus")]
    [InlineData("{ bus: sfx, volume_db: 7 }", "volume_db")]
    [InlineData("{ bus: sfx, priority: 101 }", "priority")]
    [InlineData("{ bus: sfx, max_voices: 0 }", "max_voices")]
    [InlineData("{ bus: sfx, files: [a.wav] }", ".ogg")]
    public void BadCue_IsAClearError(string cue, string mentions)
    {
        var ex = Assert.Throws<ContentLoadException>(() => AudioCatalogLoader.Load(Write($"cues:\n  shot.bad: {cue}\n")));
        Assert.Contains(mentions, ex.Message);
        Assert.Contains("shot.bad", ex.Message);
    }

    [Fact]
    public void MissingFiles_AreListed()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sfx"));
        File.WriteAllBytes(Path.Combine(_dir, "sfx", "a.ogg"), [0]);
        var catalog = AudioCatalogLoader.Load(Write("""
            cues:
              x: { bus: sfx, files: [sfx/a.ogg, sfx/b.ogg] }
            """));
        Assert.Equal(["sfx/b.ogg"], AudioCatalogLoader.MissingFiles(catalog, _dir));
    }
}
