using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class CardBadgeTests
{
    private static Unit Man()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        return sim.SpawnUnit(Side.Blue, new Vec2(150, 150), 8, new WeaponDef("w", "Kivääri", WeaponClass.Rifle, 5, 4, 1, 0, 2, 10, 0, 30_000, 70, 80, 30_000));
    }

    [Fact]
    public void AFitMan_HasNoBadge()
    {
        Assert.Equal(new CardBadge("", BadgeTone.None), CardBadges.For(Man()));
    }

    [Fact]
    public void TheWorstOfHisTroubles_ShowsFirst()
    {
        var man = Man();
        man.Wound = WoundLevel.Light;
        Assert.Equal(new CardBadge("✚", BadgeTone.Warning), CardBadges.For(man));
        man.MoraleState = MoraleState.Pinned;
        Assert.Equal(new CardBadge("!", BadgeTone.Bad), CardBadges.For(man));
        man.Wound = WoundLevel.Serious;
        Assert.Equal(new CardBadge("✚", BadgeTone.Critical), CardBadges.For(man));
        man.MoraleState = MoraleState.Broken;
        Assert.Equal(new CardBadge("!!", BadgeTone.Critical), CardBadges.For(man));
        man.Wound = WoundLevel.Incapacitated;
        Assert.Equal(new CardBadge("✚", BadgeTone.Critical), CardBadges.For(man));
        man.Wound = WoundLevel.Dead;
        Assert.Equal(new CardBadge("✖", BadgeTone.Gone), CardBadges.For(man));
    }

    [Fact]
    public void OutOfAmmo_IsShown()
    {
        var man = Man();
        man.Ammo = 0;
        man.Magazines = 0;
        Assert.Equal(new CardBadge("∅", BadgeTone.Warning), CardBadges.For(man));
    }

    [Fact]
    public void TheMoraleBar_GoesFromGreenToRed()
    {
        Assert.Equal(BadgeTone.None, CardBadges.MoraleTone(800));
        Assert.Equal(BadgeTone.Warning, CardBadges.MoraleTone(450));
        Assert.Equal(BadgeTone.Critical, CardBadges.MoraleTone(200));
    }
}
