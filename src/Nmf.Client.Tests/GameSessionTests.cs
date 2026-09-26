using Nmf.Sim.Orders;
using Nmf.Sim.Events;
using Nmf.Sim.Combat;
using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class GameSessionTests
{
    private static GameSession NewSession(GridMap? map = null)
    {
        map ??= new GridMap(200, 40, ["none"], new MapFeatures(
            [],
            [
                new MapPoint("b1", "blue", new Vec2(150, 150)),
                new MapPoint("b2", "blue", new Vec2(350, 150)),
                new MapPoint("r1", "red", new Vec2(19_500, 3_500)),
            ],
            []));
        return new GameSession(SkirmishScenario.Create(map, 1));
    }

    private static void SelectAll(GameSession s) =>
        s.Selection.SelectInBox(s.Sim.Units, s.PlayerSide, Vec2.Zero, new Vec2(s.Sim.Map.WidthCm, s.Sim.Map.HeightCm), false);

    [Fact]
    public void ThePlatoonMoving_EachSquadFormsUpOnItsOwn_SideBySide()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(8050 + i * 300, 18_050))).ToList();
        var session = new GameSession(SkirmishScenario.Create(new GridMap(200, 200, ["none"], new MapFeatures([], points, [])), 1));
        var men = session.OwnUnits.ToList();
        for (int i = 3; i < 6; i++)
            men[i].Squad = 1; // the eastern three
        int before = session.Sim.OrderLog.Count;
        session.OrderMove(new Vec2(10_050, 4050), MoveMode.Walk); // far north
        session.StepOnce();
        var targets = session.Sim.OrderLog.Skip(before).Select(o => (MoveOrder)o.Order)
            .ToDictionary(o => o.Unit, o => o.Target);
        Vec2 Centre(IEnumerable<Unit> squad) => new((int)squad.Average(u => targets[u.Id].X), (int)squad.Average(u => targets[u.Id].Y));
        var west = Centre(men.Take(3));
        var east = Centre(men.Skip(3));
        Assert.InRange(east.X - west.X, 1500, 2600); // 20 m apart, the western squad staying west
        Assert.InRange(Math.Abs(east.Y - west.Y), 0, 500);
        Assert.All(men.Take(3), u => Assert.True((targets[u.Id] - west).LengthSquared < 700L * 700));
    }

    [Fact]
    public void ASquadWithOneStragglerFromTheOther_MovesAsOneFormation()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(8050 + i * 300, 18_050))).ToList();
        var session = new GameSession(SkirmishScenario.Create(new GridMap(200, 200, ["none"], new MapFeatures([], points, [])), 1));
        var men = session.OwnUnits.ToList();
        for (int i = 3; i < 6; i++)
            men[i].Squad = 1;
        session.Selection.SelectInBox(men.Take(4), Side.Blue, Vec2.Zero, new Vec2(20_000, 20_000), false); // squad 0 and one of squad 1
        int before = session.Sim.OrderLog.Count;
        session.OrderMove(new Vec2(10_050, 4050), MoveMode.Walk);
        session.StepOnce();
        var targets = session.Sim.OrderLog.Skip(before).Select(o => ((MoveOrder)o.Order).Target).ToList();
        Assert.Equal(4, targets.Count);
        Assert.True(targets.Max(p => p.X) - targets.Min(p => p.X) < 1200, "the straggler was sent off on his own");
    }

    [Fact]
    public void ClickingAtThePlatoonsOwnCentre_StillSpreadsTheSquads()
    {
        var points = Enumerable.Range(0, 4).Select(i => new MapPoint($"b{i}", "blue", new Vec2(8050 + i * 300, 18_050))).ToList();
        var session = new GameSession(SkirmishScenario.Create(new GridMap(200, 200, ["none"], new MapFeatures([], points, [])), 1));
        var men = session.OwnUnits.ToList();
        men[2].Squad = men[3].Squad = 1;
        int before = session.Sim.OrderLog.Count;
        session.OrderMove(new Vec2(8500, 18_050), MoveMode.Walk);
        session.StepOnce();
        var targets = session.Sim.OrderLog.Skip(before).Select(o => (MoveOrder)o.Order).ToDictionary(o => o.Unit, o => o.Target);
        long gap = Math.Abs((targets[men[0].Id] - targets[men[2].Id]).Length);
        Assert.True(gap > 1200, $"squads only {gap} cm apart");
    }

    [Fact]
    public void OneSquadMoving_KeepsItsOneFormation()
    {
        var session = NewSession();
        SelectAll(session);
        int before = session.Sim.OrderLog.Count;
        session.OrderMove(new Vec2(10_050, 2050), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(2, session.Sim.OrderLog.Count - before);
    }

    [Fact]
    public void Speed_DoublesUpToEightTimes_AndHalvesDownToAQuarter()
    {
        var session = NewSession();
        var seen = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            session.SpeedUp();
            seen.Add(session.Clock.TimeScale);
        }
        Assert.Equal([2, 4, 8, 8, 8], seen);
        for (int i = 0; i < 7; i++)
            session.SlowDown();
        Assert.Equal(0.25, session.Clock.TimeScale);
    }

    [Fact]
    public void AtEightTimes_ThirtyFramesASecond_KeepUp()
    {
        var session = NewSession();
        session.SetSpeed(8);
        int steps = 0;
        for (int i = 0; i < 30; i++)
            steps += session.Update(1.0 / 30);
        Assert.InRange(steps, 158, 160);
    }

    [Fact]
    public void SetSpeed_KeepsToTheOfferedRange()
    {
        var session = NewSession();
        session.SetSpeed(16);
        Assert.Equal(GameSession.MaxSpeed, session.Clock.TimeScale);
        session.SetSpeed(0.1);
        Assert.Equal(GameSession.MinSpeed, session.Clock.TimeScale);
    }

    [Fact]
    public void Click_ReachesAsFarAsTheHoverTip_WhenZoomedOut()
    {
        var map = new GridMap(100, 100, ["none"], new MapFeatures(
            [],
            [new MapPoint("b1", "blue", new Vec2(1050, 9050)), new MapPoint("r1", "red", new Vec2(1050, 5050))],
            []));
        var session = new GameSession(SkirmishScenario.Create(map, 1));
        var red = session.Sim.Units.First(u => u.Side == Side.Red);
        for (int i = 0; i < 40; i++)
            session.StepOnce();
        var beside = red.Position + new Vec2(400, 0); // a dot's width away when zoomed far out
        Assert.Equal(ClickResult.MoveOrdered, session.HandleLeftClick(beside, false, false, false).Result);
        Assert.Equal(ClickResult.FireOrdered, session.HandleLeftClick(beside, false, false, false, radiusCm: 500).Result);
        red.Wound = WoundLevel.Dead; // a body is searched only when clicked right on it
        Assert.Equal(ClickResult.MoveOrdered, session.HandleLeftClick(beside, true, false, false, radiusCm: 500).Result);
        Assert.Equal(ClickResult.LootOrdered, session.HandleLeftClick(red.Position, true, false, false, radiusCm: 500).Result);
    }

    [Fact]
    public void InspectAt_FindsSeenEnemies_AliveFirst_ThenTheFallen_ButNeverUnseenOnes()
    {
        var map = new GridMap(100, 100, ["none"], new MapFeatures(
            [],
            [
                new MapPoint("b1", "blue", new Vec2(1050, 9050)),
                new MapPoint("r1", "red", new Vec2(1050, 7050)),
                new MapPoint("r2", "red", new Vec2(1150, 7050)),
            ],
            []));
        var session = new GameSession(SkirmishScenario.Create(map, 1));
        var reds = session.Sim.Units.Where(u => u.Side == Side.Red).ToList();
        foreach (var red in reds)
            session.Sim.Submit(Side.Red, new SetFirePolicyOrder(red.Id, FirePolicy.HoldFire));
        foreach (var own in session.OwnUnits)
            session.Sim.Submit(Side.Blue, new SetFirePolicyOrder(own.Id, FirePolicy.HoldFire));
        Assert.Null(session.InspectAt(reds[0].Position, 300)); // not yet spotted
        for (int i = 0; i < 40; i++)
            session.StepOnce();
        Assert.Equal(reds[0].Id, session.InspectAt(reds[0].Position, 300)?.Id);
        reds[0].Wound = WoundLevel.Dead;
        Assert.Equal(reds[1].Id, session.InspectAt(reds[0].Position, 300)?.Id);
        Assert.Equal(reds[0].Id, session.InspectAt(reds[0].Position, 40)?.Id);
        Assert.Null(session.InspectAt(session.OwnUnits.First().Position, 40));
    }

    [Fact]
    public void Update_OneStepOfTime_AdvancesOneTick()
    {
        var session = NewSession();
        Assert.Equal(1, session.Update(0.05));
        Assert.Equal(1, session.Sim.Tick);
    }

    [Fact]
    public void Update_WhilePaused_DoesNotAdvanceOrMove()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.Update(0.05);
        var position = session.Sim.Units[0].Position;

        session.Clock.Paused = true;
        Assert.Equal(0, session.Update(1.0));
        Assert.Equal(position, session.Sim.Units[0].Position);
        var (x, y) = session.InterpolatedPositionCm(session.Sim.Units[0]);
        Assert.InRange(x, 150, position.X);
        Assert.InRange(y, 150, position.Y);
    }

    [Fact]
    public void OrderMove_SpreadsSelectedUnitsInFormation()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(2000, 2000), session.Sim.Units[0].MoveTarget);
        Assert.Equal(new Vec2(2000, 1800), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_OffsetOnImpassableCell_FallsBackToClickedPoint()
    {
        var session = NewSession();
        session.Sim.Map[new Vec2(2000, 1800).ToCell()].ExtraMoveCost = CellData.Impassable;
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(2000, 2000), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_OffsetOutsideMap_FallsBackToClickedPoint()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(1000, 100), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(1000, 100), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_NothingSelected_MovesTheWholeSquad()
    {
        var session = NewSession();
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(2, session.Sim.OrderLog.Count);
    }

    [Fact]
    public void OrderStance_AppliesToSelection()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderStance(Stance.Prone);
        for (int i = 0; i < 25; i++) session.StepOnce();
        Assert.All(session.OwnUnits, u => Assert.Equal(Stance.Prone, u.Stance));
    }

    [Fact]
    public void Fog_ShowsNearbyGroundAndHidesFarGround()
    {
        var session = NewSession();
        Assert.True(session.Fog.IsVisible(new Vec2(150, 150)));
        Assert.False(session.Fog.IsVisible(new Vec2(19_950, 150)));
        int version = session.FogVersion;
        session.OrderMove(new Vec2(5_000, 150), MoveMode.Run);
        for (int i = 0; i < 60; i++) session.StepOnce();
        Assert.True(session.FogVersion > version);
    }

    [Fact]
    public void Fog_FallenMenSeeNothing()
    {
        var session = NewSession();
        foreach (var unit in session.OwnUnits)
            Nmf.Sim.Combat.Damage.SetWound(session.Sim, unit, Nmf.Sim.Combat.WoundLevel.Dead, 0, []);
        for (int i = 0; i < 5; i++) session.StepOnce();
        Assert.False(session.Fog.IsVisible(new Vec2(150, 150)));
    }

    [Fact]
    public void InterpolatedPosition_IsBetweenPreviousAndCurrentTick()
    {
        var session = NewSession();
        session.Selection.SelectAt(session.Sim.Units, Side.Blue, new Vec2(150, 150), 50, false);
        session.OrderMove(new Vec2(1500, 150), MoveMode.Walk);
        session.Update(0.05);  // one step: 150 -> 158
        session.Update(0.025); // half a step later
        var (x, y) = session.InterpolatedPositionCm(session.Sim.Units[0]);
        Assert.Equal(154, x, 3);
        Assert.Equal(150, y, 3);
    }

    [Fact]
    public void GameTime_FollowsTicks()
    {
        var session = NewSession();
        for (int i = 0; i < 40; i++) session.StepOnce();
        Assert.Equal(TimeSpan.FromSeconds(2), session.GameTime);
    }

    [Fact]
    public void IsShownToPlayer_OnlyOwnOrVisibleEnemiesUnlessRevealed()
    {
        var session = NewSession();
        var own = session.Sim.Units[0];
        var enemy = session.Sim.Units[2];
        Assert.True(session.IsShownToPlayer(own, revealAll: false));
        Assert.False(session.IsShownToPlayer(enemy, revealAll: false));
        Assert.True(session.IsShownToPlayer(enemy, revealAll: true));
    }

    [Fact]
    public void TakeEvents_ReturnsEventsOnceSinceLastCall()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        var first = session.TakeEvents();
        Assert.Contains(first, e => e is UnitMoved);
        Assert.Empty(session.TakeEvents());
    }

    [Fact]
    public void CycleFirePolicy_AdvancesFromTheFirstSelectedUnit()
    {
        var session = NewSession();
        SelectAll(session);
        session.CycleFirePolicy();
        session.StepOnce();
        Assert.All(session.OwnUnits, u => Assert.Equal(FirePolicy.ReturnFire, u.FirePolicy));
    }

    [Fact]
    public void EnemyAt_FindsOnlyEnemiesShownToThePlayer()
    {
        var session = NewSession();
        var enemy = session.Sim.Units[2];
        Assert.Null(session.EnemyAt(enemy.Position, 100));
        enemy.Position = new Vec2(400, 150); // right next to the blue men: spotted after a few vision updates
        for (int i = 0; i < 30; i++) session.StepOnce();
        Assert.Same(enemy, session.EnemyAt(enemy.Position, 100));
        Assert.Null(session.EnemyAt(new Vec2(3000, 3000), 100));
    }

    [Fact]
    public void OrderFireAt_SubmitsForSelection()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderFireAt(session.Sim.Units[2].Id);
        session.StepOnce();
        Assert.Equal(2, session.Sim.OrderLog.Count(o => o.Order is FireAtOrder));
    }

    [Fact]
    public void EnemyAt_IgnoresDownedEnemies()
    {
        var session = NewSession();
        var enemy = session.Sim.Units[2];
        enemy.Position = new Vec2(400, 150);
        for (int i = 0; i < 30; i++) session.StepOnce();
        enemy.Wound = WoundLevel.Incapacitated;
        Assert.Null(session.EnemyAt(enemy.Position, 100));
    }
}
