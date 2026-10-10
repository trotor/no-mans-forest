using Nmf.Sim;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>How awake an enemy looks (spec 2026-10-10-alertness-design §7): only on a side with alertness, never once he is fighting.</summary>
public static class AlertnessText
{
    /// <summary>Over him on the map: "?" wary, "!" alerted; null otherwise.</summary>
    public static string? Mark(Simulation sim, Unit enemy) =>
        !sim.AlertnessOn(enemy.Side) || enemy.IsOutOfAction ? null : enemy.Alertness switch
        {
            Alertness.Suspicious => "?",
            Alertness.Alerted => "!",
            _ => null,
        };

    /// <summary>A line of the hover tip; null when there is nothing to tell.</summary>
    public static string? Tip(Simulation sim, Unit enemy, string language)
    {
        if (!sim.AlertnessOn(enemy.Side) || enemy.IsOutOfAction)
            return null;
        bool fi = language == "fi";
        return enemy.Alertness switch
        {
            Alertness.Calm => fi ? "Rauhallinen" : "Calm",
            Alertness.Suspicious => fi ? "Epäluuloinen" : "Wary",
            Alertness.Alerted => fi ? "Hälytetty" : "Alerted",
            _ => null,
        };
    }
}
