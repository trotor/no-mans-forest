using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Effects;

public enum EffectKind
{
    Tracer,
    MuzzleFlash,
    Impact,
    MoveMarker,
    FireMarker,
    Explosion,
}

public sealed class Effect(EffectKind kind, Vec2 from, Vec2 to, double lifetime)
{
    public EffectKind Kind { get; } = kind;
    public Vec2 From { get; } = from;
    public Vec2 To { get; } = to;
    public double Lifetime { get; } = lifetime;
    public double Age { get; internal set; }
    public double Progress => Math.Clamp(Age / Lifetime, 0, 1);
}

/// <summary>A line of text floating over a man for a few seconds (e.g. what he found on a body).</summary>
public sealed class Note(UnitId unit, string text, double lifetime)
{
    public UnitId Unit { get; } = unit;
    public string Text { get; } = text;
    public double Lifetime { get; } = lifetime;
    public double Age { get; internal set; }
}

/// <summary>Short-lived tracers, muzzle flashes and bullet impacts made from simulation events (real-time, presentation only).</summary>
public sealed class CombatEffects
{
    public const double TracerSeconds = 0.12;
    public const double FlashSeconds = 0.06;
    public const double ImpactSeconds = 0.35;
    public const double MarkerSeconds = 0.6;
    public const double ExplosionSeconds = 0.6;
    public const int MaxCraters = 300;
    public const double NoteSeconds = 3.5;

    private readonly List<Effect> _active = [];
    private readonly List<Vec2> _craters = [];
    private readonly List<Note> _notes = [];

    public IReadOnlyList<Note> Notes => _notes;

    public IReadOnlyList<Effect> Active => _active;

    /// <summary>Scorch marks left by explosions (oldest dropped past <see cref="MaxCraters"/>).</summary>
    public IReadOnlyList<Vec2> Craters => _craters;

    /// <summary>Shots by shooters the player cannot see only show where the bullet landed.</summary>
    public void Add(IEnumerable<SimEvent> events, Func<UnitId, bool> shooterShown, Func<UnitLooted, string>? describeLoot = null)
    {
        foreach (var e in events)
        {
            if (e is UnitLooted looted)
            {
                if (describeLoot is not null && shooterShown(looted.Looter))
                    _notes.Add(new Note(looted.Looter, describeLoot(looted), NoteSeconds));
                continue;
            }
            if (e is GrenadeExploded blast)
            {
                // Explosions are heard and seen by everyone.
                _active.Add(new Effect(EffectKind.Explosion, blast.At, blast.At, ExplosionSeconds));
                _craters.Add(blast.At);
                if (_craters.Count > MaxCraters)
                    _craters.RemoveAt(0);
                continue;
            }
            if (e is not ShotFired shot)
                continue;
            bool shown = shooterShown(shot.Shooter);
            if (shown)
            {
                _active.Add(new Effect(EffectKind.Tracer, shot.From, shot.To, TracerSeconds));
                _active.Add(new Effect(EffectKind.MuzzleFlash, shot.From, shot.To, FlashSeconds));
            }
            // A hidden shooter's bullet is only noticed where it lands, hit or miss.
            if (shot.Hit is null || !shown)
                _active.Add(new Effect(EffectKind.Impact, shot.To, shot.To, ImpactSeconds));
        }
    }

    /// <summary>Feedback for a click: where the men were sent, or whom they were told to shoot.</summary>
    public void AddMarker(EffectKind kind, Vec2 at) => _active.Add(new Effect(kind, at, at, MarkerSeconds));

    public void Update(double seconds)
    {
        foreach (var effect in _active)
            effect.Age += seconds;
        _active.RemoveAll(e => e.Age >= e.Lifetime);
        foreach (var note in _notes)
            note.Age += seconds;
        _notes.RemoveAll(n => n.Age >= n.Lifetime);
    }
}
