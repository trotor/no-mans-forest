using System.Text.Json;

namespace Nmf.Client;

/// <summary>
/// The player's settings (spec 2026-10-10-settings-hints-design §3), kept in user://settings.json. Read leniently: what is
/// missing takes its default, what is out of range is brought back in.
/// </summary>
public sealed record GameSettings
{
    public static readonly int[] UiScales = [100, 125, 150];
    public static readonly string[] CardSizes = ["large", "small", "icon"];
    private const double SilentDb = -80;

    public int Master { get; init; } = 80;
    public int Sfx { get; init; } = 100;
    public int Voice { get; init; } = 100;
    public int Ambience { get; init; } = 70;
    public int Music { get; init; } = 60;
    public bool Mute { get; init; }
    public bool SoundMarks { get; init; }
    /// <summary>"fi" or "en"; null = from the system.</summary>
    public string? Language { get; init; }
    public bool Fullscreen { get; init; }
    public int UiScalePct { get; init; } = 100;
    public string CardSize { get; init; } = "small";
    public bool FollowAtStart { get; init; }
    /// <summary>The short note by the cursor of what a click and a double click would do.</summary>
    public bool CursorHints { get; init; } = true;
    /// <summary>A double click on an enemy stops the game and shows the attack plan first (spec 2026-10-10-plan-preview-design).</summary>
    public bool PreviewAttacks { get; init; } = true;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static GameSettings FromJson(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<GameSettings>(json, Options) ?? new GameSettings()).Clamped();
        }
        catch (JsonException)
        {
            return new GameSettings();
        }
    }

    public GameSettings Clamped() => this with
    {
        Master = Math.Clamp(Master, 0, 100),
        Sfx = Math.Clamp(Sfx, 0, 100),
        Voice = Math.Clamp(Voice, 0, 100),
        Ambience = Math.Clamp(Ambience, 0, 100),
        Music = Math.Clamp(Music, 0, 100),
        Language = Language is "fi" or "en" ? Language : null,
        UiScalePct = UiScales.Contains(UiScalePct) ? UiScalePct : 100,
        CardSize = CardSizes.Contains(CardSize) ? CardSize : "small",
    };

    /// <summary>A volume slider's percent as decibels for a bus; 0 % is silence.</summary>
    public static double VolumeDb(int pct) => pct <= 0 ? SilentDb : 20 * Math.Log10(pct / 100.0);

    /// <summary>Sounds as marks: when asked for, or whenever the sound is off.</summary>
    public bool SoundMarksEffective(bool muteArg) => SoundMarks || Mute || Master == 0 || muteArg;
}
