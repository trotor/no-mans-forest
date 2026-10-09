using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Vision;

namespace Nmf.Client.Audio;

/// <summary>The mixing rules of spec 2026-10-09-sound-design: loudness by view size, sound delay, rough places.</summary>
public static class AudioMix
{
    /// <summary>How quiet a loud sound far off screen gets, and no quieter.</summary>
    public const double LoudFloorDb = -18;

    private const double SilentDb = -80;

    /// <summary>Beyond this from the nearest own man a shot or blast sounds distant.</summary>
    public const int FarCm = 15_000;

    /// <summary>A bullet passing this close to an own man cracks.</summary>
    public const int CrackCm = 500;

    /// <summary>Where a hidden shooter's bullet lands this close to an own man, the strike is heard.</summary>
    public const int ImpactNearCm = 4_000;

    /// <summary>An explosion is heard this far from the nearest own man.</summary>
    public const int ExplosionHeardCm = 80_000;

    /// <summary>Below this zoom, or at this speed and above, quiet sounds (steps, reloads) are left out.</summary>
    public const double QuietZoom = 0.4, QuietSpeed = 4;

    /// <summary>At least this much real time between one shooter's shot sounds.</summary>
    public const double ShooterGapSeconds = 0.05;

    private const long SoundCmPerSecond = 34_300;

    /// <summary>
    /// The volume of a sound at <paramref name="at"/> for the listener, or null when it is not heard. On screen (within half
    /// the view's diagonal) it plays at its own volume; beyond, a loud sound falls to <see cref="LoudFloorDb"/> at three half
    /// diagonals and stays there, and any other sound fades out by two.
    /// </summary>
    public static double? Volume(double cueDb, bool loud, Vec2 at, AudioListener l)
    {
        double r = Math.Sqrt((at - l.CenterCm).LengthSquared) / Math.Max(1, l.HalfDiagonalCm);
        if (r <= 1)
            return cueDb;
        if (loud)
            return cueDb + (Math.Min(cueDb, LoudFloorDb) - cueDb) * Math.Min(1, (r - 1) / 2);
        return r >= 2 ? null : cueDb + (SilentDb - cueDb) * (r - 1);
    }

    /// <summary>How many game ticks sound takes to travel this far.</summary>
    public static long DelayTicks(long distanceCm) => distanceCm * SimConstants.TicksPerSecond / SoundCmPerSecond;

    /// <summary>The middle of the 10 m square a heard but unseen sound came from (as a suspected contact is marked).</summary>
    public static Vec2 Rough(Vec2 p)
    {
        const int grid = VisionRules.SuspectedGridCm;
        return new Vec2(IntMath.FloorDiv(p.X, grid) * grid + grid / 2, IntMath.FloorDiv(p.Y, grid) * grid + grid / 2);
    }

    /// <summary>Squared distance to the nearest of <paramref name="men"/>; null when there are none.</summary>
    public static long? NearestSq(Vec2 p, IReadOnlyList<Vec2> men) =>
        men.Count == 0 ? null : men.Min(m => (m - p).LengthSquared);

    /// <summary>Whether a bullet flying from <paramref name="from"/> to <paramref name="to"/> passed within the radius of the man.</summary>
    public static bool PassesWithin(Vec2 from, Vec2 to, Vec2 man, int radiusCm)
    {
        var d = to - from;
        var rel = man - from;
        long lenSq = Math.Max(1, d.LengthSquared);
        long along = (long)rel.X * d.X + (long)rel.Y * d.Y;
        Vec2 closest = along <= 0 ? from : along >= lenSq ? to
            : new Vec2(from.X + (int)(d.X * along / lenSq), from.Y + (int)(d.Y * along / lenSq));
        return (man - closest).LengthSquared <= (long)radiusCm * radiusCm;
    }

    public static bool QuietSoundsOn(AudioListener l) => l.Zoom >= QuietZoom && l.Speed < QuietSpeed;
}
