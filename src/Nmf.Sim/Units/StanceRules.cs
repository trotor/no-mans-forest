namespace Nmf.Sim.Units;

public static class StanceRules
{
    public static int HeightCm(Stance stance) => stance switch
    {
        Stance.Standing => 170,
        Stance.Crouching => 100,
        _ => 30,
    };

    public static int EyeHeightCm(Stance stance) => stance switch
    {
        Stance.Standing => 160,
        Stance.Crouching => 90,
        _ => 25,
    };

    /// <summary>Ticks for one step between adjacent stances.</summary>
    public static int StepTicks(Stance from, Stance to) =>
        (from, to) is (Stance.Standing, Stance.Crouching) or (Stance.Crouching, Stance.Standing) ? 10 : 15;

    public static Stance NextToward(Stance current, Stance target) =>
        current < target ? current + 1 : current - 1;

    public static Stance RequiredFor(MoveMode mode) => mode == MoveMode.Crawl ? Stance.Prone : Stance.Standing;

    /// <summary>Speed on open ground for the given move mode.</summary>
    public static int SpeedCmPerTick(Unit unit, MoveMode mode) => mode switch
    {
        MoveMode.Walk => unit.SpeedCmPerTick,
        MoveMode.Run => unit.SpeedCmPerTick * 2,
        _ => Math.Max(1, unit.SpeedCmPerTick / 5),
    };
}
