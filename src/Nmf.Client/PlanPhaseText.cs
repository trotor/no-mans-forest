using Nmf.Sim;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>A man's part and phase in an attack under way, for his card (spec 2026-10-10-plan-preview-design §5).</summary>
public static class PlanPhaseText
{
    /// <summary>His line, or null when he is in no attack of his side.</summary>
    public static string? For(Simulation sim, Side side, UnitId man, string language)
    {
        var attack = sim.PlannedAttacks.FirstOrDefault(a => !a.Ended && a.Side == side && a.Members.Contains(man));
        return attack is null ? null
            : Of(attack.StrikeTeam.Contains(man), attack.Phase, attack.IsSettled(man), attack.Launched, language);
    }

    public static string Of(bool strike, PlannedPhase phase, bool atSpot, bool launched, string language)
    {
        bool fi = language == "fi";
        if (!strike)
            return atSpot || phase != PlannedPhase.Deploy
                ? (fi ? "Tuli: lamauttaa" : "Fire: covering")
                : (fi ? "Tuli: matkalla" : "Fire: moving");
        if (launched)
            return fi ? "Isku: rynnäkkö" : "Strike: assault";
        return atSpot ? (fi ? "Isku: odottaa" : "Strike: waiting") : (fi ? "Isku: matkalla" : "Strike: moving");
    }
}
