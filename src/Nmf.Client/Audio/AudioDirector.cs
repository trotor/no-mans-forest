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
    private readonly Dictionary<string, string> _lastFile = [];
    private readonly Dictionary<UnitId, long> _lastShotTick = [];
    private readonly HashSet<UnitId> _fallen = [];

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
                    Queue(e.Tick + AudioMix.DelayTicks(d), blast.At,
                        d > AudioMix.FarCm ? ["grenade.explode_far", "grenade.explode"] : ["grenade.explode"]);
                    break;
                }
                case UnitWounded { Level: >= WoundLevel.Incapacitated } hit
                    when sim.FindUnit(hit.Unit) is { } man && Heard(man) && _fallen.Add(man.Id):
                    Queue(e.Tick, man.Position, "body.fall");
                    break;
                case MeleeStarted melee when sim.FindUnit(melee.A) is { } a && sim.FindUnit(melee.B) is { } b && (Heard(a) || Heard(b)):
                    Queue(e.Tick, a.Position, "melee.scuffle");
                    break;
                case UnitLooted loot when sim.FindUnit(loot.Looter) is { } looter && Heard(looter):
                    Queue(e.Tick, looter.Position, "loot.rustle");
                    break;
            }
        }
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
        bool throttled = _lastShotTick.TryGetValue(shooter.Id, out long last)
                         && (shot.Tick - last) / (SimConstants.TicksPerSecond * Math.Max(1, speed)) < AudioMix.ShooterGapSeconds;
        if (heard && !throttled)
        {
            _lastShotTick[shooter.Id] = shot.Tick;
            long nearest = nearestSq is { } n ? IntMath.Isqrt(n) : 0;
            string[] ids = nearest > AudioMix.FarCm
                ? [$"shot_far.{cls}", $"shot.{weapon.Id}", $"shot.{cls}"]
                : [$"shot.{weapon.Id}", $"shot.{cls}"];
            Queue(shot.Tick + AudioMix.DelayTicks(nearest), seen ? shot.From : AudioMix.Rough(shot.From), ids);
        }

        // The bullet beats its own sound: a near miss cracks at once, where the man is.
        if (!ownShot)
        {
            foreach (var man in own)
            {
                if (AudioMix.PassesWithin(shot.From, shot.To, man, AudioMix.CrackCm))
                {
                    Queue(shot.Tick, man, "bullet.crack");
                    break;
                }
            }
        }

        long nearSq = (long)AudioMix.ImpactNearCm * AudioMix.ImpactNearCm;
        if ((seen || AudioMix.NearestSq(shot.To, own) <= nearSq) && sim.Map.Contains(shot.To))
            Queue(shot.Tick, shot.To, ImpactCue(sim, shot));
    }

    private static string ImpactCue(Simulation sim, ShotFired shot)
    {
        if (shot.Hit is not null)
            return "impact.body";
        var cell = sim.Map.CellAt(shot.To);
        if (sim.Map.TerrainNames[cell.TerrainId] == "water")
            return "impact.water";
        return cell.ObstacleHeightCm > 0 ? "impact.wood" : "impact.ground";
    }

    private void Queue(long tick, Vec2 at, params string[] ids)
    {
        if (catalog.Find(ids) is { } cue)
            _pending.Add((tick, cue, at));
    }

    private List<SoundCue> Release(long tick, AudioListener listener)
    {
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
