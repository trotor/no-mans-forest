using Nmf.Client;

namespace Nmf.Client.Tests;

public class MissionClockTests
{
    private static readonly DateTime Dawn = new(1942, 7, 14, 3, 10, 0);

    [Fact]
    public void TheClock_ShowsTheDayAndTheTimeOfDay_AsTheFightGoesOn()
    {
        Assert.Equal("ti 14.7.1942 klo 03:10", MissionClock.Text(Dawn, TimeSpan.Zero, "fi"));
        Assert.Equal("ti 14.7.1942 klo 03:22", MissionClock.Text(Dawn, TimeSpan.FromSeconds(12 * 60 + 59), "fi"));
        Assert.Equal("Tue 14 July 1942, 03:10", MissionClock.Text(Dawn, TimeSpan.Zero, "en"));
    }

    [Fact]
    public void PastMidnight_ItIsTheNextDay()
    {
        var late = new DateTime(1942, 7, 14, 23, 50, 0);
        Assert.Equal("ke 15.7.1942 klo 00:05", MissionClock.Text(late, TimeSpan.FromMinutes(15), "fi"));
    }

    [Fact]
    public void AMissionWithNoStartTime_HasNoClock()
    {
        Assert.Null(MissionClock.Text(null, TimeSpan.FromMinutes(3), "fi"));
    }
}
