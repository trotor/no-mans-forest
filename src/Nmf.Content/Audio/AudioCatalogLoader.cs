using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Audio;

/// <summary>Loads content/core/audio/sounds.yaml (spec 2026-10-09-sound-design).</summary>
public static class AudioCatalogLoader
{
    private const double MaxVolumeDb = 6;
    private const int MaxVoicesLimit = 32;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static AudioCatalog Load(string path)
    {
        CatalogYaml? y;
        try
        {
            y = Deserializer.Deserialize<CatalogYaml>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(path, ex.InnerException?.Message is { } inner ? $"{ex.Message} ({inner})" : ex.Message, ex);
        }
        if (y is null)
            throw new ContentLoadException(path, "file is empty");

        try
        {
            var cues = new Dictionary<string, CueDef>(StringComparer.Ordinal);
            foreach (var (id, cue) in y.Cues ?? [])
                cues[id] = Cue(id, cue ?? new CueYaml());
            return new AudioCatalog(cues, new Dictionary<string, string>(y.Surfaces ?? [], StringComparer.Ordinal));
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    /// <summary>The listed files (relative to <paramref name="audioRoot"/>) that are not on disk, in cue order.</summary>
    public static IReadOnlyList<string> MissingFiles(AudioCatalog catalog, string audioRoot) =>
        catalog.Cues.Keys.Order(StringComparer.Ordinal)
            .SelectMany(id => catalog.Cues[id].Files)
            .Where(file => !File.Exists(Path.Combine(audioRoot, file)))
            .ToList();

    private static CueDef Cue(string id, CueYaml y)
    {
        var bus = y.Bus switch
        {
            "sfx" => AudioBus.Sfx,
            "voice" => AudioBus.Voice,
            "ambience" => AudioBus.Ambience,
            "music" => AudioBus.Music,
            "ui" => AudioBus.Ui,
            _ => throw new ArgumentException($"cue '{id}': bus must be sfx, voice, ambience, music or ui, was '{y.Bus}'"),
        };
        double volume = y.VolumeDb ?? 0;
        if (volume > MaxVolumeDb)
            throw new ArgumentException($"cue '{id}': volume_db must be at most {MaxVolumeDb}, was {volume}");
        int priority = y.Priority ?? 50;
        if (priority is < 0 or > 100)
            throw new ArgumentException($"cue '{id}': priority must be 0..100, was {priority}");
        int maxVoices = y.MaxVoices ?? 6;
        if (maxVoices is < 1 or > MaxVoicesLimit)
            throw new ArgumentException($"cue '{id}': max_voices must be 1..{MaxVoicesLimit}, was {maxVoices}");
        var files = y.Files ?? [];
        foreach (var file in files)
            if (!file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"cue '{id}': files must be .ogg (use the import tool), was '{file}'");
        return new CueDef(id, bus, files, volume, priority, y.Loud ?? false, maxVoices);
    }

    private sealed class CatalogYaml
    {
        public Dictionary<string, CueYaml?>? Cues { get; set; }
        public Dictionary<string, string>? Surfaces { get; set; }
    }

    private sealed class CueYaml
    {
        public string? Bus { get; set; }
        public List<string>? Files { get; set; }
        public double? VolumeDb { get; set; }
        public int? Priority { get; set; }
        public bool? Loud { get; set; }
        public int? MaxVoices { get; set; }
    }
}
