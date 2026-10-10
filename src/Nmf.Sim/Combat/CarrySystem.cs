using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

/// <summary>
/// Carrying a man who cannot move (spec 2026-10-10-carrying-design): walking over and lifting him, keeping him on the
/// carrier's back and the helper beside, and laying him down when the carrier needs his hands, falls, is pinned or breaks
/// (a helper then takes him over). Runs after movement, so the carried man is where his carrier stepped.
/// </summary>
public static class CarrySystem
{
    /// <summary>A man of the side who is down but alive: he can be carried.</summary>
    public static bool CanBeCarried(Unit carrier, Unit wounded) =>
        wounded != carrier && wounded.Side == carrier.Side && wounded.IsOutOfAction && wounded.IsAlive;

    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var unit in sim.Units)
        {
            if (unit.CarryTarget is not null)
                Approach(sim, unit, tick, events);
            else if (unit.Carrying is { } carried)
                Hold(sim, unit, carried, tick, events);
        }
        foreach (var unit in sim.Units)
            if (unit.HelpingCarry is { } helped)
                Help(sim, unit, helped);
    }

    /// <summary>On his way to the man, then lifting him.</summary>
    private static void Approach(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        var wounded = sim.FindUnit(unit.CarryTarget!.Value);
        if (wounded?.CarriedBy is { } other && other != unit.Id && !sim.Units.Any(u => u.HelpingCarry == wounded.Id))
        {
            // Someone else got him up first (even while this man was lifting too): this man helps instead.
            if (unit.Action == CombatAction.Lifting)
                unit.Action = CombatAction.None;
            unit.CarryTarget = null;
            unit.HelpingCarry = wounded.Id;
            return;
        }
        if (unit.IsOutOfAction || unit.MoraleState != MoraleState.Steady || wounded is null || !CanBeCarried(unit, wounded) || wounded.CarriedBy is not null)
        {
            unit.CarryTarget = null;
            if (unit.Action == CombatAction.Lifting)
                unit.Action = CombatAction.None;
            return;
        }
        if (unit.Action == CombatAction.Lifting)
        {
            if (--unit.ActionTicksLeft > 0)
                return;
            unit.Action = CombatAction.None;
            unit.CarryTarget = null;
            unit.Carrying = wounded.Id;
            wounded.CarriedBy = unit.Id;
            Movement.ClearPath(wounded);
            wounded.Position = unit.Position;
            if (unit.Stance == Stance.Prone)
                Movement.BeginStanceChange(unit, Stance.Crouching); // nobody carries a man crawling
            events.Add(new UnitLifted(tick, unit.Id, wounded.Id));
            return;
        }
        bool there = (wounded.Position - unit.Position).LengthSquared <= (long)CombatRules.CarryRangeCm * CombatRules.CarryRangeCm;
        if (!there)
        {
            if (unit.MoveTarget is null && unit.TargetStance is null)
            {
                var path = Pathfinder.FindPath(sim.Map, unit.Position, wounded.Position);
                if (path is null)
                    unit.CarryTarget = null;
                else
                    Movement.StartPath(unit, wounded.Position, MoveMode.Walk, path);
            }
            return;
        }
        if (unit.MoveTarget is not null)
            Movement.ClearPath(unit);
        if (unit.TargetStance is not null)
            return;
        Firing.Cancel(unit);
        if (unit.Action != CombatAction.None)
            return; // a reload, a throw, a fight: done first, then he lifts
        unit.Action = CombatAction.Lifting;
        unit.ActionTicksLeft = CombatRules.LiftTicks;
    }

    /// <summary>The carried man on the carrier's back; laid down if either cannot go on.</summary>
    private static void Hold(Simulation sim, Unit carrier, UnitId carriedId, long tick, List<SimEvent> events)
    {
        var carried = sim.FindUnit(carriedId);
        if (carried is null || !carried.IsAlive)
        {
            Drop(sim, carrier, tick, events);
            return;
        }
        if (carrier.IsOutOfAction || carrier.MoraleState != MoraleState.Steady)
        {
            // The helper beside him takes him over; one still on his way goes on to lift him; else he is laid down here.
            var helper = sim.Units.FirstOrDefault(u => u.HelpingCarry == carriedId && !u.IsOutOfAction && u.MoraleState == MoraleState.Steady);
            Drop(sim, carrier, tick, events);
            if (helper is null)
                return;
            helper.HelpingCarry = null;
            if ((helper.Position - carrier.Position).LengthSquared <= (long)CombatRules.HelperBesideCm * CombatRules.HelperBesideCm)
            {
                helper.Carrying = carriedId;
                carried.CarriedBy = helper.Id;
                carried.Position = helper.Position;
                events.Add(new UnitLifted(tick, helper.Id, carriedId));
            }
            else
            {
                helper.CarryTarget = carriedId;
            }
            return;
        }
        carried.Position = carrier.Position;
        carrier.CarryHelped = sim.Units.Any(u => u.HelpingCarry == carriedId && !u.IsOutOfAction
                                                  && (u.Position - carrier.Position).LengthSquared <= (long)CombatRules.HelperBesideCm * CombatRules.HelperBesideCm);
    }

    /// <summary>A helper goes to the carrier, then walks at his side.</summary>
    private static void Help(Simulation sim, Unit helper, UnitId carriedId)
    {
        var carried = sim.FindUnit(carriedId);
        var carrier = carried?.CarriedBy is { } id ? sim.FindUnit(id) : null;
        if (helper.IsOutOfAction || helper.MoraleState != MoraleState.Steady || carrier is null)
        {
            helper.HelpingCarry = null;
            return;
        }
        long apartSq = (helper.Position - carrier.Position).LengthSquared;
        if (apartSq > (long)CombatRules.HelperBesideCm * CombatRules.HelperBesideCm)
        {
            if (helper.MoveTarget is null || (helper.MoveTarget.Value - carrier.Position).LengthSquared > 300L * 300)
            {
                if (Pathfinder.FindPath(sim.Map, helper.Position, carrier.Position) is { } path)
                    Movement.StartPath(helper, carrier.Position, MoveMode.Walk, path);
                else
                    helper.HelpingCarry = null;
            }
            return;
        }
        // Beside him: a step to his side where the ground allows, else right with him.
        Movement.ClearPath(helper);
        var beside = carrier.Position + new Vec2(100, 0);
        helper.Position = sim.Map.Contains(beside) && sim.Map.CellAt(beside).IsPassable ? beside : carrier.Position;
    }

    /// <summary>Lays down the man he carries where he stands.</summary>
    public static void Drop(Simulation sim, Unit carrier, long tick, List<SimEvent> events)
    {
        if (carrier.Carrying is not { } id)
            return;
        carrier.Carrying = null;
        carrier.CarryHelped = false;
        if (sim.FindUnit(id) is { } carried && carried.CarriedBy == carrier.Id)
        {
            carried.CarriedBy = null;
            events.Add(new UnitDropped(tick, carrier.Id, id));
        }
    }

    /// <summary>Whatever he had to do with carrying is over: a carrier lays his man down, a helper lets go, a lift stops.</summary>
    public static void LetGo(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        Drop(sim, unit, tick, events);
        unit.HelpingCarry = null;
        if (unit.CarryTarget is not null)
        {
            unit.CarryTarget = null;
            if (unit.Action == CombatAction.Lifting)
                unit.Action = CombatAction.None;
        }
    }
}
