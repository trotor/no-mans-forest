using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class LootTests
{
    private static readonly WeaponDef Rifle = TestWeapons.Rifle(magazine: 5);
    private static readonly WeaponDef Smg = TestWeapons.Smg();

    private static (Simulation Sim, Unit Looter, Unit Body) Setup(WeaponDef? looterWeapon, WeaponDef? bodyWeapon,
        GrenadeDef? looterGrenade = null, GrenadeDef? bodyGrenade = null)
    {
        var sim = new Simulation(new GridMap(40, 20, ["none"]), 1);
        var looter = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, looterWeapon, grenade: looterGrenade);
        var body = sim.SpawnUnit(Side.Red, new Vec2(1150, 1050), 7, bodyWeapon, grenade: bodyGrenade);
        body.Wound = WoundLevel.Dead;
        return (sim, looter, body);
    }

    private static UnitLooted Loot(Simulation sim, Unit looter, Unit body)
    {
        var events = new List<SimEvent>();
        LootSystem.Transfer(looter, body, sim.Tick, events);
        return Assert.IsType<UnitLooted>(Assert.Single(events));
    }

    [Fact]
    public void SameWeapon_TakesSpareAndFullMagazines()
    {
        var (sim, looter, body) = Setup(Rifle, Rifle);
        body.Magazines = 3;
        var e = Loot(sim, looter, body);
        Assert.Equal(4, e.Magazines); // 3 spares + the full one in his rifle
        Assert.Equal(4 + 4, looter.Magazines);
        Assert.Equal(0, body.Magazines);
        Assert.Equal(0, body.Ammo);
        Assert.True(body.Looted);
    }

    [Fact]
    public void PartialMagazine_IsLeft()
    {
        var (sim, looter, body) = Setup(Rifle, Rifle);
        body.Magazines = 0;
        body.Ammo = 2;
        Assert.Equal(0, Loot(sim, looter, body).Magazines);
        Assert.Equal(2, body.Ammo);
        Assert.False(body.Looted); // a loaded weapon is still there for a man who runs dry
    }

    [Fact]
    public void OtherWeapon_NoAmmoTaken()
    {
        var (sim, looter, body) = Setup(Rifle, Smg);
        var e = Loot(sim, looter, body);
        Assert.Equal(0, e.Magazines);
        Assert.Null(e.WeaponTaken);
        Assert.Same(Rifle, looter.Weapon);
        Assert.False(body.Looted); // the SMG and its drums are left for someone who can use them
    }

    [Fact]
    public void ASearch_IsRememberedBySide_EvenWhenSomethingIsLeft()
    {
        var (sim, looter, body) = Setup(Rifle, Smg);
        Assert.False(body.WasSearchedBy(Side.Blue));
        Loot(sim, looter, body);
        Assert.False(body.Looted); // the SMG is left for another man
        Assert.True(body.WasSearchedBy(Side.Blue));
        Assert.False(body.WasSearchedBy(Side.Red));
    }

    [Fact]
    public void BodyEmptied_IsLooted()
    {
        var (sim, looter, body) = Setup(Rifle, Rifle);
        body.AddItem(new Item("x", "X"));
        Loot(sim, looter, body);
        Assert.True(body.Looted);
    }

    [Fact]
    public void SameGrenades_Taken()
    {
        var g = GrenadeDefTests.Test();
        var (sim, looter, body) = Setup(Rifle, Rifle, g, g);
        Assert.Equal(2, Loot(sim, looter, body).Grenades);
        Assert.Equal(4, looter.Grenades);
        Assert.Equal(0, body.Grenades);
    }

    [Fact]
    public void OtherGrenadesWhenOwnGone_TakenAndTypeSwitches()
    {
        var own = GrenadeDefTests.Test();
        var other = own with { Id = "other" };
        var (sim, looter, body) = Setup(Rifle, Rifle, own, other);
        looter.Grenades = 0;
        Assert.Equal(2, Loot(sim, looter, body).Grenades);
        Assert.Same(other, looter.GrenadeType);
        Assert.Equal(2, looter.Grenades);
    }

    [Fact]
    public void OtherGrenadesWhileHoldingOwn_Left()
    {
        var own = GrenadeDefTests.Test();
        var (sim, looter, body) = Setup(Rifle, Rifle, own, own with { Id = "other" });
        Assert.Equal(0, Loot(sim, looter, body).Grenades);
        Assert.Equal(2, body.Grenades);
    }

    [Fact]
    public void EmptyWeapon_SwappedForLoadedOne()
    {
        var (sim, looter, body) = Setup(Rifle, Smg);
        looter.Ammo = 0;
        looter.Magazines = 0;
        looter.Action = CombatAction.Reloading;
        var e = Loot(sim, looter, body);
        Assert.Equal(Smg.Id, e.WeaponTaken);
        Assert.Same(Smg, looter.Weapon);
        Assert.Equal(Smg.MagazineSize, looter.Ammo);
        Assert.Equal(Smg.SpareMagazines, looter.Magazines);
        Assert.Equal(CombatAction.None, looter.Action);
        Assert.Same(Rifle, body.Weapon);
        Assert.Equal(0, body.Ammo);
    }

    [Fact]
    public void UnarmedMan_TakesTheWeapon()
    {
        var (sim, looter, body) = Setup(null, Smg);
        Assert.Equal(Smg.Id, Loot(sim, looter, body).WeaponTaken);
        Assert.Same(Smg, looter.Weapon);
        Assert.Null(body.Weapon);
    }

    [Fact]
    public void Items_MoveToTheLooter()
    {
        var (sim, looter, body) = Setup(Rifle, Rifle);
        var orders = new Item("soviet_orders", "Soviet orders");
        body.AddItem(orders);
        Assert.Equal(new[] { orders }, Loot(sim, looter, body).Items);
        Assert.Equal(new[] { orders }, looter.Items);
        Assert.Empty(body.Items);
    }

    [Fact]
    public void LootedBody_GivesNothingTwice()
    {
        var (sim, looter, body) = Setup(Rifle, Rifle);
        Loot(sim, looter, body);
        var again = Loot(sim, looter, body);
        Assert.Equal(0, again.Magazines);
        Assert.Equal(0, again.Grenades);
        Assert.Null(again.WeaponTaken);
        Assert.Empty(again.Items);
    }

    [Fact]
    public void HasUsefulLoot_OnlyForAmmoHeCanUse()
    {
        var (_, looter, body) = Setup(Rifle, Smg);
        Assert.False(LootSystem.HasUsefulLoot(looter, body));
        var (_, looter2, body2) = Setup(Rifle, Rifle);
        Assert.True(LootSystem.HasUsefulLoot(looter2, body2));
        body2.Magazines = 0;
        body2.Ammo = 1;
        Assert.False(LootSystem.HasUsefulLoot(looter2, body2));
        looter.Ammo = 0;
        looter.Magazines = 0;
        Assert.True(LootSystem.HasUsefulLoot(looter, body)); // out of ammo: a loaded SMG will do
        body.Looted = true;
        Assert.False(LootSystem.HasUsefulLoot(looter, body));
    }
}
