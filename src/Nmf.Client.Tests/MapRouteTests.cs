using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Routes clicked on the paper map (M): select men, send them, queue the legs, and see the routes drawn.</summary>
public class MapRouteTests
{
    /// <summary>Two Finns at the bottom of a 100 m field, an enemy far off in the corner.</summary>
    private static (GameSession Session, Unit A, Unit B) Field()
    {
        var points = new List<MapPoint>
        {
            new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(4350, 9050)), new("r0", "red", new Vec2(9550, 550)),
        };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        var a = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 9050));
        var b = session.OwnUnits.Single(u => u.Position == new Vec2(4350, 9050));
        return (session, a, b);
    }

    private const int Reach = 800; // a few map pixels

    [Fact]
    public void NobodySelected_AClickOnTheGround_IsLeftToLooking()
    {
        var (s, _, _) = Field();
        Assert.Equal(ClickResult.None, s.MapClick(new Vec2(5000, 5000), Reach, false, false, false).Result);
        Assert.Empty(s.Sim.OrderLog);
    }

    [Fact]
    public void ClickOnAMan_SelectsHim_ShiftAdds()
    {
        var (s, a, b) = Field();
        Assert.Equal(ClickResult.Selected, s.MapClick(a.Position + new Vec2(0, -300), Reach, false, false, false).Result);
        Assert.Equal([a.Id], s.Selection.Ids);
        s.MapClick(b.Position, Reach, false, shift: true, alt: false);
        Assert.Equal(2, s.Selection.Ids.Count);
    }

    [Fact]
    public void WithMenSelected_AClick_SendsThemThere_EvenOverAnEnemy()
    {
        var (s, a, _) = Field();
        s.Selection.Add(a.Id);
        var enemy = s.Sim.Units.Single(u => u.Side == Side.Red);
        int before = s.Sim.OrderLog.Count;
        Assert.Equal(ClickResult.MoveOrdered, s.MapClick(enemy.Position, Reach, false, false, false).Result); // on the map a click is a route, never fire
        s.StepOnce();
        var move = Assert.IsType<MoveOrder>(Assert.Single(s.Sim.OrderLog.Skip(before)).Order);
        Assert.Equal(enemy.Position, move.Target);
        Assert.Equal(MoveMode.Auto, move.Mode);
    }

    [Theory]
    [InlineData(false, true, MoveMode.Sneak)]
    [InlineData(true, false, MoveMode.Run)]
    public void AltSneaks_DoubleRuns(bool doubleClick, bool alt, MoveMode mode)
    {
        var (s, a, _) = Field();
        s.Selection.Add(a.Id);
        int before = s.Sim.OrderLog.Count;
        s.MapClick(new Vec2(5050, 3050), Reach, doubleClick, false, alt);
        s.StepOnce();
        Assert.Equal(mode, Assert.IsType<MoveOrder>(Assert.Single(s.Sim.OrderLog.Skip(before)).Order).Mode);
    }

    [Fact]
    public void ShiftClicks_QueueTheLegs_AndTheRouteShowsThemAll()
    {
        var (s, a, _) = Field();
        s.Selection.Add(a.Id);
        // As on the ground view, Shift-clicks make the queue: the first leg starts at once, the others wait their turn.
        Assert.Equal(ClickResult.Queued, s.MapClick(new Vec2(4050, 6050), Reach, false, true, false).Result);
        Assert.Equal(ClickResult.Queued, s.MapClick(new Vec2(7050, 6050), Reach, false, true, false).Result);
        Assert.Equal(ClickResult.Queued, s.MapClick(new Vec2(7050, 3050), Reach, false, true, false).Result);
        s.StepOnce();
        s.StepOnce();
        var route = Assert.Single(MapRoutes.For(s));
        Assert.Equal([a.Id], route.Men);
        Assert.Equal(a.Position, route.Points[0]);
        Assert.Equal(new Vec2(4050, 6050), route.Points[^3]);
        Assert.Equal([new Vec2(7050, 6050), new Vec2(7050, 3050)], route.Points.Skip(route.Points.Count - 2));
        Assert.Equal(route.Points.Count - 2, route.QueuedFrom);
    }

    [Fact]
    public void NoMove_NoRoute_TheFallenNone()
    {
        var (s, a, b) = Field();
        Assert.Empty(MapRoutes.For(s));
        s.Selection.Add(a.Id);
        s.MapClick(new Vec2(4050, 6050), Reach, false, false, false);
        s.StepOnce();
        Assert.Single(MapRoutes.For(s));
        a.Wound = WoundLevel.Dead;
        Assert.Empty(MapRoutes.For(s));
        Assert.NotNull(b);
    }

    [Fact]
    public void WhilePaused_TheOrderNotYetTaken_ShowsStraightAway()
    {
        // The map pauses the game: a route clicked there is drawn before the men take the order.
        var (s, a, _) = Field();
        s.Selection.Add(a.Id);
        s.MapClick(new Vec2(4050, 6050), Reach, false, false, false);
        var route = Assert.Single(MapRoutes.For(s));
        Assert.Equal([a.Position, new Vec2(4050, 6050)], route.Points);
    }
}
