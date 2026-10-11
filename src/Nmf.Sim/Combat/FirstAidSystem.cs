using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

/// <summary>
/// The medic binds a bad wound so it no longer bleeds (spec 2026-10-11-campaign-design §9b, "Ensiapu"): ordered, or
/// on his own when he has nothing else to do. The binding goes on only while the two are together and still.
/// </summary>
public static class FirstAidSystem
{
    /// <summary>A bad wound, not yet bound: the man would bleed until he goes down.</summary>
    public static bool NeedsAid(Unit patient) =>
        patient.Wound == WoundLevel.Serious && !patient.Bandaged && !patient.IsCaptured && patient.CarriedBy is null;

    /// <summary>He can bind this man: a medic, of the same side, and the man needs it.</summary>
    public static bool CanTreat(Unit medic, Unit patient) =>
        medic.Specialty == Specialty.Medic && !medic.IsOutOfAction && patient.Side == medic.Side && NeedsAid(patient);

    /// <summary>Gives up binding: the wound stays as it is.</summary>
    public static void Abandon(Unit unit)
    {
        if (unit.Action == CombatAction.FirstAid)
        {
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
        }
        unit.FirstAidTarget = null;
    }

    /// <summary>Counts the binding down, after looting in the step; drops what can no longer be done.</summary>
    internal static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var medic in sim.Units)
        {
            if (medic.FirstAidTarget is not { } id)
                continue;
            var patient = sim.FindUnit(id);
            if (patient is null || !CanTreat(medic, patient) || medic.MoraleState != MoraleState.Steady)
            {
                Abandon(medic);
                continue;
            }
            if (medic.Action != CombatAction.FirstAid)
                continue;
            if (!InReach(medic, patient) || medic.IsMoving || patient.IsMoving)
            {
                // Apart, or one of them on the move: the binding starts over once they are together again.
                medic.Action = CombatAction.None;
                medic.ActionTicksLeft = 0;
                continue;
            }
            if (--medic.ActionTicksLeft > 0)
                continue;
            medic.Action = CombatAction.None;
            medic.FirstAidTarget = null;
            patient.Bandaged = true;
            events.Add(new UnitBandaged(tick, medic.Id, patient.Id));
        }
    }

    /// <summary>
    /// Called by the brain for a medic with a patient: walks to him (after him, if he moves), and starts binding once
    /// both are still beside each other. True while the binding keeps him busy.
    /// </summary>
    internal static bool Approach(Simulation sim, Unit medic)
    {
        if (medic.Action == CombatAction.FirstAid)
            return true;
        var patient = sim.FindUnit(medic.FirstAidTarget!.Value);
        if (patient is null || !CanTreat(medic, patient))
        {
            Abandon(medic);
            return false;
        }
        if (!InReach(medic, patient))
        {
            bool headingThere = medic.MoveTarget is { } goal
                && (goal - patient.Position).LengthSquared <= (long)CombatRules.FirstAidRangeCm * CombatRules.FirstAidRangeCm;
            if (!headingThere && medic.TargetStance is null && medic.MoraleState == MoraleState.Steady)
            {
                var path = Pathfinder.FindPath(sim.Map, medic.Position, patient.Position);
                if (path is null)
                {
                    Abandon(medic);
                    return false;
                }
                Movement.StartPath(medic, patient.Position, AI.SoldierBrain.ChoosePace(sim, medic), path);
            }
            return false;
        }
        if (medic.MoveTarget is not null)
            Movement.ClearPath(medic); // beside him: no need to go the last steps
        if (medic.TargetStance is not null || patient.IsMoving)
            return true;
        Firing.Cancel(medic);
        if (medic.Action != CombatAction.None)
            return true; // finish the reload first
        medic.Action = CombatAction.FirstAid;
        medic.ActionTicksLeft = CombatRules.FirstAidTicks;
        return true;
    }

    /// <summary>
    /// A medic with nothing else to do and no fire round him for a while sees to the nearest of his own badly wounded
    /// within 20 m (himself too); a tie goes to the lower id. Nobody another medic already sees to.
    /// </summary>
    internal static Unit? ChoosePatient(Simulation sim, Unit medic, long tick)
    {
        if (medic.Specialty != Specialty.Medic || medic.FirstAidTarget is not null || medic.LootTarget is not null
            || medic.CarryTarget is not null || medic.HandsBusy || medic.AssaultTarget is not null || medic.PlannedAttackId is not null
            || medic.MoveTarget is not null || medic.Action != CombatAction.None || medic.MoraleState != MoraleState.Steady
            || medic.Stranded || tick - medic.LastSuppressedTick < CombatRules.FirstAidQuietTicks)
            return null;
        Unit? best = null;
        long bestSq = long.MaxValue;
        foreach (var man in sim.Units)
        {
            if (!CanTreat(medic, man) || sim.Units.Any(other => other != medic && other.FirstAidTarget == man.Id))
                continue;
            long sq = (man.Position - medic.Position).LengthSquared;
            if (sq > (long)CombatRules.AutoFirstAidRangeCm * CombatRules.AutoFirstAidRangeCm)
                continue;
            if (sq < bestSq || (sq == bestSq && man.Id.Value < best!.Id.Value))
            {
                best = man;
                bestSq = sq;
            }
        }
        return best;
    }

    private static bool InReach(Unit medic, Unit patient) =>
        (patient.Position - medic.Position).LengthSquared <= (long)CombatRules.FirstAidRangeCm * CombatRules.FirstAidRangeCm;
}
