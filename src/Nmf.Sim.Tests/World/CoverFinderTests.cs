using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

public class CoverFinderTests
{
    private static readonly Vec2 ThreatEast = new CellCoord(28, 10).CenterCm;

    private static (Simulation Sim, Unit Man) Setup(int rockX = 13, int manX = 10)
    {
        var map = new GridMap(30, 20, ["none"]);
        if (rockX >= 0)
            map[new CellCoord(rockX, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var sim = new Simulation(map, 1);
        var man = sim.SpawnUnit(Side.Blue, new CellCoord(manX, 10).CenterCm, 7);
        return (sim, man);
    }

    /// <summary>Makes the rock three cells tall so there is more than one cell behind it.</summary>
    private static void Wall(Simulation sim)
    {
        sim.Map[new CellCoord(13, 9)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        sim.Map[new CellCoord(13, 11)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
    }

    [Fact]
    public void AFallenTreeInTheForest_IsCoverToGetBehind()
    {
        var (sim, man) = Setup(rockX: -1);
        sim.Map[new CellCoord(13, 10)] = new CellData(0, 1500, 8, 26, 0) with { LowCover = 179, LowCoverHeightCm = 50 };
        Assert.Equal(new CellCoord(12, 10).CenterCm, CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void RockBetweenHimAndTheThreat_CellBehindItChosen()
    {
        var (sim, man) = Setup();
        Assert.Equal(new CellCoord(12, 10).CenterCm, CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void CellOnTheThreatSideOfTheRock_NotChosen()
    {
        var (sim, man) = Setup(manX: 15);
        var cover = CoverFinder.Find(sim, man, ThreatEast);
        Assert.NotNull(cover);
        Assert.True(cover!.Value.ToCell().X < 13, $"chose {cover} on the enemy's side of the rock");
    }

    [Fact]
    public void OpenField_Null()
    {
        var (sim, man) = Setup(rockX: -1);
        Assert.Null(CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void CoverFartherThanEightMetres_Null()
    {
        var (sim, man) = Setup(rockX: 20);
        Assert.Null(CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void CellTakenByAFriend_Skipped()
    {
        var (sim, man) = Setup();
        Wall(sim);
        sim.SpawnUnit(Side.Blue, new CellCoord(12, 10).CenterCm, 7);
        var cover = CoverFinder.Find(sim, man, ThreatEast);
        Assert.NotNull(cover);
        Assert.NotEqual(new CellCoord(12, 10).CenterCm, cover);
    }

    [Fact]
    public void CellAFriendIsRunningTo_Skipped()
    {
        var (sim, man) = Setup();
        Wall(sim);
        var friend = sim.SpawnUnit(Side.Blue, new CellCoord(10, 12).CenterCm, 7);
        sim.Submit(Side.Blue, new Nmf.Sim.Orders.MoveOrder(friend.Id, new CellCoord(12, 10).CenterCm));
        sim.Step();
        Assert.NotEqual(new CellCoord(12, 10).CenterCm, CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void NoThreat_AnyCoveredCell()
    {
        var (sim, man) = Setup();
        var cover = CoverFinder.Find(sim, man, null);
        Assert.NotNull(cover);
        var c = cover!.Value.ToCell();
        Assert.True(Math.Abs(c.X - 13) <= 1 && Math.Abs(c.Y - 10) <= 1);
    }

    [Fact]
    public void CoveredAt_OnlyFacingTheThreat()
    {
        var (sim, _) = Setup();
        Assert.True(CoverFinder.CoveredAt(sim.Map, new CellCoord(12, 10), ThreatEast) > 0);
        Assert.Equal(0, CoverFinder.CoveredAt(sim.Map, new CellCoord(14, 10), ThreatEast));
        Assert.True(CoverFinder.CoveredAt(sim.Map, new CellCoord(14, 10), null) > 0);
    }

    [Fact]
    public void RockOnlyDiagonal_ThreatAlongTheAxis_NotCovered()
    {
        var (sim, _) = Setup();
        Assert.Equal(0, CoverFinder.CoveredAt(sim.Map, new CellCoord(12, 11), new CellCoord(28, 11).CenterCm));
    }

    [Fact]
    public void ThreatAlongTheRockRow_CellStraightBehindChosen()
    {
        var (sim, man) = Setup();
        man.Position = new CellCoord(10, 14).CenterCm;
        Assert.Equal(new CellCoord(12, 10).CenterCm, CoverFinder.Find(sim, man, ThreatEast));
    }

    [Fact]
    public void CloseThreat_NeverRunsPastOrTowardIt()
    {
        var (sim, man) = Setup(rockX: 16);
        var threat = new CellCoord(13, 10).CenterCm; // e.g. a grenade 3 m away, the rock beyond it
        Assert.Null(CoverFinder.Find(sim, man, threat));
    }

    [Fact]
    public void WeakCoverFarAway_NotWorthRunningFor()
    {
        var map = new GridMap(30, 20, ["none"]);
        map[new CellCoord(16, 10)] = new CellData(0, 60, 20, 25, 0); // a thin bush 5 m off
        var sim = new Simulation(map, 1);
        var man = sim.SpawnUnit(Side.Blue, new CellCoord(10, 10).CenterCm, 7);
        Assert.Null(CoverFinder.Find(sim, man, ThreatEast));
    }
}
