using Nmf.Content.Audio;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Audio;

/// <summary>
/// Turns what happened into the sounds the player's side could hear (spec 2026-10-09-sound-design). Pure: Godot only plays
/// the returned cues. It has its own random generator and never draws from the simulation's.
/// </summary>
public sealed class AudioDirector(AudioCatalog catalog, int seed = 1)
{
    private const double PitchSpread = 0.04;

    private readonly Random _rng = new(seed);
    private readonly List<(long Tick, CueDef Cue, Vec2 At)> _pending = [];
    private readonly List<HeardSound> _pendingHeard = [];
    private readonly List<HeardSound> _heard = [];

    /// <summary>
    /// What the player's side heard this frame (own shots and quiet sounds left out), whether or not there are sound files
    /// or sound at all: for drawing sounds as marks.
    /// </summary>
    public IReadOnlyList<HeardSound> Heard => _heard;
    private readonly Dictionary<string, string> _lastFile = [];
    private readonly Dictionary<UnitId, long> _lastShotTick = [];
    private readonly HashSet<UnitId> _fallen = [];
    private readonly HashSet<UnitId> _reloading = [];
    private readonly Dictionary<UnitId, Vec2> _lastPos = [];
    private readonly Dictionary<UnitId, long> _stepTimer = [];
    private long _lastTick;

    /// <summary>Footsteps come from at most this many movers, those nearest the camera centre.</summary>
    public const int SteppingMen = 4;

    /// <summary>
    /// The sounds to start this frame: from this frame's <paramref name="events"/>, and those delayed by distance whose time
    /// has come. <paramref name="shown"/> tells whether the player sees a man.
    /// </summary>
    public IReadOnlyList<SoundCue> Update(Simulation sim, Side playerSide, IReadOnlyList<SimEvent> events,
        Func<Unit, bool> shown, AudioListener listener, double realDeltaSeconds)
    {
        var own = sim.Units.Where(u => u.Side == playerSide && !u.IsOutOfAction).Select(u => u.Position).ToList();
        bool Heard(Unit u) => u.Side == playerSide || shown(u);
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotFired shot when sim.FindUnit(shot.Shooter) is { Weapon: { } weapon } shooter:
                    Shot(sim, shot, shooter, weapon, Heard(shooter), shooter.Side == playerSide, own, listener.Speed);
                    break;
                case GrenadeThrown thrown when sim.FindUnit(thrown.Thrower) is { } thrower && Heard(thrower):
                    Queue(e.Tick, thrown.From, "grenade.throw");
                    break;
                case GrenadeExploded blast when AudioMix.NearestSq(blast.At, own) is { } sq
                                                && sq <= (long)AudioMix.ExplosionHeardCm * AudioMix.ExplosionHeardCm:
                {
                    long d = IntMath.Isqrt(sq);
                    Hear(HeardKind.Explosion, e.Tick + AudioMix.DelayTicks(d), blast.At);
                    Queue(e.Tick + AudioMix.DelayTicks(d), blast.At,
                        d > AudioMix.FarCm ? ["grenade.explode_far", "grenade.explode"] : ["grenade.explode"]);
                    break;
                }
                case UnitWounded { Level: >= WoundLevel.Incapacitated } hit
                    when sim.FindUnit(hit.Unit) is { } man && Heard(man) && _fallen.Add(man.Id):
                    Hear(HeardKind.Fall, e.Tick, man.Position);
                    Queue(e.Tick, man.Position, "body.fall");
                    break;
                case MeleeStarted melee when sim.FindUnit(melee.A) is { } a && sim.FindUnit(melee.B) is { } b && (Heard(a) || Heard(b)):
                    Hear(HeardKind.Melee, e.Tick, a.Position);
                    Queue(e.Tick, a.Position, "melee.scuffle");
                    break;
                case UnitLooted loot when sim.FindUnit(loot.Looter) is { } looter && Heard(looter):
                    Queue(e.Tick, looter.Position, "loot.rustle");
                    break;
            }
        }
        long elapsed = sim.Tick - _lastTick;
        _lastTick = sim.Tick;
        Reloads(sim, Heard, listener);
        Steps(sim, Heard, listener, elapsed);
        return Release(sim.Tick, listener);
    }

    /// <summary>An interface sound (no place in the world); null when the cue has no files.</summary>
    public SoundCue? Ui(string cueId) =>
        catalog.Find(cueId) is { } cue ? new SoundCue(cue.Id, Pick(cue), cue.Bus, null, cue.VolumeDb, Pitch(), cue.Priority, cue.MaxVoices) : null;

    private void Shot(Simulation sim, ShotFired shot, Unit shooter, WeaponDef weapon, bool seen, bool ownShot, List<Vec2> own, double speed)
    {
        string cls = weapon.Class.ToString().ToLowerInvariant();
        long? nearestSq = AudioMix.NearestSq(shot.From, own);
        long radius = weapon.NoiseRadiusCm;
        bool heard = seen || (nearestSq is { } sq && sq <= radius * radius);
        // Fast forward thins one man's rounds out, and with them their cracks and strikes: a burst is a few sounds, not a buzz.
        if (_lastShotTick.TryGetValue(shooter.Id, out long last)
            && (shot.Tick - last) / (SimConstants.TicksPerSecond * Math.Max(1, speed)) < AudioMix.ShooterGapSeconds)
            return;
        _lastShotTick[shooter.Id] = shot.Tick;
        if (heard)
        {
            long nearest = nearestSq is { } n ? IntMath.Isqrt(n) : 0;
            string[] ids = nearest > AudioMix.FarCm
                ? [$"shot_far.{cls}", $"shot.{weapon.Id}", $"shot.{cls}"]
                : [$"shot.{weapon.Id}", $"shot.{cls}"];
            var heardAt = seen ? shot.From : AudioMix.Rough(shot.From);
            if (!ownShot)
                Hear(HeardKind.Shot, shot.Tick + AudioMix.DelayTicks(nearest), heardAt);
            Queue(shot.Tick + AudioMix.DelayTicks(nearest), heardAt, ids);
        }

        // The bullet beats its own sound: a near miss cracks at once, where the man is.
        if (!ownShot)
        {
            foreach (var man in own)
            {
                if (AudioMix.PassesWithin(shot.From, shot.To, man, AudioMix.CrackCm))
                {
                    Hear(HeardKind.Crack, shot.Tick, man);
                    Queue(shot.Tick, man, "bullet.crack");
                    break;
                }
            }
        }

        long nearSq = (long)AudioMix.ImpactNearCm * AudioMix.ImpactNearCm;
        if ((seen || AudioMix.NearestSq(shot.To, own) <= nearSq) && sim.Map.Contains(shot.To))
            Queue(shot.Tick, shot.To, ImpactCue(sim, shot));
    }

    /// <summary>A reload sounds once, when a man's action turns to reloading.</summary>
    private void Reloads(Simulation sim, Func<Unit, bool> heard, AudioListener listener)
    {
        foreach (var u in sim.Units)
        {
            if (u.Action != CombatAction.Reloading || u.IsOutOfAction)
                _reloading.Remove(u.Id);
            else if (_reloading.Add(u.Id) && u.Weapon is { } weapon && heard(u) && AudioMix.QuietSoundsOn(listener))
                Queue(sim.Tick, u.Position, $"reload.{weapon.Class.ToString().ToLowerInvariant()}");
        }
    }

    /// <summary>
    /// Footsteps by the ground under each man, from the <see cref="SteppingMen"/> movers nearest the camera centre. A man who
    /// starts moving steps at once, then at his pace.
    /// </summary>
    private void Steps(Simulation sim, Func<Unit, bool> heard, AudioListener listener, long elapsed)
    {
        if (elapsed == 0)
            return; // a frame drawn between ticks: nobody has moved, and nobody has stopped either
        var movers = sim.Units
            .Where(u => _lastPos.TryGetValue(u.Id, out var was) && was != u.Position && !u.IsOutOfAction && heard(u))
            .OrderBy(u => (u.Position - listener.CenterCm).LengthSquared)
            .Take(SteppingMen)
            .ToHashSet();
        foreach (var u in sim.Units)
        {
            _lastPos[u.Id] = u.Position;
            if (!movers.Contains(u) || !AudioMix.QuietSoundsOn(listener))
            {
                _stepTimer.Remove(u.Id);
                continue;
            }
            long left = _stepTimer.TryGetValue(u.Id, out long timer) ? timer - elapsed : 0;
            if (left > 0)
            {
                _stepTimer[u.Id] = left;
                continue;
            }
            _stepTimer[u.Id] = left + StepTicks(u.MoveMode);
            if (u.MoveMode == MoveMode.Crawl)
                Queue(sim.Tick, u.Position, "crawl.rustle");
            else if (sim.Map.Contains(u.Position))
                Queue(sim.Tick, u.Position, $"footstep.{Surface(sim, u.Position)}");
        }
    }

    private static long StepTicks(MoveMode mode) => mode switch
    {
        MoveMode.Run => 7,
        MoveMode.Sneak => 16,
        MoveMode.Crawl => 20,
        _ => 11,
    };

    private string Surface(Simulation sim, Vec2 at) =>
        catalog.Surfaces.GetValueOrDefault(sim.Map.TerrainNames[sim.Map.CellAt(at).TerrainId], "forest");

    private static string ImpactCue(Simulation sim, ShotFired shot)
    {
        if (shot.Hit is not null)
            return "impact.body";
        var cell = sim.Map.CellAt(shot.To);
        if (sim.Map.TerrainNames[cell.TerrainId] == "water")
            return "impact.water";
        return cell.ObstacleHeightCm > 0 ? "impact.wood" : "impact.ground";
    }

    private void Hear(HeardKind kind, long tick, Vec2 at) => _pendingHeard.Add(new HeardSound(kind, at, tick));

    private void Queue(long tick, Vec2 at, params string[] ids)
    {
        if (catalog.Find(ids) is { } cue)
            _pending.Add((tick, cue, at));
    }

    private List<SoundCue> Release(long tick, AudioListener listener)
    {
        _heard.Clear();
        for (int i = 0; i < _pendingHeard.Count; i++)
        {
            if (_pendingHeard[i].Tick > tick)
                continue;
            _heard.Add(_pendingHeard[i]);
            _pendingHeard.RemoveAt(i--);
        }
        var due = new List<SoundCue>();
        for (int i = 0; i < _pending.Count; i++)
        {
            var (at, cue, place) = _pending[i];
            if (at > tick)
                continue;
            _pending.RemoveAt(i--);
            if (AudioMix.Volume(cue.VolumeDb, cue.Loud, place, listener) is { } volume)
                due.Add(new SoundCue(cue.Id, Pick(cue), cue.Bus, place, volume, Pitch(), cue.Priority, cue.MaxVoices));
        }
        return due;
    }

    /// <summary>A random version, never the one played last when there is another.</summary>
    private string Pick(CueDef cue)
    {
        var choices = cue.Files.Count > 1 && _lastFile.TryGetValue(cue.Id, out var last)
            ? cue.Files.Where(f => f != last).ToList()
            : cue.Files;
        string file = choices[_rng.Next(choices.Count)];
        _lastFile[cue.Id] = file;
        return file;
    }

    private double Pitch() => 1 + (_rng.NextDouble() * 2 - 1) * PitchSpread;
}
