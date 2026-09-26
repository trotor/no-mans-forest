using Nmf.Sim.Core;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Mission;

/// <summary>
/// Where the next objective roughly is, for the guide at the edge of the screen (spec 2026-09-26-guide-grenades-design):
/// its zone; for papers to take, the enemy leader once seen (standing or fallen), else the reported enemy position; for
/// papers already taken, the man who has them.
/// </summary>
public static class ObjectiveGuide
{
    public readonly record struct Target(Vec2 At, string Label, MapZone? Zone);

    public static Target? Next(GameSession session)
    {
        if (session.Tracker is not { } tracker || tracker.Result is not null)
            return null;
        var objective = tracker.Spec.Objectives.FirstOrDefault(o => !tracker.IsDone(o.Id));
        if (objective is null)
            return null;
        string lang = session.Language;
        bool fi = lang == "fi";
        var zones = session.Sim.Map.Features.Zones;
        if (objective.Zone is { } name && zones.FirstOrDefault(z => z.Name == name) is { } zone)
            return new Target(Centre(zone), ZoneLabel(zone.Name, lang), zone);
        if (objective.Item is { } item)
        {
            if (session.OwnUnits.FirstOrDefault(u => u.Items.Any(i => i.Id == item)) is { } carrier)
                return new Target(carrier.Position, (fi ? "Käskyt: " : "The papers: ") + (carrier.Name ?? ""), null);
            // The briefing says the enemy leader has them: once he has been seen, that is where to go.
            var holder = session.Sim.Units.FirstOrDefault(u => u.Side != session.PlayerSide && u.Items.Any(i => i.Id == item));
            if (holder is { IsLeader: true } || holder is { IsOutOfAction: true })
            {
                var contact = session.Knowledge.Get(holder.Id);
                if (contact?.Level is ContactLevel.Visible or ContactLevel.LastKnown)
                    return new Target(contact.Position, fi ? "Käskyt: vihollisen johtaja" : "The papers: the enemy leader", null);
            }
            if (zones.FirstOrDefault(z => z.Name == "outpost") is { } outpost)
                return new Target(Centre(outpost), ZoneLabel(outpost.Name, lang), outpost);
        }
        return null;
    }

    public static string ZoneLabel(string zone, string language) => zone switch
    {
        "start_zone" => language == "fi" ? "Lähtöalue" : "Start area",
        "outpost" => language == "fi" ? "Ilmoitettu vihollinen" : "Reported enemy",
        _ => zone,
    };

    private static Vec2 Centre(MapZone zone) => new((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);
}
