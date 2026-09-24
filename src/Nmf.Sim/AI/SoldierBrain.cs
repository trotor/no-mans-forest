using Nmf.Sim.Combat;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.AI;

internal static class SoldierBrain
{
    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction || unit.MoraleState == MoraleState.Broken)
            return;
        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (!idle || unit.Weapon is null || unit.Action != CombatAction.None || unit.FirePolicy == FirePolicy.HoldFire)
            return;
        var target = ChooseTarget(sim, unit, tick);
        if (target is not null)
            Firing.StartAiming(unit, target);
    }

    public static Unit? ChooseTarget(Simulation sim, Unit unit, long tick)
    {
        var knowledge = sim.Knowledge(unit.Side);
        Unit? best = null;
        long bestDistance = long.MaxValue;
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side == unit.Side || enemy.IsOutOfAction || knowledge.LevelOf(enemy.Id) != ContactLevel.Visible)
                continue;
            if (unit.FirePolicy == FirePolicy.ReturnFire && tick - enemy.LastShotTick > CombatRules.ReturnFireMemoryTicks)
                continue;
            if (!Firing.CanSee(sim, unit, enemy))
                continue;
            if (enemy.Id == unit.OrderedTarget)
                return enemy;
            long d = (enemy.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = enemy;
                bestDistance = d;
            }
        }
        return best;
    }
}
