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
}
