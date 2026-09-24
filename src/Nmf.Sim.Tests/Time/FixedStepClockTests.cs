using Nmf.Sim.Time;

namespace Nmf.Sim.Tests.Time;

public class FixedStepClockTests
{
    [Fact]
    public void OneStepDuration_YieldsOneStep()
    {
        var clock = new FixedStepClock();
        Assert.Equal(1, clock.Advance(0.05));
        Assert.Equal(0, clock.Alpha, 6);
    }

    [Fact]
    public void PartialStep_AccumulatesAndReportsAlpha()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0, clock.Advance(0.025));
        Assert.Equal(0.5, clock.Alpha, 6);
        Assert.Equal(1, clock.Advance(0.025));
    }

    [Fact]
    public void SixtyFramesPerSecond_ProducesAboutTwentyStepsPerSecond()
    {
        var clock = new FixedStepClock();
        int total = 0;
        for (int i = 0; i < 60; i++)
            total += clock.Advance(1.0 / 60);
        Assert.InRange(total, 19, 20);
    }

    [Fact]
    public void Paused_YieldsNoSteps()
    {
        var clock = new FixedStepClock { Paused = true };
        Assert.Equal(0, clock.Advance(1.0));
    }

    [Fact]
    public void TimeScale_SpeedsUpSimulation()
    {
        var clock = new FixedStepClock { TimeScale = 2 };
        Assert.Equal(2, clock.Advance(0.05));
    }

    [Fact]
    public void LongHitch_IsCappedAndBacklogDropped()
    {
        var clock = new FixedStepClock(maxStepsPerFrame: 5);
        Assert.Equal(5, clock.Advance(10.0));
        Assert.Equal(0, clock.Alpha, 6);
        Assert.Equal(1, clock.Advance(0.05));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Advance_InvalidDelta_Throws(double delta)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock().Advance(delta));
    }

    [Fact]
    public void NegativeTimeScale_Throws()
    {
        var clock = new FixedStepClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.TimeScale = -1);
    }

    [Fact]
    public void ZeroMaxSteps_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(0));
    }
}
