using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class WeaponDefTests
{
    [Theory]
    [InlineData(0, 1, 100, "magazine")]
    [InlineData(5, 0, 100, "burst")]
    [InlineData(5, 1, 101, "lethality")]
    public void Validated_RejectsBadValues(int magazine, int burst, int lethality, string field)
    {
        var bad = new WeaponDef("x", "X", WeaponClass.Rifle, magazine, 1, burst, 0, 1, 1, 5, 10_000, lethality, 10, 100);
        var ex = Assert.Throws<ArgumentException>(() => bad.Validated());
        Assert.Contains(field, ex.Message);
        Assert.Contains("'x'", ex.Message);
    }

    [Fact]
    public void SpawnUnit_WithWeapon_StartsLoadedWithBaseMorale()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var rifleman = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7, TestWeapons.Rifle(magazine: 5));
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(150, 50), 7, TestWeapons.Smg(), isLeader: true);
        var unarmed = sim.SpawnUnit(Side.Red, new Vec2(250, 50), 7);

        Assert.Equal(5, rifleman.Ammo);
        Assert.Equal(CombatRules.BaseMorale, rifleman.Morale);
        Assert.Equal(CombatRules.LeaderMorale, leader.Morale);
        Assert.True(leader.IsLeader);
        Assert.Equal(100, leader.LeaderQualityPct);
        Assert.Null(unarmed.Weapon);
        Assert.True(unarmed.IsAlive);
        Assert.False(unarmed.IsOutOfAction);
        Assert.Equal(FirePolicy.FireAtWill, unarmed.FirePolicy);
        Assert.Equal(Unit.NeverShot, unarmed.LastShotTick);
    }

    [Fact]
    public void StateHash_ChangesWithCombatState()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7, TestWeapons.Rifle());
        var before = StateHash.Compute(sim);
        u.Suppression = 10;
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void SpareMagazines_MustNotBeNegative()
    {
        var ex = Assert.Throws<ArgumentException>(() => (TestWeapons.Rifle() with { SpareMagazines = -1 }).Validated());
        Assert.Contains("spare_magazines", ex.Message);
    }
}
