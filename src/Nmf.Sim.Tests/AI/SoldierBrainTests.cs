using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class SoldierBrainTests
{
    [Fact]
    public void SuppressedIdleSoldier_GoesProne()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Suppression = 320;
        sim.Step();
        Assert.Equal(Stance.Prone, u.TargetStance);
        Assert.Equal(MoraleState.Steady, u.MoraleState);
    }

    [Fact]
    public void BrokenSoldier_RunsAwayFromTheThreatThenStaysDown()
    {
        var sim = new Simulation(new GridMap(80, 20, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(4050, 1050), 7);
        sim.SpawnUnit(Side.Red, new Vec2(6050, 1050), 7);
        for (int i = 0; i < 25; i++) sim.Step(); // red becomes visible to blue
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 5; i++) sim.Step();
        Assert.NotNull(blue.MoveTarget);
        Assert.True(blue.MoveTarget!.Value.X < 4050 - 1500);
        Assert.Equal(MoveMode.Run, blue.MoveMode);

        for (int i = 0; i < 400; i++) sim.Step();
        Assert.Null(blue.MoveTarget);
        Assert.True(blue.Stance == Stance.Prone || blue.TargetStance == Stance.Prone);
    }

    [Fact]
    public void BrokenSoldierAtMapEdge_ClampsTheRetreatAndDoesNotCrash()
    {
        var sim = new Simulation(new GridMap(40, 10, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(60, 550), 7);
        sim.SpawnUnit(Side.Red, new Vec2(1060, 550), 7);
        for (int i = 0; i < 25; i++) sim.Step();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 200; i++) sim.Step();
        Assert.True(sim.Map.Contains(blue.Position));
    }

    [Fact]
    public void BoxedInBrokenSoldier_GoesProneInstead()
    {
        var map = new GridMap(40, 10, ["none"]);
        for (int x = 18; x <= 22; x++) { map[new CellCoord(x, 3)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(x, 7)].ExtraMoveCost = CellData.Impassable; }
        for (int y = 3; y <= 7; y++) { map[new CellCoord(18, y)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(22, y)].ExtraMoveCost = CellData.Impassable; }
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(2050, 550), 7);
        sim.SpawnUnit(Side.Red, new Vec2(3550, 550), 7);
        for (int i = 0; i < 25; i++) sim.Step();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 60; i++) sim.Step();
        Assert.Null(blue.MoveTarget);
        Assert.True(blue.Stance == Stance.Prone || blue.TargetStance == Stance.Prone);
    }
}
