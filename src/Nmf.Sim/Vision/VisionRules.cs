using Nmf.Sim.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

/// <summary>Tunable numbers of the spotting and hearing model.</summary>
public static class VisionRules
{
    public const int IntervalTicks = 5;
    public const int MaxSightRangeCm = 40_000;
    public const int SpottedThreshold = 1000;
    public const int BaseGainPerUpdate = 250;
    public const int DecayPerUpdate = 50;
    public const int ReacquireProgress = 500;
    public const int SuspectedTimeoutTicks = 200;
    public const int SuspectedGridCm = 1000;

    public static int NoiseRadiusCm(MoveMode mode) => mode switch
    {
        MoveMode.Walk => 3000,
        MoveMode.Sneak => 1500,
        MoveMode.Run => 6000,
        _ => 800,
    };

    /// <summary>Firing makes a soldier much easier to notice for a moment; moving targets are easier to notice too.</summary>
    public static int VisibilityPct(Unit unit, long tick)
    {
        if (tick - unit.LastShotTick <= CombatRules.FiringVisibilityTicks)
            return CombatRules.FiringVisibilityPct;
        return !unit.IsMoving ? 100 : unit.MoveMode switch
        {
            MoveMode.Walk => 200,
            MoveMode.Run => 300,
            MoveMode.Sneak => 150,
            _ => 120,
        };
    }

    /// <summary>Standing in a foxhole only his head shows: as hard to make out as a man lying down.</summary>
    public static int StanceVisibilityPct(World.GridMap map, Units.Unit target) =>
        World.CoverFinder.InPit(map, target.Position) ? StanceVisibilityPct(Stance.Prone) : StanceVisibilityPct(target.Stance);

    public static int StanceVisibilityPct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 60,
        _ => 30,
    };

    public static int EyeHeightAbsCm(GridMap map, Unit unit) =>
        map.CellAt(unit.Position).GroundHeightCm + StanceRules.EyeHeightCm(unit.Stance);

    /// <summary>Just below the top of the head.</summary>
    public static int TargetHeightAbsCm(GridMap map, Unit unit) =>
        map.CellAt(unit.Position).GroundHeightCm + StanceRules.HeightCm(unit.Stance) - 10;
}
