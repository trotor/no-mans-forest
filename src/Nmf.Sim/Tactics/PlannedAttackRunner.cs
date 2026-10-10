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
            Finish(sim, attack, tick, events, success: false);
            return;
        }

        // The position as the side now knows it.
        var knowledge = sim.Knowledge(attack.Side);
        var known = TacticPlanner.PositionOf(sim, attack.Side, attack.Centre)
            .Concat(attack.PositionList.Where(id => sim.FindUnit(id) is { IsOutOfAction: false } && knowledge.LevelOf(id) != ContactLevel.Unknown))
            .Distinct().OrderBy(id => id.Value).ToList();
        if (known.Count == 0)
        {
            Finish(sim, attack, tick, events, success: attack.PositionList.Any(id => sim.FindUnit(id)?.IsOutOfAction == true));
            return;
        }
        foreach (var id in known.Where(id => !attack.PositionList.Contains(id)))
            attack.PositionList.Add(id);
        attack.PositionList.RemoveAll(id => !known.Contains(id) && sim.FindUnit(id)?.IsOutOfAction != true);
        var places = known.Select(id => Place(sim, knowledge, id)).ToList();
        attack.Centre = new Vec2((int)places.Average(p => (long)p.X), (int)places.Average(p => (long)p.Y));

        var members = attack.MemberList.Select(id => sim.FindUnit(id)!).ToList();
        var strike = members.Where(m => attack.Strike.Contains(m.Id)).ToList();
        var fire = members.Where(m => !attack.Strike.Contains(m.Id)).ToList();
        int casualties = attack.Fallen + members.Count(m => m.Wound >= WoundLevel.Serious);

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
        if ((attack.Centre - attack.Plan.Aim).LengthSquared > (long)MovedCm * MovedCm)
        {
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
        attack.Phase = phase;
        attack.PhaseTick = tick;
        events.Add(new PlannedAttackPhaseChanged(tick, attack.Side, attack.Id, phase));
    }

    /// <summary>Each man off to his spot: the fire team straight there, the strike team along the planned way round.</summary>
    private static void Send(Simulation sim, PlannedAttack attack, List<Unit> members)
    {
        foreach (var man in members)
        {
            if (attack.Sent.Contains(man.Id) || man.MoraleState == MoraleState.Pinned || !attack.Spots.TryGetValue(man.Id, out var spot))
                continue;
            attack.Sent.Add(man.Id);
            var path = attack.Strike.Contains(man.Id) ? RouteFor(sim, attack, man, spot) : Pathfinder.FindPath(sim.Map, man.Position, spot);
            if (path is null || path.Count == 0)
                continue;
            man.AutoPace = true;
            Movement.StartPath(man, spot, SoldierBrain.ChoosePace(sim, man), path);
        }
    }

    /// <summary>From him to the planned way, along it leg by leg, and on to his own spot at the form-up.</summary>
    private static List<Vec2>? RouteFor(Simulation sim, PlannedAttack attack, Unit man, Vec2 spot)
    {
        var path = new List<Vec2>();
        var at = man.Position;
        foreach (var point in attack.Plan.Route.Append(spot))
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
    private static bool Arrived(PlannedAttack attack, Unit man) => attack.Sent.Contains(man.Id) && man.MoveTarget is null;

    /// <summary>A man at his spot gets down to fire, facing the position.</summary>
    private static void SettleArrived(Simulation sim, PlannedAttack attack, List<Unit> members)
    {
        foreach (var man in members)
        {
            if (attack.Settled.Contains(man.Id) || !attack.Sent.Contains(man.Id) || man.MoveTarget is not null || man.AssaultTarget is not null)
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

    /// <summary>True when the fire base has not fired once in <see cref="NoFireTicks"/> of trying; <paramref name="firing"/>: it has.</summary>
    private static bool NoFire(PlannedAttack attack, List<Unit> fire, long tick, out bool firing)
    {
        long since = attack.Phase == PlannedPhase.Suppress ? attack.PhaseTick : long.MinValue;
        long first = fire.Where(m => m.LastShotTick >= since).Select(m => m.LastShotTick).DefaultIfEmpty(long.MaxValue).Min();
        if (first != long.MaxValue)
            attack.FirstFireTick ??= first;
        firing = attack.FirstFireTick is not null;
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
            return seen.Count > 0 || tick - attack.PhaseTick >= assault.WaitTicks;
        return tick - attack.PhaseTick >= assault.WaitTicks && seen.All(u => u.MoraleState == MoraleState.Pinned || u.Suppression >= needed / 2);
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
            man.PlannedAttackId = null;
            man.AutoPace = false;
            bool fireMan = !attack.Strike.Contains(man.Id);
            man.AreaTarget = fireMan && man.Weapon is not null && man.Magazines > 0
                             && (attack.Centre - man.Position).LengthSquared <= (long)man.Weapon.RangeCm * man.Weapon.RangeCm
                ? attack.Centre
                : null;
        }
        attack.Ended = true;
    }

    private static void Finish(Simulation sim, PlannedAttack attack, long tick, List<SimEvent> events, bool success)
    {
        events.Add(new PlannedAttackEnded(tick, attack.Side, attack.Id, success));
        foreach (var man in attack.MemberList.Select(sim.FindUnit).OfType<Unit>())
        {
            man.PlannedAttackId = null;
            man.AreaTarget = null;
            man.AutoPace = false;
            if (man.OrderedTarget is { } target && sim.FindUnit(target) is { IsOutOfAction: true })
                man.OrderedTarget = null;
        }
        attack.Ended = true;
    }
}
