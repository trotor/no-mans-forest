using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class OrderQueueTests
{
    private static readonly TacticDef FireAndMovement = new("fire_and_movement", "Fire and movement", "Tuli ja liike", 3_500, 10_000,
        new TacticManeuver(60, 2_500, 4_500), new TacticAssault(250, 400, 1_200), 30, 200);

    /// <summary>Six Finns at (90..105, 180) m on a 200 m field, a Soviet at (100, 60) m they see, woods on his flanks.</summary>
    private static (GameSession Session, Unit Red) Field()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(9050 + i * 300, 18_050)))
            .Append(new MapPoint("r1", "red", new Vec2(10_050, 6_050))).ToList();
        var map = new GridMap(200, 200, ["none"], new MapFeatures([], points, []));
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
                if (x < 85 || x > 115)
                {
                    ref var cell = ref map[new CellCoord(x, y)];
                    cell.ConcealmentPerM = 60;
                    cell.ObstacleHeightCm = 1500;
                }
        var session = new GameSession(SkirmishScenario.Create(map, 1)) { Tactics = [FireAndMovement] };
        var red = session.Sim.Units.Single(u => u.Side == Side.Red);
        red.FirePolicy = FirePolicy.HoldFire;
        var contact = session.Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
        contact.Level = ContactLevel.Visible;
        contact.Position = red.Position;
        return (session, red);
    }

    private static List<Order> NewOrders(GameSession s, ref int seen)
    {
        var list = s.Sim.OrderLog.Skip(seen).Select(o => o.Order).ToList();
        seen = s.Sim.OrderLog.Count;
        return list;
    }

    private static void Steps(GameSession s, int n)
    {
        for (int i = 0; i < n; i++)
            s.StepOnce();
        s.Queue.Update(s, s.TakeEvents());
    }

    [Fact]
    public void ShiftOrders_AreQueued_FirstStartsAtOnce()
    {
        var (s, _) = Field();
        int seen = 0;
        Assert.Equal(ClickResult.Queued, s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false).Result);
        Assert.Equal(ClickResult.Queued, s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false).Result);
        Steps(s, 1);
        Assert.All(NewOrders(s, ref seen), o => Assert.InRange(Assert.IsType<MoveOrder>(o).Target.Y, 14_450, 15_650));
        var group = Assert.Single(s.Queue.Groups);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void MoveDone_NextStarts()
    {
        var (s, _) = Field();
        int seen = 0;
        s.HandleLeftClick(new Vec2(9_550, 17_050), false, shift: true, alt: false); // 10 m ahead
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        Steps(s, 1);
        NewOrders(s, ref seen);
        for (int i = 0; i < 400 && s.Queue.Groups.Count > 0 && s.Queue.Groups[0].Current == 0; i++)
            Steps(s, 1);
        Assert.Equal(1, s.Queue.Groups[0].Current);
        s.StepOnce(); // the next order is in the log once applied
        Assert.Contains(NewOrders(s, ref seen), o => o is MoveOrder m && Math.Abs(m.Target.Y - 15_050) < 600);
    }

    [Fact]
    public void AttackSuccess_NextStarts()
    {
        var (s, red) = Field();
        int seen = 0;
        s.HandleLeftClick(red.Position, doubleClick: true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        Steps(s, 2);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        NewOrders(s, ref seen);
        s.Queue.Update(s, [new PlannedAttackEnded(s.Sim.Tick, Side.Blue, attack.Id, true)]);
        Assert.Equal(1, s.Queue.Groups[0].Current);
        Steps(s, 1);
        Assert.Contains(NewOrders(s, ref seen), o => o is MoveOrder);
    }

    [Fact]
    public void AttackHalted_StopsAndAsks_WithWhatIsLeft()
    {
        var (s, red) = Field();
        s.HandleLeftClick(red.Position, doubleClick: true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        Steps(s, 2);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        s.Queue.Update(s, [new PlannedAttackHalted(s.Sim.Tick, Side.Blue, attack.Id, HaltReason.Casualties, 2)]);
        var group = s.Queue.Groups[0];
        Assert.True(group.Paused);
        Assert.Equal(2, group.Left);
        Assert.Equal(HaltReason.Casualties, group.Halt!.Reason);
    }

    [Fact]
    public void Continue_And_Cancel()
    {
        var (s, red) = Field();
        int seen = 0;
        s.HandleLeftClick(red.Position, doubleClick: true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        Steps(s, 2);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        s.Queue.Update(s, [new PlannedAttackHalted(s.Sim.Tick, Side.Blue, attack.Id, HaltReason.NoFire, 0)]);
        NewOrders(s, ref seen);
        s.Queue.Continue(s, s.Queue.Groups[0]);
        Steps(s, 1);
        Assert.Contains(NewOrders(s, ref seen), o => o is MoveOrder);
        s.Queue.Cancel(s.Queue.Groups[0]);
        Assert.Empty(s.Queue.Groups);
    }

    [Fact]
    public void OrderingSomeOfTheMen_ClearsOnlyTheirQueue()
    {
        var (s, _) = Field();
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        var two = s.OwnUnits.Take(2).Select(u => u.Id).ToList();
        foreach (var id in two)
            s.Selection.Add(id);
        s.HandleLeftClick(new Vec2(5_050, 17_050), false, shift: false, alt: false); // these two elsewhere, no shift
        var group = Assert.Single(s.Queue.Groups);
        Assert.Equal(4, group.Men.Count);
        Assert.DoesNotContain(two[0], group.Men);
    }

    [Fact]
    public void AttackOnAFallenMan_IsSkipped()
    {
        var (s, red) = Field();
        int seen = 0;
        s.HandleLeftClick(new Vec2(9_550, 17_050), false, shift: true, alt: false);
        s.HandleLeftClick(red.Position, doubleClick: true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        red.Wound = WoundLevel.Dead;
        for (int i = 0; i < 400 && s.Queue.Groups.Count > 0 && s.Queue.Groups[0].Current < 2; i++)
            Steps(s, 1);
        Assert.True(2 == s.Queue.Groups[0].Current, $"current {s.Queue.Groups[0].Current} paused {s.Queue.Groups[0].Paused} {s.Queue.Groups[0].Stop}");
        Assert.DoesNotContain(NewOrders(s, ref seen), o => o is PlannedAttackOrder);
    }

    [Fact]
    public void AllDown_Stops()
    {
        var (s, _) = Field();
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        foreach (var man in s.OwnUnits)
            man.Wound = WoundLevel.Dead;
        Steps(s, 1);
        Assert.True(s.Queue.Groups.Count == 0 || s.Queue.Groups[0].Paused);
    }

    [Fact]
    public void AttackForward_AttacksWhatItMeets_ThenGoesOn()
    {
        var (s, red) = Field();
        int seen = 0;
        s.HandleLeftClick(new Vec2(10_050, 3_050), false, shift: false, alt: false, attackForward: true);
        Steps(s, 12);
        Steps(s, 1);
        var orders = NewOrders(s, ref seen);
        Assert.Contains(orders, o => o is PlannedAttackOrder p && p.Plan.Target == red.Id);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        red.Wound = WoundLevel.Dead;
        s.Queue.Update(s, [new PlannedAttackEnded(s.Sim.Tick, Side.Blue, attack.Id, true)]);
        Steps(s, 1);
        Assert.Contains(NewOrders(s, ref seen), o => o is MoveOrder m && m.Target.Y < 4_000);
    }

    [Fact]
    public void AttackForward_WithoutAPlan_FiresAndGoesOn()
    {
        var (s, red) = Field();
        s.Tactics = [];
        int seen = 0;
        foreach (var man in s.OwnUnits.Skip(3).ToList())
            man.Wound = WoundLevel.Dead;
        s.Tactics = [FireAndMovement]; // three men: no fire and movement plan possible
        s.HandleLeftClick(new Vec2(10_050, 3_050), false, shift: false, alt: false, attackForward: true);
        Steps(s, 12);
        Steps(s, 1);
        var orders = NewOrders(s, ref seen);
        Assert.Contains(orders, o => o is FireAtOrder);
        Assert.Contains(orders, o => o is MoveOrder);
        Assert.Single(s.Queue.Groups);
        Assert.False(s.Queue.Groups[0].Paused);
    }

    [Fact]
    public void ACasualty_DoesNotBreakTheQueue()
    {
        var (s, _) = Field();
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.OwnUnits.First().Wound = WoundLevel.Dead;
        Steps(s, 1);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        var group = Assert.Single(s.Queue.Groups);
        Assert.Equal(2, group.Items.Count);
        Assert.False(group.Paused);
        s.HandleLeftClick(new Vec2(5_050, 17_050), false, shift: false, alt: false); // an order of their own
        Steps(s, 1);
        Assert.Empty(s.Queue.Groups); // and no false "nobody left" question
    }

    [Fact]
    public void ShiftDoubleClick_QueuesOneOrder_TheDoubleClicksOwn()
    {
        var (s, red) = Field();
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(red.Position, false, shift: true, alt: false);  // the first click of the double click
        s.HandleLeftClick(red.Position, true, shift: true, alt: false);   // the double click itself
        var group = Assert.Single(s.Queue.Groups);
        Assert.Equal(2, group.Items.Count);
        Assert.Equal(QueuedKind.Attack, group.Items[1].Kind);
    }

    [Fact]
    public void AQueue_DoesNotTakeAnAttackTheMenHadBefore()
    {
        var (s, red) = Field();
        s.HandleLeftClick(red.Position, true, shift: false, alt: false); // an attack of their own, now
        Steps(s, 2);
        var old = Assert.Single(s.Sim.PlannedAttacks);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        s.Queue.Update(s, []); // a frame without a step: the move is not applied yet
        Steps(s, 1);
        s.Queue.Update(s, [new PlannedAttackEnded(s.Sim.Tick, Side.Blue, old.Id, false, Disbanded: true)]);
        Assert.Equal(0, s.Queue.Groups[0].Current); // still on the first move
    }

    [Fact]
    public void AttackForward_WithoutAPlan_DoesNotLoop()
    {
        var (s, _) = Field();
        foreach (var man in s.OwnUnits.Skip(3).ToList())
            man.Wound = WoundLevel.Dead;
        int seen = 0;
        s.HandleLeftClick(new Vec2(10_050, 3_050), false, shift: false, alt: false, attackForward: true);
        for (int i = 0; i < 6; i++)
            Steps(s, 10);
        var orders = NewOrders(s, ref seen);
        Assert.True(orders.OfType<FireAtOrder>().Count() <= 3, $"{orders.OfType<FireAtOrder>().Count()} fire orders: it keeps re-engaging");
        Assert.True(orders.OfType<MoveOrder>().Count() <= 6, $"{orders.OfType<MoveOrder>().Count()} move orders");
    }

    [Fact]
    public void Halt_ClearsTheQueue()
    {
        var (s, _) = Field();
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 12_050), false, shift: true, alt: false);
        s.OrderStop();
        Assert.Empty(s.Queue.Groups);
    }

    [Fact]
    public void ContinueAfterAHaltedDetour_GoesOnForward()
    {
        var (s, red) = Field();
        int seen = 0;
        s.HandleLeftClick(new Vec2(10_050, 3_050), false, shift: false, alt: false, attackForward: true);
        Steps(s, 12);
        Steps(s, 1);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        s.Queue.Update(s, [new PlannedAttackHalted(s.Sim.Tick, Side.Blue, attack.Id, HaltReason.Casualties, 1)]);
        Assert.True(s.Queue.Groups[0].Paused);
        NewOrders(s, ref seen);
        s.Queue.Continue(s, s.Queue.Groups[0]);
        Steps(s, 1);
        Assert.Contains(NewOrders(s, ref seen), o => o is MoveOrder m && m.Target.Y < 4_000);
        Assert.Single(s.Queue.Groups);
    }

    [Fact]
    public void AFailedAttack_StopsTheQueueToAsk()
    {
        var (s, red) = Field();
        s.HandleLeftClick(red.Position, true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        Steps(s, 2);
        var attack = Assert.Single(s.Sim.PlannedAttacks);
        s.Queue.Update(s, [new PlannedAttackEnded(s.Sim.Tick, Side.Blue, attack.Id, false)]);
        Assert.True(s.Queue.Groups[0].Paused);
    }

    [Fact]
    public void AQueuedAttackWithoutAPlan_FiresItsWhileBeforeTheNext()
    {
        var (s, red) = Field();
        foreach (var man in s.OwnUnits.Skip(3).ToList())
            man.Wound = WoundLevel.Dead;
        s.HandleLeftClick(red.Position, true, shift: true, alt: false);
        s.HandleLeftClick(new Vec2(9_550, 15_050), false, shift: true, alt: false);
        Steps(s, 40); // 2 s: a fire order lasts longer
        Assert.Equal(0, s.Queue.Groups[0].Current);
    }
}
