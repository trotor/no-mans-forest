using Nmf.Sim.Mission;

namespace Nmf.Client.Campaign;

/// <summary>
/// A mission played with the campaign's men (spec 2026-10-11-campaign-design §9a). The mission's own men are slots: a
/// slot keeps its place, squad, leading, weapon, grenade and kit; the campaign man in it brings his name and his skills.
/// Used only on the campaign path: plain play and the probes never come here.
/// </summary>
public static class CampaignMission
{
    /// <summary>
    /// The men who can go: the fit ones; with none fit, the recovering come back (recovery only comes with a mission
    /// played, so the campaign would otherwise stand still).
    /// </summary>
    public static IReadOnlyList<CampaignManState> Available(CampaignState state)
    {
        var fit = state.Men.Where(m => m.Status == ManStatus.Fit).ToList();
        return fit.Count > 0 ? fit : state.Men.Where(m => m.Status == ManStatus.Recovering).ToList();
    }

    /// <summary>Whether anyone is left to send.</summary>
    public static bool CanPlay(CampaignState state) => Available(state).Count > 0;

    /// <summary>
    /// Who takes each of the mission's player entries (null: a stranded man, who stays as he is, or a slot left empty).
    /// In turn: a man in his own named slot; the leader slots, the best leader first; the kit slots (the radio, an
    /// ahkio, items); the plain slots, the most experienced first. Ties go by the roster's order.
    /// </summary>
    public static IReadOnlyList<CampaignManState?> Fill(MissionSpec mission, CampaignState state)
    {
        var player = mission.Player;
        var left = Available(state).ToList(); // in roster order
        var taken = new CampaignManState?[player.Count];
        var slots = Enumerable.Range(0, player.Count).Where(i => !player[i].Stranded).ToList();
        int radio = RadioSlot(mission);

        foreach (int i in slots)
            if (left.FirstOrDefault(m => m.Name == player[i].Name) is { } own)
                Take(i, own);
        foreach (int i in slots.Where(i => taken[i] is null && player[i].Leader))
            if (left.OrderByDescending(m => m.Leadership).ThenByDescending(m => m.Experience).FirstOrDefault() is { } leader)
                Take(i, leader);
        foreach (int i in slots.Where(i => taken[i] is null && (i == radio || player[i].Ahkio || player[i].Items is { Count: > 0 })))
            Take(i, Experienced());
        foreach (int i in slots.Where(i => taken[i] is null))
            Take(i, Experienced());
        return taken;

        CampaignManState? Experienced() => left.OrderByDescending(m => m.Experience).FirstOrDefault();

        void Take(int slot, CampaignManState? man)
        {
            if (man is null)
                return;
            taken[slot] = man;
            left.Remove(man);
        }
    }

    /// <summary>The mission with the campaign's men in its slots. Throws <see cref="CampaignException"/> when nobody is left.</summary>
    public static MissionSpec Apply(MissionSpec mission, CampaignState state)
    {
        if (!CanPlay(state))
            throw new CampaignException("Nobody is left to send");
        var fill = Fill(mission, state);
        int radio = RadioSlot(mission);
        var player = new List<SoldierSpec>();
        string? radioman = null;
        for (int i = 0; i < mission.Player.Count; i++)
        {
            var slot = mission.Player[i];
            if (slot.Stranded)
            {
                player.Add(slot);
                continue;
            }
            if (fill[i] is not { } man)
                continue;
            player.Add(slot with
            {
                Name = man.Name, Nerve = man.Nerve, Morale = man.Morale ?? slot.Morale, Marksmanship = man.Marksmanship,
                Leadership = man.Leadership, Experience = man.Experience, State = SoldierState.Fit,
                Specialty = man.Specialty, // his own skill, none included: the slot's is not his
            });
            if (i == radio)
                radioman = man.Name;
        }
        PromoteLeaders(mission, player);
        // The mortar goes with the radio slot: whoever fills it calls; with it empty there is no mortar.
        var support = mission.Support is { Mortar: { } mortar } s
            ? s with { Mortar = radioman is null ? null : mortar with { Radio = radioman } }
            : mission.Support;
        return mission with { Player = player, Support = support };
    }

    /// <summary>A squad left with men but no leader slot filled: its best leader leads.</summary>
    private static void PromoteLeaders(MissionSpec mission, List<SoldierSpec> player)
    {
        string? first = mission.SquadNames.Keys.FirstOrDefault();
        foreach (var squad in player.Where(s => !s.Stranded).GroupBy(s => s.Squad ?? first).ToList())
        {
            if (squad.Any(s => s.Leader))
                continue;
            var best = squad.OrderByDescending(s => s.Leadership).ThenByDescending(s => s.Experience).First();
            player[player.IndexOf(best)] = best with { Leader = true };
        }
    }

    /// <summary>The radioman's slot, by his name in the mission as written (-1: no mortar).</summary>
    private static int RadioSlot(MissionSpec mission) =>
        mission.Support?.Mortar is { } mortar ? mission.Player.ToList().FindIndex(s => s.Name == mortar.Radio) : -1;
}
