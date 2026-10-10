using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Spec 2026-10-10-mortar-design §4: the mortar from the player's side.</summary>
public class MortarClientTests
{
    private static (GameSession Session, Unit Radio) Field(bool mortar = true)
    {
        var points = new List<MapPoint> { new("b0", "blue", new Vec2(5050, 9050)), new("b1", "blue", new Vec2(5350, 9050)), new("r0", "red", new Vec2(5050, 1050)) };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        var radio = session.OwnUnits.Last();
        if (mortar)
            session.Sim.AddMortarSupport(new MortarSupport(Side.Blue, radio.Id, 8, 1200, 2400));
        return (session, radio);
    }

    [Fact]
    public void Menu_HasTheMortarRow_ItCallsThroughTheRadioman()
    {
        var (s, radio) = Field();
        s.Selection.Add(s.OwnUnits.First().Id); // not the radioman: the call still goes through him
        var row = Assert.Single(ContextMenu.Items(s, new Vec2(5050, 3050), GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FireMission);
        Assert.Equal("Kranaatinheitintuli tänne (HE 8)", row.Text);
        int before = s.Sim.OrderLog.Count;
        ContextMenu.Run(s, row);
        s.StepOnce();
        var order = Assert.IsType<FireMissionOrder>(Assert.Single(s.Sim.OrderLog.Skip(before)).Order);
        Assert.Equal(radio.Id, order.Unit);
        Assert.Equal(new Vec2(5050, 3050), order.Target);
    }

    [Fact]
    public void Menu_NoRow_WithoutAMortar_OrWithTheRadiomanDown()
    {
        var (bare, _) = Field(mortar: false);
        Assert.DoesNotContain(ContextMenu.Items(bare, new Vec2(5050, 3050), GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FireMission);
        var (s, radio) = Field();
        radio.Wound = WoundLevel.Dead;
        Assert.DoesNotContain(ContextMenu.Items(s, new Vec2(5050, 3050), GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FireMission);
    }

    [Fact]
    public void Status_FollowsTheMission()
    {
        var (s, radio) = Field();
        Assert.Equal("Kranaatinheitin HE 8", MortarText.Status(s, "fi"));
        s.OrderFireMission(new Vec2(5050, 3050));
        s.StepOnce();
        Assert.Equal("Kranaatinheitin HE 8 · tilaus…", MortarText.Status(s, "fi"));
        for (int i = 0; i < MortarSupport.CallTicks + 5; i++) s.StepOnce();
        Assert.StartsWith("Kranaatinheitin HE 8 · iskut ", MortarText.Status(s, "fi"));
        radio.Wound = WoundLevel.Dead;
        Assert.Contains("ei yhteyttä", MortarText.Status(s, "fi"));
        Assert.Null(MortarText.Status(Field(mortar: false).Session, "fi"));
    }

    [Fact]
    public void Alerts_SayWhatTheMortarDoes()
    {
        Assert.Equal("Tuli tilattu: ensimmäiset iskut 1–2 minuutin päästä", Alerts.Mortar(new FireMissionCalled(0, Side.Blue), "fi"));
        Assert.Equal("Yhteys ei toimi: tilaus ei mennyt perille", Alerts.Mortar(new FireMissionFailed(0, Side.Blue), "fi"));
        Assert.Equal("Tähystyslaukaus tulossa", Alerts.Mortar(new FireMissionPhaseChanged(0, Side.Blue, MortarPhase.Ranging), "fi"));
        Assert.Equal("Tehokas tuli", Alerts.Mortar(new FireMissionPhaseChanged(0, Side.Blue, MortarPhase.Effect), "fi"));
        Assert.Equal("Kranaatit loppuivat", Alerts.Mortar(new FireMissionEnded(0, Side.Blue, true), "fi"));
        Assert.Equal("Fire over", Alerts.Mortar(new FireMissionEnded(0, Side.Blue, false), "en"));
        Assert.Equal("Radiomies kaatui: kranaatinheitintuki menetetty", Alerts.Mortar(new FireMissionEnded(0, Side.Blue, false, RadioLost: true), "fi"));
        Assert.Null(Alerts.Mortar(new FireMissionCalled(0, Side.Red), "fi")); // not ours
    }
}
