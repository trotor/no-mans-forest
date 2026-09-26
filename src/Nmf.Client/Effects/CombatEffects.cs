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

public enum SignalKind
{
    Gunfire,
    Explosion,
    OwnHit,
    EnemyDown,
    /// <summary>The enemy's call to counterattack, heard.</summary>
    Shout,
}

/// <summary>A brief marker for something happening, drawn at a fixed screen size when the view is zoomed far out.</summary>
public sealed class Signal(SignalKind kind, Vec2 at, double lifetime)
{
    public SignalKind Kind { get; } = kind;
    public Vec2 At { get; internal set; } = at;
    public double Lifetime { get; } = lifetime;
    /// <summary>Game seconds since it was last renewed; it goes when this reaches its lifetime.</summary>
    public double Age { get; internal set; }
    /// <summary>Game seconds since it first appeared, for the pulse animation (renewals do not reset it).</summary>
    public double Pulse { get; internal set; }
}

public enum OrderFlashKind
{
    Move,
    Fire,
    Stance,
}

/// <summary>The men who just got an order, and where it sends them, flashed so they stand out when the view is far out.</summary>
public sealed class OrderFlash(IReadOnlyList<UnitId> units, Vec2? target, OrderFlashKind kind, double lifetime)
{
    public IReadOnlyList<UnitId> Units { get; } = units;
    public Vec2? Target { get; } = target;
    public OrderFlashKind Kind { get; } = kind;
    public double Lifetime { get; } = lifetime;
    public double Age { get; internal set; }
    public double Progress => Math.Clamp(Age / Lifetime, 0, 1);
}

/// <summary>What the signals need to know about a unit: where he is, whether he is ours, whether the player sees him.</summary>
public readonly record struct SignalUnit(Vec2 Position, bool Own, bool Shown);

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
    public const double SignalSeconds = 1.2;
    /// <summary>A new signal this close to a live one of the same kind refreshes it instead (burst fire is one pulsing ring).</summary>
    public const int SignalMergeCm = 1500;
    /// <summary>A hidden shooter's fire is only marked where it strikes near one of ours (incoming fire), never at him.</summary>
    public const int IncomingFireCm = 1000;
    public const double OrderFlashSeconds = 0.9;

    private OrderFlash? _orderFlash;

    /// <summary>The last order's flash, if it is still showing (a new order replaces it).</summary>
    public IReadOnlyList<OrderFlash> OrderFlashes => _orderFlash is null ? [] : [_orderFlash];

    public void AddOrderFlash(IReadOnlyList<UnitId> units, Vec2? target, OrderFlashKind kind) =>
        _orderFlash = units.Count == 0 ? null : new OrderFlash(units.ToList(), target, kind, OrderFlashSeconds);

    private readonly List<Effect> _active = [];
    private readonly List<Vec2> _craters = [];
    private readonly List<Note> _notes = [];

    public IReadOnlyList<Note> Notes => _notes;

    private readonly List<Signal> _signals = [];
    public IReadOnlyList<Signal> Signals => _signals;

    /// <summary>
    /// Signals for what the player would notice (spec 2026-09-26-maps-design §4): fire at a seen shooter or where hidden
    /// fire strikes, every explosion, own men hit, seen enemies falling.
    /// </summary>
    public void AddSignals(IEnumerable<SimEvent> events, Func<UnitId, SignalUnit?> unit, IReadOnlyList<Vec2> ownMen)
    {
        long incomingSq = (long)IncomingFireCm * IncomingFireCm;
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotFired shot:
                    if (unit(shot.Shooter) is { Shown: true })
                        Signal(SignalKind.Gunfire, shot.From);
                    else if (ownMen.Any(p => (p - shot.To).LengthSquared <= incomingSq))
                        Signal(SignalKind.Gunfire, shot.To);
                    break;
                case GrenadeExploded blast:
                    Signal(SignalKind.Explosion, blast.At);
                    break;
                case CounterattackStarted shout when Nmf.Client.Mission.Alerts.Counterattack(shout, ownMen, "en") is not null:
                    // Heard, not seen: marked in the middle of the 10 m square the shout came from.
                    Signal(SignalKind.Shout, new Vec2(shout.At.X / 1000 * 1000 + 500, shout.At.Y / 1000 * 1000 + 500));
                    break;
                case UnitWounded wounded when unit(wounded.Unit) is { } who:
                    if (who.Own)
                        Signal(SignalKind.OwnHit, who.Position);
                    else if (who.Shown && wounded.Level >= Nmf.Sim.Combat.WoundLevel.Incapacitated)
                        Signal(SignalKind.EnemyDown, who.Position);
                    break;
            }
        }
    }

    private void Signal(SignalKind kind, Vec2 at)
    {
        long mergeSq = (long)SignalMergeCm * SignalMergeCm;
        // Fire and blasts merge (a burst is one ring); every man hit or falling keeps his own marker.
        bool merges = kind is SignalKind.Gunfire or SignalKind.Explosion;
        foreach (var s in merges ? _signals : [])
        {
            if (s.Kind == kind && (s.At - at).LengthSquared <= mergeSq)
            {
                s.Age = 0;
                s.At = at;
                return;
            }
        }
        _signals.Add(new Signal(kind, at, SignalSeconds));
    }

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

    /// <summary>Ages the signals by game time, so a paused game (e.g. the map open) keeps the last moments on show.</summary>
    public void UpdateSignals(double gameSeconds)
    {
        foreach (var signal in _signals)
        {
            signal.Age += gameSeconds;
            signal.Pulse += gameSeconds;
        }
        _signals.RemoveAll(s => s.Age >= s.Lifetime);
    }

    public void Update(double seconds)
    {
        // Real time: an order given while paused still flashes and fades.
        if (_orderFlash is not null && (_orderFlash.Age += seconds) >= _orderFlash.Lifetime)
            _orderFlash = null;
        foreach (var effect in _active)
            effect.Age += seconds;
        _active.RemoveAll(e => e.Age >= e.Lifetime);
        foreach (var note in _notes)
            note.Age += seconds;
        _notes.RemoveAll(n => n.Age >= n.Lifetime);

    }
}
