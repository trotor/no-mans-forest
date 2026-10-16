using Nmf.Sim.Mission;

namespace Nmf.Client.Campaign;

/// <summary>
/// The player's lineup for the next mission (spec 2026-10-11-campaign-design §9c): who goes into which slot, which
/// slots stay empty, and which weapon each man carries. The weapons to hand out are the mission's slots' own and the
/// platoon's armoury.
/// </summary>
public static class CampaignLineup
{
    /// <summary>Puts a man into a slot (taking him out of any other he was picked for); null makes the slot automatic again.</summary>
    public static void PickMan(this CampaignState state, MissionSpec mission, int slot, string? man)
    {
        if (!IsSlot(mission, slot))
            return;
        if (man is not null)
            foreach (var other in state.Lineup.Where(p => p.Man == man && p.Slot != slot))
                other.Man = null;
        var pick = PickFor(state, slot);
        pick.Man = man;
        pick.Empty = false;
        Tidy(state);
    }

    /// <summary>Nobody goes into the slot.</summary>
    public static void LeaveEmpty(this CampaignState state, MissionSpec mission, int slot)
    {
        if (!IsSlot(mission, slot))
            return;
        var pick = PickFor(state, slot);
        pick.Man = null;
        pick.Empty = true;
    }

    /// <summary>
    /// Gives a filled slot this weapon: a free one if there is one, else the slot that has it gets this slot's weapon
    /// in exchange. False when the platoon has no such weapon or nobody goes into the slot.
    /// </summary>
    public static bool PickWeapon(this CampaignState state, MissionSpec mission, int slot, string weapon)
    {
        if (!IsSlot(mission, slot))
            return false;
        var pool = Pool(mission, state);
        var weapons = Weapons(mission, state);
        if (!pool.ContainsKey(weapon) || weapons[slot] is not { } current)
            return false;
        if (current == weapon)
            return true;
        if (weapons.Count(w => w == weapon) >= pool[weapon])
        {
            int other = weapons.Select((w, i) => (w, i)).First(x => x.w == weapon && x.i != slot).i;
            PickFor(state, other).Weapon = current;
        }
        PickFor(state, slot).Weapon = weapon;
        return true;
    }

    /// <summary>The weapons there are for this mission, in the order the slots name them, the armoury's after (by id).</summary>
    public static IReadOnlyDictionary<string, int> Pool(MissionSpec mission, CampaignState state)
    {
        var pool = new Dictionary<string, int>();
        foreach (var slot in mission.Player.Where(s => !s.Stranded))
            pool[slot.Weapon] = pool.GetValueOrDefault(slot.Weapon) + 1;
        foreach (var (weapon, count) in state.Armoury.Where(a => a.Value > 0).OrderBy(a => a.Key, StringComparer.Ordinal))
            pool[weapon] = pool.GetValueOrDefault(weapon) + count;
        return pool;
    }

    /// <summary>
    /// Each slot's weapon (null: an empty slot, or a stranded man, who keeps his own and is none of the platoon's). In slot order a man gets his pick while
    /// there is one left, else the slot's own, else the first left: never more than the pool has.
    /// </summary>
    public static IReadOnlyList<string?> Weapons(MissionSpec mission, CampaignState state)
    {
        var fill = CampaignMission.Fill(mission, state);
        var left = new Dictionary<string, int>(Pool(mission, state));
        var weapons = new string?[mission.Player.Count];
        // Picks first, so that a pick is not taken by an earlier slot's own weapon.
        for (int i = 0; i < weapons.Length; i++)
            if (!mission.Player[i].Stranded && fill[i] is not null && Pick(state, i)?.Weapon is { } wanted && Take(wanted))
                weapons[i] = wanted;
        for (int i = 0; i < weapons.Length; i++)
        {
            var slot = mission.Player[i];
            if (!slot.Stranded && fill[i] is not null && weapons[i] is null)
                weapons[i] = Take(slot.Weapon) ? slot.Weapon : TakeAny() ?? slot.Weapon;
        }
        return weapons;

        string? TakeAny() => left.FirstOrDefault(l => l.Value > 0).Key is { } any && Take(any) ? any : null;

        bool Take(string weapon)
        {
            if (left.GetValueOrDefault(weapon) <= 0)
                return false;
            left[weapon]--;
            return true;
        }
    }

    /// <summary>The player's pick for a slot, if he made one.</summary>
    public static SlotPick? Pick(CampaignState state, int slot) => state.Lineup.FirstOrDefault(p => p.Slot == slot);

    private static SlotPick PickFor(CampaignState state, int slot)
    {
        if (Pick(state, slot) is { } pick)
            return pick;
        pick = new SlotPick { Slot = slot };
        state.Lineup.Add(pick);
        return pick;
    }

    /// <summary>Drops the picks that no longer say anything.</summary>
    private static void Tidy(CampaignState state) => state.Lineup.RemoveAll(p => p.Man is null && !p.Empty && p.Weapon is null);

    private static bool IsSlot(MissionSpec mission, int slot) => slot >= 0 && slot < mission.Player.Count && !mission.Player[slot].Stranded;
}
