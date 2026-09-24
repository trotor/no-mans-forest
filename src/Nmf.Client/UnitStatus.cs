using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>Short status text for portrait cards.</summary>
public static class UnitStatus
{
    public static string Describe(Unit unit)
    {
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
}
