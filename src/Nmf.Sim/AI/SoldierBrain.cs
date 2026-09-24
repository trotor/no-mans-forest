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
        if (unit.Action is CombatAction.Melee or CombatAction.Throwing)
            return;
        if (unit.AssaultTarget is not null)
        {
            Assault(sim, unit, tick);
            return;
        }
        if (unit.AutoPace && unit.MoveTarget is not null)
            AdjustPace(sim, unit);

        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (idle && unit.Suppression >= CombatRules.GoProneAt && unit.Stance != Stance.Prone)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        if (idle && unit.Stance == Stance.Standing && unit.Suppression < CombatRules.CalmSuppression
            && !unit.StanceOrdered && unit.Action is not (CombatAction.Aiming or CombatAction.Firing)
            && EnemyInSightWithin(sim, unit, CombatRules.AutoCrouchRangeCm))
        {
            Movement.BeginStanceChange(unit, Stance.Crouching);
            return;
        }
        if (!idle || unit.Action != CombatAction.None || unit.FirePolicy == FirePolicy.HoldFire)
            return;
        if (ChooseGrenadeTarget(sim, unit, tick) is { } grenadeTarget)
        {
            GrenadeSystem.StartThrow(unit, grenadeTarget);
            return;
        }
        if (unit.Weapon is null)
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
            if (!Firing.CanSee(sim, unit, enemy) || Firing.FriendInLine(sim, unit, enemy))
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

    /// <summary>The pace a soldier on an Auto move picks: run under fire, sneak with the enemy in sight nearby, else walk.</summary>
    public static MoveMode ChoosePace(Simulation sim, Unit unit)
    {
        if (unit.Suppression >= CombatRules.AutoRunSuppression)
            return MoveMode.Run;
        return EnemyInSightWithin(sim, unit, CombatRules.SneakRangeCm) ? MoveMode.Sneak : MoveMode.Walk;
    }

    private static void AdjustPace(Simulation sim, Unit unit)
    {
        var mode = ChoosePace(sim, unit);
        if (mode == unit.MoveMode)
            return;
        unit.MoveMode = mode;
        Movement.BeginStanceChange(unit, StanceRules.RequiredFor(mode));
    }

    private static bool EnemyInSightWithin(Simulation sim, Unit unit, int rangeCm)
    {
        long rangeSq = (long)rangeCm * rangeCm;
        var knowledge = sim.Knowledge(unit.Side);
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side != unit.Side && !enemy.IsOutOfAction && knowledge.LevelOf(enemy.Id) == ContactLevel.Visible
                && (enemy.Position - unit.Position).LengthSquared <= rangeSq)
                return true;
        }
        return false;
    }

    private static Unit? ChooseGrenadeTarget(Simulation sim, Unit unit, long tick)
    {
        if (unit.Grenades <= 0)
            return null;
        var knowledge = sim.Knowledge(unit.Side);
        Unit? best = null;
        long bestDistance = long.MaxValue;
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side == unit.Side || enemy.IsOutOfAction || knowledge.LevelOf(enemy.Id) != ContactLevel.Visible)
                continue;
            if (unit.FirePolicy == FirePolicy.ReturnFire && tick - enemy.LastShotTick > CombatRules.ReturnFireMemoryTicks)
                continue;
            if (!GrenadeSystem.CanThrowAt(sim, unit, enemy, tick))
                continue;
            long d = (enemy.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = enemy;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Charge the target: re-plan when he moves, throw a grenade on the way when it makes sense, close in for melee.</summary>
    private static void Assault(Simulation sim, Unit unit, long tick)
    {
        var target = sim.FindUnit(unit.AssaultTarget!.Value);
        if (target is null || target.IsOutOfAction)
        {
            unit.AssaultTarget = null;
            Movement.ClearPath(unit);
            return;
        }
        if (FriendlyGrenadeAhead(sim, unit, target.Position))
        {
            Movement.ClearPath(unit); // wait for our own grenade to go off
            return;
        }
        if (unit.MoraleState == MoraleState.Pinned || unit.TargetStance is not null || unit.Action != CombatAction.None)
            return;
        if (sim.Knowledge(unit.Side).LevelOf(target.Id) == ContactLevel.Visible && GrenadeSystem.CanThrowAt(sim, unit, target, tick))
        {
            GrenadeSystem.StartThrow(unit, target);
            return;
        }
        if ((target.Position - unit.Position).LengthSquared <= (long)CombatRules.MeleeRangeCm * CombatRules.MeleeRangeCm)
            return;
        if (unit.MoveTarget is not null
            && (unit.AssaultGoal - target.Position).LengthSquared <= (long)CombatRules.AssaultRepathCm * CombatRules.AssaultRepathCm)
            return;
        var path = Pathfinder.FindPath(sim.Map, unit.Position, target.Position);
        if (path is null)
        {
            unit.AssaultTarget = null;
            Movement.ClearPath(unit);
            return;
        }
        Movement.StartPath(unit, target.Position, MoveMode.Run, path);
        unit.AssaultGoal = target.Position;
    }

    /// <summary>A live grenade of our own lies close by on the way to the target.</summary>
    private static bool FriendlyGrenadeAhead(Simulation sim, Unit unit, Vec2 goal)
    {
        foreach (var grenade in sim.Grenades)
        {
            if (grenade.Exploded || grenade.Side != unit.Side)
                continue;
            var toGrenade = grenade.Landing - unit.Position;
            long keep = grenade.Def.LethalRadiusCm + CombatRules.AssaultGrenadeClearanceCm;
            if (toGrenade.LengthSquared < keep * keep && toGrenade.Dot(goal - unit.Position) > 0)
                return true;
        }
        return false;
    }

    private static void Retreat(Simulation sim, Unit unit)
    {
        // Panic overrides whatever stance change was under way; only an ongoing retreat run is left alone.
        if (unit.MoveTarget is not null)
            return;
        if (unit.Retreated)
        {
            if (unit.TargetStance is null && unit.Stance != Stance.Prone)
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
