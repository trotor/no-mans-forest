using Nmf.Sim.Combat;
using Nmf.Client.Art;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests.Art;

public class UnitAnimatorTests
{
    private static readonly SpriteSheet Sheet = SpriteSheet.Parse(File.ReadAllText(SheetPath()));

    private static string SheetPath()
    {
        var root = ContentLocator.FindContentRoot(AppContext.BaseDirectory)
                   ?? throw new InvalidOperationException("content/ not found");
        return Path.Combine(root, "core", "art", "soldiers", "sheet.json");
    }

    private static (Simulation Sim, Unit Unit) NewUnit(Side side = Side.Blue)
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        return (sim, sim.SpawnUnit(side, new Vec2(1000, 1000), 7));
    }

    [Fact]
    public void Idle_FacesNorthForBlueAndSouthForRed()
    {
        var (sim, blue) = NewUnit(Side.Blue);
        var red = sim.SpawnUnit(Side.Red, new Vec2(1500, 1000), 7);
        var a = new UnitAnimator();
        a.Update(blue, (1000, 1000), paused: false);
        a.Update(red, (1000, 1000), paused: false);
        Assert.Equal(new AnimationFrame("idle", Facing.North, 0), a.Current(blue, Sheet));
        Assert.Equal(new AnimationFrame("idle", Facing.South, 0), a.Current(red, Sheet));
    }

    [Fact]
    public void Walking_FacesMovementAndAdvancesFramesWithDistance()
    {
        var (sim, unit) = NewUnit();
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(3000, 1000)));
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        sim.Step();
        a.Update(unit, (1007, 1000), false);
        Assert.Equal(new AnimationFrame("walk", Facing.East, 0), a.Current(unit, Sheet));
        sim.Step();
        a.Update(unit, (1027, 1000), false); // 27 cm walked, 20 cm per frame
        Assert.Equal(1, a.Current(unit, Sheet).Frame);
    }

    [Fact]
    public void Stopping_KeepsLastFacing()
    {
        var (sim, unit) = NewUnit();
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        a.Update(unit, (1000, 1010), false); // moved south
        sim.Step();                           // unit.IsMoving is false now
        a.Update(unit, (1000, 1010), false);
        a.Update(unit, (1000, 1010), false);
        Assert.Equal(new AnimationFrame("idle", Facing.South, 0), a.Current(unit, Sheet));
    }

    [Fact]
    public void Paused_KeepsAnimationAndFrame()
    {
        var (sim, unit) = NewUnit();
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(3000, 1000)));
        sim.Step();
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        a.Update(unit, (1047, 1000), false);
        var before = a.Current(unit, Sheet);
        a.Update(unit, (1047, 1000), paused: true);
        a.Update(unit, (1047, 1000), paused: true);
        Assert.Equal(before, a.Current(unit, Sheet));
    }

    [Fact]
    public void ProneAndCrouch_ChooseTheirAnimations()
    {
        var (sim, unit) = NewUnit();
        var a = new UnitAnimator();
        sim.Submit(Side.Blue, new SetStanceOrder(unit.Id, Stance.Crouching));
        for (int i = 0; i < 10; i++) sim.Step();
        a.Update(unit, (1000, 1000), false);
        a.Update(unit, (1000, 1000), false);
        Assert.Equal("crouch", a.Current(unit, Sheet).Animation);

        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(1000, 3000), MoveMode.Crawl));
        for (int i = 0; i < 16; i++) sim.Step();
        a.Update(unit, (1000, 1002), false);
        a.Update(unit, (1000, 1005), false);
        Assert.Equal(new AnimationFrame("crawl", Facing.South, 0), a.Current(unit, Sheet));
    }

    [Fact]
    public void Running_UsesRunAnimation()
    {
        var (sim, unit) = NewUnit();
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(1000, 100), MoveMode.Run));
        sim.Step();
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        a.Update(unit, (1000, 986), false);
        Assert.Equal(new AnimationFrame("run", Facing.North, 0), a.Current(unit, Sheet));
    }

    [Fact]
    public void SlowCrawl_WithTinyPerFrameSteps_StillTurnsToMovementDirection()
    {
        var (sim, unit) = NewUnit();
        var a = new UnitAnimator();
        double x = 1000;
        a.Update(unit, (x, 1000), false);
        for (int i = 0; i < 60; i++)
        {
            x += 0.33; // 1 cm/tick crawl rendered at 60 fps
            a.Update(unit, (x, 1000), false);
        }
        Assert.Equal(Facing.East, a.Current(unit, Sheet).Direction);
    }

    [Fact]
    public void DirectionOf_ReturnsLastKnownFacingOrFallback()
    {
        var (_, unit) = NewUnit();
        var a = new UnitAnimator();
        Assert.Equal(Facing.South, a.DirectionOf(unit.Id, Facing.South));
        a.Update(unit, (1000, 1000), false);
        a.Update(unit, (1010, 1000), false);
        Assert.Equal(Facing.East, a.DirectionOf(unit.Id, Facing.South));
    }

    [Fact]
    public void OutOfAction_UsesDeadAnimation()
    {
        var (_, unit) = NewUnit();
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        unit.Wound = WoundLevel.Dead;
        Assert.Equal("dead", a.Current(unit, Sheet).Animation);
    }
}
