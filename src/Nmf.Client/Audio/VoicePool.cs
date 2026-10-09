namespace Nmf.Client.Audio;

/// <summary>
/// Which of a fixed number of players a new sound goes to (spec 2026-10-09-sound-design): a cue has at most its
/// <see cref="SoundCue.MaxVoices"/> at once, and when every player is busy the quietest of no higher priority gives way.
/// </summary>
public sealed class VoicePool(int slots = 32)
{
    private sealed record Voice(string CueId, int Priority, double VolumeDb, double EndsAt);

    private readonly Voice?[] _voices = new Voice?[slots];
    private double _now;

    public int Slots => _voices.Length;

    /// <summary>Real time passes (not while paused: paused players keep their place).</summary>
    public void Advance(double realSeconds)
    {
        _now += realSeconds;
        for (int i = 0; i < _voices.Length; i++)
            if (_voices[i] is { } v && v.EndsAt <= _now)
                _voices[i] = null;
    }

    /// <summary>The slot the cue plays in, or -1 when it is not worth a player.</summary>
    public int Claim(SoundCue cue, double lengthSeconds)
    {
        int slot = Choose(cue);
        if (slot >= 0)
            _voices[slot] = new Voice(cue.CueId, cue.Priority, cue.VolumeDb, _now + lengthSeconds);
        return slot;
    }

    private int Choose(SoundCue cue)
    {
        var same = Enumerable.Range(0, _voices.Length).Where(i => _voices[i]?.CueId == cue.CueId).ToList();
        if (same.Count >= cue.MaxVoices)
        {
            int quietest = same.MinBy(i => _voices[i]!.VolumeDb);
            return _voices[quietest]!.VolumeDb <= cue.VolumeDb ? quietest : -1;
        }
        int free = Array.FindIndex(_voices, v => v is null);
        if (free >= 0)
            return free;
        var yielding = Enumerable.Range(0, _voices.Length).Where(i => _voices[i]!.Priority <= cue.Priority).ToList();
        return yielding.Count == 0 ? -1 : yielding.MinBy(i => (_voices[i]!.VolumeDb, _voices[i]!.Priority));
    }
}
