using Nmf.Sim.Combat;
using Nmf.Sim.Vision;

namespace Nmf.Client;

/// <summary>The top bar's news line, in the player's language.</summary>
public static class HudText
{
    public static string Status(GameSession session, string language, string commanding)
    {
        bool fi = language == "fi";
        var contacts = session.Knowledge.Contacts.ToList();
        int seen = contacts.Count(c => c.Level == ContactLevel.Visible && session.Sim.FindUnit(c.Target) is { IsOutOfAction: false });
        int heard = contacts.Count(c => c.Level == ContactLevel.Suspected);
        int lastKnown = contacts.Count(c => c.Level == ContactLevel.LastKnown);
        int dead = session.OwnUnits.Count(u => u.Wound == WoundLevel.Dead);
        int wounded = session.OwnUnits.Count(u => u.Wound is > WoundLevel.None and < WoundLevel.Dead);
        int enemyDown = session.Sim.Units.Count(u => u.Side != session.PlayerSide && u.IsOutOfAction && session.Knowledge.LevelOf(u.Id) >= ContactLevel.LastKnown);
        const string gap = "      ";
        var parts = fi
            ? new List<string>
            {
                $"Vihollinen: {seen} nähty · {heard} kuultu · {lastKnown} muistissa",
                $"Tappiot: {dead} kaatunut{(dead == 1 ? "" : "ta")} · {wounded} haavoittunut{(wounded == 1 ? "" : "ta")}",
                $"Vihollisia maassa (nähty): {enemyDown}",
                $"Komennossa: {commanding}",
            }
            : new List<string>
            {
                $"Enemy: {seen} seen · {heard} heard · {lastKnown} last known",
                $"Losses: {dead} killed · {wounded} wounded",
                $"Enemy down (seen): {enemyDown}",
                $"Commanding: {commanding}",
            };
        if (session.CarriedPapers.Count > 0)
            parts.Add((fi ? "Paperit: " : "Papers: ") + string.Join(", ", session.CarriedPapers));
        parts.Add(fi ? "F1 ohje" : "F1 help");
        return string.Join(gap, parts);
    }
}
