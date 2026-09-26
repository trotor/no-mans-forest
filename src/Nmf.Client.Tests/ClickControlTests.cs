using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class ClickControlTests
{
    private static readonly Vec2 Blue1 = new(1050, 1050);
    private static readonly Vec2 Blue2 = new(1450, 1050);
    private static readonly Vec2 RedPos = new(1850, 1450);

    /// <summary>Two blue soldiers and one red one close enough to be seen quickly.</summary>
    private static GameSession NewSession(GridMap? map = null)
    {
        map ??= new GridMap(60, 40, ["none"]);
        var withPoints = new GridMap(map.Width, map.Height, map.TerrainNames, new MapFeatures(
            [],
            [new MapPoint("b1", "blue", Blue1), new MapPoint("b2", "blue", Blue2), new MapPoint("r1", "red", RedPos)],
            []));
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                withPoints[new CellCoord(x, y)] = map[new CellCoord(x, y)];
        return new GameSession(SkirmishScenario.Create(withPoints, 1));
    }

    private static void SeeEnemy(GameSession s)
    {
        for (int i = 0; i < 30; i++) s.StepOnce();
    }

    private static List<Order> OrdersAfterStep(GameSession s)
    {
        int before = s.Sim.OrderLog.Count;
        s.StepOnce();
        return s.Sim.OrderLog.Skip(before).Select(o => o.Order).ToList();
    }

    [Fact]
    public void ClickOnOwnSoldier_SelectsHim_ShiftAdds_DoubleClickSelectsSquad()
    {
        var s = NewSession();
        Assert.Equal(ClickResult.Selected, s.HandleLeftClick(Blue1, doubleClick: false, shift: false, alt: false).Result);
        Assert.Equal(1, s.Selection.Count);
        s.HandleLeftClick(Blue2, false, shift: true, alt: false);
        Assert.Equal(2, s.Selection.Count);

        s.HandleLeftClick(Blue1, false, false, false);
        Assert.Equal(1, s.Selection.Count);
        Assert.Equal(ClickResult.SelectedSquad, s.HandleLeftClick(Blue1, doubleClick: true, false, false).Result);
        Assert.True(s.IsSquadCommanded);
        Assert.Equal(2, s.CommandedIds.Count);
    }

    [Fact]
    public void ClickOnGround_WithSelection_WalksDoubleClickRunsAltCrawls()
    {
        var s = NewSession();
        s.HandleLeftClick(Blue1, false, false, false);
        var target = new Vec2(1050, 3050);

        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(target, false, false, false).Result);
        Assert.Equal(MoveMode.Auto, Assert.IsType<MoveOrder>(Assert.Single(OrdersAfterStep(s))).Mode);

        s.HandleLeftClick(target, doubleClick: true, false, false);
        Assert.Equal(MoveMode.Run, Assert.IsType<MoveOrder>(Assert.Single(OrdersAfterStep(s))).Mode);

        s.HandleLeftClick(target, false, false, alt: true);
        Assert.Equal(MoveMode.Crawl, Assert.IsType<MoveOrder>(Assert.Single(OrdersAfterStep(s))).Mode);
    }

    [Fact]
    public void ClickOnGround_WithNothingSelected_MovesTheWholeSquad()
    {
        var s = NewSession();
        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(new Vec2(1050, 3050), false, false, false).Result);
        Assert.Equal(2, OrdersAfterStep(s).OfType<MoveOrder>().Count());
    }

    [Fact]
    public void SquadCommand_SkipsMenWhoAreDownOrCaptured()
    {
        var s = NewSession();
        s.Sim.Units[1].IsCaptured = true;
        s.HandleLeftClick(new Vec2(1050, 3050), false, false, false);
        var orders = OrdersAfterStep(s).OfType<MoveOrder>().ToList();
        Assert.Equal(new[] { s.Sim.Units[0].Id }, orders.Select(o => o.Unit));
    }

    [Fact]
    public void ClickOnSeenEnemy_WithSelection_FiresAtHim_DoubleClickWholeSquad()
    {
        var s = NewSession();
        SeeEnemy(s);
        var red = s.Sim.Units[2];
        s.HandleLeftClick(Blue1, false, false, false);

        Assert.Equal(ClickResult.FireOrdered, s.HandleLeftClick(red.Position, false, false, false).Result);
        var single = OrdersAfterStep(s);
        Assert.Equal(new[] { s.Sim.Units[0].Id }, single.OfType<FireAtOrder>().Select(o => o.Unit));

        s.HandleLeftClick(Blue1, doubleClick: true, false, false); // back to the whole squad
        Assert.Equal(ClickResult.AttackOrdered, s.HandleLeftClick(red.Position, doubleClick: true, false, false).Result);
        Assert.Equal(2, OrdersAfterStep(s).OfType<AttackOrder>().Count());
        Assert.Equal(ClickResult.AssaultOrdered, s.HandleLeftClick(red.Position, doubleClick: true, shift: true, false).Result);
        Assert.Equal(2, OrdersAfterStep(s).OfType<AssaultOrder>().Count());
    }

    [Fact]
    public void ClickOnEnemy_WithNothingSelected_TheWholeSquadFires()
    {
        var s = NewSession();
        SeeEnemy(s);
        Assert.Equal(ClickResult.FireOrdered, s.HandleLeftClick(s.Sim.Units[2].Position, false, false, false).Result);
        Assert.Equal(2, OrdersAfterStep(s).OfType<FireAtOrder>().Count());
    }

    [Fact]
    public void ClickOnUnseenEnemy_IsAMoveThere()
    {
        var s = NewSession();
        s.HandleLeftClick(Blue1, false, false, false);
        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(RedPos, false, false, false).Result);
    }

    [Fact]
    public void RightClick_ClearsSelection()
    {
        var s = NewSession();
        s.HandleLeftClick(Blue1, false, false, false);
        Assert.Equal(ClickResult.Cleared, s.HandleRightClick().Result);
        Assert.Equal(0, s.Selection.Count);
        Assert.True(s.IsSquadCommanded);
    }

    [Fact]
    public void ClickOnRock_SendsTheSoldierNextToIt()
    {
        var map = new GridMap(60, 40, ["none"]);
        var rock = new CellCoord(10, 30);
        map[rock] = new CellData(0, 120, 255, 230, 0, CellData.Impassable);
        var s = NewSession(map);
        s.HandleLeftClick(Blue1, false, false, false);
        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(rock.CenterCm, false, false, false).Result);
        s.StepOnce();
        var goal = s.Sim.Units[0].MoveTarget;
        Assert.NotNull(goal);
        Assert.True(s.Sim.Map.CellAt(goal!.Value).IsPassable);
        Assert.True((goal.Value - rock.CenterCm).Length <= 300);
    }

    [Fact]
    public void ClickInsideUnreachablePocket_GoesToTheNearestReachableSpot()
    {
        var map = new GridMap(60, 40, ["none"]);
        for (int x = 28; x <= 32; x++) { map[new CellCoord(x, 18)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(x, 22)].ExtraMoveCost = CellData.Impassable; }
        for (int y = 18; y <= 22; y++) { map[new CellCoord(28, y)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(32, y)].ExtraMoveCost = CellData.Impassable; }
        var s = NewSession(map);
        s.HandleLeftClick(Blue1, false, false, false);
        var inside = new CellCoord(30, 20).CenterCm;
        s.HandleLeftClick(inside, false, false, false);
        s.StepOnce();
        var goal = s.Sim.Units[0].MoveTarget;
        Assert.NotNull(goal);
        Assert.True((goal!.Value - inside).Length <= 500);
    }

    [Fact]
    public void ArrivingGroup_TakesCoverBesideARockNearTheClickedPoint()
    {
        var map = new GridMap(60, 40, ["none"]);
        var rock = new CellCoord(21, 30);
        map[rock] = new CellData(0, 120, 255, 230, 0, CellData.Impassable);
        var s = NewSession(map);
        s.HandleLeftClick(Blue1, false, false, false);
        var clicked = new CellCoord(20, 30).CenterCm + new Vec2(-100, 0); // one metre beside the cell next to the rock
        s.HandleLeftClick(clicked, false, false, false);
        s.StepOnce();
        var goalCell = s.Sim.Units[0].MoveTarget!.Value.ToCell();
        Assert.True(Math.Max(Math.Abs(goalCell.X - rock.X), Math.Abs(goalCell.Y - rock.Y)) == 1, $"goal {goalCell} is not next to the rock");
    }

    [Fact]
    public void OwnSoldierCloserThanEnemy_WinsTheClick()
    {
        var s = NewSession();
        SeeEnemy(s);
        s.HandleLeftClick(Blue2, false, false, false);
        var between = new Vec2(1500, 1100); // 71 cm from blue 2, far from red
        Assert.Equal(ClickResult.Selected, s.HandleLeftClick(between, false, false, false).Result);
    }

    [Fact]
    public void SelectedMenAllDown_CommandFallsBackToTheSquad()
    {
        var s = NewSession();
        s.HandleLeftClick(Blue1, false, false, false);
        s.Sim.Units[0].Wound = WoundLevel.Dead;
        Assert.True(s.IsSquadCommanded);
        Assert.Equal(new[] { s.Sim.Units[1].Id }, s.CommandedIds);
        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(new Vec2(1050, 3050), false, false, false).Result);
    }

    [Fact]
    public void ClickBody_SendsNearestCommandedMan()
    {
        var s = NewSession();
        SeeEnemy(s);
        var red = s.Sim.Units[2];
        Damage.SetWound(s.Sim, red, WoundLevel.Dead, s.Sim.Tick, []);
        Assert.Equal(ClickResult.LootOrdered, s.HandleLeftClick(RedPos, false, false, false).Result);
        var order = Assert.IsType<LootOrder>(Assert.Single(OrdersAfterStep(s)));
        Assert.Equal(s.Sim.Units[1].Id, order.Unit); // Blue2 is nearer
        Assert.Equal(red.Id, order.Body);
    }

    [Fact]
    public void ClickBodyNearLiveEnemy_EnemyWins()
    {
        var s = NewSession();
        var body = s.Sim.SpawnUnit(Side.Red, RedPos + new Vec2(100, 0), 7);
        SeeEnemy(s);
        Damage.SetWound(s.Sim, body, WoundLevel.Dead, s.Sim.Tick, []);
        Assert.Equal(ClickResult.FireOrdered, s.HandleLeftClick(RedPos + new Vec2(100, 0), false, false, false).Result);
    }

    [Fact]
    public void ClickLootedOrUnseenBody_Moves()
    {
        var s = NewSession();
        SeeEnemy(s);
        var red = s.Sim.Units[2];
        Damage.SetWound(s.Sim, red, WoundLevel.Dead, s.Sim.Tick, []);
        red.Looted = true;
        Assert.Equal(ClickResult.MoveOrdered, s.HandleLeftClick(RedPos, false, false, false).Result);

        var unseen = NewSession();
        Damage.SetWound(unseen.Sim, unseen.Sim.Units[2], WoundLevel.Dead, 0, []);
        Assert.Equal(ClickResult.MoveOrdered, unseen.HandleLeftClick(RedPos, false, false, false).Result);
    }

    [Fact]
    public void CarriedPapers_ListsItemsOfOwnMenInAction()
    {
        var s = NewSession();
        Assert.Empty(s.CarriedPapers);
        s.Sim.Units[0].AddItem(SkirmishScenario.SovietOrders);
        Assert.Equal(new[] { "Soviet orders" }, s.CarriedPapers);
        Damage.SetWound(s.Sim, s.Sim.Units[0], WoundLevel.Dead, 0, []);
        Assert.Empty(s.CarriedPapers);
    }

    [Fact]
    public void ClickBody_PrefersTheManWhoCanUseTheAmmo()
    {
        var s = NewSession();
        SeeEnemy(s);
        var rifle = new WeaponDef("r", "R", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000);
        var smg = rifle with { Id = "s", Class = WeaponClass.Smg };
        foreach (var (man, weapon) in new[] { (s.Sim.Units[0], rifle), (s.Sim.Units[1], smg) }) // Blue1 further away, Blue2 nearer
        {
            man.Weapon = weapon;
            man.Ammo = weapon.MagazineSize;
            man.Magazines = 2;
        }
        var red = s.Sim.Units[2];
        red.Weapon = rifle;
        red.Magazines = 3;
        Damage.SetWound(s.Sim, red, WoundLevel.Dead, s.Sim.Tick, []);
        s.HandleLeftClick(RedPos, false, false, false);
        var order = Assert.IsType<LootOrder>(Assert.Single(OrdersAfterStep(s)));
        Assert.Equal(s.Sim.Units[0].Id, order.Unit);
    }

    [Fact]
    public void ClickBody_SkipsPinnedMen_AndDoesNothingWhenNobodyCanGo()
    {
        var s = NewSession();
        SeeEnemy(s);
        var red = s.Sim.Units[2];
        Damage.SetWound(s.Sim, red, WoundLevel.Dead, s.Sim.Tick, []);
        Pin(s.Sim.Units[1]);
        s.HandleLeftClick(RedPos, false, false, false);
        Assert.Equal(s.Sim.Units[0].Id, Assert.IsType<LootOrder>(Assert.Single(OrdersAfterStep(s))).Unit);

        Pin(s.Sim.Units[0]);
        Assert.Equal(ClickResult.None, s.HandleLeftClick(RedPos, false, false, false).Result);
        Assert.Empty(OrdersAfterStep(s));
    }

    private static void Pin(Unit man)
    {
        man.MoraleState = MoraleState.Pinned;
        man.Suppression = 600; // stays pinned through the next steps
    }
}
