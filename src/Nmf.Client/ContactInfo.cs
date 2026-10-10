using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Client;

/// <summary>
/// The hover tip on a "?" or a last-seen mark (spec 2026-10-10-settings-hints-design §1): what was heard or seen of him,
/// about how far, how long ago. Only what the side knows — a heard man's weapon stays unknown.
/// </summary>
public static class ContactInfo
{
    private const int RoundToM = 5;

    /// <summary>From the contact alone — what our men heard or saw — never from the man as he is now.</summary>
    public static IReadOnlyList<string> Describe(Contact contact, IEnumerable<Unit> own, long nowTick, string language)
    {
        if (contact.Level is not (ContactLevel.Suspected or ContactLevel.LastKnown))
            return [];
        bool fi = language == "fi";
        long seconds = Math.Max(0, nowTick - contact.LastUpdateTick) / SimConstants.TicksPerSecond;
        var lines = new List<string>();
        if (contact.Level == ContactLevel.Suspected)
        {
            lines.Add(fi ? $"Kuultu vihollinen: {(contact.HeardShots ? "laukauksia" : "liikettä")}"
                         : $"Enemy heard: {(contact.HeardShots ? "shooting" : "movement")}");
        }
        else
        {
            string role = EnemyInfo.Role(contact.SeenLeader, contact.SeenClass, fi);
            role = char.ToLowerInvariant(role[0]) + role[1..];
            string weapon = contact.SeenWeapon is { } w ? $", {w}" : "";
            lines.Add(fi ? $"Viimeksi nähty {seconds} s sitten: {role}{weapon}" : $"Last seen {seconds} s ago: {role}{weapon}");
        }
        var fit = own.Where(u => !u.IsOutOfAction).ToList();
        if (fit.Count > 0)
        {
            long cm = IntMath.Isqrt(fit.Min(u => (u.Position - contact.Position).LengthSquared));
            long metres = (cm / 100 + RoundToM / 2) / RoundToM * RoundToM;
            lines.Add(fi ? $"noin {metres} m" : $"about {metres} m");
        }
        if (contact.Level == ContactLevel.Suspected)
            lines.Add(fi ? $"{seconds} s sitten" : $"{seconds} s ago");
        return lines;
    }
}
