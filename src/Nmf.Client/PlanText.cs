using Nmf.Sim.Core;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Client;

/// <summary>What the preview panel says (spec 2026-10-10-plan-preview-design §2): the title, the buttons, the reason, the warnings.</summary>
public static class PlanText
{
    public static string Title(AttackPreview preview, GameSession session, string language)
    {
        bool fi = language == "fi";
        int known = TacticPlanner.AimOf(session.Sim, session.PlayerSide, preview.Target) is { } aim
            ? TacticPlanner.PositionOf(session.Sim, session.PlayerSide, aim).Count
            : 0;
        return fi
            ? $"Hyökkäys: asema, {known} {(known == 1 ? "tunnettu vihollinen" : "tunnettua vihollista")}"
            : $"Attack: a position, {known} known {(known == 1 ? "enemy" : "enemies")}";
    }

    /// <summary>"1 Tuli ja liike (ehdotus)": the key, the name, and whether it is the suggestion.</summary>
    public static string Button(PreviewOption option, int index, string language) =>
        $"{index + 1} {option.Name(language)}" + (option.Suggested ? (language == "fi" ? " (ehdotus)" : " (suggested)") : "");

    public static string? WhyNot(PreviewOption option, string language)
    {
        bool fi = language == "fi";
        return option.WhyNot switch
        {
            PlanWhyNot.TooFewMen => fi ? "liian vähän miehiä (vähintään 4)" : "too few men (4 at least)",
            PlanWhyNot.NoPlace => fi ? "ei sopivaa paikkaa" : "no place for it",
            _ => null,
        };
    }

    public static string Reason(PreviewOption option, string language)
    {
        bool fi = language == "fi";
        if (option.Tactic is null)
            return fi ? "Kaikki rynnäköivät suoraan kohteeseen ilman tukea." : "Everyone charges straight at the target, with no support.";
        if (option.Plan is not { } plan)
            return WhyNot(option, language) ?? "";
        int fireM = plan.Reason.FireBaseM;
        if (plan.FireTeam.Count == 0)
        {
            int closeM = (int)(IntMath.Isqrt((AttackPreview.Centre(plan.FormUp.Select(p => p.At)) - plan.Aim).LengthSquared) / 100);
            return fi
                ? $"Koko ryhmä ({plan.StrikeTeam.Count}) hiipii piilossa kokoontumispaikkaan {closeM} m päähän ja rynnäköi kranaatein."
                : $"The whole group ({plan.StrikeTeam.Count}) creeps unseen to a form-up {closeM} m off and goes in with grenades.";
        }
        if (plan.StrikeTeam.Count == 0)
            return fi
                ? $"Kaikki tuliasemaan {fireM} m päähän. Kukaan ei mene sisään."
                : $"Everyone to a fire base {fireM} m off. Nobody goes in.";
        int formUpM = (int)(IntMath.Isqrt((AttackPreview.Centre(plan.FormUp.Select(p => p.At)) - plan.Aim).LengthSquared) / 100);
        string side = plan.Reason.Side switch
        {
            < 0 => fi ? " vasemmalta" : " from the left",
            > 0 => fi ? " oikealta" : " from the right",
            _ => "",
        };
        return fi
            ? $"Tuliasema {fireM} m päässä ({plan.FireTeam.Count} miestä). Iskuryhmä ({plan.StrikeTeam.Count}) kiertää{side} kokoontumispaikkaan {formUpM} m päähän."
            : $"Fire base {fireM} m off ({plan.FireTeam.Count} men). The strike team ({plan.StrikeTeam.Count}) goes round{side} to a form-up {formUpM} m off.";
    }

    public static IReadOnlyList<string> Warnings(AttackPreview preview, GameSession session, string language)
    {
        bool fi = language == "fi";
        var lines = new List<string>();
        if (preview.Plan is { } plan)
        {
            if (plan.FireBaseBlind)
                lines.Add(fi ? "Tuliasema ei näe kohdetta: tuli on arvailua." : "The fire base cannot see the target: the fire is guesswork.");
            var exposed = plan.RouteExposed ?? [];
            int seen = exposed.Take(Math.Max(0, exposed.Count - 1)).Count(e => e);
            if (seen > 0)
                lines.Add(fi ? $"Reitti näkyy viholliselle ({seen} {(seen == 1 ? "kohta" : "kohtaa")})." : $"The enemy sees the way ({seen} {(seen == 1 ? "point" : "points")}).");
            if (plan.StrikeTeam.Count > 0 && exposed.Count > 0 && exposed[^1])
                lines.Add(fi ? "Kokoontumispaikka näkyy viholliselle." : "The enemy sees the form-up.");
        }
        if (preview.Option.Tactic is null || preview.Option.Tactic.Maneuver is not null || preview.Option.Tactic.Infiltrate is not null)
        {
            int fit = preview.Men.Count(id => session.Sim.FindUnit(id) is { IsOutOfAction: false });
            int known = session.Sim.Knowledge(session.PlayerSide).Contacts
                .Count(c => c.Level != ContactLevel.Unknown && session.Sim.FindUnit(c.Target) is { IsOutOfAction: false });
            if (known >= fit)
                lines.Add(fi ? "Vihollisia tiedetään vähintään yhtä monta kuin meitä." : "We know of as many enemies as we are, or more.");
        }
        if (preview.Option.Tactic?.Infiltrate is { } infiltrate)
        {
            // What makes the planner pass creeping in by, said aloud when the player picks it all the same.
            var men = preview.Men.Select(session.Sim.FindUnit).Where(u => u is { IsOutOfAction: false }).Cast<Unit>().ToList();
            if (men.Any(u => session.Sim.Tick - u.LastSuppressedTick < TacticPlanner.UnderFireTicks))
                lines.Add(fi ? "Miehiä on tulen alla: hiipiminen paljastuu heti." : "Men are under fire: creeping in will be found out at once.");
            int experience = men.Count == 0 ? 0 : (int)(men.Sum(u => (long)u.Experience) / men.Count);
            if (experience < infiltrate.MinExperience)
                lines.Add(fi ? $"Miehet ovat kokemattomia hiipimään (kokemus {experience}, tarvitaan {infiltrate.MinExperience})."
                    : $"The men are too green to creep in (experience {experience}, {infiltrate.MinExperience} needed).");
        }
        if (preview.CannotGoThere)
            lines.Add(fi ? "Sinne ei pääse." : "They cannot get there.");
        return lines;
    }
}
