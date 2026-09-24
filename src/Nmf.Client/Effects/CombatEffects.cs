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

/// <summary>Short-lived tracers, muzzle flashes and bullet impacts made from simulation events (real-time, presentation only).</summary>
public sealed class CombatEffects
{
    public const double TracerSeconds = 0.12;
    public const double FlashSeconds = 0.06;
    public const double ImpactSeconds = 0.35;
    public const double MarkerSeconds = 0.6;

    private readonly List<Effect> _active = [];

    public IReadOnlyList<Effect> Active => _active;

    /// <summary>Shots by shooters the player cannot see only show where the bullet landed.</summary>
    public void Add(IEnumerable<SimEvent> events, Func<UnitId, bool> shooterShown)
    {
        foreach (var e in events)
        {
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
    }
}
