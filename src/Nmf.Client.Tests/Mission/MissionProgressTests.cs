using Nmf.Client.Mission;

namespace Nmf.Client.Tests.Mission;

public class MissionProgressTests
{
    private static MissionResult Result(bool success, int killed = 0, int wounded = 0, int seconds = 600) =>
        new(success, seconds, killed, wounded, EnemyDown: 5, ObjectivesDone: success ? 2 : 1, Objectives: 2);

    [Fact]
    public void ARecord_CountsAttempts_AndKeepsTheBestSuccess()
    {
        var progress = new MissionProgress();
        progress.Record("iskuosasto", Result(false, killed: 7));
        progress.Record("iskuosasto", Result(true, killed: 2, wounded: 1, seconds: 900));
        progress.Record("iskuosasto", Result(true, killed: 3, seconds: 500)); // more losses: not better
        progress.Record("iskuosasto", Result(true, killed: 2, wounded: 1, seconds: 700)); // same losses, faster: better
        var record = progress.Of("iskuosasto")!;
        Assert.Equal(4, record.Attempts);
        Assert.True(record.Completed);
        Assert.Equal(700, record.Best!.Seconds);
        Assert.Null(progress.Of("other"));
    }

    [Fact]
    public void Progress_SurvivesSavingAndLoading_AndABrokenFileIsAFreshStart()
    {
        var progress = new MissionProgress();
        progress.Record("iskuosasto", Result(true, killed: 1));
        var again = MissionProgress.FromJson(progress.ToJson());
        Assert.True(again.Of("iskuosasto")!.Completed);
        Assert.Equal(1, again.Of("iskuosasto")!.Best!.OwnKilled);
        Assert.Null(MissionProgress.FromJson("{ not json").Of("iskuosasto"));
        Assert.Null(MissionProgress.FromJson("").Of("iskuosasto"));
    }

    [Fact]
    public void StatusText_TellsWhatHasBeenDone()
    {
        var progress = new MissionProgress();
        Assert.Equal("Uusi tehtävä", progress.StatusText("iskuosasto", "fi"));
        progress.Record("iskuosasto", Result(false));
        Assert.Equal("1 yritys — ei vielä suoritettu", progress.StatusText("iskuosasto", "fi"));
        progress.Record("iskuosasto", Result(true, killed: 1, wounded: 2, seconds: 754));
        Assert.Equal("✓ Suoritettu — paras: 1 kaatunut, 2 haavoittunutta, 12:34", progress.StatusText("iskuosasto", "fi"));
        Assert.Equal("✓ Accomplished — best: 1 killed, 2 wounded, 12:34", progress.StatusText("iskuosasto", "en"));
    }
}
