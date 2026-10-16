using System.Text.Json;
using System.Text.Json.Serialization;
using Nmf.Content.Campaigns;
using Nmf.Sim.Units;

namespace Nmf.Client.Campaign;

/// <summary>Where a man of the platoon stands between missions (spec 2026-10-11-campaign-design §9a).</summary>
public enum ManStatus
{
    Fit,
    /// <summary>Badly wounded in the last mission he was in: he sits the next one out.</summary>
    Recovering,
    /// <summary>Killed or taken: gone for good.</summary>
    Fallen,
}

/// <summary>A man of the platoon as the campaign keeps him: what he brings to a slot, and what he has done (no weapon: the slot's applies).</summary>
public sealed class CampaignManState
{
    public string Name { get; set; } = "";
    public int Nerve { get; set; }
    public int? Morale { get; set; }
    public int Marksmanship { get; set; }
    public int Leadership { get; set; }
    public int Experience { get; set; }
    /// <summary>What he has learned to do best (spec §9b); chosen once, from level 2.</summary>
    public Specialty Specialty { get; set; }
    /// <summary>Experience points earned in the campaign.</summary>
    public int Xp { get; set; }
    public ManStatus Status { get; set; }
    public int Missions { get; set; }
    public int Kills { get; set; }
    public int Hits { get; set; }
}

/// <summary>
/// The player's choice for one slot of the next mission (spec §9c): a man for it, or nobody, and a weapon. A slot
/// without a pick is filled by the rules; a pick naming a man who cannot go counts for nothing.
/// </summary>
public sealed class SlotPick
{
    public int Slot { get; set; }
    public string? Man { get; set; }
    /// <summary>Nobody goes into this slot.</summary>
    public bool Empty { get; set; }
    public string? Weapon { get; set; }
}

/// <summary>One mission played in the campaign.</summary>
public sealed record CampaignHistoryEntry(string Mission, bool Success, int Seconds);

/// <summary>A campaign under way; kept between games as user://campaign-&lt;id&gt;.json.</summary>
public sealed class CampaignState
{
    public string Campaign { get; set; } = "";
    /// <summary>The chapter to play next (the last one once <see cref="Completed"/>).</summary>
    public int Chapter { get; set; }
    public bool Completed { get; set; }
    public List<CampaignManState> Men { get; set; } = [];
    public List<CampaignHistoryEntry> History { get; set; } = [];
    /// <summary>The player's picks for the next mission's slots; cleared once a mission is recorded.</summary>
    public List<SlotPick> Lineup { get; set; } = [];
    /// <summary>The platoon's spare weapons beyond the mission's own (weapon id → how many): from the start and from the enemy.</summary>
    public Dictionary<string, int> Armoury { get; set; } = [];
    /// <summary>How many replacements have joined; the next candidates are worked out from it.</summary>
    public int Recruited { get; set; }

    /// <summary>The start of a campaign: its whole roster fit, no points yet.</summary>
    public static CampaignState New(CampaignSpec spec) => new()
    {
        Campaign = spec.Id,
        Men = spec.Roster.Select(m => new CampaignManState
        {
            Name = m.Name, Nerve = m.Nerve, Morale = m.Morale, Marksmanship = m.Marksmanship, Leadership = m.Leadership, Experience = m.Experience,
            Specialty = m.Specialty,
        }).ToList(),
        Armoury = spec.Armoury.ToDictionary(),
    };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>The saved campaign; a missing, broken or foreign file (another campaign, a chapter it does not have) is a fresh start.</summary>
    public static CampaignState Load(string? json, CampaignSpec spec)
    {
        if (string.IsNullOrWhiteSpace(json))
            return New(spec);
        try
        {
            var state = JsonSerializer.Deserialize<CampaignState>(json, Options);
            if (state is null || state.Campaign != spec.Id || state.Chapter < 0 || state.Chapter >= spec.Chapters.Count
                || state.Men is not { Count: > 0 } || state.Men.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name)))
                return New(spec);
            state.History = (state.History ?? []).Where(h => h is not null).ToList();
            state.Lineup = (state.Lineup ?? []).Where(p => p is not null).ToList();
            state.Armoury ??= [];
            return state;
        }
        catch (JsonException)
        {
            return New(spec);
        }
    }
}

/// <summary>The campaign cannot go on: nobody is left to send.</summary>
public sealed class CampaignException(string message) : Exception(message);
