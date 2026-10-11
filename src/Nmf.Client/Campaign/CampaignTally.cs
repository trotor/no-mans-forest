using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Campaign;

/// <summary>
/// What each of our men did in a campaign mission (spec 2026-10-11-campaign-design §9a): his hits on the enemy and the
/// enemies he put out of the fight. Fed every step's events, since the session keeps only the latest of them; it reads
/// the sim and never changes it.
/// </summary>
/// <remarks>
/// A hit is a shot of his that struck an enemy, or a wound from the burst of a grenade he threw. An enemy out of the
/// fight (incapacitated, dead or taken) is the kill of the last of ours to hit him, or of the man who beat him hand to
/// hand; a mortar shell leaves him nobody's. No enemy counts twice, nor one down before the tally began.
/// </remarks>
public sealed class CampaignTally(Side side, IEnumerable<UnitId>? alreadyDown = null)
{
    /// <summary>What the tally needs to know of a man: his side, his name, where he is.</summary>
    public readonly record struct Seen(Side Side, string? Name, Vec2 Position);

    private readonly Dictionary<int, (UnitId Thrower, int BlastCm)> _grenades = [];
    private readonly Dictionary<UnitId, UnitId?> _lastHitter = [];
    private readonly HashSet<UnitId> _down = [.. alreadyDown ?? []];
    private readonly Dictionary<string, (int Hits, int Kills)> _men = [];

    /// <summary>A tally for a session's sim: the enemies already down at the start are nobody's.</summary>
    public static CampaignTally For(Simulation sim, Side side) => new(side, sim.Units.Where(u => u.IsOutOfAction).Select(u => u.Id));

    /// <summary>Hits and kills of one of our men by name (none: zero).</summary>
    public (int Hits, int Kills) Of(string name) => _men.GetValueOrDefault(name);

    /// <summary>One step's events, then the grenades lying live after it.</summary>
    public void Observe(Simulation sim, IReadOnlyList<SimEvent> events) =>
        Observe(events, id => sim.FindUnit(id) is { } u ? new Seen(u.Side, u.Name, u.Position) : null,
            sim.Grenades.Select(g => (g.Id, g.Thrower, g.Def.BlastRadiusCm)));

    public void Observe(IReadOnlyList<SimEvent> events, Func<UnitId, Seen?> find, IEnumerable<(int Id, UnitId Thrower, int BlastCm)> liveGrenades)
    {
        // The burst the wounds that follow come from: a burst emits its wounds right after itself.
        (Vec2 At, UnitId? Thrower, int ReachCm)? burst = null;
        var downNow = new List<UnitId>();
        var meleeWins = new Dictionary<UnitId, UnitId>();
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotFired shot:
                    burst = null;
                    if (shot.Hit is { } hit)
                        Hit(hit, shot.Shooter);
                    break;
                case GrenadeExploded g:
                    burst = _grenades.Remove(g.Grenade, out var thrown) ? (g.At, thrown.Thrower, thrown.BlastCm) : (g.At, null, 0);
                    break;
                case ShellExploded shell:
                    burst = (shell.At, null, MortarSupport.Round.BlastRadiusCm);
                    break;
                case UnitWounded w:
                    if (burst is { } b && find(w.Unit) is { } man && (man.Position - b.At).LengthSquared <= (long)b.ReachCm * b.ReachCm)
                    {
                        if (b.Thrower is { } thrower)
                            Hit(w.Unit, thrower);
                        else
                            _lastHitter[w.Unit] = null; // a shell: nobody's
                    }
                    if (w.Level >= WoundLevel.Incapacitated)
                        downNow.Add(w.Unit);
                    break;
                case UnitCaptured c:
                    downNow.Add(c.Unit);
                    break;
                case MeleeEnded m:
                    meleeWins[m.Loser] = m.Winner;
                    // A loser left only badly wounded, who bleeds out later, is still the winner's man.
                    if (find(m.Loser) is { } loser && loser.Side != side)
                        _lastHitter[m.Loser] = Ours(m.Winner) is not null ? m.Winner : null;
                    break;
            }
        }
        // Decided at the end of the step: a melee's loser falls before it is said who won.
        foreach (var id in downNow)
        {
            if (!_down.Add(id) || find(id) is not { } enemy || enemy.Side == side)
                continue;
            var by = meleeWins.TryGetValue(id, out var winner) ? winner : _lastHitter.GetValueOrDefault(id);
            if (by is { } killer && Ours(killer) is { } name)
                _men[name] = (_men.GetValueOrDefault(name).Hits, _men.GetValueOrDefault(name).Kills + 1);
        }
        foreach (var (id, thrower, blast) in liveGrenades)
            _grenades[id] = (thrower, blast);

        void Hit(UnitId target, UnitId by)
        {
            if (_down.Contains(target) || find(target) is not { } struck || struck.Side == side)
                return;
            if (Ours(by) is { } name)
            {
                _men[name] = (_men.GetValueOrDefault(name).Hits + 1, _men.GetValueOrDefault(name).Kills);
                _lastHitter[target] = by;
            }
            else
                _lastHitter[target] = null;
        }

        string? Ours(UnitId id) => find(id) is { } man && man.Side == side ? man.Name : null;
    }
}
