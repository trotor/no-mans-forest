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
        if (plan.FireTeam.Count == 0 && plan.FormUp.Count > 0)
        {
            // Creeping in: how close they will gather, and from which side.
            long x = 0, y = 0;
            foreach (var p in plan.FormUp)
            {
                x += p.At.X;
                y += p.At.Y;
            }
            var formUp = new Nmf.Sim.Core.Vec2((int)(x / plan.FormUp.Count), (int)(y / plan.FormUp.Count));
            int metres = (int)(Nmf.Sim.Core.IntMath.Isqrt((formUp - plan.Aim).LengthSquared) / 100);
            string creep = $"{tactic.Name(language)}: {(fi ? "kokoontumispaikka" : "form-up")} {metres} m";
            if (plan.Reason.Side != 0)
                creep += fi ? $", {(plan.Reason.Side < 0 ? "vasemmalta" : "oikealta")}" : $", from the {(plan.Reason.Side < 0 ? "left" : "right")}";
            return creep;
        }
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
            Nmf.Sim.Tactics.HaltReason.Discovered => fi ? "ryhmä paljastui kaukana asemasta" : "found out far from the position",
            _ => fi ? "kohde siirtyi" : "the enemy has moved",
        };
        return (fi ? "Hyökkäys pysähtyi: " : "Attack halted: ") + why;
    }

    /// <summary>Why a queue stopped and how many orders wait (spec 2026-10-10-controls-design §4).</summary>
    public static string QueueStopped(Nmf.Client.OrderGroup group, string language)
    {
        bool fi = language == "fi";
        string why = group.Stop switch
        {
            Nmf.Client.QueueStop.AttackHalted when group.Halt is { } halted => Halted(halted, language),
            Nmf.Client.QueueStop.Rejected => fi ? "Käskyä ei voitu toteuttaa" : "An order could not be carried out",
            _ => fi ? "Jonon miehistä ei ole ketään jäljellä" : "None of the queue's men is left",
        };
        int left = Math.Max(0, group.Left);
        return fi ? $"{why}. Jonossa vielä {left} {(left == 1 ? "käsky" : "käskyä")}." : $"{why}. {left} {(left == 1 ? "order" : "orders")} still queued.";
    }

    /// <summary>Only the moment the strike team goes in is announced.</summary>
    public static string? Phase(PlannedAttackPhaseChanged changed, string language) =>
        changed.Phase == Nmf.Sim.Tactics.PlannedPhase.Assault ? (language == "fi" ? "Isku lähtee!" : "Going in!") : null;

    /// <summary>Null when our men were all given other orders (or are down): nothing to tell.</summary>
    public static string? Ended(PlannedAttackEnded ended, string language) => ended.Disbanded ? null : ended.Success
        ? (language == "fi" ? "Asema vallattu" : "Position taken")
        : (language == "fi" ? "Hyökkäys päättyi: vihollinen katosi" : "Attack over: the enemy is gone");
}
