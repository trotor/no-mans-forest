using Nmf.Client.Art;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Art;

public class ReliefTests
{
    private static GridMap Map(Func<int, int, int> heightCm, int size = 160)
    {
        var map = new GridMap(size, size, ["none", "forest"]);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                map[new CellCoord(x, y)] = new CellData((short)heightCm(x, y), 0, 0, 0, 1);
        return map;
    }

    private static int Cone(int x, int y, int cx, int cy, int radius, int heightCm)
    {
        double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        return d >= radius ? 0 : (int)(heightCm * (1 - d / radius));
    }

    [Fact]
    public void FlatGround_IsEvenlyLit()
    {
        var relief = Relief.Of(Map((_, _) => 500));
        Assert.All(relief.Light, l => Assert.Equal(1f, l, 3));
    }

    [Fact]
    public void AKnoll_IsLitFromTheNorthWest_AndItsTopStandsOut()
    {
        var relief = Relief.Of(Map((x, y) => Cone(x, y, 80, 80, 35, 600)));
        float northWest = relief.LightAtCell(64, 64), southEast = relief.LightAtCell(96, 96);
        Assert.True(northWest > southEast + 0.25f, $"NW {northWest:F2}, SE {southEast:F2}");
        Assert.True(relief.LightAtCell(80, 80) > relief.LightAtCell(10, 150) + 0.05f, "the top is lighter than the flat");
    }

    [Fact]
    public void AHollow_IsDarkerThanTheGroundAround()
    {
        var relief = Relief.Of(Map((x, y) => 800 - Cone(x, y, 80, 80, 35, 600)));
        Assert.True(relief.LightAtCell(80, 80) < relief.LightAtCell(10, 150) - 0.05f);
    }

    [Fact]
    public void Rise_TellsKnollsFromHollows_AgainstTheGroundAround()
    {
        var knoll = Relief.Of(Map((x, y) => Cone(x, y, 80, 80, 35, 600)));
        Assert.True(knoll.RiseAtCell(80, 80) > 0.5f);
        Assert.InRange(knoll.RiseAtCell(10, 150), -0.1f, 0.1f);
        var hollow = Relief.Of(Map((x, y) => 800 - Cone(x, y, 80, 80, 35, 600)));
        Assert.True(hollow.RiseAtCell(80, 80) < -0.5f);
        Assert.All(knoll.Rise, r => Assert.InRange(r, -1f, 1f));
        Assert.All(Relief.Of(Map((_, _) => 500)).Rise, r => Assert.Equal(0f, r, 3));
    }

    [Fact]
    public void Hummocks_DoNotSpeckleTheGround()
    {
        var rng = new Random(3);
        var noise = new int[160 * 160];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = rng.Next(-40, 41);
        var relief = Relief.Of(Map((x, y) => 500 + noise[y * 160 + x]));
        double mean = relief.Light.Average();
        double sd = Math.Sqrt(relief.Light.Average(l => (l - mean) * (l - mean)));
        Assert.True(sd < 0.03, $"sd {sd:F3}");
    }

    [Fact]
    public void LightAt_ReadsTheCellUnderAPosition_AndStaysInsideTheMap()
    {
        var relief = Relief.Of(Map((x, y) => Cone(x, y, 80, 80, 35, 600)));
        Assert.Equal(relief.LightAtCell(64, 64), relief.LightAt(new CellCoord(64, 64).CenterCm));
        Assert.Equal(relief.LightAtCell(0, 0), relief.LightAt(new Vec2(-500, -500)));
        Assert.All(relief.Light, l => Assert.InRange(l, Relief.Darkest, Relief.Lightest));
    }
}
