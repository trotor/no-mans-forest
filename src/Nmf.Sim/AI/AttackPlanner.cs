using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>
/// Runs an attack group every brain tick: one half dashes forward to cover while the other half, down in firing positions,
/// keeps the target under fire; they swap; close in on a suppressed target they go in with grenades and bayonets.
/// </summary>
internal static class AttackPlanner
{
    public static void Update(Simulation sim, AttackGroup group, long tick, List<SimEvent> events)
    {
        group.MemberList.RemoveAll(id => sim.FindUnit(id) is not { } m || m.IsOutOfAction || m.MoraleState == MoraleState.Broken
                                         || m.AttackGroupId != group.Id);
        var target = sim.FindUnit(group.Target);
        if (group.MemberList.Count == 0 || target is null || target.IsOutOfAction)
        {
            End(sim, group);
            return;
        }

        // Where the men believe the target is.
        var contact = sim.Knowledge(group.Side).Get(target.Id);
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
        group.LastKnown = aim;

        var members = group.MemberList.Select(id => sim.FindUnit(id)!).ToList();
        long nearest = members.Min(m => (m.Position - aim).LengthSquared);
        bool suppressed = target.MoraleState == MoraleState.Pinned || target.Suppression >= CombatRules.AttackSuppressedTarget;
        if (nearest <= Sq(CombatRules.FinalAssaultCm) && (suppressed || nearest <= Sq(CombatRules.PointBlankCm)))
        {
            foreach (var man in members)
                sim.StartAssault(man, target);
            End(sim, group, keepOrders: true);
            return;
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
        if (bounders.Count == 0 || (bounders.All(Settled) || tick - group.BoundStartTick >= CombatRules.BoundTimeoutTicks))
        {
            // The dash is over: the others go (or, if there are no others, the same men dash again).
            int other = 1 - group.BoundingTeam;
            if (members.Any(m => group.Team[m.Id] == other))
                group.BoundingTeam = other;
            group.BoundStartTick = tick;
            foreach (var man in members)
            {
                man.BoundIssued = false;
                man.BoundSettled = false;
            }
            bounders = members.Where(m => group.Team[m.Id] == group.BoundingTeam).ToList();
        }

        foreach (var man in members)
        {
            bool bounding = group.Team[man.Id] == group.BoundingTeam;
            man.AttackRole = bounding ? AttackRole.Bounding : AttackRole.Covering;
            man.OrderedTarget = target.Id;
            if (bounding && !man.BoundIssued)
                StartBound(sim, man, aim);
            if (!man.BoundSettled && man.MoveTarget is null && man.TargetStance is null
                && man.Action is not (CombatAction.Throwing or CombatAction.Melee) && (!bounding || man.BoundIssued))
            {
                SoldierBrain.TakeFiringStance(sim, man, aim);
                man.BoundSettled = true;
            }
        }
    }

    /// <summary>A man who has made his dash and taken his firing position.</summary>
    private static bool Settled(Unit man) => man.BoundIssued && man.BoundSettled && man.MoveTarget is null && man.TargetStance is null;

    /// <summary>Some 25 m toward the target, stopping short of it, into the best cover near that spot.</summary>
    private static void StartBound(Simulation sim, Unit man, Vec2 aim)
    {
        man.BoundIssued = true;
        man.BoundSettled = false;
        var toTarget = aim - man.Position;
        long distance = IntMath.Isqrt(toTarget.LengthSquared);
        long step = Math.Min(CombatRules.BoundCm, distance - CombatRules.BoundStopShortCm);
        if (step < CombatRules.MinBoundCm)
            return; // close enough already: stay and fire
        var spot = man.Position + new Vec2((int)(toTarget.X * step / distance), (int)(toTarget.Y * step / distance));
        var goal = CoverFinder.Find(sim, man, aim, spot, CombatRules.BoundPathNodeBudget) ?? spot;
        if (!sim.Map.Contains(goal) || Pathfinder.FindPath(sim.Map, man.Position, goal, CombatRules.BoundPathNodeBudget) is not { } path)
            return;
        Movement.StartPath(man, goal, MoveMode.Run, path);
    }

    private static void End(Simulation sim, AttackGroup group, bool keepOrders = false)
    {
        foreach (var id in group.MemberList)
        {
            if (sim.FindUnit(id) is not { } man || man.AttackGroupId != group.Id)
                continue;
            man.AttackGroupId = null;
            man.AttackRole = AttackRole.None;
            man.BoundIssued = man.BoundSettled = false;
            if (!keepOrders)
                man.OrderedTarget = null;
        }
        group.Ended = true;
    }

    private static long Sq(long v) => v * v;
}
