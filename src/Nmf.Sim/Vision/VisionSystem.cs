using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Vision;

/// <summary>Updates both sides' knowledge; runs every <see cref="VisionRules.IntervalTicks"/> ticks.</summary>
internal static class VisionSystem
{
    private static readonly Side[] Sides = [Side.Blue, Side.Red];

    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var side in Sides)
        {
            var knowledge = sim.Knowledge(side);
            foreach (var target in sim.Units)
            {
                if (target.Side == side || !target.IsAlive)
                    continue;
                var contact = knowledge.GetOrAdd(target.Id);
                UpdateSight(sim, side, contact, target, tick, events);
                UpdateHearing(sim, side, contact, target, tick, events);
                if (contact.Level == ContactLevel.Suspected && tick - contact.LastUpdateTick > VisionRules.SuspectedTimeoutTicks)
                {
                    contact.Level = ContactLevel.Unknown;
                    events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Unknown, contact.Position));
                }
            }
        }
        foreach (var unit in sim.Units)
            unit.MovedSinceVisionUpdate = false;
    }

    private static void UpdateSight(Simulation sim, Side side, Contact contact, Unit target, long tick, List<SimEvent> events)
    {
        var map = sim.Map;
        int targetHeight = VisionRules.TargetHeightAbsCm(map, target);
        long bestGain = 0;
        bool seen = false;

        foreach (var observer in sim.Units)
        {
            if (observer.Side != side || observer.IsOutOfAction)
                continue;
            long distance = IntMath.Isqrt((target.Position - observer.Position).LengthSquared);
            if (distance > VisionRules.MaxSightRangeCm)
                continue;
            int clarity = LineOfSight.Clarity(map, observer.Position, VisionRules.EyeHeightAbsCm(map, observer), target.Position, targetHeight);
            if (clarity == 0)
                continue;
            seen = true;
            long gain = (long)VisionRules.BaseGainPerUpdate * clarity * (VisionRules.MaxSightRangeCm - distance)
                        * VisionRules.VisibilityPct(target, tick) * VisionRules.StanceVisibilityPct(target.Stance)
                        / (255L * VisionRules.MaxSightRangeCm * 100 * 100);
            bestGain = Math.Max(bestGain, gain);
        }

        if (contact.Level == ContactLevel.Visible)
        {
            if (seen)
            {
                contact.Position = target.Position;
                contact.LastUpdateTick = tick;
            }
            else
            {
                contact.Level = ContactLevel.LastKnown;
                contact.Progress = VisionRules.ReacquireProgress;
                events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.LastKnown, contact.Position));
            }
            return;
        }

        if (bestGain > 0)
        {
            contact.Progress = (int)Math.Min(VisionRules.SpottedThreshold, contact.Progress + bestGain);
            if (contact.Progress >= VisionRules.SpottedThreshold)
            {
                contact.Level = ContactLevel.Visible;
                contact.Position = target.Position;
                contact.LastUpdateTick = tick;
                events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Visible, contact.Position));
            }
        }
        else
        {
            contact.Progress = Math.Max(0, contact.Progress - VisionRules.DecayPerUpdate);
        }
    }

    private static void UpdateHearing(Simulation sim, Side side, Contact contact, Unit target, long tick, List<SimEvent> events)
    {
        bool fired = target.Weapon is not null && target.LastShotTick > tick - VisionRules.IntervalTicks;
        if (contact.Level == ContactLevel.Visible || (!target.MovedSinceVisionUpdate && !fired))
            return;
        // A recent sighting is more precise than a noise; keep the last-seen marker until it goes stale.
        if (contact.Level == ContactLevel.LastKnown && tick - contact.LastUpdateTick <= VisionRules.SuspectedTimeoutTicks)
            return;

        long radius = target.MovedSinceVisionUpdate
            ? (long)VisionRules.NoiseRadiusCm(target.MoveMode) * sim.Map.CellAt(target.Position).MoveCostPct / 100
            : 0;
        if (fired)
            radius = Math.Max(radius, target.Weapon!.NoiseRadiusCm);
        bool heard = false;
        foreach (var listener in sim.Units)
        {
            if (listener.Side == side && !listener.IsOutOfAction && (target.Position - listener.Position).LengthSquared <= radius * radius)
            {
                heard = true;
                break;
            }
        }
        if (!heard)
            return;

        var rough = RoughPosition(target.Position);
        bool changed = contact.Level != ContactLevel.Suspected || contact.Position != rough;
        contact.Level = ContactLevel.Suspected;
        contact.Position = rough;
        contact.LastUpdateTick = tick;
        if (changed)
            events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Suspected, rough));
    }

    private static Vec2 RoughPosition(Vec2 p)
    {
        const int grid = VisionRules.SuspectedGridCm;
        return new Vec2(IntMath.FloorDiv(p.X, grid) * grid + grid / 2, IntMath.FloorDiv(p.Y, grid) * grid + grid / 2);
    }
}
