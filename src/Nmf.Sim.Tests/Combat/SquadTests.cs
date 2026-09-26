using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Spec 2026-09-26-squads-area-fire-design §2.2: a leader leads his own squad only.</summary>
public class SquadTests
{
    /// <summary>Two squads side by side: squad 0 (leader first) at x 10–13 m, squad 1 (leader first) at x 16–19 m.</summary>
    private static (Simulation Sim, List<Unit> First, List<Unit> Second) Setup()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        List<Unit> Squad(int squad, int x0) => Enumerable.Range(0, 3).Select(i =>
        {
            var u = sim.SpawnUnit(Side.Blue, new Vec2(x0 + i * 100, 3050), 8, TestWeapons.Rifle(), isLeader: i == 0);
            u.Squad = squad;
            return u;
        }).ToList();
        return (sim, Squad(0, 1050), Squad(1, 1650));
    }

    [Fact]
    public void ALeader_LeadsOnlyHisOwnSquad()
    {
        var (sim, first, second) = Setup();
        Assert.Same(first[0], MoraleSystem.LeaderInRange(sim, first[2]));
        Assert.Same(second[0], MoraleSystem.LeaderInRange(sim, second[1]));
        second[0].Wound = WoundLevel.Dead;
        Assert.Null(MoraleSystem.LeaderInRange(sim, second[1])); // the other squad's leader is no leader of his
    }

    [Fact]
    public void WhenALeaderFalls_OneOfHisOwnMenTakesOver_AndOnlyHisSquadIsShaken()
    {
        var (sim, first, second) = Setup();
        second[1].Position = first[0].Position + new Vec2(50, 0); // the nearest man belongs to the other squad
        var events = new List<SimEvent>();
        Damage.SetWound(sim, first[0], WoundLevel.Dead, 0, events);
        var change = Assert.Single(events.OfType<LeaderChanged>());
        Assert.Contains(sim.FindUnit(change.Leader)!, first);
        Assert.True(second[0].IsLeader);
        Assert.Equal(1, second.Count(u => u.IsLeader));
        // Both saw him fall; only his own squad lost its leader.
        Assert.Equal(second[2].Morale - CombatRules.LeaderLossMoraleLoss, first[2].Morale);
    }
}
