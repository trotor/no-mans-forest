namespace Nmf.Client.Mission;

/// <summary>
/// When the guide to the objective shows (spec 2026-09-26-guide-grenades-design §2.1): out of the way while the men
/// move or fight, or once they are close to the objective; back to remind of the way when all has been quiet a while.
/// </summary>
public sealed class GuideFade
{
    /// <summary>Quiet this long (no orders, nobody moving, no fighting) and the guide comes back.</summary>
    public const double QuietSeconds = 12;
    private const double FadeSeconds = 0.6;

    private double _quiet = QuietSeconds;

    public double Alpha { get; private set; } = 1;

    /// <param name="activity">An order given, own men on the move, or fighting seen or heard this frame.</param>
    /// <param name="near">The men are at or close to the objective.</param>
    public void Update(double realSeconds, bool activity, bool near)
    {
        _quiet = activity ? 0 : _quiet + realSeconds;
        double target = !near && _quiet >= QuietSeconds ? 1 : 0;
        double step = realSeconds / FadeSeconds;
        Alpha = Alpha < target ? Math.Min(target, Alpha + step) : Math.Max(target, Alpha - step);
    }
}
