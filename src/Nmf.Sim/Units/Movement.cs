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

    /// <summary>How much nearer his goal than the man whose pace he keeps an escort may be.</summary>
    public const int EscortLeadCm = 1_000;

    /// <summary>
    /// An escort ahead of the man whose pace he keeps (by what is left of their ways) waits for him; once that man has
    /// stopped or fallen he goes his own pace.
    /// </summary>
    internal static bool WaitsForPace(Simulation sim, Unit unit)
    {
        if (unit.PaceWith is not { } id || unit.MoveTarget is not { } goal || sim.FindUnit(id) is not { } lead
            || lead.IsOutOfAction || lead.MoveTarget is not { } leadGoal)
            return false;
        return IntMath.Isqrt((goal - unit.Position).LengthSquared) + EscortLeadCm < IntMath.Isqrt((leadGoal - lead.Position).LengthSquared);
    }

    /// <param name="hold">He waits where he is this step (an escort ahead of his man): stances still change.</param>
    public static void Update(Unit unit, GridMap map, long tick, List<SimEvent> events, WinterState? winter = null, bool hold = false)
    {
        unit.IsMoving = false;
        if (unit.IsOutOfAction || unit.Action is CombatAction.Throwing or CombatAction.Melee or CombatAction.Looting or CombatAction.Lifting)
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

        if (unit.MoveTarget is not null && !hold)
            FollowPath(unit, map, tick, events, winter);
    }

    /// <summary>
    /// Through the snow (spec 2026-10-10-winter-design §2): the cell's cost times the snow's for him, a sled for the
    /// wounded; reckoned in hundredths of a centimetre, the part of a centimetre left kept for the next step (so a crawler
    /// in deep snow goes on some steps not at all).
    /// </summary>
    private static int SnowBudget(Unit unit, CellData here, WinterState winter)
    {
        long hundredths = StanceRules.SpeedHundredths(unit, unit.MoveMode, winter) * 10_000L / ((long)here.MoveCostPct * winter.FactorPct(unit, here))
                          * CombatRules.WoundSpeedPct(unit.Wound) / 100 + unit.MoveRemainder;
        unit.MoveRemainder = (int)(hundredths % 100);
        return (int)(hundredths / 100); // under a centimetre this step: he wades on, and gets it on the next
    }

    private static void FollowPath(Unit unit, GridMap map, long tick, List<SimEvent> events, WinterState? winter)
    {
        var from = unit.Position;
        var here = map.CellAt(from);
        int budget = winter is null
            ? Math.Max(1, StanceRules.SpeedCmPerTick(unit, unit.MoveMode) * 100 / here.MoveCostPct
                          * CombatRules.WoundSpeedPct(unit.Wound) / 100)
            : SnowBudget(unit, here, winter);
        if (budget == 0)
        {
            // Wading through deep snow, nothing gained this step: still on his way, seen and heard as a man on the move.
            unit.IsMoving = true;
            unit.MovedSinceVisionUpdate = true;
            return;
        }
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
