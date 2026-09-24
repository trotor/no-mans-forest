using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Wound severity, escalation, going down and bleeding.</summary>
internal static class Damage
{
    public static void ApplyHit(Simulation sim, Unit unit, WeaponDef weapon, long tick, List<SimEvent> events) =>
        ApplyHit(sim, unit, weapon.LethalityPct, tick, events);

    public static void ApplyHit(Simulation sim, Unit unit, int lethalityPct, long tick, List<SimEvent> events)
    {
        int roll = sim.Rng.NextInt(100);
        int l = lethalityPct;
        var level = roll < l / 3 ? WoundLevel.Dead
            : roll < l * 2 / 3 ? WoundLevel.Incapacitated
            : roll < l ? WoundLevel.Serious
            : WoundLevel.Light;
        if (unit.Wound != WoundLevel.None && level <= unit.Wound)
            level = (WoundLevel)Math.Min((int)WoundLevel.Dead, (int)unit.Wound + 1);
        SetWound(sim, unit, level, tick, events);
        MoraleSystem.AddSuppression(sim, unit, CombatRules.HitSuppression, tick, events);
    }

    public static void SetWound(Simulation sim, Unit unit, WoundLevel level, long tick, List<SimEvent> events)
    {
        bool wasInAction = !unit.IsOutOfAction;
        unit.Wound = level;
        unit.WoundTick = tick;
        events.Add(new UnitWounded(tick, unit.Id, level));

        if (unit.IsOutOfAction)
        {
            Movement.ClearPath(unit);
            unit.TargetStance = null;
            unit.StanceTicksLeft = 0;
            unit.Stance = Stance.Prone;
            Firing.Cancel(unit);
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
            unit.Suppression = 0;
            if (wasInAction)
                MoraleSystem.OnCasualty(sim, unit, tick, events);
        }
        else
        {
            unit.Morale = Math.Max(0, unit.Morale - CombatRules.WoundMoraleLoss);
            MoraleSystem.Check(sim, unit, tick, events);
        }
    }

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.Wound == WoundLevel.Serious && tick - unit.WoundTick >= CombatRules.SeriousBleedTicks)
            SetWound(sim, unit, WoundLevel.Incapacitated, tick, events);
    }
}
