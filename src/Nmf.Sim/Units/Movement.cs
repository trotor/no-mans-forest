using Nmf.Sim.Core;
using Nmf.Sim.Events;

namespace Nmf.Sim.Units;

/// <summary>Straight-line movement toward the unit's move target. Pathfinding arrives in phase 2.</summary>
internal static class Movement
{
    public static void Advance(Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.MoveTarget is not { } target)
            return;

        var from = unit.Position;
        var delta = target - from;
        long distance = IntMath.Isqrt(delta.LengthSquared);
        bool arrived = distance <= unit.SpeedCmPerTick;
        Vec2 to;

        if (arrived)
        {
            to = target;
        }
        else
        {
            long speed = unit.SpeedCmPerTick;
            int mx = (int)(delta.X * speed / distance);
            int my = (int)(delta.Y * speed / distance);
            if (mx == 0 && my == 0)
            {
                // Truncation can round a slow diagonal step down to nothing; always make progress.
                if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
                    mx = Math.Sign(delta.X);
                else
                    my = Math.Sign(delta.Y);
            }
            to = new Vec2(from.X + mx, from.Y + my);
        }

        unit.Position = to;
        if (to != from)
            events.Add(new UnitMoved(tick, unit.Id, from, to));
        if (arrived)
        {
            unit.MoveTarget = null;
            events.Add(new UnitArrived(tick, unit.Id, to));
        }
    }
}
