using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

/// <summary>Spec 2026-10-10-alertness-design: each man's own alertness, and how the alarm spreads.</summary>
public class AlertnessTests
{
    private const int Second = SimConstants.TicksPerSecond;

    private static Simulation Field(int widthM = 400, int heightM = 400, ulong seed = 1) => new(new GridMap(widthM, heightM, ["none"]), seed);

    /// <summary>A tall thicket nobody sees through, between x0..x1 and y0..y1 metres.</summary>
    private static void Wall(Simulation sim, int x0, int x1, int y0, int y1)
    {
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                ref var cell = ref sim.Map[new CellCoord(x, y)];
                cell.ObstacleHeightCm = 1_500;
                cell.ConcealmentPerM = 255;
            }
    }

    private static Unit Man(Simulation sim, Side side, int xM, int yM, FirePolicy policy = FirePolicy.HoldFire)
    {
        var u = sim.SpawnUnit(side, new Vec2(xM * 100 + 50, yM * 100 + 50), 8, TestWeapons.Rifle());
        u.FirePolicy = policy;
        return u;
    }

    private static List<SimEvent> Run(Simulation sim, int ticks, Func<bool>? until = null)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < ticks && until?.Invoke() != true; i++)
            events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void Disabled_NothingChanges()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100, FirePolicy.FireAtWill);
        Man(sim, Side.Blue, 100, 200);
        var events = Run(sim, 20 * Second);
        Assert.Equal(Alertness.Combat, red.Alertness);
        Assert.False(sim.AlertnessOn(Side.Red));
        Assert.DoesNotContain(events, e => e is AlertnessChanged or AlarmRaised);
        Assert.Contains(events, e => e is ShotFired s && s.Shooter == red.Id);
    }

    [Fact]
    public void Enabled_TheSideStartsCalm_TheOtherNot()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100);
        var blue = Man(sim, Side.Blue, 300, 300);
        sim.EnableAlertness(Side.Red);
        Assert.Equal(Alertness.Calm, red.Alertness);
        Assert.Equal(Alertness.Combat, blue.Alertness);
        Assert.Equal(Alertness.Calm, Man(sim, Side.Red, 50, 50).Alertness); // a man joining later too
    }

    [Fact]
    public void MovementHeard_Suspicious()
    {
        var sim = Field();
        Wall(sim, 0, 400, 110, 120);
        var red = Man(sim, Side.Red, 100, 100);
        var blue = Man(sim, Side.Blue, 60, 125);
        sim.EnableAlertness(Side.Red);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(14_050, 12_550), MoveMode.Walk));
        var events = Run(sim, 20 * Second, () => red.Alertness != Alertness.Calm);
        Assert.Equal(Alertness.Suspicious, red.Alertness);
        Assert.Contains(events, e => e is AlertnessChanged { Level: Alertness.Suspicious } c && c.Unit == red.Id);
    }

    [Fact]
    public void Shot_Alerts()
    {
        var sim = Field();
        Wall(sim, 0, 400, 110, 120);
        var red = Man(sim, Side.Red, 100, 100);
        var blue = Man(sim, Side.Blue, 100, 300, FirePolicy.FireAtWill);
        sim.EnableAlertness(Side.Red);
        sim.Submit(Side.Blue, new AreaFireOrder(blue.Id, new Vec2(30_050, 35_050)));
        Run(sim, 20 * Second, () => red.Alertness != Alertness.Calm);
        Assert.Equal(Alertness.Alerted, red.Alertness);
    }

    [Fact]
    public void Shout_Chain_EachOnce()
    {
        var sim = Field(800, 100);
        var men = Enumerable.Range(0, 20).Select(i => Man(sim, Side.Red, 10 + i * 40, 50)).ToList();
        var dead = Man(sim, Side.Red, 10, 70);
        sim.EnableAlertness(Side.Red);
        dead.Wound = WoundLevel.Dead;
        var events = Run(sim, 60 * Second);
        Assert.All(men, m => Assert.True(m.Alertness >= Alertness.Alerted, $"{m.Id} stayed {m.Alertness}"));
        var shouts = events.OfType<AlarmRaised>().Where(a => a.Kind == AlarmKind.Shout).ToList();
        Assert.Equal(20, shouts.Count);
        Assert.Equal(20, shouts.Select(s => s.By).Distinct().Count());
    }

    [Fact]
    public void Calm_DoesNotFire_SurpriseDelaysTheFirstShot()
    {
        static (Alertness Before, long Combat, long FirstShot) Engage(bool alertness)
        {
            var sim = Field();
            var red = Man(sim, Side.Red, 100, 100, FirePolicy.FireAtWill);
            Man(sim, Side.Blue, 100, 180);
            if (alertness)
                sim.EnableAlertness(Side.Red);
            var before = Alertness.Calm;
            long combat = -1;
            foreach (var e in Run(sim, 60 * Second))
            {
                if (e is AlertnessChanged c && c.Unit == red.Id)
                {
                    if (c.Level == Alertness.Combat)
                        combat = c.Tick;
                    else
                        before = c.Level;
                }
                if (e is ShotFired s && s.Shooter == red.Id)
                    return (before, combat, s.Tick);
            }
            return (before, combat, long.MaxValue);
        }
        var calm = Engage(true);
        var ready = Engage(false);
        Assert.True(calm.Combat >= 0, "never went to combat");
        int surprise = calm.Before == Alertness.Calm ? 2 * Second : 1 * Second; // from calm 2 s, from wary 1 s
        Assert.True(calm.FirstShot >= calm.Combat + surprise, $"fired at {calm.FirstShot}, combat at {calm.Combat} from {calm.Before}");
        Assert.True(calm.FirstShot > ready.FirstShot, "a calm man fired as soon as a ready one");
    }

    [Fact]
    public void UnderFire_FromCalm_TwoSecondsBeforeHeFires()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100, FirePolicy.FireAtWill);
        sim.EnableAlertness(Side.Red);
        red.Suppression = 300;
        sim.Step();
        Assert.Equal(Alertness.Combat, red.Alertness);
        Assert.Equal(AlertnessSystem.SurpriseCalmTicks, red.ReadyTick);
    }

    [Fact]
    public void Suspicious_CalmsAfter30s()
    {
        var sim = Field();
        Wall(sim, 0, 400, 110, 120);
        var red = Man(sim, Side.Red, 100, 100);
        var blue = Man(sim, Side.Blue, 100, 125);
        sim.EnableAlertness(Side.Red);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(10_550, 12_550), MoveMode.Walk));
        Run(sim, 10 * Second, () => red.Alertness == Alertness.Suspicious);
        Assert.Equal(Alertness.Suspicious, red.Alertness);
        long since = sim.Tick;
        var events = Run(sim, 40 * Second, () => red.Alertness == Alertness.Calm);
        Assert.Equal(Alertness.Calm, red.Alertness);
        Assert.InRange(sim.Tick - since, 25 * Second, 35 * Second);
        Assert.Contains(events, e => e is AlertnessChanged { Level: Alertness.Calm });
    }

    [Fact]
    public void FallenComrade_Alerts()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100);
        var other = Man(sim, Side.Red, 100, 130);
        sim.EnableAlertness(Side.Red);
        other.Wound = WoundLevel.Dead;
        Run(sim, 2 * Second);
        Assert.Equal(Alertness.Alerted, red.Alertness);
        Assert.Equal(other.Position, red.AlarmAt);
    }

    [Fact]
    public void Glimpse_Suspicious_BeforeHeIsSeen()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 10);
        var blue = Man(sim, Side.Blue, 100, 390);
        blue.Stance = Stance.Prone;
        sim.EnableAlertness(Side.Red);
        Run(sim, 120 * Second, () => red.Alertness != Alertness.Calm);
        Assert.Equal(Alertness.Suspicious, red.Alertness);
        Assert.NotEqual(ContactLevel.Visible, sim.Knowledge(Side.Red).LevelOf(blue.Id));
    }

    [Fact]
    public void CalmSpotsSlower()
    {
        static long Spotted(bool alertness)
        {
            var sim = Field();
            Man(sim, Side.Red, 100, 10);
            var blue = Man(sim, Side.Blue, 100, 330);
            blue.Stance = Stance.Crouching;
            if (alertness)
                sim.EnableAlertness(Side.Red);
            Run(sim, 300 * Second, () => sim.Knowledge(Side.Red).LevelOf(blue.Id) == ContactLevel.Visible);
            return sim.Tick;
        }
        Assert.True(Spotted(true) > Spotted(false));
    }

    [Fact]
    public void UnderFire_Combat()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100);
        sim.EnableAlertness(Side.Red);
        red.Suppression = 100;
        Run(sim, VisionRules.IntervalTicks + 1);
        Assert.Equal(Alertness.Combat, red.Alertness);
    }

    [Fact]
    public void AlertOrder_Raises_WithoutAShout()
    {
        var sim = Field();
        var red = Man(sim, Side.Red, 100, 100);
        var near = Man(sim, Side.Red, 100, 120);
        sim.EnableAlertness(Side.Red);
        sim.Submit(Side.Red, new AlertOrder(red.Id, Alertness.Alerted));
        var events = Run(sim, 5 * Second);
        Assert.Equal(Alertness.Alerted, red.Alertness);
        Assert.Equal(Alertness.Calm, near.Alertness);
        Assert.DoesNotContain(events, e => e is AlarmRaised);
        sim.Submit(Side.Red, new AlertOrder(red.Id, Alertness.Calm)); // never down
        Run(sim, 2);
        Assert.Equal(Alertness.Alerted, red.Alertness);
    }

    [Fact]
    public void SameSeed_SameAlarm()
    {
        static ulong Hash()
        {
            var sim = Field(seed: 5);
            Wall(sim, 0, 400, 110, 120);
            for (int i = 0; i < 6; i++)
                Man(sim, Side.Red, 60 + i * 30, 100, FirePolicy.FireAtWill);
            var blue = Man(sim, Side.Blue, 100, 300, FirePolicy.FireAtWill);
            sim.EnableAlertness(Side.Red);
            sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(10_050, 12_550), MoveMode.Walk));
            Run(sim, 90 * Second);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Hash(), Hash());
    }
}
