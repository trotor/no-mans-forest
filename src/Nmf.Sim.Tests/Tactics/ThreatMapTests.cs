using Nmf.Sim.Core;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Tactics;

public class ThreatMapTests
{
    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    /// <summary>A 200 m open field, a red at (100, 20) m whose contact blue has at the given level and place.</summary>
    private static (Simulation Sim, Unit Red) Field(ContactLevel level = ContactLevel.Visible, Vec2? contactAt = null)
    {
        var sim = new Simulation(new GridMap(200, 200, ["none"]), 1);
        var red = sim.SpawnUnit(Side.Red, M(100, 20), 8);
        sim.SpawnUnit(Side.Blue, M(100, 180), 8);
        if (level != ContactLevel.Unknown)
        {
            var contact = sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
            contact.Level = level;
            contact.Position = contactAt ?? red.Position;
        }
        return (sim, red);
    }

    [Fact]
    public void OpenGround_IsExposed()
    {
        var (sim, _) = Field();
        Assert.Equal(1, ThreatMap.Build(sim, Side.Blue, M(100, 60), 10_000).ExposureAt(M(100, 80)));
    }

    [Fact]
    public void BehindARidge_IsNotExposed()
    {
        var (sim, _) = Field();
        for (int x = 0; x < 200; x++)
            sim.Map[new CellCoord(x, 50)].GroundHeightCm = 300;
        var threat = ThreatMap.Build(sim, Side.Blue, M(100, 60), 10_000);
        Assert.Equal(0, threat.ExposureAt(M(100, 80)));
        Assert.Equal(1, threat.ExposureAt(M(100, 40)));
    }

    [Fact]
    public void UnknownEnemy_DoesNotCount()
    {
        var (sim, _) = Field(ContactLevel.Unknown);
        Assert.Equal(0, ThreatMap.Build(sim, Side.Blue, M(100, 60), 10_000).ExposureAt(M(100, 80)));
    }

    [Fact]
    public void SuspectedEnemy_CountsFromTheContactPlace_NotHisRealPlace()
    {
        var (sim, red) = Field(ContactLevel.Suspected, M(20, 100));
        for (int y = 0; y < 200; y++)
            sim.Map[new CellCoord(60, y)].GroundHeightCm = 300; // a ridge between the contact place (x 20) and x 100
        var threat = ThreatMap.Build(sim, Side.Blue, M(100, 100), 12_000);
        Assert.Equal(1, threat.ExposureAt(M(40, 100)));  // the contact place sees this
        Assert.Equal(0, threat.ExposureAt(M(100, 30)));  // the real man would, the contact place does not
        Assert.NotEqual(M(20, 100), red.Position);
    }

    [Fact]
    public void BeyondSight_IsNotExposed()
    {
        var (sim, _) = Field();
        var threat = ThreatMap.Build(sim, Side.Blue, M(100, 100), 20_000);
        Assert.Equal(1, threat.ExposureAt(M(100, 160)));
        Assert.Equal(0, threat.ExposureAt(M(100, 180)));
    }

    [Fact]
    public void ExcludingThePosition_DropsItsSources()
    {
        var (sim, _) = Field();
        var threat = ThreatMap.Build(sim, Side.Blue, M(100, 60), 10_000);
        Assert.Equal(0, threat.ExposureAt(M(100, 80), M(100, 22), 3_000));
        Assert.Equal(1, threat.ExposureAt(M(100, 80), M(100, 90), 3_000));
    }

    [Fact]
    public void OutsideTheWindow_IsZero()
    {
        var (sim, _) = Field();
        Assert.Equal(0, ThreatMap.Build(sim, Side.Blue, M(100, 60), 2_000).ExposureAt(M(100, 150)));
    }
}
