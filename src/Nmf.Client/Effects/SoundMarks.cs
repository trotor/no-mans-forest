using Nmf.Client.Audio;
using Nmf.Sim.Core;

namespace Nmf.Client.Effects;

/// <summary>A heard sound shown on the map for a moment.</summary>
public sealed class SoundMark(HeardKind kind, Vec2 at, double life)
{
    public HeardKind Kind { get; } = kind;
    public Vec2 At { get; internal set; } = at;
    public double Age { get; internal set; }
    public double Life { get; } = life;
}

/// <summary>
/// Sounds shown as marks, for playing without sound (spec 2026-10-10-settings-hints-design §2): each heard sound a brief
/// mark where it was heard; marks of a kind close together are one, kept fresh. Ages on game time only.
/// </summary>
public sealed class SoundMarks
{
    public const double LifeSeconds = 1.5, ExplosionLifeSeconds = 2.5;
    public const int MergeCm = 1_000;

    private readonly List<SoundMark> _marks = [];

    public IReadOnlyList<SoundMark> Marks => _marks;

    public void Add(IEnumerable<HeardSound> heard)
    {
        long mergeSq = (long)MergeCm * MergeCm;
        foreach (var sound in heard)
        {
            if (_marks.FirstOrDefault(m => m.Kind == sound.Kind && (m.At - sound.At).LengthSquared <= mergeSq) is { } near)
            {
                near.Age = 0;
                near.At = sound.At;
                continue;
            }
            _marks.Add(new SoundMark(sound.Kind, sound.At, sound.Kind == HeardKind.Explosion ? ExplosionLifeSeconds : LifeSeconds));
        }
    }

    /// <summary>Game time passes (0 while paused).</summary>
    public void Update(double gameSeconds)
    {
        foreach (var mark in _marks)
            mark.Age += gameSeconds;
        _marks.RemoveAll(m => m.Age > m.Life);
    }

    public void Clear() => _marks.Clear();
}
