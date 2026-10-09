namespace Nmf.Content.Audio;

public enum AudioBus { Sfx, Voice, Ambience, Music, Ui }

/// <summary>One sound cue: its bus, the file versions to pick from, and how it mixes (spec 2026-10-09-sound-design).</summary>
public sealed record CueDef(string Id, AudioBus Bus, IReadOnlyList<string> Files, double VolumeDb = 0, int Priority = 50,
    bool Loud = false, int MaxVoices = 6);

/// <summary>Every sound cue of the game (content/core/audio/sounds.yaml). A cue without files is silent.</summary>
public sealed class AudioCatalog(IReadOnlyDictionary<string, CueDef> cues, IReadOnlyDictionary<string, string> surfaces)
{
    public static AudioCatalog Empty { get; } = new(new Dictionary<string, CueDef>(), new Dictionary<string, string>());

    public IReadOnlyDictionary<string, CueDef> Cues => cues;

    /// <summary>Map terrain name → footstep surface.</summary>
    public IReadOnlyDictionary<string, string> Surfaces => surfaces;

    /// <summary>The first of <paramref name="ids"/> that has files; null = silence.</summary>
    public CueDef? Find(params string[] ids)
    {
        foreach (var id in ids)
            if (cues.TryGetValue(id, out var cue) && cue.Files.Count > 0)
                return cue;
        return null;
    }
}
