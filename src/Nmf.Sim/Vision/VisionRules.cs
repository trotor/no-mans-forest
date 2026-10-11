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

    /// <summary>A scout's movement is this much harder to see and to hear (spec 2026-10-11-campaign-design §9b).</summary>
    public const int ScoutMovingPct = 70;

    /// <summary>A scout makes out a man this much sooner.</summary>
    public const int ScoutObserverBonus = 20;

    /// <summary>How far this man's movement is heard: a scout's less far.</summary>
    public static int NoiseRadiusCm(Unit unit) =>
        unit.Specialty == Specialty.Scout ? NoiseRadiusCm(unit.MoveMode) * ScoutMovingPct / 100 : NoiseRadiusCm(unit.MoveMode);

    /// <summary>
    /// Firing makes a soldier much easier to notice for a moment; moving targets are easier to notice too, a scout less
    /// so (never less than standing still).
    /// </summary>
    public static int VisibilityPct(Unit unit, long tick)
    {
        if (tick - unit.LastShotTick <= CombatRules.FiringVisibilityTicks)
            return CombatRules.FiringVisibilityPct;
        int pct = !unit.IsMoving ? 100 : unit.MoveMode switch
        {
            MoveMode.Walk => 200,
            MoveMode.Run => 300,
            MoveMode.Sneak => 150,
            _ => 120,
        };
        return unit.Specialty == Specialty.Scout && unit.IsMoving ? Math.Max(100, pct * ScoutMovingPct / 100) : pct;
    }

    /// <summary>
    /// An old hand makes out a man in the brush sooner than a recruit: (50 + experience) %, the average soldier 100 %
    /// (a veteran of 90 at 140 %, a recruit of 30 at 80 %); a scout 20 points more.
    /// </summary>
    public static int ObserverPct(Unit observer) =>
        50 + observer.Experience + (observer.Specialty == Specialty.Scout ? ScoutObserverBonus : 0);

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
