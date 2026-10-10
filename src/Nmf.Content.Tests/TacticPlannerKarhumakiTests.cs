using System.Diagnostics;
using Nmf.Content.Missions;
using Nmf.Content.Tactics;
using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim.Core;
using Nmf.Sim.Mission;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Content.Tests;

/// <summary>The planner on the real 1 km map, from where the Iskuosasto attack meets the knoll post.</summary>
[Collection(TimingCollection.Name)]
public class TacticPlannerKarhumakiTests
{
    [Fact]
    public void IskuosastoContact_PlansFireAndMovement_InTime()
    {
        string content = Path.Combine(CoreContentTests.RepoRoot(), "content", "core");
        var spec = MissionLoader.Load(Path.Combine(content, "missions", "iskuosasto"));
        var map = TmxMapLoader.Load(Path.Combine(content, "maps", spec.Map + ".tmx"));
        var scenario = MissionScenario.Create(map, spec, WeaponLoader.LoadDirectory(Path.Combine(content, "weapons")),
            GrenadeLoader.LoadDirectory(Path.Combine(content, "grenades")), 1);
        var tactics = TacticLoader.LoadDirectory(Path.Combine(content, "tactics"));
        var sim = scenario.Sim;

        // The Finns 55 m south-west of the knoll, on the plan's attack route; the whole post seen.
        var blues = sim.Units.Where(u => u.Side == Side.Blue).ToList();
        var near = new Vec2(410 * 100 + 50, 604 * 100 + 50);
        int placed = 0;
        for (int i = 0; placed < blues.Count && i < 100; i++)
        {
            var spot = near + new Vec2((i % 10 - 5) * 200, (i / 10 - 5) * 200);
            if (map.Contains(spot) && map.CellAt(spot).IsPassable)
                blues[placed++].Position = spot;
        }
        var post = sim.Units.Where(u => u.Side == Side.Red && u.Squad == sim.Units.First(r => r.IsLeader && r.Side == Side.Red).Squad).ToList();
        foreach (var red in post)
        {
            var c = sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
            c.Level = ContactLevel.Visible;
            c.Position = red.Position;
        }
        var belov = post.First(r => r.IsLeader);
        Pathfinder.Reachable(map, near, belov.Position); // the map's regions are worked out once per map, not per plan

        var clock = Stopwatch.StartNew();
        var plan = TacticPlanner.Propose(sim, Side.Blue, blues.Select(b => b.Id).ToList(), belov.Id, tactics[0]);
        clock.Stop();

        Assert.NotNull(plan);
        Assert.Equal("fire_and_movement", plan.TacticId);
        Assert.NotEmpty(plan.Route);
        Assert.True(clock.ElapsedMilliseconds < TimingCollection.Budget(500), $"planning took {clock.ElapsedMilliseconds} ms");
    }
}
