using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>Each soldier's own decisions (spec §6): drop under fire, pick targets, retreat when broken.</summary>
internal static class SoldierBrain
{
    private static readonly (int Dx, int Dy)[] RetreatFallbacks =
        [(200, 0), (-200, 0), (0, 200), (0, -200), (200, 200), (-200, 200), (200, -200), (-200, -200)];

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction)
            return;
        if (unit.MoraleState == MoraleState.Broken)
        {
            Retreat(sim, unit);
            return;
        }
        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (idle && unit.Suppression >= CombatRules.GoProneAt && unit.Stance != Stance.Prone)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
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

    private static void Retreat(Simulation sim, Unit unit)
    {
        if (unit.MoveTarget is not null || unit.TargetStance is not null)
            return;
        if (unit.Retreated)
        {
            if (unit.Stance != Stance.Prone)
                Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        unit.Retreated = true;

        if (NearestThreat(sim, unit) is not { } threat)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        var away = unit.Position - threat;
        long length = Math.Max(1, IntMath.Isqrt(away.LengthSquared));
        var spot = Clamp(sim.Map, new Vec2(
            unit.Position.X + (int)(away.X * CombatRules.RetreatDistanceCm / length),
            unit.Position.Y + (int)(away.Y * CombatRules.RetreatDistanceCm / length)));

        var path = Pathfinder.FindPath(sim.Map, unit.Position, spot);
        foreach (var (dx, dy) in RetreatFallbacks)
        {
            if (path is not null)
                break;
            path = Pathfinder.FindPath(sim.Map, unit.Position, Clamp(sim.Map, spot + new Vec2(dx, dy)));
        }
        if (path is null)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        Movement.StartPath(unit, path[^1], MoveMode.Run, path);
    }

    private static Vec2? NearestThreat(Simulation sim, Unit unit)
    {
        Vec2? best = null;
        long bestDistance = long.MaxValue;
        foreach (var contact in sim.Knowledge(unit.Side).Contacts)
        {
            if (contact.Level == ContactLevel.Unknown || sim.FindUnit(contact.Target) is not { IsAlive: true })
                continue;
            long d = (contact.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = contact.Position;
                bestDistance = d;
            }
        }
        return best;
    }

    private static Vec2 Clamp(GridMap map, Vec2 p) =>
        new(Math.Clamp(p.X, 50, map.WidthCm - 50), Math.Clamp(p.Y, 50, map.HeightCm - 50));
}
