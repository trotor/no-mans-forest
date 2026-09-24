using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;

namespace Nmf.Content.Tests;

/// <summary>The real skirmish map and weapons fight for two minutes without errors, deterministically.</summary>
public class SkirmishFightTests
{
    [Fact]
    public void TwoMinuteFight_IsDeterministicAndHasShots()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));

        (ulong Hash, int Shots) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons);
            int shots = 0;
            for (int i = 0; i < 2400; i++)
            {
                scenario.Tick();
                shots += scenario.Sim.Step().Count(e => e is ShotFired);
            }
            return (StateHash.Compute(scenario.Sim), shots);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Shots > 0);
    }

    [Fact]
    public void ThreeMinuteFightWithGrenadesAndAnAdvance_IsDeterministic()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(root, "content", "core", "grenades"));

        (ulong Hash, int Shots, int Grenades) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons, grenades);
            int shots = 0, thrown = 0;
            for (int i = 0; i < 3600; i++)
            {
                if (i == 40)
                {
                    // Like a player would: send the Finns forward at the Soviet position.
                    var redLeader = scenario.Sim.Units.First(u => u.Side == Nmf.Sim.Units.Side.Red);
                    foreach (var blue in scenario.Sim.Units.Where(u => u.Side == Nmf.Sim.Units.Side.Blue))
                        scenario.Sim.Submit(Nmf.Sim.Units.Side.Blue, new Nmf.Sim.Orders.MoveOrder(blue.Id, redLeader.Position, Nmf.Sim.Units.MoveMode.Auto));
                }
                scenario.Tick();
                foreach (var e in scenario.Sim.Step())
                {
                    if (e is ShotFired) shots++;
                    if (e is GrenadeThrown) thrown++;
                }
            }
            return (StateHash.Compute(scenario.Sim), shots, thrown);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Shots > 0);
        var loaded = SkirmishScenario.Create(map, 1942, weapons, grenades).Sim.Units;
        Assert.All(loaded, u => Assert.Equal(Nmf.Sim.Combat.CombatRules.GrenadesPerSoldier, u.Grenades));
    }
}
