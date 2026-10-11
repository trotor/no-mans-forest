using Nmf.Content.Campaigns;
using Nmf.Sim.Combat;

namespace Nmf.Client.Campaign;

/// <summary>How a campaign mission ended for each of our men who went (the stranded are not the platoon's).</summary>
public sealed record CampaignOutcome(bool Success, int Seconds, IReadOnlyList<CampaignOutcome.Man> Men, CampaignTally Tally)
{
    /// <param name="Led">A leader at the end: his squad's from the start, or one who took over.</param>
    public sealed record Man(string Name, WoundLevel Wound, bool Captured, bool Led = false);

    public static CampaignOutcome From(GameSession session, CampaignTally tally) => new(
        session.Tracker?.Result == true,
        (int)session.GameTime.TotalSeconds,
        session.CardUnits.Where(u => u.Name is not null).Select(u => new Man(u.Name!, u.Wound, u.IsCaptured, u.IsLeader)).ToList(),
        tally);
}

/// <summary>What the mission gave one man: the points earned in it, where he stands now, what he did, his level before and after.</summary>
public sealed record ManReport(string Name, int Xp, ManStatus Status, int Hits, int Kills, WoundLevel Wound, int LevelBefore = 1, int LevelAfter = 1);

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
            int levelBefore = CampaignGrowth.Level(man.Xp);
            man.Xp += xp;
            man.Hits += hits;
            man.Kills += kills;
            man.Missions++;
            man.Status = status;
            if (status != ManStatus.Fallen)
                CampaignGrowth.Grow(man, levelBefore, hits, result.Led);
            reports.Add(new ManReport(man.Name, xp, status, hits, kills, result.Wound, levelBefore, CampaignGrowth.Level(man.Xp)));
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
