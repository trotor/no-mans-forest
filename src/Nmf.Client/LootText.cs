using Nmf.Sim.Events;

namespace Nmf.Client;

/// <summary>The short text shown over a man who has searched a fallen one.</summary>
public static class LootText
{
    public static string Describe(UnitLooted looted, Func<string, string> weaponName)
    {
        var parts = new List<string>();
        var ammo = new List<string>();
        if (looted.Magazines > 0)
            ammo.Add($"+{looted.Magazines} {(looted.Magazines == 1 ? "mag" : "mags")}");
        if (looted.Grenades > 0)
            ammo.Add($"+{looted.Grenades} {(looted.Grenades == 1 ? "grenade" : "grenades")}");
        if (ammo.Count > 0)
            parts.Add(string.Join(" ", ammo));
        if (looted.WeaponTaken is { } weapon)
            parts.Add(weaponName(weapon));
        parts.AddRange(looted.Items.Select(i => i.Name));
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}
