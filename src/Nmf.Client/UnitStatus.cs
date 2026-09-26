using Nmf.Sim.Combat;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>Short status text for portrait cards.</summary>
public static class UnitStatus
{
    public static string Describe(Unit unit)
    {
        if (unit.Wound == WoundLevel.Dead)
            return "Dead";
        if (unit.IsCaptured)
            return "Captured";
        if (unit.IsOutOfAction)
            return "Down";
        if (unit.MoraleState == MoraleState.Broken)
            return "Broken";
        if (unit.MoraleState == MoraleState.Pinned)
            return "Pinned";
        if (unit.Action == CombatAction.Melee)
            return "Melee";
        if (unit.Action == CombatAction.Throwing)
            return "Throwing";
        if (unit.Action == CombatAction.Looting)
            return "Looting";
        if (unit.Action == CombatAction.Reloading)
            return "Reloading";
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
            return "Firing";
        if (unit.TargetStance is { } target)
            return target > unit.Stance ? "Getting down" : "Getting up";
        bool moving = unit.MoveTarget is not null;
        if (!moving && unit.Weapon is not null && unit.OutOfAmmo)
            return "Out of ammo";
        if (unit.AttackRole != AttackRole.None && moving)
            return "Bounding";
        if (unit.AttackRole != AttackRole.None)
            return "Covering fire";
        if (moving && unit.TakingCover)
            return "Taking cover";
        if (moving && unit.AssaultTarget is not null)
            return "Assaulting";
        if (moving && unit.MoveMode == MoveMode.Sneak)
            return "Sneaking";
        return unit.Stance switch
        {
            Stance.Prone => moving ? "Crawling" : "Prone",
            Stance.Crouching => "Crouching",
            _ => !moving ? "Standing" : unit.MoveMode == MoveMode.Run ? "Running" : "Walking",
        };
    }

    /// <summary>"Ammo 5+12": rounds in the weapon + spare magazines.</summary>
    public static string AmmoText(Unit unit) => $"Ammo {unit.Ammo}+{unit.Magazines}";

    public static string Condition(Unit unit) => unit.Wound switch
    {
        WoundLevel.None => "Unhurt",
        WoundLevel.Light => "Light wound",
        WoundLevel.Serious => "Serious wound",
        WoundLevel.Incapacitated => "Down",
        _ => "Dead",
    };

    public static string PolicyName(FirePolicy policy) => policy switch
    {
        FirePolicy.FireAtWill => "Fire at will",
        FirePolicy.ReturnFire => "Return fire",
        _ => "Hold fire",
    };
}
