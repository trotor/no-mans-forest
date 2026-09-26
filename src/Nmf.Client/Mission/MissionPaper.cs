using System.Globalization;
using Nmf.Sim.Combat;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;

namespace Nmf.Client.Mission;

/// <summary>The text of the orders paper: objectives with their state, and the roster with each man's qualities.</summary>
public static class MissionPaper
{
    public static IReadOnlyList<(string Text, bool Done)> Objectives(MissionTracker tracker, string language) =>
        tracker.Spec.Objectives.Select(o => (o.Text.In(language), tracker.IsDone(o.Id))).ToList();

    public static string RosterLine(Unit unit, Func<WeaponDef, string> weaponName, string language = "en")
    {
        bool fi = language == "fi";
        var parts = new List<string> { unit.Name ?? "?" };
        if (unit.IsTough)
            parts[0] += " ★";
        if (unit.Weapon is { } weapon)
            parts.Add(weaponName(weapon));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(fi ? "sisu" : "nerve")} {unit.Nerve}"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(fi ? "moraali" : "morale")} {unit.BaseMorale}"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(fi ? "ampumataito" : "shooting")} {unit.Marksmanship}"));
        parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(fi ? "kokemus" : "experience")} {unit.Experience}"));
        if (unit.IsLeader)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{(fi ? "johtamiskyky" : "leadership")} {unit.LeaderQualityPct}"));
        return string.Join(" · ", parts);
    }
}
