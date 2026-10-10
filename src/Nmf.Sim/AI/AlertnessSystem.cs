using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>
/// Each man's own alertness and the alarm spreading (spec 2026-10-10-alertness-design): what he sees and hears himself
/// wakes him, and a man alarmed shouts the others awake. Runs with the vision pass, inside the step, only for sides
/// given alertness: for the others it does nothing at all.
/// </summary>
internal static class AlertnessSystem
{
    public const int SuspicionFadeTicks = 30 * SimConstants.TicksPerSecond;
    public const int ShoutDelayTicks = 1 * SimConstants.TicksPerSecond;
    public const int ShoutHeardCm = 5_000;
    public const int SurpriseCalmTicks = 2 * SimConstants.TicksPerSecond, SurpriseSuspiciousTicks = 1 * SimConstants.TicksPerSecond;
    public const int ExplosionHeardCm = 50_000, MeleeHeardCm = 3_000;
    public const int FlareDelayTicks = 3 * SimConstants.TicksPerSecond, FlareIntervalTicks = 60 * SimConstants.TicksPerSecond;
    public const int FlareSeenCm = 60_000;
    /// <summary>Tracks in the snow are made out this near.</summary>
    public const int TrackSeenCm = 2_000;
    /// <summary>A fallen comrade is noticed this near.</summary>
    public const int FallenSeenCm = 5_000;
    /// <summary>Something glimpsed: the side is this far along to making him out.</summary>
    public const int GlimpseProgress = VisionRules.SpottedThreshold / 3;

    /// <summary>How quick he is to make out a man: a calm one slowly, a wary one looks hard (spec §2).</summary>
    public static int SpottingPct(Alertness level) => level switch
    {
        Alertness.Calm => 40,
        Alertness.Suspicious => 120,
        _ => 100,
    };

    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        if (!sim.AnyAlertness)
            return;
        foreach (var unit in sim.Units)
        {
            if (unit.ShoutTick >= 0 && tick >= unit.ShoutTick)
                Shout(sim, unit, tick, events);
            if (unit.FlareTick >= 0 && tick >= unit.FlareTick)
                Flare(sim, unit, tick, events);
        }
        foreach (var unit in sim.Units)
        {
            if (!sim.AlertnessOn(unit.Side) || unit.IsOutOfAction || unit.Alertness == Alertness.Combat)
                continue;
            if (Stimulus(sim, unit, tick) is { } heard)
                Raise(sim, unit, heard.Level, heard.At, tick, events);
            else if (unit.Alertness == Alertness.Suspicious && tick - unit.AlertSinceTick >= SuspicionFadeTicks)
            {
                unit.Alertness = Alertness.Calm;
                events.Add(new AlertnessChanged(tick, unit.Id, Alertness.Calm));
            }
        }
        sim.ClearNoises();
    }

    /// <summary>An order raises him (a search party setting out): no shout, no running for cover.</summary>
    public static void Order(Unit unit, Alertness level, long tick, List<SimEvent> events)
    {
        if (level <= unit.Alertness)
            return;
        unit.Alertness = level;
        unit.AlertSinceTick = tick;
        events.Add(new AlertnessChanged(tick, unit.Id, level));
    }

    /// <summary>Something woke him to <paramref name="level"/>; the alarm came from <paramref name="at"/>.</summary>
    internal static void Raise(Simulation sim, Unit unit, Alertness level, Vec2 at, long tick, List<SimEvent> events)
    {
        unit.AlertSinceTick = tick;
        if (level < unit.Alertness)
            return;
        if (level == unit.Alertness)
        {
            if (level == Alertness.Suspicious)
                unit.AlarmAt = at; // he turns to the latest sound
            return;
        }
        var was = unit.Alertness;
        unit.Alertness = level;
        unit.AlarmAt = at;
        events.Add(new AlertnessChanged(tick, unit.Id, level));
        if (was < Alertness.Alerted && level >= Alertness.Alerted)
        {
            if (!unit.Shouted && unit.ShoutTick < 0)
                unit.ShoutTick = tick + ShoutDelayTicks;
            if (unit.IsLeader && unit.FlareTick < 0 && sim.FlaresLeft(unit.Side) > 0)
                unit.FlareTick = tick + FlareDelayTicks; // a leader sends a flare up as well
        }
        switch (level)
        {
            case Alertness.Combat:
                unit.ReadyTick = tick + was switch
                {
                    Alertness.Calm => SurpriseCalmTicks,
                    Alertness.Suspicious => SurpriseSuspiciousTicks,
                    _ => 0,
                };
                break;
            case Alertness.Alerted:
                // To his post: down in cover facing where the alarm came from.
                unit.CoverReactionPending = true;
                unit.CoverThreat = at;
                break;
            case Alertness.Suspicious when unit.MoveTarget is not null && unit.AutoPace && unit.PlannedAttackId is null:
                Movement.ClearPath(unit); // he stops to listen
                break;
        }
    }

    /// <summary>He calls the alarm: every man of his side within earshot is alerted, and calls it on in turn.</summary>
    private static void Shout(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        unit.ShoutTick = -1;
        if (unit.IsOutOfAction)
            return;
        if (unit.Action == Nmf.Sim.Combat.CombatAction.Melee)
        {
            unit.ShoutTick = tick + ShoutDelayTicks; // grappling, he has no breath to shout: once he is out of it, if he is
            return;
        }
        unit.Shouted = true;
        events.Add(new AlarmRaised(tick, unit.Side, unit.Id, unit.Position, AlarmKind.Shout));
        long heardSq = (long)ShoutHeardCm * ShoutHeardCm;
        foreach (var other in sim.Units)
            if (other != unit && other.Side == unit.Side && !other.IsOutOfAction && other.Alertness < Alertness.Alerted
                && (other.Position - unit.Position).LengthSquared <= heardSq)
                Raise(sim, other, Alertness.Alerted, unit.AlarmAt, tick, events);
    }

    /// <summary>A flare goes up: seen by all his side 600 m round, unless one went up within the minute.</summary>
    private static void Flare(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        unit.FlareTick = -1;
        if (unit.IsOutOfAction || sim.FlaresLeft(unit.Side) == 0 || tick - sim.LastFlareTick(unit.Side) < FlareIntervalTicks)
            return;
        sim.FireFlare(unit.Side, tick);
        events.Add(new AlarmRaised(tick, unit.Side, unit.Id, unit.Position, AlarmKind.Flare));
        long seenSq = (long)FlareSeenCm * FlareSeenCm;
        foreach (var other in sim.Units)
            if (other != unit && other.Side == unit.Side && !other.IsOutOfAction && other.Alertness < Alertness.Alerted
                && (other.Position - unit.Position).LengthSquared <= seenSq)
                Raise(sim, other, Alertness.Alerted, unit.AlarmAt, tick, events);
    }

    /// <summary>The strongest thing he sees or hears this pass, and where from; null: all quiet.</summary>
    private static (Alertness Level, Vec2 At)? Stimulus(Simulation sim, Unit unit, long tick)
    {
        if (unit.Suppression > 0 || unit.Wound > Combat.WoundLevel.None)
            return (Alertness.Combat, unit.CoverThreat ?? unit.Position);
        var map = sim.Map;
        var knowledge = sim.Knowledge(unit.Side);
        int eye = VisionRules.EyeHeightAbsCm(map, unit);
        long sightSq = (long)VisionRules.MaxSightRangeCm * VisionRules.MaxSightRangeCm;
        (Alertness Level, Vec2 At)? best = null;
        void Note(Alertness level, Vec2 at)
        {
            if (best is null || level > best.Value.Level)
                best = (level, at);
        }

        foreach (var other in sim.Units)
        {
            if (other == unit)
                continue;
            long distSq = (other.Position - unit.Position).LengthSquared;
            if (other.Side == unit.Side)
            {
                // A comrade down, seen close by.
                if (other.IsOutOfAction && distSq <= (long)FallenSeenCm * FallenSeenCm
                    && LineOfSight.Clarity(map, unit.Position, eye, other.Position, VisionRules.TargetHeightAbsCm(map, other)) > 0)
                    Note(Alertness.Alerted, other.Position);
                if (Fired(other, tick) is { } own && distSq <= own * own)
                    Note(Alertness.Alerted, other.Target is { } t && sim.FindUnit(t) is { } shotAt ? VisionSystem.RoughPosition(shotAt.Position) : other.Position);
                continue;
            }
            if (!other.IsAlive)
                continue;
            if (Fired(other, tick) is { } report && distSq <= report * report)
                Note(Alertness.Alerted, VisionSystem.RoughPosition(other.Position));
            if (!other.IsOutOfAction && distSq <= sightSq)
            {
                var contact = knowledge.Get(other.Id);
                bool known = contact is { Level: ContactLevel.Visible };
                bool glimpsed = contact is { Progress: >= GlimpseProgress };
                if ((known || glimpsed)
                    && LineOfSight.Clarity(map, unit.Position, eye, other.Position, VisionRules.TargetHeightAbsCm(map, other)) > 0)
                {
                    if (known)
                        return (Alertness.Combat, other.Position);
                    Note(Alertness.Suspicious, VisionSystem.RoughPosition(other.Position));
                }
            }
            if (other.MovedSinceVisionUpdate)
            {
                long radius = (long)VisionRules.NoiseRadiusCm(other.MoveMode) * map.CellAt(other.Position).MoveCostPct / 100;
                if (distSq <= radius * radius)
                    Note(Alertness.Suspicious, VisionSystem.RoughPosition(other.Position));
            }
        }
        // Fresh tracks of the enemy in the snow near a calm man: someone has been here (spec 2026-10-10-winter-design §3).
        if (sim.Winter is not null && unit.Alertness == Alertness.Calm)
            foreach (var (at, _) in TrackSystem.FreshAround(sim, unit.Position, unit.Side, TrackSeenCm, tick, long.MinValue, take: 3))
                if (LineOfSight.Clarity(map, unit.Position, eye, at, map.CellAt(at).GroundHeightCm + 10) > 0)
                {
                    Note(Alertness.Suspicious, at);
                    break;
                }
        foreach (var noise in sim.Noises)
            if ((noise.At - unit.Position).LengthSquared <= (long)noise.RadiusCm * noise.RadiusCm)
                Note(Alertness.Alerted, noise.At);
        return best;
    }

    /// <summary>He fired since the last pass: how far the report carries; null if he did not.</summary>
    private static long? Fired(Unit man, long tick) =>
        man.Weapon is { } weapon && man.LastShotTick > tick - VisionRules.IntervalTicks ? weapon.NoiseRadiusCm : null;
}
