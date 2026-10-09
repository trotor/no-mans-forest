using Nmf.Client.Audio;
using Nmf.Content.Audio;
using Nmf.Sim.Core;

namespace Nmf.Client.Tests.Audio;

public class VoicePoolTests
{
    private static SoundCue Cue(string id, double db, int priority, int max = 6) =>
        new(id, id + ".ogg", AudioBus.Sfx, new Vec2(0, 0), db, 1, priority, max);

    [Fact]
    public void AFullPool_ReplacesTheQuietestOfLowerPriority()
    {
        var pool = new VoicePool(2);
        int a = pool.Claim(Cue("a", -10, 10), 5);
        int b = pool.Claim(Cue("b", -3, 70), 5);
        Assert.NotEqual(a, b);
        Assert.Equal(a, pool.Claim(Cue("c", -5, 70), 5));
    }

    [Fact]
    public void AFullPool_RefusesWhenEverythingIsMoreImportant()
    {
        var pool = new VoicePool(1);
        Assert.Equal(0, pool.Claim(Cue("a", 0, 80), 5));
        Assert.Equal(-1, pool.Claim(Cue("b", 0, 10), 5));
    }

    [Fact]
    public void MaxVoices_ReplacesTheQuietestOfTheSameCue()
    {
        var pool = new VoicePool(8);
        int loud = pool.Claim(Cue("x", -2, 50, max: 2), 5);
        int quiet = pool.Claim(Cue("x", -9, 50, max: 2), 5);
        Assert.NotEqual(loud, quiet);
        Assert.Equal(quiet, pool.Claim(Cue("x", -5, 50, max: 2), 5));
        Assert.Equal(-1, pool.Claim(Cue("x", -20, 50, max: 2), 5)); // quieter than both: not worth a voice
    }

    [Fact]
    public void EndedVoices_FreeTheirSlot()
    {
        var pool = new VoicePool(1);
        Assert.Equal(0, pool.Claim(Cue("a", 0, 90), 0.5));
        Assert.Equal(-1, pool.Claim(Cue("b", 0, 0), 1));
        pool.Advance(0.6);
        Assert.Equal(0, pool.Claim(Cue("b", 0, 0), 1));
    }
}
