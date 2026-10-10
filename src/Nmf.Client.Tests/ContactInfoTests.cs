using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class ContactInfoTests
{
    private static readonly WeaponDef Mosin = new("mosin_9130", "Mosin 91/30", WeaponClass.Rifle, 5, 30, 1, 0, 24, 80, 6, 30_000, 45, 80, 30_000);

    /// <summary>An own man at (10, 90) m and an enemy whose contact is at (10, 7) m, last updated at tick 0; now tick 240 (12 s).</summary>
    private static (Simulation Sim, Unit Own, Unit Enemy, Contact Contact) Setup(ContactLevel level, bool shots = false)
    {
        var sim = new Simulation(new GridMap(100, 100, ["none"]), 1);
        var own = sim.SpawnUnit(Side.Blue, new Vec2(1050, 9050), 8, Mosin);
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(1050, 1050), 8, Mosin);
        var contact = sim.Knowledge(Side.Blue).GetOrAdd(enemy.Id);
        contact.Level = level;
        contact.Position = new Vec2(1050, 750);
        contact.LastUpdateTick = 0;
        contact.HeardShots = shots;
        contact.SeenWeapon = Mosin.Name;
        contact.SeenClass = WeaponClass.Rifle;
        return (sim, own, enemy, contact);
    }

    [Fact]
    public void HeardShots_SaysShots_DistanceAndTime()
    {
        var (_, own, enemy, contact) = Setup(ContactLevel.Suspected, shots: true);
        Assert.Equal(["Kuultu vihollinen: laukauksia", "noin 85 m", "12 s sitten"], ContactInfo.Describe(contact, [own], 240, "fi"));
        Assert.Equal(["Enemy heard: shooting", "about 85 m", "12 s ago"], ContactInfo.Describe(contact, [own], 240, "en"));
    }

    [Fact]
    public void HeardMovement_SaysMovement()
    {
        var (_, own, enemy, contact) = Setup(ContactLevel.Suspected);
        Assert.Equal("Kuultu vihollinen: liikettä", ContactInfo.Describe(contact, [own], 240, "fi")[0]);
    }

    [Fact]
    public void LastSeen_SaysRoleWeaponAndWhen()
    {
        var (_, own, enemy, contact) = Setup(ContactLevel.LastKnown);
        Assert.Equal(["Viimeksi nähty 12 s sitten: kiväärimies, Mosin 91/30", "noin 85 m"], ContactInfo.Describe(contact, [own], 240, "fi"));
        Assert.Equal(["Last seen 12 s ago: rifleman, Mosin 91/30", "about 85 m"], ContactInfo.Describe(contact, [own], 240, "en"));
    }

    [Fact]
    public void Distance_FromNearestFitOwnMan_RoundedTo5m()
    {
        var (sim, own, enemy, contact) = Setup(ContactLevel.Suspected);
        var near = sim.SpawnUnit(Side.Blue, new Vec2(1050, 4050), 8, Mosin); // 33 m off
        var fallen = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1550), 8, Mosin);
        fallen.Wound = WoundLevel.Dead;
        Assert.Equal("noin 35 m", ContactInfo.Describe(contact, [own, near, fallen], 240, "fi")[1]);
    }

    [Fact]
    public void AMarkOfAManFallenOutOfSight_StillHasItsHint()
    {
        var (_, own, enemy, contact) = Setup(ContactLevel.LastKnown);
        enemy.Wound = WoundLevel.Dead; // our men do not know: the tip must not tell
        Assert.Equal("Viimeksi nähty 12 s sitten: kiväärimies, Mosin 91/30", ContactInfo.Describe(contact, [own], 240, "fi")[0]);
    }

    [Fact]
    public void LastSeen_IsAsHeWasSeen_NotAsHeIsNow()
    {
        var (_, own, enemy, contact) = Setup(ContactLevel.LastKnown);
        enemy.IsLeader = true; // promoted out of sight after the leader fell
        Assert.StartsWith("Viimeksi nähty 12 s sitten: kiväärimies", ContactInfo.Describe(contact, [own], 240, "fi")[0]);
    }

    [Fact]
    public void NotAMark_NoHint()
    {
        var (_, own, _, contact) = Setup(ContactLevel.Visible);
        Assert.Empty(ContactInfo.Describe(contact, [own], 240, "fi"));
    }
}
