using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>
/// Runs an attack group every brain tick (spec 2026-09-26-attack-design §2): one half dashes forward to cover while the
/// other half, down in firing positions, keeps the target under fire; they swap; close in on a suppressed target (or
/// after being held up there a while) they go in with grenades and bayonets, then on to the rest of his position.
/// Pinned men are given no orders; everything is planned on what the side knows, not on where the enemy really is.
/// </summary>
internal static class AttackPlanner
{
    public static void Update(Simulation sim, AttackGroup group, long tick, List<SimEvent> events)
    {
        // Out: the fallen, the broken, those given another order, and those with nothing left to shoot (they may go looting).
        foreach (var id in group.MemberList.ToList())
        {
            var m = sim.FindUnit(id);
            if (m is not null && !m.IsOutOfAction && m.MoraleState != MoraleState.Broken && m.AttackGroupId == group.Id && !m.OutOfAmmo)
                continue;
            group.MemberList.Remove(id);
            if (m is not null && m.AttackGroupId == group.Id)
            {
                Simulation.LeaveAttack(m);
                m.AreaTarget = null;
            }
        }

        var target = sim.FindUnit(group.Target);
        if (group.MemberList.Count > 0 && (target is null || target.IsOutOfAction) && NextTarget(sim, group) is { } next)
        {
            // He is down: go on and clear the rest of his position.
            group.Target = next.Id;
            group.LastKnown = next.Position;
            group.CloseSince = null;
            group.StalledSwaps = 0;
            group.Route = null;
            foreach (var id in group.MemberList)
                if (sim.FindUnit(id) is { } man)
                {
                    man.BoundIssued = man.BoundSettled = false;
                    man.OrderedTarget = next.Id;
                }
            target = next;
        }
        if (group.MemberList.Count == 0 || target is null || target.IsOutOfAction)
        {
            End(sim, group);
            return;
        }

        // Where the men believe the target is.
        var contact = sim.Knowledge(group.Side).Get(target.Id);
        bool seen = contact?.Level == ContactLevel.Visible;
        Vec2? known = contact?.Level switch
        {
            ContactLevel.Visible => target.Position,
            ContactLevel.LastKnown or ContactLevel.Suspected => contact.Position,
            _ => group.LastKnown,
        };
        if (known is not { } aim)
        {
            End(sim, group);
            return;
        }
        if (group.LastKnown is { } previous && (previous - aim).LengthSquared > Sq(CombatRules.RouteRefreshCm))
            group.Route = null;
        group.LastKnown = aim;

        var members = group.MemberList.Select(id => sim.FindUnit(id)!).ToList();
        long nearest = members.Min(m => (m.Position - aim).LengthSquared);
        if (nearest <= Sq(CombatRules.FinalAssaultCm))
        {
            group.CloseSince ??= tick;
            // Old hands see the moment sooner: both the suppression they wait for and how long they wait scale with experience.
            int timePct = CombatRules.ExperienceTimePct(members.Sum(m => m.Experience) / members.Count);
            bool suppressed = seen && (target.MoraleState == MoraleState.Pinned
                                       || target.Suppression >= CombatRules.AttackSuppressedTarget * timePct / 100);
            if (suppressed || nearest <= Sq(CombatRules.PointBlankCm) || tick - group.CloseSince >= CombatRules.CloseStallTicks * timePct / 100)
            {
                foreach (var man in members.Where(m => m.MoraleState != MoraleState.Pinned))
                    sim.StartAssault(man, target, aim);
                End(sim, group, keepOrders: true);
                return;
            }
        }
        else
        {
            group.CloseSince = null;
        }

        if (!group.TeamsAssigned)
        {
            group.MemberList.Sort((a, b) => a.Value.CompareTo(b.Value));
            for (int i = 0; i < group.MemberList.Count; i++)
                group.Team[group.MemberList[i]] = i % 2;
            group.TeamsAssigned = true;
            group.BoundingTeam = 0;
            group.BoundStartTick = tick;
        }

        var bounders = members.Where(m => group.Team[m.Id] == group.BoundingTeam).ToList();
        bool dashDone = bounders.All(Settled) || tick - group.BoundStartTick >= CombatRules.BoundTimeoutTicks;
        if (dashDone && tick - group.BoundStartTick >= CombatRules.MinSwapTicks)
        {
                // No bounder found a way forward, three dashes running: there is none.
            group.StalledSwaps = group.MovedThisBound ? 0 : group.StalledSwaps + 1;
            if (group.StalledSwaps >= CombatRules.MaxStalledSwaps)
            {
                End(sim, group);
                return;
            }
            int other = 1 - group.BoundingTeam;
            if (members.Any(m => group.Team[m.Id] == other))
                group.BoundingTeam = other;
            group.BoundStartTick = tick;
            group.MovedThisBound = false;
            foreach (var man in members.Where(m => m.MoraleState != MoraleState.Pinned))
            {
                man.BoundIssued = false;
                man.BoundSettled = false;
            }
        }

        foreach (var man in members)
        {
            bool bounding = group.Team[man.Id] == group.BoundingTeam;
            man.AttackRole = bounding ? AttackRole.Bounding : AttackRole.Covering;
            if (man.MoraleState == MoraleState.Pinned)
                continue; // pinned men stay down; the plan waits for them
            if (bounding && !man.BoundIssued)
            {
                man.AreaTarget = null; // no firing on the dash
                group.MovedThisBound |= StartBound(sim, group, man, aim);
            }
            if (!man.BoundSettled && man.MoveTarget is null && man.TargetStance is null
                && man.Action is not (CombatAction.Throwing or CombatAction.Melee) && (!bounding || man.BoundIssued))
            {
                SoldierBrain.TakeFiringStance(sim, man, aim);
                man.BoundSettled = true;
            }
            // Down in position, he keeps the target's head down: at him when he is seen, else where he is believed to be.
            bool inPosition = !bounding || (man.BoundSettled && man.MoveTarget is null);
            man.AreaTarget = inPosition && !seen && man.Magazines > 0 ? aim : null;
        }
    }

    /// <summary>A man who has made his dash and taken his firing position — or who is pinned and cannot.</summary>
    private static bool Settled(Unit man) =>
        man.MoraleState == MoraleState.Pinned || (man.BoundIssued && man.BoundSettled && man.MoveTarget is null && man.TargetStance is null);

    /// <summary>
    /// Some 25 m toward the target, stopping short of it, into cover near that spot; where the straight way is blocked,
    /// the same distance along the group's route round the obstacle. False only when the way forward is blocked.
    /// </summary>
    private static bool StartBound(Simulation sim, AttackGroup group, Unit man, Vec2 aim)
    {
        man.BoundIssued = true;
        man.BoundSettled = false;
        var toTarget = aim - man.Position;
        long distance = IntMath.Isqrt(toTarget.LengthSquared);
        long step = Math.Min(CombatRules.BoundCm, distance - CombatRules.BoundStopShortCm);
        if (step < CombatRules.MinBoundCm)
            return true; // as close as a dash may take him: stay and fire
        var spot = man.Position + new Vec2((int)(toTarget.X * step / distance), (int)(toTarget.Y * step / distance));
        if (!sim.Map.Contains(spot) || !sim.Map.CellAt(spot).IsPassable
            || Pathfinder.FindPath(sim.Map, man.Position, spot, CombatRules.BoundPathNodeBudget) is null)
        {
            if (RouteSpot(sim, group, man, aim) is not { } routed)
                return false;
            spot = routed;
        }
        var goal = CoverFinder.Find(sim, man, aim, spot, CombatRules.CoverPathNodeBudget, CombatRules.BoundCoverTries,
                       CombatRules.PointBlankCm + CombatRules.BoundCoverSlackCm) ?? spot;
        if (Pathfinder.FindPath(sim.Map, man.Position, goal, CombatRules.BoundPathNodeBudget) is not { } path)
            return false;
        Movement.StartPath(man, goal, MoveMode.Run, path);
        return true;
    }

    /// <summary>A point a dash further along the group's way to the target (found once, with a full search, when needed).</summary>
    private static Vec2? RouteSpot(Simulation sim, AttackGroup group, Unit man, Vec2 aim)
    {
        group.Route ??= Pathfinder.FindPath(sim.Map, man.Position, aim) is { } found ? [man.Position, .. found] : [];
        if (group.Route.Count < 2)
            return null;
        // From the point of the route nearest to him, walk on a dash's length.
        int segment = 0;
        var at = group.Route[0];
        long best = long.MaxValue;
        for (int i = 0; i + 1 < group.Route.Count; i++)
        {
            var a = group.Route[i];
            var ab = group.Route[i + 1] - a;
            long lengthSq = Math.Max(1, ab.LengthSquared);
            long t = Math.Clamp((man.Position - a).Dot(ab), 0, lengthSq);
            var p = a + new Vec2((int)(ab.X * t / lengthSq), (int)(ab.Y * t / lengthSq));
            long d = (p - man.Position).LengthSquared;
            if (d < best)
            {
                best = d;
                segment = i;
                at = p;
            }
        }
        long left = CombatRules.BoundCm;
        for (int i = segment + 1; i < group.Route.Count; i++)
        {
            var leg = group.Route[i] - at;
            long length = IntMath.Isqrt(leg.LengthSquared);
            if (length >= left)
                return at + new Vec2((int)(leg.X * left / Math.Max(1, length)), (int)(leg.Y * left / Math.Max(1, length)));
            left -= length;
            at = group.Route[i];
        }
        return at == man.Position ? null : at;
    }

    /// <summary>The nearest enemy the side sees within the fallen target's position (where he was believed to be), if any.</summary>
    private static Unit? NextTarget(Simulation sim, AttackGroup group)
    {
        if (group.LastKnown is not { } centre)
            return null;
        long radiusSq = (long)CombatRules.AttackPositionRadiusCm * CombatRules.AttackPositionRadiusCm;
        var knowledge = sim.Knowledge(group.Side);
        return sim.Units
            .Where(u => u.Side != group.Side && !u.IsOutOfAction && knowledge.LevelOf(u.Id) == ContactLevel.Visible
                        && (u.Position - centre).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - centre).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    private static void End(Simulation sim, AttackGroup group, bool keepOrders = false)
    {
        foreach (var id in group.MemberList)
        {
            if (sim.FindUnit(id) is not { } man || man.AttackGroupId != group.Id)
                continue;
            Simulation.LeaveAttack(man);
            man.AreaTarget = null;
            if (!keepOrders)
                man.OrderedTarget = null;
        }
        group.Ended = true;
    }

    private static long Sq(long v) => v * v;
}
