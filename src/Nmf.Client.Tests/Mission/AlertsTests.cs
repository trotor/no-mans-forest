using Nmf.Client.Mission;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Mission;

public class AlertsTests
{
    private static readonly CounterattackStarted Shout = new(100, Side.Red, new UnitId(5), new Vec2(10_000, 10_000), new UnitId(1));

    [Fact]
    public void TheShout_IsHeardWithin300Metres()
    {
        Assert.Equal("Kuuluu huuto: \"Urraa!\" — vihollinen hyökkää!", Alerts.Counterattack(Shout, [new Vec2(10_000, 39_000)], "fi"));
        Assert.Equal("A shout: \"Urraa!\" — the enemy is attacking!", Alerts.Counterattack(Shout, [new Vec2(10_000, 39_000)], "en"));
        Assert.Null(Alerts.Counterattack(Shout, [new Vec2(10_000, 41_000)], "fi"));
        Assert.Null(Alerts.Counterattack(Shout, [], "fi"));
    }
}
