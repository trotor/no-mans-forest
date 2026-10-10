using Nmf.Sim.AI;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tactics;

/// <summary>
/// Carries out a planned attack on every brain tick (spec 2026-10-10-tactics-design, "Toteutus"): both teams off at once,
/// the fire base keeps the whole position down, the strike team goes in only when every man of it seen is down, and the
/// attack halts by itself on losses, a fallen leader, a pinned strike team, a position that will not go down, no fire at
/// all, or a position that has moved off.
/// </summary>
internal static class PlannedAttackRunner
{
    /// <summary>A plan's position this far from where it was believed to be is no longer the one planned for.</summary>
    public const int MovedCm = 2_000;
    public const int DeployTimeoutTicks = 60 * SimConstants.TicksPerSecond;
    public const int NoFireTicks = 20 * SimConstants.TicksPerSecond;
    public const int FireBeforeAssaultTicks = 5 * SimConstants.TicksPerSecond;
    /// <summary>A position that keeps moving is planned for again at most this many times (spec 6a-2 §6).</summary>
    public const int MaxReplans = 3;
    /// <summary>Infiltration: found out with the lead man this close to the position they charge; further off they halt.</summary>
    public const int DiscoveredChargeCm = 4_000;
    /// <summary>Infiltration: creeping in may take this long before they go in from where they are.</summary>
    public const int InfiltrateTimeoutTicks = 240 * SimConstants.TicksPerSecond;
    /// <summary>Infiltration: from the opening grenades to the charge.</summary>
    public const int GrenadeWaitTicks = 2 * SimConstants.TicksPerSecond;
    /// <summary>This close to his spot a man has arrived.</summary>
    private const int ArrivedCm = 200;

    /// <summary>Starts the attack (the order applied); false when nobody of the plan can go.</summary>
    public static PlannedAttack? Start(Simulation sim, int id, Side side, TacticPlan plan, TacticDef tactic, long tick)
    {
        var attack = new PlannedAttack(id, side, plan, tactic, tick);
        foreach (var man in plan.StrikeTeam)
            attack.Strike.Add(man);
        var men = plan.FireTeam.Select(p => p.Man).Concat(plan.StrikeTeam).Distinct()
            .Select(sim.FindUnit)
            .Where(u => u is not null && u.Side == side && !u.IsOutOfAction && u.MoraleState == MoraleState.Steady)
            .Cast<Unit>()
            .OrderBy(u => u.Id.Value)
            .ToList();
        if (men.Count == 0)
            return null;
        foreach (var man in men)
        {
            Simulation.LeaveAttack(man);
            man.AreaTarget = null;
            LootSystem.Abandon(man);
            SoldierBrain.ForgetCover(man);
            man.AssaultTarget = null;
            man.AutoPace = false;
            man.StanceOrdered = false;
            Movement.ClearPath(man);
            man.PlannedAttackId = id;
            if (!attack.Strike.Contains(man.Id))
                man.OrderedTarget = plan.Target;
            attack.MemberList.Add(man.Id);
        }
        foreach (var man in men.Where(m => m.Wound >= WoundLevel.Serious))
            attack.WoundedAtStart.Add(man.Id);
        HoldFire(attack, men);
        var knowledge = sim.Knowledge(side);
        foreach (var enemy in plan.Position.Where(e => sim.FindUnit(e) is { IsOutOfAction: false }))
            attack.PositionAt[enemy] = Place(sim, knowledge, enemy);
        attack.PositionHome = attack.PositionAt.Count == 0 ? plan.Aim
            : new Vec2((int)attack.PositionAt.Values.Average(p => (long)p.X), (int)attack.PositionAt.Values.Average(p => (long)p.Y));
        Hold(sim, attack);
        attack.Strength = men.Count;
        attack.AverageNerve = (int)men.Average(m => m.Nerve);
        attack.Leader = men.Where(m => m.IsLeader).OrderByDescending(m => m.LeaderQualityPct).ThenBy(m => m.Id.Value)
            .Select(m => m.Id).DefaultIfEmpty(men[0].Id).First();
        return attack;
    }

    public static void Update(Simulation sim, PlannedAttack attack, long tick, List<SimEvent> events)
    {
        bool leaderDown = false;
        foreach (var id in attack.MemberList.ToList())
        {
            var man = sim.FindUnit(id);
            if (man is not null && man.PlannedAttackId == attack.Id && !man.IsOutOfAction && man.MoraleState != MoraleState.Broken)
                continue;
            attack.MemberList.Remove(id);
            if (man is null)
                continue;
            GiveBackFire(attack, man);
            if (man.IsOutOfAction)
            {
                attack.Fallen++;
                leaderDown |= id == attack.Leader;
            }
            if (man.PlannedAttackId == attack.Id)
            {
                man.PlannedAttackId = null;
                man.AreaTarget = null;
            }
        }
        if (attack.MemberList.Count == 0)
        {
            Finish(sim, attack, tick, events, success: false, disbanded: true);
            return;
        }

        // The position as the side now knows it.
        var knowledge = sim.Knowledge(attack.Side);
        var known = TacticPlanner.PositionOf(sim, attack.Side, attack.PositionHome)
            .Concat(attack.PositionList.Where(id => sim.FindUnit(id) is { IsOutOfAction: false } && knowledge.LevelOf(id) != ContactLevel.Unknown))
            .Distinct().OrderBy(id => id.Value).ToList();
        if (known.Count == 0)
        {
            Finish(sim, attack, tick, events, success: attack.PositionList.Any(id => sim.FindUnit(id)?.IsOutOfAction == true));
            return;
        }
        foreach (var id in known.Where(id => !attack.PositionList.Contains(id)))
            attack.PositionList.Add(id);
        foreach (var id in known)
            attack.PositionAt.TryAdd(id, Place(sim, knowledge, id));
        attack.PositionList.RemoveAll(id => !known.Contains(id) && sim.FindUnit(id)?.IsOutOfAction != true);
        var places = known.Select(id => Place(sim, knowledge, id)).ToList();
        attack.Centre = new Vec2((int)places.Average(p => (long)p.X), (int)places.Average(p => (long)p.Y));

        var members = attack.MemberList.Select(id => sim.FindUnit(id)!).ToList();
        var strike = members.Where(m => attack.Strike.Contains(m.Id)).ToList();
        var fire = members.Where(m => !attack.Strike.Contains(m.Id)).ToList();
        int casualties = attack.Fallen + members.Count(m => m.Wound >= WoundLevel.Serious && !attack.WoundedAtStart.Contains(m.Id));

        if (leaderDown)
        {
            Halt(sim, attack, HaltReason.LeaderDown, casualties, tick, events);
            return;
        }
        int limit = Math.Max(1, attack.Strength * attack.Tactic.AbortCasualtiesPct * (100 + attack.AverageNerve - 50) / 10_000);
        if (casualties >= limit)
        {
            Halt(sim, attack, HaltReason.Casualties, casualties, tick, events);
            return;
        }
        // Moved off: every man of it still known is well away from where he was (one of them falling is not the position moving).
        if (known.All(id => (Place(sim, knowledge, id) - attack.PositionAt[id]).LengthSquared > (long)MovedCm * MovedCm))
        {
            if (!Replan(sim, attack, known, knowledge, tick, events))
                Halt(sim, attack, HaltReason.TargetMoved, casualties, tick, events);
            return;
        }
        var upright = strike.Where(m => !m.IsOutOfAction).ToList();
        if (upright.Count > 0 && upright.Count(m => m.MoraleState == MoraleState.Pinned) * 2 >= upright.Count)
        {
            attack.PinnedSince ??= tick;
            if (tick - attack.PinnedSince >= attack.Tactic.AbortPinnedTicks)
            {
                Halt(sim, attack, HaltReason.StrikePinned, casualties, tick, events);
                return;
            }
        }
        else
        {
            attack.PinnedSince = null;
        }

        // A man pinned short of his spot lost his way there: once he is up again he is sent on.
        foreach (var man in members)
        {
            if (attack.Sent.Contains(man.Id) && !attack.Stuck.Contains(man.Id) && man.MoveTarget is null && man.AssaultTarget is null
                && man.MoraleState != MoraleState.Pinned && Destination(attack, man) is { } spot
                && (man.Position - spot).LengthSquared > (long)ArrivedCm * ArrivedCm)
            {
                attack.Sent.Remove(man.Id);
                attack.Settled.Remove(man.Id);
            }
        }
        if (attack.Tactic.Infiltrate is not null)
        {
            Infiltrate(sim, attack, members, known, knowledge, casualties, tick, events);
            return;
        }
        if (attack.Phase != PlannedPhase.Deploy && fire.Any(m => m.MoraleState != MoraleState.Pinned
                && known.Any(e => sim.FindUnit(e) is { IsOutOfAction: false } enemy && Firing.CanSee(sim, m, enemy))))
            attack.SightTick = tick;

        switch (attack.Phase)
        {
            case PlannedPhase.Deploy:
                Send(sim, attack, members);
                SettleArrived(sim, attack, members);
                if (fire.All(m => m.MoraleState == MoraleState.Pinned || (attack.Settled.Contains(m.Id) && m.TargetStance is null))
                    || tick - attack.PhaseTick >= DeployTimeoutTicks)
                    Enter(attack, PlannedPhase.Suppress, tick, events);
                break;
            case PlannedPhase.Suppress:
                Send(sim, attack, members);
                SettleArrived(sim, attack, members);
                Distribute(sim, attack, fire, known, knowledge);
                if (NoFire(attack, fire, tick, out bool firing))
                {
                    Halt(sim, attack, HaltReason.NoFire, casualties, tick, events);
                    return;
                }
                if (attack.Tactic.Assault is not null && firing && tick - attack.FirstFireTick >= FireBeforeAssaultTicks
                    && upright.Where(m => m.MoraleState != MoraleState.Pinned).All(m => Arrived(attack, m)))
                    Enter(attack, PlannedPhase.Assault, tick, events);
                break;
            case PlannedPhase.Assault:
                SettleArrived(sim, attack, members);
                Distribute(sim, attack, fire, known, knowledge);
                if (attack.AssaultLaunched)
                {
                    // In: a man whose enemy is down goes for the next of the position.
                    foreach (var man in strike.Where(m => m.AssaultTarget is null && m.MoraleState != MoraleState.Pinned))
                        GoIn(sim, knowledge, man, known);
                    break;
                }
                if (Down(sim, attack, strike, known, knowledge, tick))
                {
                    foreach (var man in strike.Where(m => m.MoraleState != MoraleState.Pinned))
                        GoIn(sim, knowledge, man, known);
                    attack.AssaultLaunched = true;
                }
                else if (tick - attack.PhaseTick >= attack.Tactic.Assault!.GiveUpTicks)
                {
                    Halt(sim, attack, HaltReason.NotSuppressed, casualties, tick, events);
                }
                break;
        }
    }

    private static Vec2 Place(Simulation sim, SideKnowledge knowledge, UnitId id) =>
        knowledge.Get(id) is { } c ? TacticPlanner.KnownPlace(sim, c) : sim.FindUnit(id)!.Position;

    private static void Enter(PlannedAttack attack, PlannedPhase phase, long tick, List<SimEvent> events)
    {
        attack.Stuck.Clear(); // a new phase, new places to go: try the way again
        attack.Phase = phase;
        attack.PhaseTick = tick;
        events.Add(new PlannedAttackPhaseChanged(tick, attack.Side, attack.Id, phase));
    }

    /// <summary>Each man off to his spot: the fire team straight there, the strike team along the planned way round.</summary>
    private static void Send(Simulation sim, PlannedAttack attack, List<Unit> members)
    {
        foreach (var man in members)
        {
            if (attack.Sent.Contains(man.Id) || man.MoraleState == MoraleState.Pinned || Destination(attack, man) is not { } spot)
                continue;
            attack.Sent.Add(man.Id);
            bool holding = Holding(attack) && attack.HoldSpots.ContainsKey(man.Id);
            var path = attack.Strike.Contains(man.Id)
                ? RouteFor(sim, attack.Plan.Route, man, spot, holding ? attack.HoldBefore : attack.Plan.Route.Count)
                : RouteFor(sim, attack.Plan.FireRoute ?? [], man, spot, attack.Plan.FireRoute?.Count ?? 0);
            if (path is null || path.Count == 0)
            {
                if ((man.Position - spot).LengthSquared > (long)ArrivedCm * ArrivedCm)
                    attack.Stuck.Add(man.Id); // no way there: he stays put and counts as there
                continue;
            }
            // Creeping in, they sneak all the way; otherwise each man keeps his own pace to the ground he is on.
            bool creeping = attack.Tactic.Infiltrate is not null;
            man.AutoPace = !creeping;
            Movement.StartPath(man, spot, creeping ? MoveMode.Sneak : SoldierBrain.ChoosePace(sim, man), path);
        }
    }

    /// <summary>
    /// Infiltration (spec 2026-10-10-infiltration-enemy-design §1): creep in holding fire; found out close by, charge,
    /// further off, halt; all at the form-up (or out of time): grenades first where they reach, then everyone in.
    /// </summary>
    private static void Infiltrate(Simulation sim, PlannedAttack attack, List<Unit> members, List<UnitId> known, SideKnowledge knowledge,
        int casualties, long tick, List<SimEvent> events)
    {
        if (attack.Phase == PlannedPhase.Deploy)
        {
            Send(sim, attack, members);
            if (members.Any(m => m.LastSuppressedTick >= attack.StartTick))
            {
                // Strung out along the way: it is the lead man's distance that says whether a charge can carry.
                long nearest = members.Min(m => (m.Position - attack.Centre).LengthSquared);
                if (nearest > (long)DiscoveredChargeCm * DiscoveredChargeCm)
                {
                    Halt(sim, attack, HaltReason.Discovered, casualties, tick, events);
                    return;
                }
                Enter(attack, PlannedPhase.Assault, tick, events);
                GiveBackFire(attack, members);
                Charge(sim, attack, members, known, knowledge);
                return;
            }
            bool there = members.Where(m => m.MoraleState != MoraleState.Pinned).All(m => Arrived(attack, m));
            if (!there && tick - attack.PhaseTick < InfiltrateTimeoutTicks)
                return;
            Enter(attack, PlannedPhase.Assault, tick, events);
            GiveBackFire(attack, members);
            // Grenades first, at the known men within reach; the rest wait for them to go off.
            foreach (var man in members.Where(m => m.MoraleState != MoraleState.Pinned))
            {
                var enemy = known.Select(id => sim.FindUnit(id)!)
                    .Where(e => GrenadeSystem.CanOpenWith(sim, man, e, tick))
                    .OrderBy(e => (e.Position - man.Position).LengthSquared).ThenBy(e => e.Id.Value)
                    .FirstOrDefault();
                if (enemy is null)
                    continue;
                GrenadeSystem.StartThrow(man, enemy);
                attack.GrenadeTick = tick;
            }
            if (attack.GrenadeTick is null)
                Charge(sim, attack, members, known, knowledge);
            return;
        }
        if (!attack.AssaultLaunched)
        {
            if (tick - attack.GrenadeTick >= GrenadeWaitTicks)
                Charge(sim, attack, members, known, knowledge);
            return;
        }
        foreach (var man in members.Where(m => m.AssaultTarget is null && m.MoraleState != MoraleState.Pinned))
            GoIn(sim, knowledge, man, known);
    }

    private static void Charge(Simulation sim, PlannedAttack attack, List<Unit> members, List<UnitId> known, SideKnowledge knowledge)
    {
        foreach (var man in members.Where(m => m.MoraleState != MoraleState.Pinned))
            GoIn(sim, knowledge, man, known);
        attack.AssaultLaunched = true;
    }

    /// <summary>Infiltration: nobody gives the group away by firing while it creeps in.</summary>
    private static void HoldFire(PlannedAttack attack, IEnumerable<Unit> men)
    {
        if (attack.Tactic.Infiltrate is null)
            return;
        foreach (var man in men)
        {
            attack.PolicyBefore.TryAdd(man.Id, man.FirePolicy);
            man.FirePolicy = FirePolicy.HoldFire;
        }
    }

    private static void GiveBackFire(PlannedAttack attack, IEnumerable<Unit> men)
    {
        foreach (var man in men)
            GiveBackFire(attack, man);
    }

    private static void GiveBackFire(PlannedAttack attack, Unit man)
    {
        if (attack.PolicyBefore.Remove(man.Id, out var policy))
            man.FirePolicy = policy;
    }

    /// <summary>
    /// From him to the planned way (its point nearest him), along it leg by leg up to point <paramref name="upTo"/>
    /// (exclusive), and on to his own spot.
    /// </summary>
    private static List<Vec2>? RouteFor(Simulation sim, IReadOnlyList<Vec2> route, Unit man, Vec2 spot, int upTo)
    {
        var path = new List<Vec2>();
        var at = man.Position;
        int from = 0;
        for (int i = 1; i < Math.Min(upTo, route.Count); i++)
            if ((route[i] - man.Position).LengthSquared <= (route[from] - man.Position).LengthSquared)
                from = i;
        foreach (var point in route.Skip(from).Take(Math.Max(0, Math.Min(upTo, route.Count) - from)).Append(spot))
        {
            if (point.ToCell() == at.ToCell())
                continue;
            var leg = Pathfinder.FindPath(sim.Map, at, point, path.Count == 0 ? Pathfinder.MaxExpandedNodes : 4_000);
            if (leg is null)
                return path.Count == 0 ? Pathfinder.FindPath(sim.Map, man.Position, spot) : path;
            path.AddRange(leg);
            at = point;
        }
        return path;
    }

    /// <summary>Sent off and stopped: at his spot, or as near as the way allowed.</summary>
    private static bool Arrived(PlannedAttack attack, Unit man) =>
        attack.Sent.Contains(man.Id) && man.MoveTarget is null
        && (attack.Stuck.Contains(man.Id) || Destination(attack, man) is not { } spot || (man.Position - spot).LengthSquared <= (long)ArrivedCm * ArrivedCm);

    /// <summary>
    /// Where the man is going now: until the fire base is firing at a position it sees, a strike man waits short of the
    /// open; else his own spot.
    /// </summary>
    private static Vec2? Destination(PlannedAttack attack, Unit man) =>
        Holding(attack) && attack.HoldSpots.TryGetValue(man.Id, out var hold) ? hold
        : attack.Spots.TryGetValue(man.Id, out var spot) ? spot : null;

    /// <summary>The strike team waits: the fire base is not yet firing at men it can see.</summary>
    private static bool Holding(PlannedAttack attack) =>
        attack.Tactic.Infiltrate is null && attack.Phase == PlannedPhase.Deploy || (attack.Phase == PlannedPhase.Suppress && (attack.FirstFireTick is null || attack.SightTick is null));

    /// <summary>
    /// The strike team's waiting places for the deploy: short of the first point of its way the enemy sees, each man
    /// placed as at the form-up; where it starts in the open it waits in the nearest cover (spec 6a-2 §4).
    /// </summary>
    private static void Hold(Simulation sim, PlannedAttack attack)
    {
        attack.HoldSpots.Clear();
        var exposed = attack.Plan.RouteExposed;
        attack.HoldBefore = exposed is null ? -1 : exposed.ToList().IndexOf(true);
        if (attack.HoldBefore < 0 || attack.Plan.FormUp.Count == 0)
            return;
        var reachable = attack.HoldBefore == 0 ? null : Pathfinder.ReachableFrom(sim.Map, attack.Plan.Route[attack.HoldBefore - 1]);
        foreach (var id in attack.Plan.StrikeTeam)
        {
            if (sim.FindUnit(id) is not { } man)
                continue;
            if (attack.HoldBefore == 0)
            {
                // Already in the open: he waits behind the nearest thing facing the position, or down where he is.
                attack.HoldSpots[id] = CoverFinder.Find(sim, man, attack.Plan.Aim) ?? man.Position;
                continue;
            }
            var basePoint = attack.Plan.Route[attack.HoldBefore - 1];
            var offset = attack.Spots.TryGetValue(id, out var spot) ? spot - attack.Plan.FormUp[0].At : Vec2.Zero;
            var hold = basePoint + offset;
            attack.HoldSpots[id] = reachable!(hold) ? hold : basePoint;
        }
    }

    /// <summary>
    /// The position has moved off: plan again, the same way, against its nearest man still known (spec 6a-2 §6). False
    /// when it has moved too often or no plan can be made.
    /// </summary>
    private static bool Replan(Simulation sim, PlannedAttack attack, List<UnitId> known, SideKnowledge knowledge, long tick, List<SimEvent> events)
    {
        if (attack.Replans >= MaxReplans)
            return false;
        var target = known.OrderBy(id => (Place(sim, knowledge, id) - attack.PositionHome).LengthSquared).ThenBy(id => id.Value).First();
        if (TacticPlanner.Propose(sim, attack.Side, attack.MemberList.ToList(), target, attack.Tactic) is not { } plan)
            return false;
        attack.Replans++;
        attack.Plan = plan;
        attack.Strike.Clear();
        foreach (var id in plan.StrikeTeam)
            attack.Strike.Add(id);
        attack.Spots.Clear();
        foreach (var p in plan.FireTeam.Concat(plan.FormUp))
            attack.Spots[p.Man] = p.At;
        attack.Sent.Clear();
        attack.Settled.Clear();
        attack.Stuck.Clear();
        attack.PositionAt.Clear();
        foreach (var id in plan.Position.Where(e => sim.FindUnit(e) is { IsOutOfAction: false }))
            attack.PositionAt[id] = Place(sim, knowledge, id);
        attack.PositionHome = attack.PositionAt.Count == 0 ? plan.Aim
            : new Vec2((int)attack.PositionAt.Values.Average(p => (long)p.X), (int)attack.PositionAt.Values.Average(p => (long)p.Y));
        attack.FirstFireTick = null;
        attack.SightTick = null;
        attack.PinnedSince = null;
        attack.AssaultLaunched = false; // any assault on the old place is off
        attack.PositionList.Clear();
        attack.PositionList.AddRange(plan.Position);
        foreach (var man in attack.MemberList.Select(sim.FindUnit).OfType<Unit>())
        {
            Movement.ClearPath(man);
            man.AssaultTarget = null;
            man.AreaTarget = null;
            man.OrderedTarget = attack.Strike.Contains(man.Id) ? man.OrderedTarget : plan.Target;
        }
        attack.Phase = PlannedPhase.Deploy;
        attack.StartTick = tick;
        attack.GrenadeTick = null;
        HoldFire(attack, attack.MemberList.Select(sim.FindUnit).OfType<Unit>());
        Hold(sim, attack);
        Enter(attack, PlannedPhase.Deploy, tick, events);
        return true;
    }

    /// <summary>A man at his spot gets down to fire, facing the position.</summary>
    private static void SettleArrived(Simulation sim, PlannedAttack attack, List<Unit> members)
    {
        foreach (var man in members)
        {
            if (attack.Settled.Contains(man.Id) || !Arrived(attack, man) || man.AssaultTarget is not null)
                continue;
            man.AutoPace = false;
            SoldierBrain.TakeFiringStance(sim, man, attack.Centre);
            attack.Settled.Add(man.Id);
        }
    }

    /// <summary>
    /// The fire shared over the position: their machine gun first (to ours), then the nearest; each man aims at his own man
    /// when he sees him and otherwise bursts at where that man is believed to be.
    /// </summary>
    private static void Distribute(Simulation sim, PlannedAttack attack, List<Unit> fire, List<UnitId> known, SideKnowledge knowledge)
    {
        var fireBase = attack.Plan.FireTeam.Count == 0 ? attack.Plan.Aim
            : new Vec2((int)attack.Plan.FireTeam.Average(p => (long)p.At.X), (int)attack.Plan.FireTeam.Average(p => (long)p.At.Y));
        var targets = known
            .Select(id => sim.FindUnit(id)!)
            .OrderBy(u => knowledge.LevelOf(u.Id) == ContactLevel.Visible && u.Weapon?.Class == WeaponClass.Lmg ? 0 : 1)
            .ThenBy(u => (Place(sim, knowledge, u.Id) - fireBase).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .ToList();
        var shooters = fire.OrderBy(m => m.Weapon?.Class == WeaponClass.Lmg ? 0 : 1).ThenBy(m => m.Id.Value).ToList();
        for (int i = 0; i < shooters.Count; i++)
        {
            var man = shooters[i];
            var enemy = targets[i % targets.Count];
            man.OrderedTarget = enemy.Id;
            man.AreaTarget = man.Weapon is not null && man.Magazines > 0 ? Place(sim, knowledge, enemy.Id) : null;
        }
    }

    /// <summary>
    /// True when, <see cref="NoFireTicks"/> into keeping the position down, the fire base has not fired once or has not
    /// once seen a man of it (bursts into the blue keep nobody's head down); <paramref name="firing"/>: it has done both.
    /// </summary>
    private static bool NoFire(PlannedAttack attack, List<Unit> fire, long tick, out bool firing)
    {
        long since = attack.Phase == PlannedPhase.Suppress ? attack.PhaseTick : long.MinValue;
        long first = fire.Where(m => m.LastShotTick >= since).Select(m => m.LastShotTick).DefaultIfEmpty(long.MaxValue).Min();
        if (first != long.MaxValue)
            attack.FirstFireTick ??= first;
        firing = attack.FirstFireTick is not null && attack.SightTick is not null;
        return !firing && tick - attack.PhaseTick >= NoFireTicks;
    }

    /// <summary>Every man of the position the side sees is down, pinned or suppressed enough; after the wait, half will do.</summary>
    private static bool Down(Simulation sim, PlannedAttack attack, List<Unit> strike, List<UnitId> known, SideKnowledge knowledge, long tick)
    {
        var assault = attack.Tactic.Assault!;
        int experience = strike.Count == 0 ? 50 : (int)strike.Average(m => m.Experience);
        int needed = assault.Suppression * CombatRules.ExperienceTimePct(experience) / 100;
        var seen = known.Where(id => knowledge.LevelOf(id) == ContactLevel.Visible).Select(id => sim.FindUnit(id)!).ToList();
        if (seen.All(u => u.MoraleState == MoraleState.Pinned || u.Suppression >= needed))
            return seen.Count > 0; // nobody of it in sight: nobody goes in blind
        return seen.Count > 0 && tick - attack.PhaseTick >= assault.WaitTicks
               && seen.All(u => u.MoraleState == MoraleState.Pinned || u.Suppression >= needed / 2);
    }

    private static void GoIn(Simulation sim, SideKnowledge knowledge, Unit man, List<UnitId> known)
    {
        var enemy = known.Select(id => sim.FindUnit(id)!).Where(u => !u.IsOutOfAction)
            .OrderBy(u => (Place(sim, knowledge, u.Id) - man.Position).LengthSquared).ThenBy(u => u.Id.Value).FirstOrDefault();
        if (enemy is null)
            return;
        man.AreaTarget = null;
        sim.StartAssault(man, enemy, Place(sim, knowledge, enemy.Id));
    }

    /// <summary>Stopped short: the strike team fends for itself, the fire base keeps the position down.</summary>
    private static void Halt(Simulation sim, PlannedAttack attack, HaltReason reason, int casualties, long tick, List<SimEvent> events)
    {
        events.Add(new PlannedAttackHalted(tick, attack.Side, attack.Id, reason, casualties));
        foreach (var man in attack.MemberList.Select(sim.FindUnit).OfType<Unit>())
        {
            GiveBackFire(attack, man);
            man.PlannedAttackId = null;
            man.AutoPace = false;
            if (man.MoveTarget is not null && man.AssaultTarget is null)
                Movement.ClearPath(man); // nobody walks on to a form-up of an attack called off
            bool fireMan = !attack.Strike.Contains(man.Id);
            // The strike team gets behind something facing the position, at its own pace (spec 6a-2 §5).
            if (!fireMan && man.AssaultTarget is null && man.MoraleState != MoraleState.Pinned
                && CoverFinder.Find(sim, man, attack.Centre) is { } cover && Pathfinder.FindPath(sim.Map, man.Position, cover) is { } toCover)
            {
                man.AutoPace = true;
                Movement.StartPath(man, cover, SoldierBrain.ChoosePace(sim, man), toCover);
            }
            man.AreaTarget = fireMan && man.Weapon is not null && man.Magazines > 0
                             && (attack.Centre - man.Position).LengthSquared <= (long)man.Weapon.RangeCm * man.Weapon.RangeCm
                ? attack.Centre
                : null;
        }
        attack.Ended = true;
    }

    private static void Finish(Simulation sim, PlannedAttack attack, long tick, List<SimEvent> events, bool success, bool disbanded = false)
    {
        events.Add(new PlannedAttackEnded(tick, attack.Side, attack.Id, success, disbanded));
        foreach (var man in attack.MemberList.Select(sim.FindUnit).OfType<Unit>())
        {
            GiveBackFire(attack, man);
            man.PlannedAttackId = null;
            man.AreaTarget = null;
            man.AutoPace = false;
            if (man.OrderedTarget is { } target && sim.FindUnit(target) is { IsOutOfAction: true })
                man.OrderedTarget = null;
        }
        attack.Ended = true;
    }
}
