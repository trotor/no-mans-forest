using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class SelectionTests
{
    private static Simulation NewSim(out Unit a, out Unit b, out Unit enemy)
    {
        var sim = new Simulation(new GridMap(40, 40, ["none"]), 1);
        a = sim.SpawnUnit(Side.Blue, new Vec2(500, 500), 7);
        b = sim.SpawnUnit(Side.Blue, new Vec2(800, 500), 7);
        enemy = sim.SpawnUnit(Side.Red, new Vec2(560, 500), 7);
        return sim;
    }

    [Fact]
    public void SelectAt_PicksNearestOwnUnitWithinRadius()
    {
        var sim = NewSim(out var a, out _, out _);
        var selection = new Selection();
        Assert.True(selection.SelectAt(sim.Units, Side.Blue, new Vec2(580, 500), 150, additive: false));
        Assert.Equal(new[] { a.Id }, selection.Ids);
    }

    [Fact]
    public void SelectAt_EmptyGround_ClearsSelection()
    {
        var sim = NewSim(out _, out _, out _);
        var selection = new Selection();
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(500, 500), 150, false);
        Assert.False(selection.SelectAt(sim.Units, Side.Blue, new Vec2(3000, 3000), 150, false));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void SelectAt_Additive_KeepsExistingSelection()
    {
        var sim = NewSim(out var a, out var b, out _);
        var selection = new Selection();
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(500, 500), 150, false);
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(800, 500), 150, true);
        Assert.Equal(new[] { a.Id, b.Id }, selection.Ids);
    }

    [Fact]
    public void SelectInBox_SelectsOnlyOwnUnits_CornersInAnyOrder()
    {
        var sim = NewSim(out var a, out var b, out var enemy);
        var selection = new Selection();
        int added = selection.SelectInBox(sim.Units, Side.Blue, new Vec2(900, 600), new Vec2(400, 400), false);
        Assert.Equal(2, added);
        Assert.Equal(new[] { a.Id, b.Id }, selection.Ids);
        Assert.False(selection.Contains(enemy.Id));
    }
}
