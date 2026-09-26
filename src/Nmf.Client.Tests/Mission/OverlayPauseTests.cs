using Nmf.Client.Mission;
using Nmf.Sim.Time;

namespace Nmf.Client.Tests.Mission;

public class OverlayPauseTests
{
    [Fact]
    public void AnOpenPaper_PausesTheGame_AndClosingTheLastRestoresIt()
    {
        var clock = new FixedStepClock();
        var pause = new OverlayPause(clock);
        pause.Opened("orders");
        Assert.True(clock.Paused);
        pause.Opened("map");      // orders → map: still paused
        pause.Closed("orders");
        Assert.True(clock.Paused);
        pause.Closed("map");
        Assert.False(clock.Paused);
    }

    [Fact]
    public void AGameAlreadyPaused_StaysPausedAfterwards()
    {
        var clock = new FixedStepClock { Paused = true };
        var pause = new OverlayPause(clock);
        pause.Opened("map");
        pause.Closed("map");
        Assert.True(clock.Paused);
    }

    [Fact]
    public void ClosingTwice_OrClosingWhatWasNotOpen_ChangesNothing()
    {
        var clock = new FixedStepClock();
        var pause = new OverlayPause(clock);
        pause.Closed("map");
        Assert.False(clock.Paused);
        pause.Opened("orders");
        pause.Closed("orders");
        clock.Paused = true;      // the player paused afterwards
        pause.Closed("orders");
        Assert.True(clock.Paused);
        Assert.False(pause.AnyOpen);
    }
}
