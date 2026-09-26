using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests;

public class LootTextTests
{
    private static string Name(string id) => id == "ppsh41" ? "PPSh-41" : id;

    [Fact]
    public void Describe_ListsWhatWasTaken()
    {
        var e = new UnitLooted(0, new UnitId(1), new UnitId(2), 2, 1, "ppsh41", [new Item("soviet_orders", "Soviet orders")]);
        Assert.Equal("+2 mags +1 grenade, PPSh-41, Soviet orders", LootText.Describe(e, Name));
    }

    [Fact]
    public void Describe_Singular_AndNothing()
    {
        Assert.Equal("+1 mag +2 grenades", LootText.Describe(new UnitLooted(0, new UnitId(1), new UnitId(2), 1, 2, null, []), Name));
        Assert.Equal("nothing", LootText.Describe(new UnitLooted(0, new UnitId(1), new UnitId(2), 0, 0, null, []), Name));
    }

    [Fact]
    public void Describe_InFinnish_WithTheMissionsNameForThePapers()
    {
        var e = new UnitLooted(0, new UnitId(1), new UnitId(2), 2, 1, "ppsh41", [new Item("soviet_orders", "Soviet orders")]);
        Assert.Equal("+2 lipasta +1 kranaatti, PPSh-41, Neuvostopartion käskyt",
            LootText.Describe(e, Name, "fi", item => item.Id == "soviet_orders" ? "Neuvostopartion käskyt" : item.Name));
        Assert.Equal("+1 lipas +2 kranaattia", LootText.Describe(new UnitLooted(0, new UnitId(1), new UnitId(2), 1, 2, null, []), Name, "fi"));
        Assert.Equal("ei mitään", LootText.Describe(new UnitLooted(0, new UnitId(1), new UnitId(2), 0, 0, null, []), Name, "fi"));
    }
}
