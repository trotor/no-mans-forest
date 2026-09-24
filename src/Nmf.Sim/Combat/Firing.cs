using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

internal static class Firing
{
    public static void Cancel(Unit unit)
    {
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
        {
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
            unit.RoundsLeftInBurst = 0;
        }
        unit.Target = null;
    }
}
