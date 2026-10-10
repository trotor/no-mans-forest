using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Spec 2026-10-10-carrying-design §3: carrying from the player's side.</summary>
public class CarryClientTests
{
    /// <summary>Two Finns at the bottom of a 100 m field, a third down (cannot move) 10 m up from them.</summary>
    private static (GameSession Session, Unit Wounded) Field()
    {
        var points = new List<MapPoint>
        {
            new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(4350, 9050)), new("b2", "blue", new Vec2(4050, 8050)),
            new("r0", "red", new Vec2(9550, 550)),
        };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        var wounded = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 8050));
        wounded.Wound = WoundLevel.Incapacitated;
        return (session, wounded);
    }

    private static List<Order> Orders(GameSession s, int before)
    {
        s.StepOnce();
        return s.Sim.OrderLog.Skip(before).Select(o => o.Order).ToList();
    }

    [Fact]
    public void DoubleOnOwnWounded_TheNearestCarries_TheNextHelps()
    {
        var (s, wounded) = Field();
        int before = s.Sim.OrderLog.Count;
        Assert.Equal(ClickResult.CarryOrdered, s.HandleLeftClick(wounded.Position, doubleClick: true, shift: false, alt: false).Result);
        var first = Assert.IsType<CarryOrder>(Assert.Single(Orders(s, before)));
        Assert.Equal(wounded.Id, first.Wounded);
        for (int i = 0; i < 200; i++) s.StepOnce();
        Assert.NotNull(wounded.CarriedBy);
        before = s.Sim.OrderLog.Count;
        // On the carrier's back a click picks the carrier; a helper is asked for with the order itself (the menu's "Kanna").
        Assert.True(s.OrderCarry(wounded));
        var second = Assert.IsType<CarryOrder>(Assert.Single(Orders(s, before)));
        Assert.NotEqual(first.Unit, second.Unit); // the other man helps
    }

    [Fact]
    public void DoubleOnADeadMan_StillSearches()
    {
        var (s, wounded) = Field();
        wounded.Wound = WoundLevel.Dead;
        Assert.Equal(ClickResult.LootOrdered, s.HandleLeftClick(wounded.Position, doubleClick: true, shift: false, alt: false).Result);
    }

    [Fact]
    public void Preview_SaysCarry()
    {
        var (s, wounded) = Field();
        var preview = ClickPreview.Describe(s, wounded.Position, default, GameSession.ClickRadiusCm, "fi");
        Assert.Equal("kanna", preview.Double);
        Assert.Equal(ClickResult.CarryOrdered, preview.DoubleResult);
    }

    [Fact]
    public void Menu_CarryRow_AndDropForTheCarrier()
    {
        var (s, wounded) = Field();
        var row = Assert.Single(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.Carry);
        Assert.Equal("Kanna", row.Text);
        ContextMenu.Run(s, row);
        for (int i = 0; i < 200; i++) s.StepOnce();
        var carrier = s.Sim.FindUnit(wounded.CarriedBy!.Value)!;
        var drop = Assert.Single(ContextMenu.Items(s, carrier.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.Drop);
        Assert.Equal("Laske maahan", drop.Text);
        ContextMenu.Run(s, drop);
        s.StepOnce();
        Assert.Null(wounded.CarriedBy);
    }

    [Fact]
    public void CardLines()
    {
        var (s, wounded) = Field();
        wounded.Name = "Sotm. Saarinen";
        s.HandleLeftClick(wounded.Position, doubleClick: true, shift: false, alt: false);
        for (int i = 0; i < 200; i++) s.StepOnce();
        var carrier = s.Sim.FindUnit(wounded.CarriedBy!.Value)!;
        Assert.Equal("Kantaa: Saarinen", CarryText.For(s.Sim, carrier, "fi"));
        Assert.Equal("Kannetaan", CarryText.For(s.Sim, wounded, "fi"));
        Assert.Equal("Carrying Saarinen", CarryText.For(s.Sim, carrier, "en"));
        var other = s.OwnUnits.First(u => u != carrier && u != wounded);
        Assert.Null(CarryText.For(s.Sim, other, "fi"));
    }
}
