using Nmf.Cli;

namespace Nmf.Content.Tests;

/// <summary>Spec 2026-10-10-wreck-mission-design §4: the headless rescue run of the wreck mission.</summary>
public class RescueProbeTests
{
    [Fact]
    public void OneSeed_RunsToAnEnd_WithItsLine()
    {
        var output = new StringWriter();
        var run = Assert.Single(RescueProbe.Run(Path.Combine(CoreContentTests.RepoRoot(), "content"), "hylky", 1, 30, output));
        Assert.True(run.Found);
        Assert.True(run.Result is not null || run.Routed, "the run neither ended nor was routed in 30 minutes");
        Assert.StartsWith("seed 1: ", output.ToString());
        Assert.Contains("mean: rescued", output.ToString());
    }
}
