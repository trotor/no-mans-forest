using Nmf.Sim.Events;

namespace Nmf.Client;

/// <summary>The short text shown over a man who has searched a fallen one.</summary>
public static class LootText
{
    /// <param name="itemName">An item's name in the player's language (by default its English name).</param>
    public static string Describe(UnitLooted looted, Func<string, string> weaponName, string language = "en", Func<Nmf.Sim.Combat.Item, string>? itemName = null)
    {
        bool fi = language == "fi";
        var parts = new List<string>();
        var ammo = new List<string>();
        if (looted.Magazines > 0)
            ammo.Add($"+{looted.Magazines} {(fi ? (looted.Magazines == 1 ? "lipas" : "lipasta") : (looted.Magazines == 1 ? "mag" : "mags"))}");
        if (looted.Grenades > 0)
            ammo.Add($"+{looted.Grenades} {(fi ? (looted.Grenades == 1 ? "kranaatti" : "kranaattia") : (looted.Grenades == 1 ? "grenade" : "grenades"))}");
        if (ammo.Count > 0)
            parts.Add(string.Join(" ", ammo));
        if (looted.WeaponTaken is { } weapon)
            parts.Add(weaponName(weapon));
        parts.AddRange(looted.Items.Select(i => itemName?.Invoke(i) ?? i.Name));
        return parts.Count == 0 ? (fi ? "ei mitään" : "nothing") : string.Join(", ", parts);
    }
}
