using Nmf.Sim.Units;
using Nmf.Sim.Combat;
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

    /// <summary>The enemy's alarm shout carries this far; a telephone call is overheard this near.</summary>
    public const int AlarmHeardCm = 15_000, PhoneHeardCm = 5_000;

    /// <summary>
    /// The alarm on the other side and the telephone wire (spec 2026-10-10-alertness-design §7): a shout or a call only when
    /// one of our men is near enough to hear it, a flare always; null for nothing to tell.
    /// </summary>
    public static string? Alarm(SimEvent e, IEnumerable<Vec2> ownMen, Side player, string language)
    {
        bool fi = language == "fi";
        bool Near(Vec2 at, int cm) => ownMen.Any(p => (p - at).LengthSquared <= (long)cm * cm);
        return e switch
        {
            AlarmRaised { Kind: AlarmKind.Shout } a when a.Side != player && Near(a.At, AlarmHeardCm) => fi ? "Vihollinen huutaa hälytystä" : "The enemy is shouting the alarm",
            AlarmRaised { Kind: AlarmKind.Flare } a when a.Side != player => fi ? "Valoraketti!" : "A flare goes up!",
            LineCut => fi ? "Puhelinjohto katkaistu" : "Telephone wire cut",
            PhoneCalled c when c.Side != player && Near(c.At, PhoneHeardCm) => fi ? "Vihollinen soitti puhelimella" : "The enemy got through on the telephone",
            PhoneCallFailed c when c.Side != player && Near(c.At, PhoneHeardCm) => fi ? "Puhelu ei mennyt läpi" : "Their call did not get through",
            _ => null,
        };
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

    /// <summary>What the player's mortar does, for a toast (spec 2026-10-10-mortar-design §4); null for the other side or nothing to say.</summary>
    public static string? Mortar(SimEvent e, string language, Side player = Side.Blue)
    {
        bool fi = language == "fi";
        return e switch
        {
            FireMissionCalled { Side: var s } when s == player => fi ? "Tuli tilattu: ensimmäiset iskut 1–2 minuutin päästä" : "Fire called: first rounds in a minute or two",
            FireMissionFailed { Side: var s } when s == player => fi ? "Yhteys ei toimi: tilaus ei mennyt perille" : "No contact: the call did not get through",
            FireMissionPhaseChanged { Side: var s, Phase: MortarPhase.Ranging } when s == player => fi ? "Tähystyslaukaus tulossa" : "Ranging round on its way",
            FireMissionPhaseChanged { Side: var s, Phase: MortarPhase.Effect } when s == player => fi ? "Tehokas tuli" : "Fire for effect",
            FireMissionEnded { Side: var s, RadioLost: true } when s == player => fi ? "Radiomies kaatui: kranaatinheitintuki menetetty" : "Radioman down: mortar support lost",
            FireMissionEnded { Side: var s, OutOfRounds: true } when s == player => fi ? "Kranaatit loppuivat" : "Out of mortar rounds",
            FireMissionEnded { Side: var s } when s == player => fi ? "Tuli päättyi" : "Fire over",
            _ => null,
        };
    }

    /// <summary>A stranded man reached by his side (spec 2026-10-10-events-design §6).</summary>
    public static string Found(Nmf.Sim.Events.StrandedFound found, GameSession session) =>
        (session.Language == "fi" ? "Löytyi: " : "Found: ") + (session.Sim.FindUnit(found.Unit)?.Name ?? "");

    /// <summary>A hidden objective a mission event has revealed.</summary>
    public static string? Revealed(Nmf.Sim.Events.ObjectiveRevealed revealed, GameSession session) =>
        session.Mission?.Objectives.FirstOrDefault(o => o.Id == revealed.Id) is { } objective
            ? (session.Language == "fi" ? "Uusi tavoite: " : "New objective: ") + objective.Text.In(session.Language)
            : null;

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
