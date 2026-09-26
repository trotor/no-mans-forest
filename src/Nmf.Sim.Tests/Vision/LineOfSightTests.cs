using Nmf.Sim.Core;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class LineOfSightTests
{
    private static readonly Vec2 Observer = new(50, 550);  // cell (0,5)
    private static readonly Vec2 Target = new(2050, 550);  // cell (20,5)

    private static GridMap OpenMap(int w = 30, int h = 10) => new(w, h, ["none"]);

    [Fact]
    public void OpenGround_IsClear()
    {
        Assert.Equal(255, LineOfSight.Clarity(OpenMap(), Observer, 160, Target, 160));
    }

    [Fact]
    public void SameCell_IsClear()
    {
        Assert.Equal(255, LineOfSight.Clarity(OpenMap(), new Vec2(10, 10), 160, new Vec2(90, 90), 20));
    }

    [Fact]
    public void ForestStrip_AccumulatesConcealment()
    {
        var map = OpenMap();
        for (int x = 5; x <= 14; x++)
            map[new CellCoord(x, 5)] = new CellData(0, 1500, 20, 0, 0);
        Assert.Equal(55, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void OpaqueWall_Blocks()
    {
        var map = OpenMap();
        map[new CellCoord(10, 5)] = new CellData(0, 300, 255, 230, 0);
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void LowBush_BlocksProneButNotStanding()
    {
        var map = OpenMap();
        map[new CellCoord(5, 5)] = new CellData(0, 80, 255, 0, 0);
        Assert.Equal(255, LineOfSight.Clarity(map, Observer, 160, Target, 160));
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 25, Target, 20));
    }

    [Fact]
    public void HillBetween_Blocks()
    {
        var map = OpenMap();
        map[new CellCoord(10, 5)].GroundHeightCm = 300;
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void TargetInsideBush_IsPartlyHidden()
    {
        var map = OpenMap();
        map[new CellCoord(20, 5)] = new CellData(0, 80, 153, 0, 0);
        Assert.Equal(102, LineOfSight.Clarity(map, Observer, 160, Target, 20));
    }

    [Fact]
    public void DiagonalSteps_CountMoreConcealmentThanStraightOnes()
    {
        var map = OpenMap(30, 30);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 10, 0, 0);

        Assert.Equal(115, LineOfSight.Clarity(map, new Vec2(50, 50), 160, new Vec2(1050, 1050), 160));
        Assert.Equal(155, LineOfSight.Clarity(map, new Vec2(50, 50), 160, new Vec2(1050, 50), 160));
    }

    [Fact]
    public void AFallenTreeInTheForest_HidesOnlyWhatIsLowBehindIt()
    {
        var map = new GridMap(40, 5, ["none"]);
        for (int x = 0; x < 40; x++)
            map[new CellCoord(x, 2)] = new CellData(0, 1500, 3, 26, 0); // open pine forest
        for (int x = 15; x < 20; x++) // a trunk lying along the line of sight
            map[new CellCoord(x, 2)] = new CellData(0, 1500, 3, 26, 0) with { LowCover = 179, LowCoverHeightCm = 50, LowConcealmentPerM = 76 };
        var from = new CellCoord(2, 2).CenterCm;
        var to = new CellCoord(35, 2).CenterCm;
        int open = LineOfSight.Clarity(map, from, 160, to, 170);
        Assert.True(open > 0, "standing men see each other over it");
        Assert.Equal(0, LineOfSight.Clarity(map, from, 25, to, 30)); // lying down along it: hidden
    }
}
