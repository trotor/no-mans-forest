using Nmf.Sim.Core;
using Nmf.Sim.Events;

namespace Nmf.Client.Mission;

/// <summary>Messages for things our men hear rather than see.</summary>
public static class Alerts
{
    /// <summary>A leader's "Urraa!" carries this far.</summary>
    public const int ShoutHeardCm = 30_000;

    /// <summary>The enemy's call to counterattack, if one of our men (still in action) is near enough to hear it.</summary>
    public static string? Counterattack(CounterattackStarted shout, IEnumerable<Vec2> ownMen, string language)
    {
        long heardSq = (long)ShoutHeardCm * ShoutHeardCm;
        if (!ownMen.Any(p => (p - shout.At).LengthSquared <= heardSq))
            return null;
        return language == "fi" ? "Kuuluu huuto: \"Urraa!\" — vihollinen hyökkää!" : "A shout: \"Urraa!\" — the enemy is attacking!";
    }

    /// <summary>What the men make of the attack ordered (spec 2026-10-10-tactics-design, "Client").</summary>
    public static string PlanSummary(Nmf.Sim.Tactics.TacticPlan plan, Nmf.Sim.Tactics.TacticDef tactic, string language)
    {
        bool fi = language == "fi";
        string text = $"{tactic.Name(language)}: {(fi ? "tuliasema" : "fire base")} {plan.Reason.FireBaseM} m";
        if (plan.Reason.Side != 0)
            text += fi ? $", koukkaus {(plan.Reason.Side < 0 ? "vasemmalta" : "oikealta")}" : $", flanking {(plan.Reason.Side < 0 ? "left" : "right")}";
        if (plan.Reason.Blind)
            text += fi ? " (ei näkyvyyttä asemaan)" : " (no sight of the position)";
        return text;
    }

    /// <summary>No plan could be made for the attack: the men fire instead.</summary>
    public static string NoPlan(string language) =>
        language == "fi" ? "Hyökkäystä ei saa suunniteltua: ammutaan" : "No way to plan the attack: firing instead";

    public static string Halted(PlannedAttackHalted halted, string language)
    {
        bool fi = language == "fi";
        string why = halted.Reason switch
        {
            Nmf.Sim.Tactics.HaltReason.Casualties => fi ? $"tappioita {halted.Casualties}" : $"{halted.Casualties} casualties",
            Nmf.Sim.Tactics.HaltReason.LeaderDown => fi ? "johtaja kaatui" : "the leader is down",
            Nmf.Sim.Tactics.HaltReason.StrikePinned => fi ? "iskuryhmä on maassa" : "the strike team is pinned",
            Nmf.Sim.Tactics.HaltReason.NotSuppressed => fi ? "asema ei lamaudu" : "the position will not go down",
            Nmf.Sim.Tactics.HaltReason.NoFire => fi ? "tuliasemasta ei näe asemaa" : "the fire base cannot see the position",
            _ => fi ? "kohde siirtyi" : "the enemy has moved",
        };
        return (fi ? "Hyökkäys pysähtyi: " : "Attack halted: ") + why;
    }

    /// <summary>Only the moment the strike team goes in is announced.</summary>
    public static string? Phase(PlannedAttackPhaseChanged changed, string language) =>
        changed.Phase == Nmf.Sim.Tactics.PlannedPhase.Assault ? (language == "fi" ? "Isku lähtee!" : "Going in!") : null;

    public static string Ended(PlannedAttackEnded ended, string language) => ended.Success
        ? (language == "fi" ? "Asema vallattu" : "Position taken")
        : (language == "fi" ? "Hyökkäys päättyi: vihollinen katosi" : "Attack over: the enemy is gone");
}
