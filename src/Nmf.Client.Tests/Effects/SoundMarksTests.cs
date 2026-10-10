using Nmf.Client.Audio;
using Nmf.Client.Effects;
using Nmf.Sim.Core;

namespace Nmf.Client.Tests.Effects;

public class SoundMarksTests
{
    private static HeardSound Heard(HeardKind kind, int x, int y = 0) => new(kind, new Vec2(x, y), 0);

    [Fact]
    public void Marks_Expire()
    {
        var marks = new SoundMarks();
        marks.Add([Heard(HeardKind.Shot, 0)]);
        marks.Update(1.4);
        Assert.Single(marks.Marks);
        marks.Update(0.2);
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void Explosions_LastLonger()
    {
        var marks = new SoundMarks();
        marks.Add([Heard(HeardKind.Explosion, 0)]);
        marks.Update(2.0);
        Assert.Single(marks.Marks);
        marks.Update(0.6);
        Assert.Empty(marks.Marks);
    }

    [Fact]
    public void ShotsClose_Merge()
    {
        var marks = new SoundMarks();
        marks.Add([Heard(HeardKind.Shot, 0)]);
        marks.Update(1.0);
        marks.Add([Heard(HeardKind.Shot, 600), Heard(HeardKind.Shot, 900)]);
        var mark = Assert.Single(marks.Marks);
        Assert.Equal(0, mark.Age);
        Assert.Equal(new Vec2(900, 0), mark.At);
        marks.Add([Heard(HeardKind.Shot, 5_000)]);
        Assert.Equal(2, marks.Marks.Count);
    }

    [Fact]
    public void DifferentKinds_DoNotMerge()
    {
        var marks = new SoundMarks();
        marks.Add([Heard(HeardKind.Shot, 0), Heard(HeardKind.Crack, 100)]);
        Assert.Equal(2, marks.Marks.Count);
    }

    [Fact]
    public void Marks_DoNotAgeWhilePaused()
    {
        var marks = new SoundMarks();
        marks.Add([Heard(HeardKind.Shot, 0)]);
        for (int i = 0; i < 100; i++)
            marks.Update(0); // paused: no game time passes
        Assert.Equal(0, Assert.Single(marks.Marks).Age);
    }
}
