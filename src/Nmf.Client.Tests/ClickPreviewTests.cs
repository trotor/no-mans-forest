using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class ClickPreviewTests
{
    /// <summary>Own men at (90..105, 180) m, a seen Soviet at (100, 60) m, a seen body at (60, 120) m, a "?" at (140, 100) m.</summary>
    private static (GameSession Session, Unit Red, Unit Body, Contact Mark) Field()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(9050 + i * 300, 18_050)))
            .Append(new MapPoint("r1", "red", new Vec2(10_050, 6_050)))
            .Append(new MapPoint("r2", "red", new Vec2(6_050, 12_050)))
            .Append(new MapPoint("r3", "red", new Vec2(19_000, 1_000)))
            .ToList();
        var session = new GameSession(SkirmishScenario.Create(new GridMap(200, 200, ["none"], new MapFeatures([], points, [])), 1));
        var reds = session.Sim.Units.Where(u => u.Side == Side.Red).ToList();
        foreach (var red in reds)
        {
            var c = session.Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
            c.Level = ContactLevel.Visible;
            c.Position = red.Position;
        }
        reds[1].Wound = WoundLevel.Dead;
        var mark = session.Sim.Knowledge(Side.Blue).Get(reds[2].Id)!;
        mark.Level = ContactLevel.Suspected;
        mark.Position = new Vec2(14_050, 10_050);
        return (session, reds[0], reds[1], mark);
    }

    private static ClickPreview.Result Preview(GameSession s, Vec2 at, Modifiers mods = default) =>
        ClickPreview.Describe(s, at, mods, GameSession.ClickRadiusCm, "fi");

    [Fact]
    public void Ground() 
    {
        var (s, _, _, _) = Field();
        var p = new Vec2(5_050, 5_050);
        Assert.Equal(("mene", "juokse", CursorKind.Move), (Preview(s, p).Click, Preview(s, p).Double, Preview(s, p).Cursor));
        Assert.Equal(("hiivi", "ryömi"), (Preview(s, p, new Modifiers(Alt: true)).Click, Preview(s, p, new Modifiers(Alt: true)).Double));
        Assert.Equal(("aluetuli", CursorKind.Cross), (Preview(s, p, new Modifiers(Ctrl: true)).Click, Preview(s, p, new Modifiers(Ctrl: true)).Cursor));
        Assert.Equal("hyökkää eteenpäin", Preview(s, p, new Modifiers(Forward: true)).Click);
    }

    [Fact]
    public void Enemy()
    {
        var (s, red, _, _) = Field();
        var p = Preview(s, red.Position);
        Assert.Equal(("ammu", "hyökkää", CursorKind.Cross), (p.Click, p.Double, p.Cursor));
        Assert.Equal("pitkä tuli", Preview(s, red.Position, new Modifiers(Alt: true)).Double);
        Assert.Equal(("aluetuli", "suora rynnäkkö"), (Preview(s, red.Position, new Modifiers(Ctrl: true)).Click, Preview(s, red.Position, new Modifiers(Ctrl: true)).Double));
    }

    [Fact]
    public void MarkBodyOwn()
    {
        var (s, _, body, mark) = Field();
        // Nobody here is armed: a mark nobody can fire at is clicked as ground, its double click still attacks it.
        Assert.Equal(("mene", "hyökkää"), (Preview(s, mark.Position).Click, Preview(s, mark.Position).Double));
        Assert.Equal(("mene", "tutki", CursorKind.Search), (Preview(s, body.Position).Click, Preview(s, body.Position).Double, Preview(s, body.Position).Cursor));
        var own = s.OwnUnits.First();
        Assert.Equal(("valitse", "koko ryhmä", CursorKind.Select), (Preview(s, own.Position).Click, Preview(s, own.Position).Double, Preview(s, own.Position).Cursor));
    }

    [Fact]
    public void Shift_SaysWhereInTheQueue()
    {
        var (s, _, _, _) = Field();
        s.HandleLeftClick(new Vec2(5_050, 5_050), false, shift: true, alt: false);
        Assert.Equal("jonoon (2.): mene", Preview(s, new Vec2(6_050, 5_050), new Modifiers(Shift: true)).Click);
    }

    [Fact]
    public void PreviewAndClick_AgreeOnEveryTarget()
    {
        var (s, red, body, mark) = Field();
        var own = s.OwnUnits.First();
        var unfireable = s.Sim.Units.Single(u => u.Side == Side.Red && s.Knowledge.LevelOf(u.Id) == ContactLevel.Suspected);
        var far = s.Sim.Knowledge(Side.Blue).Get(unfireable.Id)!;
        far.Position = new Vec2(19_550, 550); // a "?" out of everyone's range: nobody can fire there
        foreach (var centre in new[] { red.Position, body.Position, new Vec2(14_050, 10_050), far.Position, own.Position, new Vec2(5_050, 5_050) })
        {
            for (int dx = -300; dx <= 300; dx += 100)
            {
                for (int dy = -300; dy <= 300; dy += 100)
                {
                    var at = centre + new Vec2(dx, dy);
                    foreach (bool twice in new[] { false, true })
                    {
                        foreach (var mods in new[] { new Modifiers(), new Modifiers(Alt: true), new Modifiers(Ctrl: true), new Modifiers(Forward: true) })
                        {
                            var predicted = twice ? Preview(s, at, mods).DoubleResult : Preview(s, at, mods).ClickResult;
                            var actual = s.HandleLeftClick(at, twice, shift: false, alt: mods.Alt, area: mods.Ctrl, attackForward: mods.Forward).Result;
                            Assert.True(predicted == actual, $"at {at} {(twice ? "double" : "single")} {mods}: preview {predicted}, click {actual}");
                            s.Selection.Clear();
                        }
                    }
                }
            }
        }
    }
}
