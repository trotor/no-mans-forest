using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Scenarios;

public class SkirmishCombatTests
{
    private static GridMap Field()
    {
        var points = new List<MapPoint>();
        for (int i = 0; i < 4; i++)
        {
            points.Add(new MapPoint($"b{i}", "blue", new Vec2(3000 + i * 300, 5500)));
            points.Add(new MapPoint($"r{i}", "red", new Vec2(3000 + i * 300, 1500)));
        }
        return new GridMap(80, 70, ["none"], new MapFeatures([], points, []));
    }

    [Fact]
    public void Create_WithWeapons_GivesLeaderSupportAndRiflemen()
    {
        var sim = SkirmishScenario.Create(Field(), 1, TestWeapons.SkirmishSet()).Sim;
        var blue = sim.Units.Where(u => u.Side == Side.Blue).ToList();
        Assert.True(blue[0].IsLeader);
        Assert.Equal("suomi_kp31", blue[0].Weapon!.Id);
        Assert.Equal("lahti_saloranta", blue[1].Weapon!.Id);
        Assert.Equal("mosin_m39", blue[2].Weapon!.Id);
        Assert.Equal("ppsh41", sim.Units.First(u => u.Side == Side.Red).Weapon!.Id);
    }

    [Fact]
    public void Create_WithMissingWeapon_Throws()
    {
        var weapons = TestWeapons.SkirmishSet().Where(kv => kv.Key != "dp27").ToDictionary(kv => kv.Key, kv => kv.Value);
        var ex = Assert.Throws<ArgumentException>(() => SkirmishScenario.Create(Field(), 1, weapons));
        Assert.Contains("dp27", ex.Message);
    }

    [Fact]
    public void Firefight_ProducesCasualtiesAndIsDeterministic()
    {
        (ulong Hash, int Shots, int Wounds) Run()
        {
            var scenario = SkirmishScenario.Create(Field(), 42, TestWeapons.SkirmishSet());
            int shots = 0, wounds = 0;
            for (int i = 0; i < 1200; i++)
            {
                scenario.Tick();
                foreach (var e in scenario.Sim.Step())
                {
                    if (e is ShotFired) shots++;
                    if (e is UnitWounded) wounds++;
                }
            }
            return (StateHash.Compute(scenario.Sim), shots, wounds);
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
        Assert.True(a.Shots > 10);
        Assert.True(a.Wounds > 0);
    }
}
