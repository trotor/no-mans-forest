using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.World;

namespace Nmf.Sim.Units;

/// <summary>Per-tick stance changes and path following.</summary>
internal static class Movement
{
    public static void StartPath(Unit unit, Vec2 target, MoveMode mode, List<Vec2> path)
    {
        unit.HoldsCoverStance = false;
        unit.MoveTarget = target;
        unit.MoveMode = mode;
        unit.PathPoints.Clear();
        unit.PathPoints.AddRange(path);
        unit.PathIndex = 0;
        BeginStanceChange(unit, StanceRules.RequiredFor(mode));
    }

    public static void ClearPath(Unit unit)
    {
        unit.MoveTarget = null;
        unit.PathPoints.Clear();
        unit.PathIndex = 0;
    }

    public static void BeginStanceChange(Unit unit, Stance target)
    {
        if (unit.TargetStance == target)
            return;
        if (unit.Stance == target)
        {
            unit.TargetStance = null;
            unit.StanceTicksLeft = 0;
            return;
        }
        unit.TargetStance = target;
        unit.StanceTicksLeft = StanceRules.StepTicks(unit, unit.Stance, StanceRules.NextToward(unit.Stance, target));
    }

    public static void Update(Unit unit, GridMap map, long tick, List<SimEvent> events)
    {
        unit.IsMoving = false;
        if (unit.IsOutOfAction || unit.Action is CombatAction.Throwing or CombatAction.Melee or CombatAction.Looting)
            return;

        if (unit.TargetStance is { } targetStance)
        {
            if (--unit.StanceTicksLeft > 0)
                return;
            unit.Stance = StanceRules.NextToward(unit.Stance, targetStance);
            if (unit.Stance != targetStance)
            {
                unit.StanceTicksLeft = StanceRules.StepTicks(unit, unit.Stance, StanceRules.NextToward(unit.Stance, targetStance));
                return;
            }
            unit.TargetStance = null;
            events.Add(new StanceChanged(tick, unit.Id, unit.Stance));
            return;
        }

        if (unit.MoveTarget is not null)
            FollowPath(unit, map, tick, events);
    }

    private static void FollowPath(Unit unit, GridMap map, long tick, List<SimEvent> events)
    {
        var from = unit.Position;
        int budget = Math.Max(1, StanceRules.SpeedCmPerTick(unit, unit.MoveMode) * 100 / map.CellAt(from).MoveCostPct
                                 * CombatRules.WoundSpeedPct(unit.Wound) / 100);
        var pos = from;
        var path = unit.PathPoints;

        while (budget > 0 && unit.PathIndex < path.Count)
        {
            var waypoint = path[unit.PathIndex];
            var delta = waypoint - pos;
            long distance = IntMath.Isqrt(delta.LengthSquared);
            if (distance <= budget)
            {
                pos = waypoint;
                budget -= (int)distance;
                unit.PathIndex++;
                continue;
            }

            int mx = (int)(delta.X * (long)budget / distance);
            int my = (int)(delta.Y * (long)budget / distance);
            if (mx == 0 && my == 0)
            {
                // Truncation can round a slow diagonal step down to nothing; always make progress.
                if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
                    mx = Math.Sign(delta.X);
                else
                    my = Math.Sign(delta.Y);
            }
            pos = new Vec2(pos.X + mx, pos.Y + my);
            budget = 0;
        }

        unit.Position = pos;
        if (pos != from)
        {
            unit.IsMoving = true;
            unit.MovedSinceVisionUpdate = true;
            events.Add(new UnitMoved(tick, unit.Id, from, pos));
        }
        if (unit.PathIndex >= path.Count)
        {
            ClearPath(unit);
            events.Add(new UnitArrived(tick, unit.Id, pos));
        }
    }
}
