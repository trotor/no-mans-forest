using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.AI;

/// <summary>
/// The computer side's commander (spec 2026-09-26-enemy-initiative-design): when the enemy's fire has died down and
/// the odds are on his side he counterattacks the weakest man he knows of; hearing shots from out of sight he sends two
/// men to look; when it is over his men go back to their post. Like a patrol it runs before every step and acts only
/// through ordinary orders, so it is deterministic and in the order log.
/// </summary>
public sealed class EnemyCommander
{
    public const int EvaluateTicks = 40;
    public const int CooldownTicks = 60 * SimConstants.TicksPerSecond;
    public const int QuietTicks = 10 * SimConstants.TicksPerSecond;
    public const int ReturnQuietTicks = 20 * SimConstants.TicksPerSecond;
    public const int KnownForTicks = 30 * SimConstants.TicksPerSecond;
    /// <summary>After first contact they hold and watch this long before any counterattack.</summary>
    public const int WatchTicks = 30 * SimConstants.TicksPerSecond;
    public const int ScoutTicks = 90 * SimConstants.TicksPerSecond;
    public const int CounterattackRangeCm = 15_000;
    public const int InvestigateRangeCm = 20_000;
    public const int HomeSlackCm = 1000;
    private const int SameSpotCm = 2000;

    private readonly Side _side;
    private readonly EnemyAiSpec _rules;
    private readonly SortedDictionary<int, Vec2> _home = [];
    private readonly HashSet<UnitId> _stayPut;
    private readonly SortedSet<int> _away = [];
    private readonly List<UnitId> _scouts = [];
    private long _scoutUntil;
    private Vec2? _lastScoutSpot;
    private long _lastScoutTick = long.MinValue / 2;
    private bool _attacking;
    private long _nextAttackTick;
    private long _lastEnemySeenTick = long.MinValue / 2;
    private long? _firstContactTick;
    private bool _attackFormed;
    private long _lastAttackTick = long.MinValue / 2;
    private UnitId? _lastTarget;
    private readonly SortedDictionary<int, long> _refused = [];
    /// <summary>Men giving area fire in support of the counterattack, and where.</summary>
    private readonly List<UnitId> _supporting = [];
    private Vec2 _supportSpot;
    private bool _engaged;

    /// <param name="men">His men; where they stand now is their post.</param>
    /// <param name="patrolling">Men on a patrol round: they are never sent to scout or back to a post.</param>
    public EnemyCommander(Side side, EnemyAiSpec rules, IEnumerable<Unit> men, IEnumerable<UnitId>? patrolling = null)
    {
        _side = side;
        _rules = rules;
        foreach (var man in men)
            _home[man.Id.Value] = man.Position;
        _stayPut = patrolling?.ToHashSet() ?? [];
    }

    public Side Side => _side;

    /// <summary>Call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick(Simulation sim)
    {
        long tick = sim.Tick;
        if (tick % EvaluateTicks != 0)
            return;
        var men = sim.Units.Where(u => u.Side == _side && !u.IsOutOfAction && _home.ContainsKey(u.Id.Value)).OrderBy(u => u.Id.Value).ToList();
        var knowledge = sim.Knowledge(_side);
        var known = new List<(Unit Enemy, Vec2 At, bool Seen)>();
        int seenDown = 0, heard = 0;
        foreach (var contact in knowledge.Contacts)
        {
            if (sim.FindUnit(contact.Target) is not { } enemy || enemy.Side == _side)
                continue;
            if (contact.Level == ContactLevel.Visible)
            {
                if (enemy.IsOutOfAction)
                    seenDown++;
                else
                    known.Add((enemy, enemy.Position, true));
            }
            else if (contact.Level == ContactLevel.LastKnown && tick - contact.LastUpdateTick <= KnownForTicks)
            {
                known.Add((enemy, contact.Position, false));
            }
            else if (contact.Level == ContactLevel.Suspected)
            {
                heard++;
            }
        }
        if (known.Count > 0)
            _firstContactTick ??= tick;
        // The fight is on once they have been under fire or seen one of the enemy fall; until then they only defend.
        _engaged |= seenDown > 0 || sim.Units.Any(u => u.Side == _side && _home.ContainsKey(u.Id.Value)
                                                   && (u.LastSuppressedTick > long.MinValue / 4 || u.IsOutOfAction));
        if (known.Any(k => k.Seen))
            _lastEnemySeenTick = tick;

        bool attackUnderWay = sim.AttackGroups.Any(g => g.Side == _side && !g.Ended);
        if (attackUnderWay)
            _attackFormed = true;
        // The supporting fire stops when the attack is over or its target shows himself (then they fire at him).
        if (_supporting.Count > 0 && (!attackUnderWay && tick > _lastAttackTick + EvaluateTicks
                                      || _lastTarget is { } aimed && knowledge.LevelOf(aimed) == ContactLevel.Visible))
        {
            foreach (var id in _supporting)
                if (sim.FindUnit(id) is { IsOutOfAction: false } man && man.AreaTarget == _supportSpot && man.MoraleState != MoraleState.Broken)
                    sim.Submit(_side, new StopOrder(id));
            _supporting.Clear();
        }
        if (_attacking && !attackUnderWay)
        {
            _attacking = false;
            if (_attackFormed)
                _nextAttackTick = tick + CooldownTicks;
            else if (_lastTarget is { } refused)
                _refused[refused.Value] = tick + CooldownTicks; // it never got going (no way to him): try someone else
            _attackFormed = false;
        }
        if (_scouts.Count > 0)
        {
            bool arrived = _scouts.All(id => sim.FindUnit(id) is not { IsOutOfAction: false } scout || scout.MoveTarget is null);
            if (arrived || known.Count > 0 || tick >= _scoutUntil)
            {
                // Found them (or gave up): those still creeping forward stop where they are, to fight or be called home.
                foreach (var id in _scouts)
                    if (sim.FindUnit(id) is { IsOutOfAction: false, MoveTarget: not null } scout && scout.MoraleState == MoraleState.Steady)
                        sim.Submit(_side, new StopOrder(id));
                _scouts.Clear();
            }
        }

        if (_rules.Counterattack && _engaged && !_attacking && !attackUnderWay && tick >= _nextAttackTick
            && _firstContactTick is { } first && tick - first >= WatchTicks && TryCounterattack(sim, men, known, seenDown, heard, tick))
            return;
        if (_rules.Investigate && !_attacking && !attackUnderWay && _scouts.Count == 0 && known.Count == 0 && TryInvestigate(sim, men, tick))
            return;
        if (!_attacking && !attackUnderWay && _scouts.Count == 0 && known.Count == 0 && tick - _lastEnemySeenTick >= ReturnQuietTicks)
            ReturnToPost(sim);
    }

    private static bool Fit(Unit man) =>
        !man.IsOutOfAction && man.MoraleState == MoraleState.Steady && man.Suppression < CombatRules.CalmSuppression && !man.OutOfAmmo;

    private bool TryCounterattack(Simulation sim, List<Unit> men, List<(Unit Enemy, Vec2 At, bool Seen)> known, int seenDown, int heard, long tick)
    {
        if (known.Count == 0 || men.FirstOrDefault(m => m.IsLeader && Fit(m)) is not { } leader)
            return false;
        // The enemy's fire has died down: nobody here has been under fire for a while.
        if (men.Any(m => tick - m.LastSuppressedTick < QuietTicks))
            return false;
        var fit = men.Where(m => Fit(m) && !_stayPut.Contains(m.Id)).ToList();
        // Men heard firing out of sight count as well as those seen.
        int enemies = known.Count + heard;
        bool odds = fit.Count * 2 >= enemies * 3 || (seenDown > 0 && fit.Count >= enemies);
        if (!odds)
            return false;
        long rangeSq = (long)CounterattackRangeCm * CounterattackRangeCm;
        var target = known
            .Where(k => (k.At - leader.Position).LengthSquared <= rangeSq
                        && !(_refused.TryGetValue(k.Enemy.Id.Value, out long until) && tick < until))
            .OrderBy(k => k.Seen && (k.Enemy.Wound > WoundLevel.None || k.Enemy.MoraleState == MoraleState.Pinned) ? 0 : 1)
            .ThenBy(k => (k.At - leader.Position).LengthSquared)
            .ThenBy(k => k.Enemy.Id.Value)
            .Select(k => k.Enemy)
            .FirstOrDefault();
        if (target is null)
            return false;
        var targetAt = known.First(k => k.Enemy == target).At;
        // With two squads the one nearer the enemy holds the post and gives fire, the other (the reserve) goes in;
        // with one, the machine gun stays in the post to give fire if there are men enough to go without it.
        var squads = fit.GroupBy(m => m.Squad).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        List<Unit> going, holding;
        if (squads.Count >= 2)
        {
            var post = squads.OrderBy(s => (Centre(s) - targetAt).LengthSquared).ThenBy(s => s[0].Squad).First();
            var reserve = squads.Where(s => s != post && s.Count >= 2).OrderByDescending(s => s.Count).ThenBy(s => s[0].Squad).FirstOrDefault();
            going = reserve ?? post;
            holding = fit.Where(m => !going.Contains(m)).ToList();
        }
        else
        {
            var gunner = fit.Count >= 3 ? fit.FirstOrDefault(m => m.Weapon?.Class == WeaponClass.Lmg) : null;
            going = fit.Where(m => m != gunner).ToList();
            holding = gunner is null ? [] : [gunner];
        }
        if (going.Count < 2)
            return false;
        if (fit.Count >= 3 && going.FirstOrDefault(m => m.Weapon?.Class == WeaponClass.Lmg) is { } lmg && going.Count >= 3)
        {
            going.Remove(lmg); // the post itself goes in: its machine gun stays
            holding.Add(lmg);
        }
        var caller = going.FirstOrDefault(m => m.IsLeader) ?? leader;
        sim.Submit(_side, new CounterattackOrder(caller.Id, target.Id, going.Select(m => m.Id).ToList()));
        // Those who stay keep the target's head down; if he cannot be seen, at where he was last seen.
        if (sim.Knowledge(_side).LevelOf(target.Id) != ContactLevel.Visible && sim.Map.Contains(targetAt))
        {
            _supportSpot = targetAt;
            foreach (var man in holding.Where(m => m.Magazines > 0 && !_stayPut.Contains(m.Id)))
            {
                sim.Submit(_side, new AreaFireOrder(man.Id, targetAt));
                _supporting.Add(man.Id);
            }
        }
        _attacking = true;
        _lastAttackTick = tick;
        _lastTarget = target.Id;
        var attackers = going.Select(m => m.Id).ToList();
        foreach (var id in attackers)
            _away.Add(id.Value);
        return true;
    }

    private static Vec2 Centre(List<Unit> men) =>
        new((int)(men.Sum(m => (long)m.Position.X) / men.Count), (int)(men.Sum(m => (long)m.Position.Y) / men.Count));

    private bool TryInvestigate(Simulation sim, List<Unit> men, long tick)
    {
        if (men.Count == 0)
            return false;
        var leader = men.FirstOrDefault(m => m.IsLeader);
        var centre = leader?.Position ?? new Vec2((int)(men.Sum(m => (long)m.Position.X) / men.Count), (int)(men.Sum(m => (long)m.Position.Y) / men.Count));
        long rangeSq = (long)InvestigateRangeCm * InvestigateRangeCm;
        var heard = sim.Knowledge(_side).Contacts
            .Where(c => c.Level == ContactLevel.Suspected && (c.Position - centre).LengthSquared <= rangeSq)
            .OrderBy(c => (c.Position - centre).LengthSquared)
            .ThenBy(c => c.Target.Value)
            .FirstOrDefault();
        if (heard is null)
            return false;
        var spot = heard.Position;
        if (_lastScoutSpot is { } last && (last - spot).LengthSquared <= (long)SameSpotCm * SameSpotCm && tick - _lastScoutTick < CooldownTicks)
            return false; // they have just looked there
        var scouts = men
            .Where(m => Fit(m) && !m.IsLeader && m.Weapon?.Class != WeaponClass.Lmg && !_stayPut.Contains(m.Id) && m.AttackGroupId is null
                        && m.AssaultTarget is null && m.LootTarget is null && m.Action != CombatAction.Looting)
            .OrderBy(m => (m.Position - spot).LengthSquared)
            .ThenBy(m => m.Id.Value)
            .Take(2)
            .ToList();
        if (scouts.Count == 0)
            return false;
        foreach (var scout in scouts)
        {
            sim.Submit(_side, new MoveOrder(scout.Id, spot, MoveMode.Sneak));
            _scouts.Add(scout.Id);
            _away.Add(scout.Id.Value);
        }
        _scoutUntil = tick + ScoutTicks;
        _lastScoutSpot = spot;
        _lastScoutTick = tick;
        return true;
    }

    private void ReturnToPost(Simulation sim)
    {
        foreach (int id in _away.ToList())
        {
            var man = sim.FindUnit(new UnitId(id));
            if (man is null || man.IsOutOfAction || _stayPut.Contains(man.Id))
            {
                _away.Remove(id);
                continue;
            }
            var home = _home[id];
            if ((man.Position - home).LengthSquared <= (long)HomeSlackCm * HomeSlackCm)
            {
                _away.Remove(id);
                continue;
            }
            if (man.MoraleState != MoraleState.Steady || man.MoveTarget is not null || man.AttackGroupId is not null
                || man.AssaultTarget is not null || man.LootTarget is not null || man.Action == CombatAction.Looting)
                continue; // pinned, on his way, still fighting or searching a body: later (a way home cut short is taken up again)
            sim.Submit(_side, new MoveOrder(man.Id, home, MoveMode.Auto));
        }
    }
}
