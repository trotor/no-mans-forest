using Nmf.Sim.Units;

namespace Nmf.Content;

/// <summary>How a specialty is written in mission and campaign files (spec 2026-10-11-campaign-design §9b).</summary>
public static class SpecialtyNames
{
    /// <summary>marksman, scout, sapper or medic; none written is <see cref="Specialty.None"/>; anything else throws <see cref="ArgumentException"/>.</summary>
    public static Specialty Parse(string? value, string field) => value switch
    {
        null or "" or "none" => Specialty.None,
        "marksman" => Specialty.Marksman,
        "scout" => Specialty.Scout,
        "sapper" => Specialty.Sapper,
        "medic" => Specialty.Medic,
        _ => throw new ArgumentException($"{field} must be marksman, scout, sapper or medic, was '{value}'"),
    };
}
