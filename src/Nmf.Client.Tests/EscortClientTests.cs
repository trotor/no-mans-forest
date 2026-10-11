using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>
/// Bringing a wounded man back (user 2026-10-11): with one of the men ordered on carrying him, another helps and the rest
/// go along at the carriers' pace, round them, not off ahead.
/// </summary>
public class EscortClientTests
{
    /// <summary>Four Finns at the bottom of a 100 m field, a fifth down 10 m up from them and already on a man's back.</summary>
    private static (GameSession Session, Unit Wounded, Unit Carrier) Field()
    {
        var points = new List<MapPoint>
        {
            new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(4350, 9050)), new("b2", "blue", new Vec2(4650, 9050)),
            new("b3", "blue", new Vec2(3750, 9050)), new("b4", "blue", new Vec2(4050, 8050)), new("r0", "red", new Vec2(9550, 550)),
        };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        var wounded = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 8050));
        wounded.Wound = WoundLevel.Incapacitated;
        var carrier = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 9050));
        session.Sim.Submit(session.PlayerSide, new CarryOrder(carrier.Id, wounded.Id));
        for (int i = 0; i < 400 && wounded.CarriedBy is null; i++)
            session.StepOnce();
        Assert.Equal(carrier.Id, wounded.CarriedBy);
        return (session, wounded, carrier);
    }

    private static List<Order> Orders(GameSession s, int before)
    {
        s.StepOnce();
        return s.Sim.OrderLog.Skip(before).Select(o => o.Order).ToList();
    }

    [Fact]
    public void MovingWithACarrier_TheNearestHelps_TheRestKeepHisPace()
    {
        var (s, wounded, carrier) = Field();
        int before = s.Sim.OrderLog.Count;
        s.OrderMove(new Vec2(5050, 2050), MoveMode.Auto);
        var orders = Orders(s, before);
        var help = Assert.Single(orders.OfType<CarryOrder>());
        Assert.Equal(wounded.Id, help.Wounded);
        Assert.NotEqual(carrier.Id, help.Unit);
        var moves = orders.OfType<MoveOrder>().ToList();
        Assert.Null(Assert.Single(moves, m => m.Unit == carrier.Id).PaceWith);
        Assert.DoesNotContain(moves, m => m.Unit == help.Unit); // the helper walks beside the carrier
        var escorts = moves.Where(m => m.Unit != carrier.Id).ToList();
        Assert.Equal(2, escorts.Count);
        Assert.All(escorts, m => Assert.Equal(carrier.Id, m.PaceWith));
    }

    [Fact]
    public void AHelperAlready_NoSecond()
    {
        var (s, wounded, carrier) = Field();
        Assert.True(s.OrderCarry(wounded)); // a helper now
        for (int i = 0; i < 100; i++) s.StepOnce();
        var helper = s.OwnUnits.Single(u => u.HelpingCarry == wounded.Id);
        int before = s.Sim.OrderLog.Count;
        s.OrderMove(new Vec2(5050, 2050), MoveMode.Auto);
        var orders = Orders(s, before);
        Assert.Empty(orders.OfType<CarryOrder>());
        Assert.DoesNotContain(orders.OfType<MoveOrder>(), m => m.Unit == helper.Id);
        Assert.All(orders.OfType<MoveOrder>().Where(m => m.Unit != carrier.Id), m => Assert.Equal(carrier.Id, m.PaceWith));
    }

    [Fact]
    public void NobodyCarrying_MovesAsBefore()
    {
        var points = new List<MapPoint> { new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(4350, 9050)), new("r0", "red", new Vec2(9550, 550)) };
        var s = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        int before = s.Sim.OrderLog.Count;
        s.OrderMove(new Vec2(5050, 2050), MoveMode.Auto);
        var orders = Orders(s, before);
        Assert.Equal(2, orders.OfType<MoveOrder>().Count());
        Assert.All(orders.OfType<MoveOrder>(), m => Assert.Null(m.PaceWith));
    }

    [Fact]
    public void OnTheWay_NobodyRunsOffAhead()
    {
        var (s, wounded, carrier) = Field();
        s.OrderMove(new Vec2(5050, 2050), MoveMode.Auto);
        var target = new Vec2(5050, 2050);
        for (int i = 0; i < 600; i++)
        {
            s.StepOnce();
            long carrierLeft = IntMath.Isqrt((target - carrier.Position).LengthSquared);
            foreach (var man in s.OwnUnits.Where(u => u != carrier && u != wounded && u.HelpingCarry is null))
                Assert.True(IntMath.Isqrt((target - man.Position).LengthSquared) + Movement.EscortLeadCm + 600 >= carrierLeft,
                    $"tick {s.Sim.Tick}: {man.Id} ran ahead of the carrier");
        }
    }
}
