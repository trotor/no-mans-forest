using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

/// <summary>Spec 2026-10-10-alertness-design §4: the field telephone, and cutting its line.</summary>
public class FieldPhoneTests
{
    private const int Second = SimConstants.TicksPerSecond;

    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    /// <summary>
    /// A Soviet post at (100, 100) m, its phone 2 m off, a second man 10 m off; the line runs east to the map edge.
    /// A Finn lies by the line at (250, 101) m, out of their sight behind a thicket.
    /// </summary>
    private static (Simulation Sim, FieldPhone Phone, Unit Sentry, Unit Second, Unit Finn) Post()
    {
        var sim = new Simulation(new GridMap(400, 400, ["none"]), 1);
        for (int y = 0; y < 400; y++)
            for (int x = 180; x < 190; x++)
            {
                ref var cell = ref sim.Map[new CellCoord(x, y)];
                cell.ObstacleHeightCm = 1_500;
                cell.ConcealmentPerM = 255;
            }
        var sentry = sim.SpawnUnit(Side.Red, M(100, 100), 8, TestWeapons.Rifle());
        var second = sim.SpawnUnit(Side.Red, M(110, 100), 8, TestWeapons.Rifle());
        var finn = sim.SpawnUnit(Side.Blue, M(250, 101), 8, TestWeapons.Rifle());
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        sim.EnableAlertness(Side.Red);
        var phone = sim.AddFieldPhone(new FieldPhone("hq", Side.Red, M(102, 100), [M(102, 100), M(200, 100), M(399, 100)]));
        return (sim, phone, sentry, second, finn);
    }

    private static List<SimEvent> Run(Simulation sim, int ticks, Func<bool>? until = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
            events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void AlertedPost_Calls_InFifteenSeconds()
    {
        var (sim, phone, sentry, _, _) = Post();
        Assert.Empty(Run(sim, 30 * Second).OfType<PhoneCalled>()); // calm: nobody calls
        sim.Submit(Side.Red, new AlertOrder(sentry.Id, Alertness.Alerted));
        long alarmed = sim.Tick;
        var called = Assert.Single(Run(sim, 40 * Second).OfType<PhoneCalled>());
        Assert.Equal("hq", called.Phone);
        Assert.True(phone.Called);
        Assert.InRange(called.Tick - alarmed, 15 * Second, 20 * Second);
    }

    [Fact]
    public void CutLine_TheCallFails_AndIsNotTriedAgain()
    {
        var (sim, phone, sentry, _, finn) = Post();
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
        var cut = Assert.Single(Run(sim, 20 * Second).OfType<LineCut>());
        Assert.Equal(finn.Id, cut.By);
        Assert.NotNull(phone.CutAt);
        sim.Submit(Side.Red, new AlertOrder(sentry.Id, Alertness.Alerted));
        var events = Run(sim, 90 * Second);
        Assert.Single(events.OfType<PhoneCallFailed>());
        Assert.Empty(events.OfType<PhoneCalled>());
        Assert.True(phone.Failed);
        Assert.False(phone.Called);
    }

    [Fact]
    public void CutDuringTheCall_Fails_NoRetry()
    {
        var (sim, phone, sentry, _, finn) = Post();
        sim.Submit(Side.Red, new AlertOrder(sentry.Id, Alertness.Alerted));
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
        var events = Run(sim, 90 * Second);
        Assert.Single(events.OfType<LineCut>());
        Assert.Single(events.OfType<PhoneCallFailed>());
        Assert.Empty(events.OfType<PhoneCalled>());
    }

    [Fact]
    public void Cut_TakesTenSeconds()
    {
        var (sim, _, _, _, finn) = Post();
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
        var cut = Assert.Single(Run(sim, 20 * Second).OfType<LineCut>());
        Assert.InRange(cut.Tick, 10 * Second, 12 * Second);
    }

    [Fact]
    public void Cut_Interrupted_ByAnOrder_OrGoingDown()
    {
        var (sim, phone, _, _, finn) = Post();
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
        Run(sim, 5 * Second);
        sim.Submit(Side.Blue, new MoveOrder(finn.Id, M(250, 130)));
        Assert.Empty(Run(sim, 30 * Second).OfType<LineCut>());
        Assert.Null(phone.CutAt);
        Assert.Null(phone.Cutter);

        var (sim2, phone2, _, _, finn2) = Post();
        sim2.Submit(Side.Blue, new CutLineOrder(finn2.Id, "hq", M(250, 101)));
        Run(sim2, 5 * Second);
        finn2.Wound = WoundLevel.Incapacitated;
        Assert.Empty(Run(sim2, 30 * Second).OfType<LineCut>());
        Assert.Null(phone2.CutAt);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("prone")]
    [InlineData("fire")]
    public void Cut_Interrupted_ByAnOrderThatDoesNotMoveHim(string order)
    {
        var (sim, phone, sentry, _, finn) = Post();
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
        Run(sim, 5 * Second);
        Assert.True(phone.CutDoneTick > 0, "he should be cutting by now");
        sim.Submit(Side.Blue, order switch
        {
            "stop" => new StopOrder(finn.Id),
            "prone" => new SetStanceOrder(finn.Id, Stance.Prone),
            _ => new AreaFireOrder(finn.Id, M(250, 200)),
        });
        Assert.Empty(Run(sim, 30 * Second).OfType<LineCut>());
        Assert.Null(phone.CutAt);
    }

    [Fact]
    public void CallerDown_TheNextMan_Calls()
    {
        var (sim, phone, sentry, second, _) = Post();
        sim.Submit(Side.Red, new AlertOrder(sentry.Id, Alertness.Alerted));
        sim.Submit(Side.Red, new AlertOrder(second.Id, Alertness.Alerted));
        Run(sim, 5 * Second);
        Assert.Equal(sentry.Id, phone.Caller);
        sentry.Wound = WoundLevel.Dead;
        Assert.Single(Run(sim, 60 * Second).OfType<PhoneCalled>());
        Assert.True(phone.Called);
    }

    [Fact]
    public void Cut_Rejected_AwayFromTheLine_OrOurOwn_OrNoSuchLine()
    {
        var (sim, _, sentry, _, finn) = Post();
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 110))); // 9 m off the line
        sim.Submit(Side.Red, new CutLineOrder(sentry.Id, "hq", M(150, 100)));
        sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "nope", M(250, 101)));
        Assert.Equal(3, sim.Step().OfType<OrderRejected>().Count());
    }

    [Fact]
    public void SameSeed_SameCall()
    {
        static ulong Hash()
        {
            var (sim, _, sentry, _, finn) = Post();
            sim.Submit(Side.Red, new AlertOrder(sentry.Id, Alertness.Alerted));
            sim.Submit(Side.Blue, new CutLineOrder(finn.Id, "hq", M(250, 101)));
            Run(sim, 8 * Second);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
    }
}
