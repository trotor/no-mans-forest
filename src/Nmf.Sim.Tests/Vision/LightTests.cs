using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

/// <summary>Spec 2026-10-10-winter-design §4: dusk, night, snow's glow and a flare's light.</summary>
public class LightTests
{
    private const int Second = SimConstants.TicksPerSecond;

    /// <summary>Ticks until the Soviet at the top makes out the crouching Finn 250 m off.</summary>
    private static long Spotted(LightLevel? light, bool winter = false, Action<Simulation, Unit, Unit>? setup = null)
    {
        var sim = new Simulation(new GridMap(100, 400, ["none"]), 1);
        var red = sim.SpawnUnit(Side.Red, new Vec2(5_050, 1_050), 8, TestWeapons.Rifle());
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(5_050, 26_050), 8, TestWeapons.Rifle());
        blue.Stance = Stance.Crouching;
        foreach (var u in sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        if (winter)
            sim.SetWinter(40);
        if (light is { } l)
            sim.SetLight(l);
        setup?.Invoke(sim, red, blue);
        for (int i = 0; i < 600 * Second; i++)
        {
            sim.Step();
            if (sim.Knowledge(Side.Red).LevelOf(blue.Id) == ContactLevel.Visible)
                return sim.Tick;
        }
        return long.MaxValue;
    }

    [Fact]
    public void Levels()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        Assert.Equal(100, sim.LightPct);
        sim.SetLight(LightLevel.Dusk);
        Assert.Equal(55, sim.LightPct);
        sim.SetWinter(40);
        Assert.Equal(70, sim.LightPct); // the snow brightens it
        sim.SetLight(LightLevel.Night);
        Assert.Equal(40, sim.LightPct);
    }

    [Fact]
    public void Dusk_Night_SlowerSpotting()
    {
        long day = Spotted(null);
        long dusk = Spotted(LightLevel.Dusk);
        long night = Spotted(LightLevel.Night);
        Assert.True(day < dusk && dusk < night, $"day {day}, dusk {dusk}, night {night}");
        Assert.Equal(day, Spotted(LightLevel.Day));
    }

    [Fact]
    public void SnowBrightensTheNight()
    {
        Assert.True(Spotted(LightLevel.Night, winter: true) < Spotted(LightLevel.Night));
    }

    [Fact]
    public void Flare_LightsTargetsWithin200m_For20s()
    {
        var sim = new Simulation(new GridMap(100, 400, ["none"]), 1);
        sim.SetLight(LightLevel.Night);
        sim.AddFlareLight(new Vec2(5_000, 10_000), 0);
        Assert.Equal(100, sim.LightAt(new Vec2(5_000, 25_000)));   // 150 m: lit
        Assert.Equal(25, sim.LightAt(new Vec2(5_000, 35_000)));    // 250 m: dark
        for (int i = 0; i < 21 * Second; i++)
            sim.Step();
        Assert.Equal(25, sim.LightAt(new Vec2(5_000, 25_000)));    // burnt out
    }

    [Fact]
    public void Flare_ATargetLit_IsSeenSooner_ALitObserverSeesNoBetter()
    {
        long night = Spotted(LightLevel.Night);
        long targetLit = Spotted(LightLevel.Night, setup: (sim, _, blue) => sim.AddFlareLight(blue.Position, 0, untilTick: 600 * Second));
        long observerLit = Spotted(LightLevel.Night, setup: (sim, red, _) => sim.AddFlareLight(red.Position, 0, untilTick: 600 * Second));
        Assert.True(targetLit < night);
        Assert.Equal(night, observerLit);
    }
}
