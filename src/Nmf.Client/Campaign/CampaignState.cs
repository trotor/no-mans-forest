using System.Text.Json;
using System.Text.Json.Serialization;
using Nmf.Content.Campaigns;

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
    /// <summary>Experience points earned in the campaign.</summary>
    public int Xp { get; set; }
    public ManStatus Status { get; set; }
    public int Missions { get; set; }
    public int Kills { get; set; }
    public int Hits { get; set; }
}

/// <summary>One mission played in the campaign.</summary>
public sealed record CampaignHistoryEntry(string Mission, bool Success, int Seconds);

/// <summary>A campaign under way; kept between games as user://campaign.json.</summary>
public sealed class CampaignState
{
    public string Campaign { get; set; } = "";
    /// <summary>The chapter to play next (the last one once <see cref="Completed"/>).</summary>
    public int Chapter { get; set; }
    public bool Completed { get; set; }
    public List<CampaignManState> Men { get; set; } = [];
    public List<CampaignHistoryEntry> History { get; set; } = [];

    /// <summary>The start of a campaign: its whole roster fit, no points yet.</summary>
    public static CampaignState New(CampaignSpec spec) => new()
    {
        Campaign = spec.Id,
        Men = spec.Roster.Select(m => new CampaignManState
        {
            Name = m.Name, Nerve = m.Nerve, Morale = m.Morale, Marksmanship = m.Marksmanship, Leadership = m.Leadership, Experience = m.Experience,
        }).ToList(),
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
