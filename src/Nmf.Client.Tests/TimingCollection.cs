namespace Nmf.Client.Tests;

/// <summary>Tests that assert on wall-clock time run on their own, not alongside other tests competing for the CPU.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";

    /// <summary>A time limit, stretched on slow machines (CI sets NMF_TIMING_FACTOR, e.g. 4).</summary>
    public static long Budget(long milliseconds)
    {
        string? factor = Environment.GetEnvironmentVariable("NMF_TIMING_FACTOR");
        return double.TryParse(factor, System.Globalization.CultureInfo.InvariantCulture, out double f) && f > 0
            ? (long)(milliseconds * f)
            : milliseconds;
    }
}
