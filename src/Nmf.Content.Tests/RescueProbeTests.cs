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
        Assert.True(run.Result is not null || run.Routed, "the run neither ended nor was routed in 30 minutes");
        Assert.StartsWith("seed 1: ", output.ToString());
        Assert.Contains("mean: rescued", output.ToString());
    }

    [Fact]
    public void TheWreck_IsOffTheSearchAreasMiddle_TheProbeStillFindsAndReachesIt()
    {
        var mission = Nmf.Content.Missions.MissionLoader.Load(Path.Combine(CoreContentTests.RepoRoot(), "content", "core", "missions", "hylky"));
        var area = mission.MissionZones.Single(z => z.Name == "wreck_area");
        var middle = new Nmf.Sim.Core.Vec2((area.Min.X + area.Max.X) / 2, (area.Min.Y + area.Max.Y) / 2);
        Assert.True((mission.Places["wreck"] - middle).LengthSquared >= 3_000L * 3_000, "the guide's arrow to the area would lead straight to the wreck");
        var runs = RescueProbe.Run(Path.Combine(CoreContentTests.RepoRoot(), "content"), "hylky", 3, 30, new StringWriter());
        Assert.All(runs, r => Assert.True(r.Result is not null || r.Routed, $"seed {r.Seed}: neither ended nor routed"));
        Assert.Contains(runs, r => r.Found && r.Result == true); // found where it lies off the middle, and brought home
    }

    [Fact]
    public void NoSeeds_NoCrash() =>
        Assert.Empty(RescueProbe.Run(Path.Combine(CoreContentTests.RepoRoot(), "content"), "hylky", 0, 1, new StringWriter()));
}
