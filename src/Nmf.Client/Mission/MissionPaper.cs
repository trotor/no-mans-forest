using System.Globalization;
using Nmf.Sim.Combat;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;

namespace Nmf.Client.Mission;

/// <summary>The text of the orders paper: objectives with their state, and the roster with each man's qualities.</summary>
public static class MissionPaper
{
    public static IReadOnlyList<(string Text, bool Done)> Objectives(MissionTracker tracker, string language) =>
        tracker.Spec.Objectives.Where(o => tracker.IsVisible(o.Id)).Select(o => (o.Text.In(language), tracker.IsDone(o.Id))).ToList();

    /// <summary>What the men must do now, for the corner of the screen: the first objective not done, or the result.</summary>
    /// <summary>The zone an objective's mark sits beside on the maps (null: at the spot itself, a carrier).</summary>
    public static Nmf.Sim.World.MapZone? MarkZone(GameSession session, ObjectiveSpec objective)
    {
        string? name = objective.Type switch
        {
            ObjectiveType.Find => objective.Area,
            ObjectiveType.ReachZone or ObjectiveType.Rescue => objective.Zone,
            _ => session.CardUnits.Any(u => u.Items.Any(i => i.Id == objective.Item)) ? null : "outpost",
        };
        return name is null || session.Mission is not { } mission ? null : mission.ZoneNamed(session.Sim.Map, name);
    }

    /// <summary>The objectives to mark on the maps, numbered as on the orders; where to mark each (null: nowhere yet).</summary>
    public static IReadOnlyList<(int Number, ObjectiveSpec Objective, Nmf.Sim.Core.Vec2? At)> MapObjectives(GameSession session)
    {
        var marks = new List<(int, ObjectiveSpec, Nmf.Sim.Core.Vec2?)>();
        if (session.Tracker is not { } tracker || session.Mission is not { } mission)
            return marks;
        var map = session.Sim.Map;
        Nmf.Sim.Core.Vec2? Centre(string? zone) => zone is not null && mission.ZoneNamed(map, zone) is { } z
            ? new Nmf.Sim.Core.Vec2((z.Min.X + z.Max.X) / 2, (z.Min.Y + z.Max.Y) / 2)
            : null;
        int number = 0;
        foreach (var objective in mission.Objectives.Where(o => tracker.IsVisible(o.Id)))
        {
            number++;
            Nmf.Sim.Core.Vec2? at = objective.Type switch
            {
                // A search: the area to look in, never the place itself (spec 2026-10-10-events-design §5).
                ObjectiveType.Find => Centre(objective.Area),
                // The way home, once he is with them.
                ObjectiveType.Rescue => session.OwnUnits.FirstOrDefault(u => u.Name == objective.Unit) is { Stranded: false } ? Centre(objective.Zone) : null,
                ObjectiveType.ReachZone => Centre(objective.Zone),
                // The man who has it (standing or fallen); one still to be found is not shown; else the reported enemy.
                _ => session.CardUnits.FirstOrDefault(u => u.Items.Any(i => i.Id == objective.Item)) is { } carrier ? carrier.Position
                    : session.OwnUnits.Any(u => u.Stranded && u.Items.Any(i => i.Id == objective.Item)) ? null
                    : Centre("outpost"),
            };
            marks.Add((number, objective, at));
        }
        return marks;
    }

    public static string NextStep(MissionTracker tracker, string language)
    {
        bool fi = language == "fi";
        if (tracker.Result is { } success)
            return success ? (fi ? "Tehtävä suoritettu" : "Mission accomplished") : (fi ? "Tehtävä epäonnistui" : "Mission failed");
        var next = tracker.Spec.Objectives.FirstOrDefault(o => tracker.IsVisible(o.Id) && !tracker.IsDone(o.Id));
        return next is null ? "" : (fi ? "Seuraavaksi: " : "Next: ") + next.Text.In(language);
    }

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
