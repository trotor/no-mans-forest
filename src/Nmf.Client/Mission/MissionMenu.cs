using Nmf.Content.Missions;

namespace Nmf.Client.Mission;

/// <summary>One heading of the mission menu and its missions.</summary>
public sealed record MissionGroup(string Heading, IReadOnlyList<MissionLoader.Entry> Missions);

/// <summary>
/// The mission menu's grouping: the missions under the groups their files name (in each group by <c>order</c>, then id),
/// the groups by their first mission; missions of no group (and broken ones) last; the testbeds (debug) apart, and only
/// with --debug.
/// </summary>
public static class MissionMenu
{
    public static IReadOnlyList<MissionGroup> Groups(IEnumerable<MissionLoader.Entry> entries, bool debug, string language)
    {
        bool fi = language == "fi";
        var list = entries.ToList();
        var testbeds = list.Where(e => e.Spec?.Debug == true).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        var real = list.Where(e => e.Spec?.Debug != true).ToList();
        var groups = real.Where(e => e.Spec?.Group is not null)
            .GroupBy(e => e.Spec!.Group!.In(language))
            .Select(g => (Heading: g.Key, Missions: g.OrderBy(e => e.Spec!.Order).ThenBy(e => e.Id, StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Missions.Min(e => e.Spec!.Order)).ThenBy(g => g.Heading, StringComparer.Ordinal)
            .Select(g => new MissionGroup(g.Heading, g.Missions))
            .ToList();
        var loose = real.Where(e => e.Spec?.Group is null).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        if (loose.Count > 0)
            groups.Add(new MissionGroup(groups.Count == 0 ? (fi ? "Tehtävät" : "Missions") : fi ? "Muut tehtävät" : "Other missions", loose));
        if (debug && testbeds.Count > 0)
            groups.Add(new MissionGroup(fi ? "Testiskenaariot" : "Test scenarios", testbeds));
        return groups;
    }
}
