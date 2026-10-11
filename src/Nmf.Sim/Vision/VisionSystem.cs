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
                if (target.Side == side)
                    continue;
                var contact = knowledge.GetOrAdd(target.Id);
                // The dead are no longer seen or heard: a corpse stays where it was seen, a noise simply fades.
                if (target.IsAlive)
                {
                    UpdateSight(sim, side, contact, target, tick, events);
                    UpdateHearing(sim, side, contact, target, tick, events);
                    NoteFire(sim, side, contact, target, tick);
                }
                else if (contact.Level != ContactLevel.Visible)
                {
                    // A man killed out of sight is found when someone comes to look; once seen, a corpse stays seen.
                    UpdateSight(sim, side, contact, target, tick, events);
                }
                if (contact.Level == ContactLevel.Suspected && tick - contact.LastUpdateTick > VisionRules.SuspectedTimeoutTicks)
                {
                    contact.Level = ContactLevel.Unknown;
                    events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Unknown, contact.Position));
                }
            }
        }
        AI.AlertnessSystem.Update(sim, tick, events);
        foreach (var unit in sim.Units)
            unit.MovedSinceVisionUpdate = false;
    }

    private static void UpdateSight(Simulation sim, Side side, Contact contact, Unit target, long tick, List<SimEvent> events)
    {
        var map = sim.Map;
        int targetHeight = VisionRules.TargetHeightAbsCm(map, target);
        int light = sim.LightAt(target.Position); // a man in the light is seen; one standing in it sees no better into the dark
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
                        * VisionRules.VisibilityPct(target, tick) * VisionRules.StanceVisibilityPct(map, target)
                        * VisionRules.ObserverPct(observer)
                        / (255L * VisionRules.MaxSightRangeCm * 100 * 100 * 100);
            if (sim.AlertnessOn(side))
                gain = gain * AI.AlertnessSystem.SpottingPct(observer.Alertness) / 100; // a calm man is slow to notice
            if (light != 100)
                gain = gain * light / 100;
            bestGain = Math.Max(bestGain, gain);
        }

        if (contact.Level == ContactLevel.Visible)
        {
            if (seen)
            {
                contact.Position = target.Position;
                contact.LastUpdateTick = tick;
                Remember(contact, target);
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
                contact.HeardShots = false;
                Remember(contact, target);
                events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Visible, contact.Position));
            }
        }
        else
        {
            contact.Progress = Math.Max(0, contact.Progress - VisionRules.DecayPerUpdate);
        }
    }

    /// <summary>What our men take note of while they see him: his weapon and whether he gives orders.</summary>
    private static void Remember(Contact contact, Unit target)
    {
        contact.SeenWeapon = target.Weapon?.Name;
        contact.SeenClass = target.Weapon?.Class;
        contact.SeenLeader = target.IsLeader;
    }

    /// <summary>A shot seen, or heard by anyone of the side within the weapon's report: he is still firing (spec 2026-10-10-balance-design §1).</summary>
    private static void NoteFire(Simulation sim, Side side, Contact contact, Unit target, long tick)
    {
        if (target.Weapon is not { } weapon || target.LastShotTick <= tick - VisionRules.IntervalTicks || target.LastShotTick <= contact.LastFireTick)
            return;
        long earshotSq = (long)weapon.NoiseRadiusCm * weapon.NoiseRadiusCm;
        if (contact.Level == ContactLevel.Visible
            || sim.Units.Any(l => l.Side == side && !l.IsOutOfAction && (l.Position - target.Position).LengthSquared <= earshotSq))
            contact.LastFireTick = target.LastShotTick;
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
            ? (long)VisionRules.NoiseRadiusCm(target) * sim.Map.CellAt(target.Position).MoveCostPct / 100
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
        contact.HeardShots = fired;
        contact.Level = ContactLevel.Suspected;
        contact.Position = rough;
        contact.LastUpdateTick = tick;
        if (changed)
            events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Suspected, rough));
    }

    internal static Vec2 RoughPosition(Vec2 p)
    {
        const int grid = VisionRules.SuspectedGridCm;
        return new Vec2(IntMath.FloorDiv(p.X, grid) * grid + grid / 2, IntMath.FloorDiv(p.Y, grid) * grid + grid / 2);
    }
}
