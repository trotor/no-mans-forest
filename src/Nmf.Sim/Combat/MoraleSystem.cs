using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Suppression, the Steady / Pinned / Broken states, morale checks, rally and leader succession.</summary>
internal static class MoraleSystem
{
    public static bool UnderFire(Unit unit, long tick) => tick - unit.LastSuppressedTick <= CombatRules.UnderFireTicks;

    public static void AddSuppression(Simulation sim, Unit unit, int amount, long tick, List<SimEvent> events)
    {
        if (amount <= 0 || unit.IsOutOfAction)
            return;
        unit.LastSuppressedTick = tick;
        int before = unit.Suppression;
        unit.Suppression = Math.Min(CombatRules.MaxSuppression, unit.Suppression + amount);
        if (before < CombatRules.MoraleCheckSuppression && unit.Suppression >= CombatRules.MoraleCheckSuppression)
            Check(sim, unit, tick, events);
        UpdatePinned(unit, tick, events);
    }

    public static void Tick(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction)
            return;
        if (unit.Suppression > 0)
        {
            int perSecond = CombatRules.SuppressionDecayPerSecond
                            + (unit.Stance == Stance.Prone ? CombatRules.ProneDecayBonusPerSecond : 0)
                            + (LeaderInRange(sim, unit) is not null ? CombatRules.LeaderDecayBonusPerSecond : 0);
            // Spread the per-second amount evenly over the ticks of each second, in whole points.
            int k = (int)(tick % SimConstants.TicksPerSecond);
            int decay = perSecond * (k + 1) / SimConstants.TicksPerSecond - perSecond * k / SimConstants.TicksPerSecond;
            unit.Suppression = Math.Max(0, unit.Suppression - decay);
        }
        if (tick % CombatRules.MoraleIntervalTicks == 0)
        {
            // Recovery only lifts morale back toward its base level; it never lowers morale that is above it.
            if (unit.Suppression < CombatRules.CalmSuppression && unit.Morale < BaseMorale(unit))
                unit.Morale = Math.Min(BaseMorale(unit), unit.Morale + CombatRules.MoraleRecoveryPerInterval);
            if (unit.MoraleState == MoraleState.Broken)
                TryRally(sim, unit, tick, events);
        }
        UpdatePinned(unit, tick, events);
    }

    public static void Check(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction || unit.MoraleState == MoraleState.Broken)
            return;
        int effective = unit.Morale + LeaderBonus(sim, unit, CombatRules.LeaderMoraleBonus)
                        - unit.Suppression / 4 - CombatRules.WoundMoralePenalty(unit.Wound);
        if (sim.Rng.NextInt(1000) >= effective)
            Break(unit, tick, events);
    }

    public static void OnCasualty(Simulation sim, Unit casualty, long tick, List<SimEvent> events)
    {
        long witnessSq = (long)CombatRules.CasualtyWitnessRadiusCm * CombatRules.CasualtyWitnessRadiusCm;
        foreach (var other in sim.Units)
        {
            if (other == casualty || other.Side != casualty.Side || other.IsOutOfAction)
                continue;
            if ((other.Position - casualty.Position).LengthSquared > witnessSq)
                continue;
            other.Morale = Math.Max(0, other.Morale - CombatRules.CasualtyMoraleLoss);
            Check(sim, other, tick, events);
        }

        if (!casualty.IsLeader)
            return;
        casualty.IsLeader = false;
        var successor = sim.Units.FirstOrDefault(u => u.Side == casualty.Side && !u.IsOutOfAction && u.MoraleState != MoraleState.Broken)
                        ?? sim.Units.FirstOrDefault(u => u.Side == casualty.Side && !u.IsOutOfAction);
        if (successor is not null)
        {
            successor.IsLeader = true;
            successor.LeaderQualityPct = CombatRules.ActingLeaderQualityPct;
            events.Add(new LeaderChanged(tick, casualty.Side, successor.Id));
        }
        foreach (var other in sim.Units)
        {
            if (other.Side != casualty.Side || other.IsOutOfAction)
                continue;
            other.Morale = Math.Max(0, other.Morale - CombatRules.LeaderLossMoraleLoss);
            Check(sim, other, tick, events);
        }
    }

    /// <summary>The unit's leader if he is in command radius and not broken (a leader is not his own leader).</summary>
    public static Unit? LeaderInRange(Simulation sim, Unit unit, bool requireSteady = false)
    {
        long radiusSq = (long)CombatRules.CommandRadiusCm * CombatRules.CommandRadiusCm;
        foreach (var other in sim.Units)
        {
            if (other != unit && other.IsLeader && other.Side == unit.Side && !other.IsOutOfAction
                && other.MoraleState != MoraleState.Broken && (!requireSteady || other.MoraleState == MoraleState.Steady)
                && (other.Position - unit.Position).LengthSquared <= radiusSq)
                return other;
        }
        return null;
    }

    private static int LeaderBonus(Simulation sim, Unit unit, int bonus) =>
        LeaderInRange(sim, unit) is { } leader ? bonus * leader.LeaderQualityPct / 100 : 0;

    private static int BaseMorale(Unit unit) => unit.IsLeader ? CombatRules.LeaderMorale : CombatRules.BaseMorale;

    private static void TryRally(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        var leader = LeaderInRange(sim, unit, requireSteady: true);
        int chance = unit.Morale - unit.Suppression
                     + (leader is not null ? CombatRules.LeaderRallyBonus * leader.LeaderQualityPct / 100 : -CombatRules.NoLeaderRallyPenalty);
        if (sim.Rng.NextInt(1000) >= chance)
            return;
        unit.Morale = Math.Min(CombatRules.MaxMorale, unit.Morale + CombatRules.RallyMoraleGain);
        unit.MoraleState = unit.Suppression >= CombatRules.UnpinBelow ? MoraleState.Pinned : MoraleState.Steady;
        unit.Retreated = false;
        Movement.ClearPath(unit);
        if (unit.MoraleState == MoraleState.Pinned)
            Movement.BeginStanceChange(unit, Stance.Prone);
        events.Add(new MoraleChanged(tick, unit.Id, unit.MoraleState));
    }

    private static void Break(Unit unit, long tick, List<SimEvent> events)
    {
        unit.MoraleState = MoraleState.Broken;
        unit.Retreated = false;
        unit.AssaultTarget = null; // a broken man gives up the charge for good
        LootSystem.Abandon(unit);
        Firing.Cancel(unit);
        Movement.ClearPath(unit);
        events.Add(new MoraleChanged(tick, unit.Id, MoraleState.Broken));
    }

    private static void UpdatePinned(Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.MoraleState == MoraleState.Broken)
            return;
        bool pinned = unit.MoraleState == MoraleState.Pinned
            ? unit.Suppression >= CombatRules.UnpinBelow
            : unit.Suppression >= CombatRules.PinnedAt;
        var next = pinned ? MoraleState.Pinned : MoraleState.Steady;
        if (next == unit.MoraleState)
            return;
        unit.MoraleState = next;
        events.Add(new MoraleChanged(tick, unit.Id, next));
        if (next == MoraleState.Pinned)
        {
            Movement.ClearPath(unit);
            Movement.BeginStanceChange(unit, Stance.Prone);
        }
    }
}
