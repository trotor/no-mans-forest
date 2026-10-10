using Nmf.Sim;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>A man's part in a carry, for his card (spec 2026-10-10-carrying-design §3); null when he has none.</summary>
public static class CarryText
{
    public static string? For(Simulation sim, Unit unit, string language)
    {
        bool fi = language == "fi";
        if (unit.Carrying is { } carried)
        {
            string name = Surname(sim.FindUnit(carried)?.Name);
            return fi ? $"Kantaa: {name}" : $"Carrying {name}";
        }
        if (unit.CarriedBy is not null)
            return fi ? "Kannetaan" : "Being carried";
        if (unit.HelpingCarry is not null)
            return fi ? "Auttaa kantamaan" : "Helping to carry";
        if (unit.CarryTarget is not null)
            return fi ? "Menossa kantamaan" : "Going to carry";
        return null;
    }

    /// <summary>"Sotm. Saarinen" → "Saarinen": the rank left off, as on a small card.</summary>
    private static string Surname(string? name) => name is null ? "" : name.Split(' ')[^1];
}
