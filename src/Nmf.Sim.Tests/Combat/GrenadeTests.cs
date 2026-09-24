using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class GrenadeTests
{
    private static readonly Vec2 ThrowerPos = new(550, 1050);

    private static (Simulation Sim, Unit Thrower, Unit Target) Setup(GridMap? map = null, Vec2? targetPos = null, GrenadeDef? grenade = null, ulong seed = 3)
    {
        var sim = new Simulation(map ?? new GridMap(60, 30, ["none"]), seed);
        var thrower = sim.SpawnUnit(Side.Blue, ThrowerPos, 7, null, grenade: grenade ?? GrenadeDefTests.Test());
        var target = sim.SpawnUnit(Side.Red, targetPos ?? new Vec2(2050, 1050), 7);
        return (sim, thrower, target);
    }

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < n; i++) all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void Throw_WithoutScatter_LandsOnTheTargetAfterTheThrowTicks()
    {
        var (sim, thrower, target) = Setup();
        GrenadeSystem.StartThrow(thrower, target);
        var events = StepN(sim, CombatRules.ThrowTicks);
        var thrown = Assert.Single(events.OfType<GrenadeThrown>());
        Assert.Equal(target.Position, thrown.To);
        Assert.Equal(CombatRules.ThrowTicks - 1, thrown.Tick);
        Assert.Equal(CombatRules.GrenadesPerSoldier - 1, thrower.Grenades);
        Assert.Equal(CombatAction.None, thrower.Action);
        Assert.Single(sim.Grenades);
    }

    [Fact]
    public void Explosion_WoundsMenAtTheBlastAndOnlySuppressesTheFarOnes()
    {
        // The target crouches once he sees the thrower, so a hit is likely (80 %) rather than certain: try a few seeds.
        int woundedNear = 0;
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var (sim, thrower, target) = Setup(seed: seed);
            var far = sim.SpawnUnit(Side.Red, new Vec2(2050, 1850), 7); // 8 m from the grenade: inside blast, outside lethal radius
            GrenadeSystem.StartThrow(thrower, target);
            var events = StepN(sim, CombatRules.ThrowTicks + 61);
            Assert.Contains(events, e => e is GrenadeExploded);
            Assert.Equal(WoundLevel.None, far.Wound);
            Assert.True(far.Suppression > 0 || far.MoraleState != MoraleState.Steady);
            Assert.Empty(sim.Grenades);
            if (target.Wound != WoundLevel.None) woundedNear++;
        }
        Assert.True(woundedNear >= 3, $"only {woundedNear} of 5 men at the blast were hit");
    }

    [Fact]
    public void HillBetween_ShieldsFromFragmentsAndHalvesSuppression()
    {
        var map = new GridMap(60, 30, ["none"]);
        for (int y = 0; y < 30; y++) map[new CellCoord(23, y)].GroundHeightCm = 300;
        var (sim, thrower, target) = Setup(map);
        var hidden = sim.SpawnUnit(Side.Red, new Vec2(2450, 1050), 7); // 4 m from the grenade, hill in between
        GrenadeSystem.StartThrow(thrower, target);
        StepN(sim, CombatRules.ThrowTicks + 61);
        Assert.Equal(WoundLevel.None, hidden.Wound);
    }

    [Fact]
    public void ProneMen_AreHitLessOftenThanStandingMen()
    {
        int Hits(Stance stance)
        {
            int hits = 0;
            for (ulong seed = 1; seed <= 60; seed++)
            {
                var (sim, thrower, target) = Setup(grenade: GrenadeDefTests.Test(lethality: 60), seed: seed);
                var victim = sim.SpawnUnit(Side.Red, new Vec2(2250, 1050), 7);
                victim.Stance = stance;
                GrenadeSystem.StartThrow(thrower, target);
                StepN(sim, CombatRules.ThrowTicks + 61);
                if (victim.Wound != WoundLevel.None) hits++;
            }
            return hits;
        }
        Assert.True(Hits(Stance.Prone) < Hits(Stance.Standing));
    }

    [Fact]
    public void CanThrowAt_RequiresRangeCoverSafetyGrenadesAndCooldown()
    {
        var (sim, thrower, target) = Setup(targetPos: new Vec2(2050, 1050));
        Assert.True(GrenadeSystem.CanThrowAt(sim, thrower, target, 0)); // 15 m: close enough to throw at anyone
        var openFar = sim.SpawnUnit(Side.Red, new Vec2(3050, 1050), 7);
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, openFar, 0)); // 25 m, standing in the open
        openFar.Stance = Stance.Prone;
        Assert.True(GrenadeSystem.CanThrowAt(sim, thrower, openFar, 0));
        openFar.Position = new Vec2(550, 2850);
        target.Stance = Stance.Prone;

        var tooClose = sim.SpawnUnit(Side.Red, new Vec2(1050, 1050), 7);
        tooClose.Stance = Stance.Prone;
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, tooClose, 0));
        var tooFar = sim.SpawnUnit(Side.Red, new Vec2(4550, 1050), 7);
        tooFar.Stance = Stance.Prone;
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, tooFar, 0));

        thrower.LastThrowTick = 0;
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, target, 50));
        thrower.LastThrowTick = Unit.NeverShot;

        var friend = sim.SpawnUnit(Side.Blue, new Vec2(2250, 1050), 7);
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, target, 0));
        friend.Position = new Vec2(550, 2850);
        Assert.True(GrenadeSystem.CanThrowAt(sim, thrower, target, 0));

        thrower.Grenades = 0;
        Assert.False(GrenadeSystem.CanThrowAt(sim, thrower, target, 0));
    }

    [Fact]
    public void TargetNextToHardCover_IsAGrenadeTarget()
    {
        var map = new GridMap(60, 30, ["none"]);
        map[new CellCoord(21, 10)] = new CellData(0, 120, 255, 230, 0, CellData.Impassable);
        var (sim, thrower, target) = Setup(map);
        Assert.True(GrenadeSystem.CanThrowAt(sim, thrower, target, 0));
    }

    [Fact]
    public void ThrowerHitWhileThrowing_NeverReleasesTheGrenade()
    {
        var (sim, thrower, target) = Setup();
        GrenadeSystem.StartThrow(thrower, target);
        sim.Step();
        Damage.SetWound(sim, thrower, WoundLevel.Incapacitated, sim.Tick, []);
        var events = StepN(sim, 100);
        Assert.DoesNotContain(events, e => e is GrenadeThrown);
        Assert.Equal(CombatRules.GrenadesPerSoldier, thrower.Grenades);
    }

    [Fact]
    public void SameSeed_SameExplosions()
    {
        ulong Run()
        {
            var (sim, thrower, target) = Setup(grenade: GrenadeDefTests.Test(scatter: 20, lethality: 60), seed: 9);
            GrenadeSystem.StartThrow(thrower, target);
            StepN(sim, 120);
            return StateHash.Compute(sim);
        }
        Assert.Equal(Run(), Run());
    }
}
