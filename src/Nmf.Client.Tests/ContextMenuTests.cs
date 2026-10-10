using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class ContextMenuTests
{
    private static readonly TacticDef FireAndMovement = new("fire_and_movement", "Fire and movement", "Tuli ja liike", 3_500, 10_000,
        new TacticManeuver(60, 2_500, 4_500), new TacticAssault(250, 400, 1_200), 30, 200);
    private static readonly TacticDef LongFire = new("long_fire", "Long fire", "Pitkä tuli", 5_000, 15_000, null, null, 30, 200);

    private static (GameSession Session, Unit Red) Field()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(9050 + i * 300, 18_050)))
            .Append(new MapPoint("r1", "red", new Vec2(10_050, 6_050))).ToList();
        var session = new GameSession(SkirmishScenario.Create(new GridMap(200, 200, ["none"], new MapFeatures([], points, [])), 1))
        {
            Tactics = [FireAndMovement, LongFire],
        };
        var red = session.Sim.Units.Single(u => u.Side == Side.Red);
        var c = session.Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
        c.Level = ContactLevel.Visible;
        c.Position = red.Position;
        return (session, red);
    }

    private static List<string> Texts(GameSession s, Vec2 at) =>
        ContextMenu.Items(s, at, GameSession.ClickRadiusCm, "fi").Where(i => !i.Queue).Select(i => i.Text).ToList();

    [Fact]
    public void Ground_Rows()
    {
        var (s, _) = Field();
        Assert.Equal(["Mene tänne", "Juokse tänne", "Hiivi tänne", "Ryömi tänne", "Hyökkää eteenpäin tänne", "Aluetuli tänne", "Ota asema (katse tänne)"],
            Texts(s, new Vec2(10_050, 15_050)));
    }

    [Fact]
    public void Enemy_Rows_ListTheTactics()
    {
        var (s, red) = Field();
        Assert.Equal(["Ammu", "Suunnittele hyökkäys…", "Hyökkää (paras tapa)", "Hyökkää: Tuli ja liike", "Hyökkää: Pitkä tuli", "Suora rynnäkkö", "Aluetuli"], Texts(s, red.Position));
    }

    [Fact]
    public void OwnMan_Rows()
    {
        var (s, _) = Field();
        Assert.Equal(["Valitse", "Koko ryhmä", "Tyhjennä valinta"], Texts(s, s.OwnUnits.First().Position));
    }

    [Fact]
    public void EveryOrderRow_IsTheSameOrderAsItsClick()
    {
        var (s, red) = Field();
        var ground = new Vec2(10_050, 15_050);
        var g = s.Resolve(ground, GameSession.ClickRadiusCm, false);
        var e = s.Resolve(red.Position, GameSession.ClickRadiusCm, false);
        var rows = ContextMenu.Items(s, ground, GameSession.ClickRadiusCm, "fi").Where(i => !i.Queue).ToList();
        Assert.Equal(s.QueuedFor(g, ground, false, false, false, false), rows[0].Order);
        Assert.Equal(s.QueuedFor(g, ground, true, false, false, false), rows[1].Order);
        Assert.Equal(s.QueuedFor(g, ground, false, true, false, false), rows[2].Order);
        Assert.Equal(s.QueuedFor(g, ground, true, true, false, false), rows[3].Order);
        Assert.Equal(s.QueuedFor(g, ground, false, false, false, true), rows[4].Order);
        Assert.Equal(s.QueuedFor(g, ground, false, false, true, false), rows[5].Order);
        var enemyRows = ContextMenu.Items(s, red.Position, GameSession.ClickRadiusCm, "fi").Where(i => !i.Queue).ToDictionary(i => i.Text);
        Assert.Equal(s.QueuedFor(e, red.Position, false, false, false, false), enemyRows["Ammu"].Order);
        Assert.Equal(s.QueuedFor(e, red.Position, true, false, false, false), enemyRows["Hyökkää (paras tapa)"].Order);
        Assert.Equal(s.QueuedFor(e, red.Position, true, true, false, false), enemyRows["Hyökkää: Pitkä tuli"].Order);
        Assert.Equal(s.QueuedFor(e, red.Position, true, false, true, false), enemyRows["Suora rynnäkkö"].Order);
    }

    [Fact]
    public void QueueRows_Queue()
    {
        var (s, _) = Field();
        var ground = new Vec2(10_050, 15_050);
        var items = ContextMenu.Items(s, ground, GameSession.ClickRadiusCm, "fi");
        var queued = items.First(i => i.Queue && i.Order!.Kind == QueuedKind.Move && i.Order.Mode == Nmf.Sim.Units.MoveMode.Auto);
        ContextMenu.Run(s, queued);
        ContextMenu.Run(s, queued with { Order = queued.Order! with { Point = new Vec2(10_050, 12_050) } });
        Assert.Equal(2, Assert.Single(s.Queue.Groups).Items.Count);
    }

    [Fact]
    public void PlanAttackRow_OpensThePreview()
    {
        var (s, red) = Field();
        var row = ContextMenu.Items(s, red.Position, GameSession.ClickRadiusCm, "fi").Single(i => i.Action == MenuAction.PlanAttack);
        Assert.Equal("Suunnittele hyökkäys…", row.Text);
        Assert.Equal(red.Id, row.Unit);
        int before = s.Sim.OrderLog.Count;
        ContextMenu.Run(s, row);
        Assert.Equal(red.Id, s.Preview!.Target);
        s.StepOnce();
        Assert.Equal(before, s.Sim.OrderLog.Count);
    }
}
