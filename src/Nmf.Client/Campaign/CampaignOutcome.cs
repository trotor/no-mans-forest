using Nmf.Content.Campaigns;
using Nmf.Sim.Combat;

namespace Nmf.Client.Campaign;

/// <summary>How a campaign mission ended for each of our men who went (the stranded are not the platoon's).</summary>
public sealed record CampaignOutcome(bool Success, int Seconds, IReadOnlyList<CampaignOutcome.Man> Men, CampaignTally Tally)
{
    public sealed record Man(string Name, WoundLevel Wound, bool Captured);

    public static CampaignOutcome From(GameSession session, CampaignTally tally) => new(
        session.Tracker?.Result == true,
        (int)session.GameTime.TotalSeconds,
        session.CardUnits.Where(u => u.Name is not null).Select(u => new Man(u.Name!, u.Wound, u.IsCaptured)).ToList(),
        tally);
}

/// <summary>What the mission gave one man: the points earned in it, where he stands now, what he did.</summary>
public sealed record ManReport(string Name, int Xp, ManStatus Status, int Hits, int Kills, WoundLevel Wound);

public static class CampaignXp
{
    public const int Survived = 10;
    public const int PerHit = 3;
    public const int PerKill = 10;
    public const int MissionDone = 10;
}

public static class CampaignRecord
{
    /// <summary>
    /// A mission played to its end: the men who sat it out recover, those who went earn their points and take their
    /// wounds with them; success moves on to the next chapter (the campaign is done after the last), failure plays
    /// the same one again with the losses kept.
    /// </summary>
    public static IReadOnlyList<ManReport> Record(this CampaignState state, CampaignSpec spec, CampaignOutcome outcome)
    {
        var went = outcome.Men.Select(m => m.Name).ToHashSet();
        foreach (var man in state.Men.Where(m => m.Status == ManStatus.Recovering && !went.Contains(m.Name)))
            man.Status = ManStatus.Fit;
        var reports = new List<ManReport>();
        foreach (var result in outcome.Men)
        {
            if (state.Men.FirstOrDefault(m => m.Name == result.Name) is not { } man)
                continue;
            var status = result.Captured || result.Wound == WoundLevel.Dead ? ManStatus.Fallen
                : result.Wound >= WoundLevel.Serious ? ManStatus.Recovering
                : ManStatus.Fit;
            var (hits, kills) = outcome.Tally.Of(man.Name);
            int xp = (status == ManStatus.Fallen ? 0 : CampaignXp.Survived) + hits * CampaignXp.PerHit + kills * CampaignXp.PerKill
                     + (outcome.Success ? CampaignXp.MissionDone : 0);
            man.Xp += xp;
            man.Hits += hits;
            man.Kills += kills;
            man.Missions++;
            man.Status = status;
            reports.Add(new ManReport(man.Name, xp, status, hits, kills, result.Wound));
        }
        state.History.Add(new CampaignHistoryEntry(spec.Chapters[state.Chapter].Mission, outcome.Success, outcome.Seconds));
        if (outcome.Success)
        {
            if (state.Chapter + 1 < spec.Chapters.Count)
                state.Chapter++;
            else
                state.Completed = true;
        }
        return reports;
    }
}
