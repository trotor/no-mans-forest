using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Spec 2026-10-10-winter-design §3–§5: winter and light from the player's side.</summary>
public class WinterClientTests
{
    /// <summary>Two Finns at the bottom of a 100 m field, a Soviet at the top; winter.</summary>
    private static GameSession Field(bool winter = true)
    {
        var points = new List<MapPoint> { new("b0", "blue", new Vec2(5050, 9050)), new("b1", "blue", new Vec2(5350, 9050)), new("r0", "red", new Vec2(5050, 1050)) };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        if (winter)
            session.Sim.SetWinter(40);
        return session;
    }

    [Fact]
    public void Tracks_OwnAlways_TheEnemysOnlyWhereOurMenSee()
    {
        var s = Field();
        var red = s.Sim.Units.Single(u => u.Side == Side.Red);
        var own = s.OwnUnits.First();
        s.Sim.Submit(Side.Blue, new MoveOrder(own.Id, new Vec2(5050, 8050), MoveMode.Walk));
        s.Sim.Submit(Side.Red, new MoveOrder(red.Id, new Vec2(5050, 2050), MoveMode.Walk));
        for (int i = 0; i < 200; i++)
            s.StepOnce();
        var shown = TrackMarks.Visible(s).ToList();
        Assert.Contains(shown, t => t.Side == Side.Blue);
        bool redSeen = s.Fog.IsVisible(new Vec2(5050, 1550));
        Assert.Equal(redSeen, shown.Any(t => t.Side == Side.Red));
        Assert.All(shown.Where(t => t.Side == Side.Red), t => Assert.True(s.Fog.IsVisible(t.At)));
        Assert.Empty(TrackMarks.Visible(Field(winter: false)));
    }

    [Fact]
    public void CardSays_Skis_AndAhkio_InWinter()
    {
        var s = Field();
        var man = s.OwnUnits.First();
        Assert.Null(WinterText.Status(s.Sim, man, "fi"));
        man.OnSkis = true;
        Assert.Equal("Sukset", WinterText.Status(s.Sim, man, "fi"));
        man.HasAhkio = true;
        Assert.Equal("Sukset · ahkio", WinterText.Status(s.Sim, man, "fi"));
        Assert.Equal("On skis · ahkio", WinterText.Status(s.Sim, man, "en"));
        Assert.Null(WinterText.Status(Field(winter: false).Sim, man, "fi"));
    }

    [Fact]
    public void Darkness_ByTheLight()
    {
        Assert.Equal(0f, LightOverlay.Darkness(100));
        Assert.True(LightOverlay.Darkness(25) > LightOverlay.Darkness(55));
        Assert.InRange(LightOverlay.Darkness(25), 0.4f, 0.75f); // dark, but the men can still be made out
    }

    [Fact]
    public void AnEnemyCrossingOurTrail_Unseen_LeavesItWhole()
    {
        var s = Field();
        var own = s.OwnUnits.First();
        long before = s.Sim.Tick;
        s.Sim.LeaveTrackForTest(new Vec2(5050, 5050), Side.Blue, 10);
        Assert.Contains(TrackMarks.Visible(s), t => t.At == new Vec2(5050, 5050) && t.Side == Side.Blue);
        s.Sim.LeaveTrackForTest(new Vec2(5050, 5050), Side.Red, 20); // the Soviet crosses it where our men may not see
        Assert.Contains(TrackMarks.Visible(s), t => t.At == new Vec2(5050, 5050) && t.Side == Side.Blue);
        Assert.Equal(s.Fog.IsVisible(new Vec2(5050, 5050)), TrackMarks.Visible(s).Any(t => t.At == new Vec2(5050, 5050) && t.Side == Side.Red));
    }

    [Fact]
    public void Tracks_OnlyInTheAreaAskedFor()
    {
        var s = Field();
        s.Sim.LeaveTrackForTest(new Vec2(1050, 1050), Side.Blue, 1);
        s.Sim.LeaveTrackForTest(new Vec2(8050, 8050), Side.Blue, 1);
        var near = TrackMarks.Visible(s, new CellCoord(0, 0), new CellCoord(20, 20)).ToList();
        Assert.Equal([new Vec2(1050, 1050)], near.Select(t => t.At));
        Assert.Equal(2, TrackMarks.Visible(s).Count());
    }
}
