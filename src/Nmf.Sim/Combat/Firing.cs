using System.Diagnostics.CodeAnalysis;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Combat;

/// <summary>The aim → burst → recover / reload action of one soldier, one tick at a time.</summary>
internal static class Firing
{
    /// <summary>Area fire: aim at the place in <see cref="Unit.AreaTarget"/> (no man as target).</summary>
    public static void StartAreaAiming(Unit unit)
    {
        unit.Target = null;
        if (unit.Ammo <= 0) // e.g. a reload cut short by hand-to-hand fighting
        {
            StartReloadOrStop(unit);
            return;
        }
        unit.Action = CombatAction.Aiming;
        int pct = unit.MoraleState == MoraleState.Pinned ? CombatRules.PinnedAimPct : 100;
        if (unit.Stance == Stance.Prone && !CombatRules.OnBipod(unit))
            pct = pct * CombatRules.ProneAimPct / 100;
        unit.ActionTicksLeft = Math.Max(1, unit.Weapon!.AimTicks * pct / 100);
    }

    public static void StartAiming(Unit unit, Unit target)
    {
        unit.Target = target.Id;
        if (unit.Ammo <= 0) // e.g. a reload cut short by hand-to-hand fighting
        {
            StartReloadOrStop(unit);
            return;
        }
        unit.Action = CombatAction.Aiming;
        int pct = unit.MoraleState == MoraleState.Pinned ? CombatRules.PinnedAimPct : 100;
        if (unit.Stance == Stance.Prone && !CombatRules.OnBipod(unit)) // lying is how a machine gun is meant to be fired
            pct = pct * CombatRules.ProneAimPct / 100;
        unit.ActionTicksLeft = Math.Max(1, unit.Weapon!.AimTicks * pct / 100);
    }

    /// <summary>Reload from a spare magazine; with none left the man is out of ammo and stops.</summary>
    private static void StartReloadOrStop(Unit unit)
    {
        if (unit.Magazines > 0)
        {
            unit.Action = CombatAction.Reloading;
            unit.ActionTicksLeft = unit.Weapon!.ReloadTicks;
            return;
        }
        unit.Action = CombatAction.None;
        unit.ActionTicksLeft = 0;
        unit.RoundsLeftInBurst = 0;
        unit.Target = null;
    }

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

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        var weapon = unit.Weapon;
        if (weapon is null || unit.IsOutOfAction)
            return;
        switch (unit.Action)
        {
            case CombatAction.Aiming:
            {
                Unit? target = null;
                if (!(unit.Target is null && CanEngageArea(sim, unit)) && !CanEngage(sim, unit, out target))
                {
                    Cancel(unit);
                    return;
                }
                if (--unit.ActionTicksLeft > 0)
                    return;
                unit.Action = CombatAction.Firing;
                unit.RoundsLeftInBurst = Math.Min(weapon.RoundsPerBurst, unit.Ammo);
                FireRound(sim, unit, target, weapon, tick, events);
                return;
            }
            case CombatAction.Firing:
            {
                Unit? target = null;
                if (!(unit.Target is null && CanEngageArea(sim, unit)) && !CanEngage(sim, unit, out target))
                {
                    Cancel(unit);
                    return;
                }
                if (--unit.ActionTicksLeft > 0)
                    return;
                FireRound(sim, unit, target, weapon, tick, events);
                return;
            }
            case CombatAction.Recovering:
                if (--unit.ActionTicksLeft <= 0)
                    unit.Action = CombatAction.None;
                return;
            case CombatAction.Reloading:
                if (--unit.ActionTicksLeft <= 0)
                {
                    unit.Ammo = weapon.MagazineSize;
                    unit.Magazines--;
                    unit.Action = CombatAction.None;
                }
                return;
        }
    }

    /// <summary>Within weapon range and in line of sight from the shooter's eyes.</summary>
    public static bool CanSee(Simulation sim, Unit shooter, Unit target)
    {
        long range = shooter.Weapon?.RangeCm ?? 0;
        if ((target.Position - shooter.Position).LengthSquared > range * range)
            return false;
        return LineOfSight.Clarity(sim.Map, shooter.Position, VisionRules.EyeHeightAbsCm(sim.Map, shooter),
            target.Position, VisionRules.TargetHeightAbsCm(sim.Map, target)) > 0;
    }

    /// <summary>A soldier does not fire when a comrade is within a metre of the line in front of him.</summary>
    public static bool FriendInLine(Simulation sim, Unit shooter, Unit target) => FriendInLine(sim, shooter, target.Position);

    public static bool FriendInLine(Simulation sim, Unit shooter, Vec2 point)
    {
        var dir = point - shooter.Position;
        long length = Math.Max(1, Core.IntMath.Isqrt(dir.LengthSquared));
        foreach (var friend in sim.Units)
        {
            if (friend == shooter || friend.Side != shooter.Side || friend.IsOutOfAction)
                continue;
            var rel = friend.Position - shooter.Position;
            long along = ((long)rel.X * dir.X + (long)rel.Y * dir.Y) / length;
            long side = Math.Abs((long)rel.X * dir.Y - (long)rel.Y * dir.X) / length;
            if (along > 0 && along < length && side <= CombatRules.FriendlyLineClearanceCm)
                return true;
        }
        return false;
    }

    /// <summary>Area fire goes on while he stands (or goes at his own pace), is not broken and no friend is in the way; it
    /// is an order, so a hold-fire policy does not stop it.</summary>
    private static bool CanEngageArea(Simulation sim, Unit unit) =>
        unit.AreaTarget is { } point && (unit.MoveTarget is null || unit.AutoPace) && unit.TargetStance is null
        && unit.MoraleState != MoraleState.Broken && !FriendNearAreaFire(sim, unit, point);

    /// <summary>
    /// Area fire flies on past the place, so a friend anywhere on the line out to 10 m beyond it, or within 5 m of it,
    /// holds the fire.
    /// </summary>
    public static bool FriendNearAreaFire(Simulation sim, Unit shooter, Vec2 point)
    {
        var dir = point - shooter.Position;
        long length = Math.Max(1, Core.IntMath.Isqrt(dir.LengthSquared));
        long clearSq = (long)CombatRules.AimedMissRadiusCm * CombatRules.AimedMissRadiusCm;
        foreach (var friend in sim.Units)
        {
            if (friend == shooter || friend.Side != shooter.Side || friend.IsOutOfAction)
                continue;
            if ((friend.Position - point).LengthSquared <= clearSq)
                return true;
            var rel = friend.Position - shooter.Position;
            long along = ((long)rel.X * dir.X + (long)rel.Y * dir.Y) / length;
            long side = Math.Abs((long)rel.X * dir.Y - (long)rel.Y * dir.X) / length;
            if (along > 0 && along < length + CombatRules.AreaFireOvershootCm && side <= CombatRules.FriendlyLineClearanceCm)
                return true;
        }
        return false;
    }

    private static bool CanEngage(Simulation sim, Unit unit, [NotNullWhen(true)] out Unit? target)
    {
        target = unit.Target is { } id ? sim.FindUnit(id) : null;
        if (target is null || target.IsOutOfAction || (unit.MoveTarget is not null && !unit.AutoPace) || unit.TargetStance is not null
            || unit.MoraleState == MoraleState.Broken || unit.FirePolicy == FirePolicy.HoldFire || !CanSee(sim, unit, target)
            || FriendInLine(sim, unit, target))
        {
            target = null;
            return false;
        }
        return true;
    }

    private static void FireRound(Simulation sim, Unit unit, Unit? target, WeaponDef weapon, long tick, List<SimEvent> events)
    {
        if (unit.Ammo <= 0)
        {
            StartReloadOrStop(unit);
            return;
        }
        var shot = target is not null ? Ballistics.Trace(sim, unit, target) : Ballistics.TraceAt(sim, unit, unit.AreaTarget!.Value);
        unit.Ammo--;
        unit.RoundsLeftInBurst--;
        unit.LastShotTick = tick;
        events.Add(new ShotFired(tick, unit.Id, unit.Position, shot.End, shot.Hit?.Id));

        if (shot.Hit is { IsAlive: true } hit)
            Damage.ApplyHit(sim, hit, weapon, tick, events, unit.Position);
        foreach (var miss in shot.NearMisses)
        {
            int amount = weapon.SuppressionPerRound * (miss.RadiusCm - miss.DistanceCm) / miss.RadiusCm * CombatRules.SuppressionPct(unit) / 100;
            MoraleSystem.AddSuppression(sim, miss.Unit, amount, tick, events, unit.Position);
        }

        if (unit.Ammo == 0)
        {
            StartReloadOrStop(unit);
            unit.Target = null;
        }
        else if (unit.RoundsLeftInBurst > 0)
        {
            unit.ActionTicksLeft = Math.Max(1, weapon.RoundIntervalTicks);
        }
        else
        {
            unit.Action = CombatAction.Recovering;
            unit.ActionTicksLeft = weapon.RecoverTicks;
            unit.Target = null;
        }
    }
}
