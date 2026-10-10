using Nmf.Sim.AI;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

/// <summary>Spec 2026-10-10-alertness-design §5: a search party combing an area.</summary>
public class SearchBehaviorTests
{
    /// <summary>Three Soviets at (20, 20) m; the area to search is the 60 m square from (100, 100) m: nine points 20 m apart.</summary>
    private static (Simulation Sim, List<Unit> Men, MapZone Zone) Field()
    {
        var sim = new Simulation(new GridMap(200, 200, ["none"]), 1);
        var men = Enumerable.Range(0, 3).Select(i => sim.SpawnUnit(Side.Red, new Vec2(2_050 + i * 300, 2_050), 8, TestWeapons.Rifle())).ToList();
        sim.EnableAlertness(Side.Red);
        return (sim, men, new MapZone("wreck", "mission", new Vec2(10_000, 10_000), new Vec2(16_000, 16_000)));
    }

    [Fact]
    public void Search_CombsTheArea_PointByPoint()
    {
        var (sim, men, zone) = Field();
        var search = new SearchBehavior(men.Select(m => m.Id).ToList(), zone, _ => { });
        Vec2[] points = [.. from y in new[] { 11_000, 13_000, 15_000 } from x in new[] { 11_000, 13_000, 15_000 } select new Vec2(x, y)];
        var seen = new HashSet<Vec2>();
        for (int i = 0; i < 8_000 && seen.Count < points.Length; i++)
        {
            search.Tick(sim);
            sim.Step();
            foreach (var p in points)
                if (men.Any(m => (m.Position - p).LengthSquared <= 600L * 600))
                    seen.Add(p);
        }
        Assert.Equal(points.Length, seen.Count);
        Assert.False(search.Done);
    }

    [Fact]
    public void Search_HandsOver_InAFight_AndOrdersNoMore()
    {
        var (sim, men, zone) = Field();
        List<Unit>? handed = null;
        var search = new SearchBehavior(men.Select(m => m.Id).ToList(), zone, ms => handed = ms.ToList());
        for (int i = 0; i < 200; i++) { search.Tick(sim); sim.Step(); }
        men[1].Alertness = Alertness.Combat; // he has seen the enemy
        search.Tick(sim);
        Assert.True(search.Done);
        Assert.Equal(men, handed);
        sim.Step();
        int orders = sim.OrderLog.Count;
        for (int i = 0; i < 400; i++)
        {
            foreach (var m in men)
                Movement.ClearPath(m); // idle: a search still going would send them on
            search.Tick(sim);
            sim.Step();
        }
        Assert.Equal(orders, sim.OrderLog.Count);
    }

    [Fact]
    public void Search_SecondRound_StartsFromWhereTheyAre()
    {
        // A strip of four points 20, 40, 60, 80 m in a summer field: round one from the middle, round two from the end they reached.
        var sim = new Simulation(new GridMap(200, 200, ["none"]), 1);
        var man = sim.SpawnUnit(Side.Red, new Vec2(1_050, 1_050), 8, TestWeapons.Rifle());
        var search = new SearchBehavior([man.Id], new MapZone("strip", "mission", new Vec2(1_000, 1_000), new Vec2(9_000, 3_000)), _ => { });
        var targets = new List<int>();
        for (int i = 0; i < 20_000 && targets.Count < 7; i++)
        {
            search.Tick(sim);
            sim.Step();
            if (man.MoveTarget is { } to && (targets.Count == 0 || targets[^1] != to.X))
                targets.Add(to.X);
        }
        Assert.Equal([4_000, 2_000, 6_000, 8_000, 6_000, 4_000, 2_000], targets.Take(7));
    }
}
