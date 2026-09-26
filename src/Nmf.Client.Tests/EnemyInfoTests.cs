using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class EnemyInfoTests
{
    private static WeaponDef Weapon(string name, WeaponClass kind) =>
        new("w", name, kind, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000);

    private static (Simulation Sim, Unit Own, Unit Enemy) Setup(WeaponDef? weapon, bool leader = false)
    {
        var sim = new Simulation(new GridMap(100, 100, ["none"]), 1);
        var own = sim.SpawnUnit(Side.Blue, new Vec2(1050, 9050), 8, Weapon("Kivääri M/39", WeaponClass.Rifle));
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(1050, 1050), 8, weapon, leader);
        return (sim, own, enemy);
    }

    [Fact]
    public void Leader_IsRecognised_WithHisWeapon_AndHowFarHeIs()
    {
        var (_, own, enemy) = Setup(Weapon("PPŠ-41", WeaponClass.Smg), leader: true);
        var fi = EnemyInfo.Describe(enemy, [own], "fi");
        Assert.StartsWith("Johtaja", fi[0]);
        Assert.Contains(fi, l => l.Contains("PPŠ-41"));
        Assert.Contains(fi, l => l.Contains("80 m"));
        var en = EnemyInfo.Describe(enemy, [own], "en");
        Assert.StartsWith("Leader", en[0]);
    }

    [Theory]
    [InlineData(WeaponClass.Rifle, "Kiväärimies")]
    [InlineData(WeaponClass.Smg, "Konepistoolimies")]
    [InlineData(WeaponClass.Lmg, "Pikakivääriampuja")]
    public void Soldier_IsNamedByHisWeapon(WeaponClass kind, string expected)
    {
        var (_, own, enemy) = Setup(Weapon("X", kind));
        Assert.Equal(expected, EnemyInfo.Describe(enemy, [own], "fi")[0]);
    }

    [Fact]
    public void WhatHeIsDoing_AndHowHeLooks_AreShown()
    {
        var (sim, own, enemy) = Setup(Weapon("Vintovka 91/30", WeaponClass.Rifle));
        sim.Submit(Side.Red, new MoveOrder(enemy.Id, new Vec2(8050, 1050), MoveMode.Run));
        sim.Step();
        Assert.Contains(EnemyInfo.Describe(enemy, [own], "fi"), l => l.Contains("Juoksee"));

        enemy.Wound = WoundLevel.Light;
        enemy.MoraleState = MoraleState.Pinned;
        var lines = EnemyInfo.Describe(enemy, [own], "fi");
        Assert.Contains(lines, l => l.Contains("haavoittunut", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.Contains("lamautunut", StringComparison.OrdinalIgnoreCase));

        enemy.MoraleState = MoraleState.Broken;
        Assert.Contains(EnemyInfo.Describe(enemy, [own], "fi"), l => l.Contains("Pakenee"));
    }

    [Fact]
    public void Fallen_IsShownAsFallen_AndWhetherHeHasBeenSearched()
    {
        var (_, own, enemy) = Setup(Weapon("PPŠ-41", WeaponClass.Smg));
        enemy.Wound = WoundLevel.Dead;
        var lines = EnemyInfo.Describe(enemy, [own], "fi");
        Assert.Equal("Kaatunut", lines[0]);
        Assert.Contains(lines, l => l.Contains("Tutkimatta"));
        enemy.Looted = true;
        Assert.Contains(EnemyInfo.Describe(enemy, [own], "fi"), l => l.Contains("Tutkittu"));
        Assert.DoesNotContain(EnemyInfo.Describe(enemy, [own], "fi"), l => l.Contains("Juoksee") || l.Contains("Seisoo"));
    }

    [Fact]
    public void Unarmed_AndCaptured_AreSaidSo()
    {
        var (_, own, enemy) = Setup(null);
        Assert.Equal("Aseeton sotilas", EnemyInfo.Describe(enemy, [own], "fi")[0]);
        enemy.IsCaptured = true;
        Assert.Equal("Antautunut", EnemyInfo.Describe(enemy, [own], "fi")[0]);
    }

    [Fact]
    public void Distance_IsToTheNearestOwnManStillInAction()
    {
        var (sim, own, enemy) = Setup(null);
        var fallen = sim.SpawnUnit(Side.Blue, new Vec2(1050, 2050), 8);
        fallen.Wound = WoundLevel.Dead;
        Assert.Contains(EnemyInfo.Describe(enemy, [own, fallen], "fi"), l => l.Contains("80 m"));
    }
}
