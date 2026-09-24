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
        if (unit.IsOutOfAction)
            return "Down";
        if (unit.MoraleState == MoraleState.Broken)
            return "Broken";
        if (unit.MoraleState == MoraleState.Pinned)
            return "Pinned";
        if (unit.Action == CombatAction.Reloading)
            return "Reloading";
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
            return "Firing";
        if (unit.TargetStance is { } target)
            return target > unit.Stance ? "Getting down" : "Getting up";
        bool moving = unit.MoveTarget is not null;
        return unit.Stance switch
        {
            Stance.Prone => moving ? "Crawling" : "Prone",
            Stance.Crouching => "Crouching",
            _ => !moving ? "Standing" : unit.MoveMode == MoveMode.Run ? "Running" : "Walking",
        };
    }

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
