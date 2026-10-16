using System.Text;
using Nmf.Content.Campaigns;

namespace Nmf.Client.Campaign;

/// <summary>
/// Replacements for the fallen (spec 2026-10-11-campaign-design §9c, "Täydennys"): while the platoon is below the
/// strength it started with, three candidates are on offer; the player takes one, and the next place has new ones.
/// The candidates follow from the campaign, how many have joined and the men's names, so nothing of them is saved.
/// </summary>
public static class CampaignRecruits
{
    public const int Offered = 3;

    /// <summary>The places open: the starting strength less the men still the platoon's (fit or recovering).</summary>
    public static int Vacancies(CampaignSpec spec, CampaignState state) =>
        spec.Recruits is null || state.Completed ? 0 : Math.Max(0, spec.Roster.Count - state.Men.Count(m => m.Status != ManStatus.Fallen));

    /// <summary>The men on offer for the next open place (fewer when the names run out; none without a place).</summary>
    public static IReadOnlyList<CampaignMan> Candidates(CampaignSpec spec, CampaignState state)
    {
        if (Vacancies(spec, state) == 0 || spec.Recruits is not { } r)
            return [];
        var taken = state.Men.Select(m => m.Name).ToHashSet();
        var free = r.Names.Select(n => FullName(r, n)).Where(n => !taken.Contains(n)).Distinct().ToList();
        var candidates = new List<CampaignMan>();
        for (int k = 0; k < Offered && free.Count > 0; k++)
        {
            string seed = $"{spec.Id}/{state.Recruited}/{k}";
            string name = free[(int)(Hash(seed + "/name") % (uint)free.Count)];
            free.Remove(name);
            candidates.Add(new CampaignMan(name, Roll(r.Nerve, seed + "/nerve"), r.Morale, Roll(r.Marksmanship, seed + "/marksmanship"),
                Roll(r.Leadership, seed + "/leadership"), Roll(r.Experience, seed + "/experience")));
        }
        return candidates;
    }

    /// <summary>Takes candidate <paramref name="choice"/> into the platoon: fit, no points, no specialty. False when there is no such candidate.</summary>
    public static bool Recruit(this CampaignState state, CampaignSpec spec, int choice)
    {
        var candidates = Candidates(spec, state);
        if (choice < 0 || choice >= candidates.Count)
            return false;
        var c = candidates[choice];
        state.Men.Add(new CampaignManState
        {
            Name = c.Name, Nerve = c.Nerve, Morale = c.Morale, Marksmanship = c.Marksmanship, Leadership = c.Leadership, Experience = c.Experience,
        });
        state.Recruited++;
        return true;
    }

    private static string FullName(RecruitSpec r, string name) => r.Rank.Length > 0 ? $"{r.Rank} {name}" : name;

    private static int Roll((int Min, int Max) range, string seed) => range.Min + (int)(Hash(seed) % (uint)(range.Max - range.Min + 1));

    /// <summary>FNV-1a: the same on every machine and every .NET (string.GetHashCode is not).</summary>
    private static uint Hash(string text)
    {
        uint h = 2166136261;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
            h = (h ^ b) * 16777619;
        return h;
    }
}
