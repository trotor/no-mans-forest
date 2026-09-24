using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

internal static class MoraleSystem
{
    public static void AddSuppression(Simulation sim, Unit unit, int amount, long tick, List<SimEvent> events) =>
        unit.Suppression = Math.Min(CombatRules.MaxSuppression, unit.Suppression + Math.Max(0, amount));

    public static void Check(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
    }

    public static void OnCasualty(Simulation sim, Unit casualty, long tick, List<SimEvent> events)
    {
    }

    public static void Tick(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
    }
}
