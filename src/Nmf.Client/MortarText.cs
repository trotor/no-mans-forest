using Nmf.Sim.Combat;

namespace Nmf.Client;

/// <summary>The mortar's line in the status bar (spec 2026-10-10-mortar-design §4); null without a mortar.</summary>
public static class MortarText
{
    public static string? Status(GameSession session, string language)
    {
        if (session.Mortar is not { } mortar)
            return null;
        bool fi = language == "fi";
        string head = (fi ? "Kranaatinheitin HE " : "Mortar HE ") + mortar.Rounds;
        if (mortar.Rounds == 0)
            return head + (fi ? " · kranaatit lopussa" : " · out of rounds");
        string? state = mortar.Phase switch
        {
            MortarPhase.Calling => fi ? "tilaus…" : "calling…",
            MortarPhase.Waiting => (fi ? "iskut " : "first rounds in ")
                                   + Math.Max(0, (mortar.NextTick + MortarSupport.FlightWarnTicks - session.Sim.Tick) / Nmf.Sim.SimConstants.TicksPerSecond) + " s",
            MortarPhase.Ranging => fi ? "tähystys" : "ranging",
            MortarPhase.Effect => fi ? "tuli" : "firing",
            _ => null,
        };
        if (!mortar.Available(session.Sim))
            state = state is null ? (fi ? "ei yhteyttä" : "no radio") : state + (fi ? " · ei yhteyttä" : " · no radio");
        return state is null ? head : head + " · " + state;
    }
}
