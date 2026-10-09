using Nmf.Client.Audio;
using Nmf.Sim.Core;

namespace Nmf.Client.Tests.Audio;

public class AudioMixTests
{
    private static readonly AudioListener L = new(new Vec2(0, 0), 10_000, 1.0, 1.0);

    [Theory]
    [InlineData(5_000, 0.0)]    // on screen: full
    [InlineData(20_000, -9.0)]  // r = 2: halfway to the floor
    [InlineData(30_000, -18.0)] // r = 3: the floor
    [InlineData(90_000, -18.0)] // beyond: stays at the floor
    public void LoudCues_FallToTheFloor(int x, double expected) =>
        Assert.Equal(expected, AudioMix.Volume(0, loud: true, new Vec2(x, 0), L)!.Value, 3);

    [Fact]
    public void QuietCues_AreGoneAtTwiceTheHalfDiagonal()
    {
        Assert.Equal(-14, AudioMix.Volume(-14, false, new Vec2(9_000, 0), L)!.Value, 3);
        Assert.NotNull(AudioMix.Volume(-14, false, new Vec2(15_000, 0), L));
        Assert.Null(AudioMix.Volume(-14, false, new Vec2(20_000, 0), L));
    }

    [Fact]
    public void SoundTakesItsTime() => Assert.Equal(17, AudioMix.DelayTicks(30_000)); // 300 m ≈ 0.87 s at 20 ticks/s

    [Fact]
    public void RoughPosition_IsTheMiddleOfTheTenMetreSquare() =>
        Assert.Equal(new Vec2(1_500, 2_500), AudioMix.Rough(new Vec2(1_234, 2_999)));

    [Fact]
    public void NoMen_NoNearest() => Assert.Null(AudioMix.NearestSq(new Vec2(0, 0), []));

    [Fact]
    public void Nearest_IsTheClosestMan() =>
        Assert.Equal(100L * 100, AudioMix.NearestSq(new Vec2(0, 0), [new Vec2(300, 0), new Vec2(0, 100)]));

    [Theory]
    [InlineData(5_000, 200, true)]   // beside the line, 2 m off
    [InlineData(5_000, 800, false)]  // 8 m off
    [InlineData(-300, 0, true)]      // just behind the muzzle, within 5 m of it
    [InlineData(12_000, 0, false)]   // on the line but beyond where the bullet stopped (10 m)
    public void PassesWithin_MeasuresToTheBulletsPath(int x, int y, bool expected) =>
        Assert.Equal(expected, AudioMix.PassesWithin(new Vec2(0, 0), new Vec2(10_000, 0), new Vec2(x, y), 500));

    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(0.3, 1.0, false)]
    [InlineData(1.0, 4.0, false)]
    public void QuietSounds_OffFarOut_OrFast(double zoom, double speed, bool on) =>
        Assert.Equal(on, AudioMix.QuietSoundsOn(L with { Zoom = zoom, Speed = speed }));
}
