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
    public void OrderMove_NothingSelected_SubmitsNothing()
    {
        var session = NewSession();
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Empty(session.Sim.OrderLog);
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
    public void Fog_ShowsNearbyCellsAndHidesFarOnes()
    {
        var session = NewSession();
        int width = session.Sim.Map.Width;
        Assert.True(session.VisibleCells[1 * width + 1]);
        Assert.False(session.VisibleCells[1 * width + 199]);
        int version = session.FogVersion;
        for (int i = 0; i < 5; i++) session.StepOnce();
        Assert.True(session.FogVersion > version);
    }

    [Fact]
    public void InterpolatedPosition_IsBetweenPreviousAndCurrentTick()
    {
        var session = NewSession();
        session.Selection.SelectAt(session.Sim.Units, Side.Blue, new Vec2(150, 150), 50, false);
        session.OrderMove(new Vec2(1500, 150), MoveMode.Walk);
        session.Update(0.05);  // one step: 150 -> 157
        session.Update(0.025); // half a step later
        var (x, y) = session.InterpolatedPositionCm(session.Sim.Units[0]);
        Assert.Equal(153.5, x, 3);
        Assert.Equal(150, y, 3);
    }

    [Fact]
    public void GameTime_FollowsTicks()
    {
        var session = NewSession();
        for (int i = 0; i < 40; i++) session.StepOnce();
        Assert.Equal(TimeSpan.FromSeconds(2), session.GameTime);
    }
}
