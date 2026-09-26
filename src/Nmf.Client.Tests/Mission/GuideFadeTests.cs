using Nmf.Client.Mission;

namespace Nmf.Client.Tests.Mission;

public class GuideFadeTests
{
    private static GuideFade Run(GuideFade fade, double seconds, bool activity, bool near = false)
    {
        for (double t = 0; t < seconds; t += 0.1)
            fade.Update(0.1, activity, near);
        return fade;
    }

    [Fact]
    public void ShownAtFirst_GoneWhileTheMenMoveOrFight_BackWhenAllIsQuietAWhile()
    {
        var fade = new GuideFade();
        Assert.Equal(1, fade.Alpha);
        Run(fade, 1, activity: true);
        Assert.Equal(0, fade.Alpha);
        Run(fade, GuideFade.QuietSeconds - 1, activity: false);
        Assert.Equal(0, fade.Alpha); // not yet
        Run(fade, 2, activity: false);
        Assert.Equal(1, fade.Alpha);
    }

    [Fact]
    public void CloseToTheObjective_ItStaysAway_EvenWhenQuiet()
    {
        var fade = Run(new GuideFade(), GuideFade.QuietSeconds * 2, activity: false, near: true);
        Assert.Equal(0, fade.Alpha);
    }

    [Fact]
    public void ItFades_RatherThanBlinks()
    {
        var fade = new GuideFade();
        fade.Update(0.1, activity: true, near: false);
        Assert.InRange(fade.Alpha, 0.01, 0.99);
    }
}
