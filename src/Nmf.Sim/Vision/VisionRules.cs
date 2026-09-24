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
        MoveMode.Run => 6000,
        _ => 800,
    };

    /// <summary>Moving targets are easier to notice.</summary>
    public static int MovementVisibilityPct(Unit unit) => !unit.IsMoving ? 100 : unit.MoveMode switch
    {
        MoveMode.Walk => 200,
        MoveMode.Run => 300,
        _ => 120,
    };

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
