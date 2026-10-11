using Nmf.Sim.Units;

namespace Nmf.Client.Campaign;

/// <summary>How the men grow with what they come through (spec 2026-10-11-campaign-design §9b, "Kehitys").</summary>
public static class CampaignGrowth
{
    /// <summary>The points each level starts at: Recruit, Seasoned, Veteran, Old hand.</summary>
    public static readonly IReadOnlyList<int> Thresholds = [0, 25, 70, 140];

    /// <summary>Experience and nerve each level brings.</summary>
    public const int PerLevel = 5;
    /// <summary>Marksmanship: one point for so many hits, so many points at most from one mission.</summary>
    public const int HitsPerMarksmanship = 2, MaxMarksmanshipPerMission = 4;
    /// <summary>Leadership for a leader who comes through.</summary>
    public const int LeaderGrowth = 5;
    /// <summary>The loader's ceiling for every skill.</summary>
    public const int Cap = 100;

    /// <summary>The level (1–4) these points make.</summary>
    public static int Level(int xp) => Thresholds.Count(t => xp >= t);

    /// <summary>He may take a specialty: seasoned at least, not fallen, none yet.</summary>
    public static bool CanSpecialise(CampaignManState man) =>
        man.Status != ManStatus.Fallen && man.Specialty == Specialty.None && Level(man.Xp) >= 2;

    /// <summary>Gives the man a specialty for good; false (nothing changes) when he cannot take it.</summary>
    public static bool Specialise(this CampaignState state, string name, Specialty specialty)
    {
        if (specialty == Specialty.None || state.Men.FirstOrDefault(m => m.Name == name) is not { } man || !CanSpecialise(man))
            return false;
        man.Specialty = specialty;
        return true;
    }

    /// <summary>
    /// A man who came through: each level crossed since <paramref name="levelBefore"/> adds experience and nerve, his
    /// hits sharpen his aim, and leading his squad to the end his leadership.
    /// </summary>
    internal static void Grow(CampaignManState man, int levelBefore, int hits, bool led)
    {
        int levels = Level(man.Xp) - levelBefore;
        man.Experience = Math.Min(Cap, man.Experience + levels * PerLevel);
        man.Nerve = Math.Min(Cap, man.Nerve + levels * PerLevel);
        man.Marksmanship = Math.Min(Cap, man.Marksmanship + Math.Min(MaxMarksmanshipPerMission, hits / HitsPerMarksmanship));
        if (led)
            man.Leadership = Math.Min(Cap, man.Leadership + LeaderGrowth);
    }
}
