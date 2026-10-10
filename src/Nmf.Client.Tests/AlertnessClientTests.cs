using Nmf.Client;
using Nmf.Client.Effects;
using Nmf.Client.Mission;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Spec 2026-10-10-alertness-design §7: the enemy's alertness, the alarm and the telephone wire from the player's side.</summary>
public class AlertnessClientTests
{
    /// <summary>Two Finns at the bottom of a 100 m field, a Soviet at the top with his phone; the wire runs across the middle.</summary>
    private static (GameSession Session, Unit Red) Field(bool alertness = true)
    {
        var points = new List<MapPoint> { new("b0", "blue", new Vec2(5050, 9050)), new("b1", "blue", new Vec2(5350, 9050)), new("r0", "red", new Vec2(5050, 1050)) };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        if (alertness)
        {
            session.Sim.EnableAlertness(Side.Red);
            session.Sim.AddFieldPhone(new FieldPhone("hq", Side.Red, new Vec2(5050, 1250), [new Vec2(5050, 1250), new Vec2(5050, 5050), new Vec2(9950, 5050)]));
        }
        return (session, session.Sim.Units.Single(u => u.Side == Side.Red));
    }

    [Fact]
    public void Mark_OnlyOnAShownEnemy_WhenWaryOrAlerted()
    {
        var (s, red) = Field();
        red.Alertness = Alertness.Suspicious;
        Assert.Equal("?", s.AlertMark(red, revealAll: true));
        red.Alertness = Alertness.Alerted;
        Assert.Equal("!", s.AlertMark(red, revealAll: true));
        Assert.Null(s.AlertMark(red, revealAll: false)); // not seen: nothing shows
        red.Alertness = Alertness.Combat;
        Assert.Null(s.AlertMark(red, revealAll: true));
        Assert.Null(s.AlertMark(s.OwnUnits.First(), revealAll: true));
        var (plain, plainRed) = Field(alertness: false);
        Assert.Null(plain.AlertMark(plainRed, revealAll: true));
    }

    [Fact]
    public void Tip_SaysHowAwakeHeLooks()
    {
        var (s, red) = Field();
        Assert.Contains("Rauhallinen", EnemyInfo.Describe(red, s.OwnUnits, "fi", sim: s.Sim));
        red.Alertness = Alertness.Alerted;
        Assert.Contains("Alerted", EnemyInfo.Describe(red, s.OwnUnits, "en", sim: s.Sim));
        red.Alertness = Alertness.Combat;
        Assert.DoesNotContain(EnemyInfo.Describe(red, s.OwnUnits, "fi", sim: s.Sim), l => l is "Rauhallinen" or "Epäluuloinen" or "Hälytetty");
    }

    [Fact]
    public void Menu_CutTheWire_NearIt_ByTheNearestCommandedMan()
    {
        var (s, _) = Field();
        var nearWire = new Vec2(7000, 5150);
        var row = Assert.Single(ContextMenu.Items(s, nearWire, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.CutLine);
        Assert.Equal("Katkaise puhelinjohto", row.Text);
        Assert.DoesNotContain(ContextMenu.Items(s, new Vec2(7000, 7000), GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.CutLine);
        int before = s.Sim.OrderLog.Count;
        ContextMenu.Run(s, row);
        s.StepOnce();
        var cut = Assert.IsType<Nmf.Sim.Orders.CutLineOrder>(Assert.Single(s.Sim.OrderLog.Skip(before)).Order);
        Assert.Equal("hq", cut.Phone);
        Assert.Equal(s.OwnUnits.OrderBy(u => (u.Position - nearWire).LengthSquared).First().Id, cut.Unit);
    }

    [Fact]
    public void Alerts_TheAlarm_TheFlare_TheWire()
    {
        Vec2[] own = [new Vec2(0, 0)];
        Assert.Equal("Vihollinen huutaa hälytystä",
            Alerts.Alarm(new AlarmRaised(0, Side.Red, new UnitId(9), new Vec2(10_000, 0), AlarmKind.Shout), own, Side.Blue, "fi"));
        Assert.Null(Alerts.Alarm(new AlarmRaised(0, Side.Red, new UnitId(9), new Vec2(30_000, 0), AlarmKind.Shout), own, Side.Blue, "fi")); // too far to hear
        Assert.Equal("Valoraketti!", Alerts.Alarm(new AlarmRaised(0, Side.Red, new UnitId(9), new Vec2(90_000, 0), AlarmKind.Flare), own, Side.Blue, "fi"));
        Assert.Equal("Puhelinjohto katkaistu", Alerts.Alarm(new LineCut(0, "hq", new UnitId(1), new Vec2(0, 0)), own, Side.Blue, "fi"));
        Assert.Equal("Vihollinen soitti puhelimella", Alerts.Alarm(new PhoneCalled(0, "hq", Side.Red, new Vec2(3_000, 0)), own, Side.Blue, "fi"));
        Assert.Null(Alerts.Alarm(new PhoneCalled(0, "hq", Side.Red, new Vec2(30_000, 0)), own, Side.Blue, "fi"));
        Assert.Equal("Puhelu ei mennyt läpi", Alerts.Alarm(new PhoneCallFailed(0, "hq", Side.Red, new Vec2(3_000, 0)), own, Side.Blue, "fi"));
        Assert.Null(Alerts.Alarm(new AlarmRaised(0, Side.Blue, new UnitId(1), new Vec2(0, 0), AlarmKind.Shout), own, Side.Blue, "fi")); // ours
    }

    [Fact]
    public void Signals_AnAlarmShoutHeard_IsMarkedRoughly_AFlareShows()
    {
        var fx = new CombatEffects();
        static SignalUnit? Units(UnitId id) => id.Value == 9 ? new SignalUnit(new Vec2(20_340, 10_720), Own: false, Shown: false) : null;
        fx.AddSignals([new AlarmRaised(0, Side.Red, new UnitId(9), new Vec2(10_340, 720), AlarmKind.Shout)], Units, [new Vec2(0, 0)]);
        Assert.Contains(fx.Signals, s => s.Kind == SignalKind.Shout && s.At == new Vec2(10_500, 500));
        fx.Add([new AlarmRaised(0, Side.Red, new UnitId(9), new Vec2(40_000, 0), AlarmKind.Flare)], _ => false);
        Assert.Single(fx.Flares);
        Assert.Equal(new Vec2(40_500, 500), fx.Flares[0].From); // where it went up, roughly: not the leader's exact spot
    }

    [Fact]
    public void CutOrder_ClearsHisQueue_AndSkipsAPinnedMan()
    {
        var (s, _) = Field();
        var nearWire = new Vec2(7000, 5150);
        var men = s.OwnUnits.OrderBy(u => (u.Position - nearWire).LengthSquared).ToList();
        men[0].MoraleState = MoraleState.Pinned;
        s.Queue.Add(s, [men[1].Id], new QueuedOrder(QueuedKind.Move, new Vec2(1000, 9000)));
        int before = s.Sim.OrderLog.Count;
        Assert.True(s.OrderCutLine(nearWire));
        s.StepOnce();
        var cut = Assert.IsType<Nmf.Sim.Orders.CutLineOrder>(s.Sim.OrderLog.Skip(before).Select(o => o.Order).OfType<Nmf.Sim.Orders.CutLineOrder>().Single());
        Assert.Equal(men[1].Id, cut.Unit);
        Assert.Null(s.Queue.LastOf([men[1].Id]));
    }
}
