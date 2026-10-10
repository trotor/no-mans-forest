using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

/// <summary>Spec 2026-10-10-winter-design §3: tracks in the snow.</summary>
public class TrackTests
{
    private const int Second = SimConstants.TicksPerSecond;

    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    private static Simulation Field(bool winter, ulong seed = 1)
    {
        var sim = new Simulation(new GridMap(300, 300, ["none"]), seed);
        if (winter)
            sim.SetWinter(40);
        return sim;
    }

    private static Unit Man(Simulation sim, Side side, Vec2 at)
    {
        var u = sim.SpawnUnit(side, at, 8, TestWeapons.Rifle());
        u.FirePolicy = FirePolicy.HoldFire;
        return u;
    }

    /// <summary>A Finn walks from <paramref name="from"/> to <paramref name="to"/> and is then gone (fallen far off).</summary>
    private static void Trail(Simulation sim, Vec2 from, Vec2 to)
    {
        var finn = Man(sim, Side.Blue, from);
        sim.Submit(Side.Blue, new MoveOrder(finn.Id, to, MoveMode.Walk));
        for (int i = 0; i < 300 * Second && (i < 5 || finn.MoveTarget is not null); i++)
            sim.Step();
        finn.Wound = WoundLevel.Dead;
        finn.Position = M(290, 290);
    }

    [Fact]
    public void Tracks_OnlyInWinter_NotByTheCarried()
    {
        var summer = Field(false);
        Trail(summer, M(50, 50), M(80, 50));
        Assert.Empty(summer.Tracks);

        var winter = Field(true);
        Trail(winter, M(50, 50), M(80, 50));
        Assert.InRange(winter.Tracks.Count, 28, 40);
        Assert.All(winter.Tracks.Values, t => Assert.Equal(Side.Blue, t.Side));
        Assert.True(winter.Tracks.ContainsKey(winter.Map.Width * 50 + 65));
    }

    [Fact]
    public void CalmSentry_NoticesFreshTracks()
    {
        var sim = Field(true);
        var sentry = Man(sim, Side.Red, M(100, 100));
        Trail(sim, M(60, 115), M(140, 115)); // 15 m from him, while he was not yet on the watch
        sim.EnableAlertness(Side.Red);
        for (int i = 0; i < 2 * Second; i++)
            sim.Step();
        Assert.Equal(Alertness.Suspicious, sentry.Alertness);
        Assert.InRange(sentry.AlarmAt.Y, 11_000, 12_000);
    }

    [Fact]
    public void OldTracks_Ignored()
    {
        var sim = Field(true);
        var sentry = Man(sim, Side.Red, M(100, 100));
        Trail(sim, M(60, 115), M(140, 115));
        for (int i = 0; i < 31 * 60 * Second; i++)
            sim.Step();
        sim.EnableAlertness(Side.Red);
        for (int i = 0; i < 2 * Second; i++)
            sim.Step();
        Assert.Equal(Alertness.Calm, sentry.Alertness);
    }

    [Fact]
    public void OwnTracks_Ignored()
    {
        var sim = Field(true);
        var sentry = Man(sim, Side.Red, M(100, 100));
        var comrade = Man(sim, Side.Red, M(60, 115));
        sim.Submit(Side.Red, new MoveOrder(comrade.Id, M(140, 115), MoveMode.Walk));
        for (int i = 0; i < 60 * Second; i++)
            sim.Step();
        comrade.Position = M(290, 10);
        sim.EnableAlertness(Side.Red);
        for (int i = 0; i < 2 * Second; i++)
            sim.Step();
        Assert.Equal(Alertness.Calm, sentry.Alertness);
    }

    [Fact]
    public void Search_FollowsATrail_ToItsEnd()
    {
        var sim = Field(true);
        Trail(sim, M(40, 150), M(180, 150));
        var party = Enumerable.Range(0, 3).Select(i => Man(sim, Side.Red, M(30 + i * 3, 140))).ToList();
        var zone = new MapZone("far", "mission", M(250, 10), M(290, 50)); // the grid would take them far from the trail
        var search = new SearchBehavior(party.Select(m => m.Id).ToList(), zone, _ => { });
        long nearest = long.MaxValue;
        for (int i = 0; i < 300 * Second; i++)
        {
            search.Tick(sim);
            sim.Step();
            nearest = Math.Min(nearest, party.Min(m => (m.Position - M(180, 150)).LengthSquared));
        }
        Assert.True(nearest <= 600L * 600, $"they came no nearer than {IntMath.Isqrt(nearest) / 100} m to the trail's end");
    }

    [Fact]
    public void SameSeed_SameTracks()
    {
        static ulong Hash()
        {
            var sim = Field(true, seed: 4);
            Trail(sim, M(40, 150), M(120, 160));
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
    }
}
