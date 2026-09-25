using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Combat;

/// <summary>Hand-to-hand fighting and surrender (spec 2026-09-24-grenades-melee-design §4).</summary>
internal static class MeleeSystem
{
    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        // Running fights: the lower id of each pair counts down and resolves.
        foreach (var unit in sim.Units)
        {
            if (unit.Action != CombatAction.Melee)
                continue;
            var opponent = unit.MeleeOpponent is { } id ? sim.FindUnit(id) : null;
            if (unit.IsOutOfAction || opponent is null || opponent.IsOutOfAction || opponent.MeleeOpponent != unit.Id)
            {
                End(unit);
                continue;
            }
            if (unit.Id.Value > opponent.Id.Value)
                continue;
            unit.ActionTicksLeft--;
            opponent.ActionTicksLeft = unit.ActionTicksLeft;
            if (unit.ActionTicksLeft <= 0)
                Resolve(sim, unit, opponent, tick, events);
        }

        // New fights: enemies within reach who are not already fighting; a broken man gives himself up instead.
        var units = sim.Units;
        long rangeSq = (long)CombatRules.MeleeRangeCm * CombatRules.MeleeRangeCm;
        for (int i = 0; i < units.Count; i++)
        {
            var a = units[i];
            if (!Free(a))
                continue;
            for (int j = i + 1; j < units.Count; j++)
            {
                var b = units[j];
                if (b.Side == a.Side || !Free(b) || (b.Position - a.Position).LengthSquared > rangeSq)
                    continue;
                if (a.MoraleState == MoraleState.Broken)
                {
                    Capture(sim, a, tick, events);
                    break;
                }
                if (b.MoraleState == MoraleState.Broken)
                {
                    Capture(sim, b, tick, events);
                    continue;
                }
                Start(sim, a, b, tick, events);
                break;
            }
        }
    }

    private static bool Free(Unit unit) => !unit.IsOutOfAction && unit.Action != CombatAction.Melee;

    private static void Start(Simulation sim, Unit a, Unit b, long tick, List<SimEvent> events)
    {
        foreach (var (unit, other) in new[] { (a, b), (b, a) })
        {
            // Surprise: the enemy's side did not have this man in sight when he closed in.
            unit.MeleeSurprise = sim.Knowledge(other.Side).LevelOf(unit.Id) != ContactLevel.Visible;
            Firing.Cancel(unit);
            Movement.ClearPath(unit);
            unit.ThrowTarget = null;
            LootSystem.Abandon(unit);
            unit.Action = CombatAction.Melee;
            unit.ActionTicksLeft = CombatRules.MeleeTicks;
            unit.MeleeOpponent = other.Id;
        }
        events.Add(new MeleeStarted(tick, a.Id, b.Id));
    }

    private static void Resolve(Simulation sim, Unit a, Unit b, long tick, List<SimEvent> events)
    {
        int ra = sim.Rng.NextInt(100) + Score(a);
        int rb = sim.Rng.NextInt(100) + Score(b);
        var (winner, loser) = ra >= rb ? (a, b) : (b, a);
        End(a);
        End(b);

        int roll = sim.Rng.NextInt(100);
        var level = roll < 50 ? WoundLevel.Dead : roll < 80 ? WoundLevel.Incapacitated : WoundLevel.Serious;
        if (loser.Wound != WoundLevel.None && level <= loser.Wound)
            level = (WoundLevel)Math.Min((int)WoundLevel.Dead, (int)loser.Wound + 1);
        Damage.SetWound(sim, loser, level, tick, events);
        winner.Morale = Math.Min(CombatRules.MaxMorale, winner.Morale + CombatRules.MeleeWinMorale);
        events.Add(new MeleeEnded(tick, winner.Id, loser.Id));
    }

    private static int Score(Unit unit) =>
        100 + CombatRules.MeleeSkill(unit.Weapon?.Class) + unit.Morale / 10
        + (unit.MeleeSurprise ? CombatRules.MeleeSurpriseBonus : 0)
        - CombatRules.MeleeWoundPenalty(unit.Wound) - unit.Suppression / 20;

    private static void End(Unit unit)
    {
        if (unit.Action == CombatAction.Melee)
            unit.Action = CombatAction.None;
        unit.ActionTicksLeft = 0;
        unit.MeleeOpponent = null;
    }

    private static void Capture(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        unit.IsCaptured = true;
        Movement.ClearPath(unit);
        unit.TargetStance = null;
        unit.StanceTicksLeft = 0;
        unit.Stance = Stance.Crouching;
        Firing.Cancel(unit);
        unit.Action = CombatAction.None;
        unit.ActionTicksLeft = 0;
        unit.Suppression = 0;
        unit.AssaultTarget = null;
        events.Add(new UnitCaptured(tick, unit.Id));
        MoraleSystem.OnCasualty(sim, unit, tick, events);
    }
}
