namespace Nmf.Content.Tests;

/// <summary>Tests that assert on wall-clock time run on their own, not alongside other tests competing for the CPU.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingCollection
{
    public const string Name = "Timing";
}
