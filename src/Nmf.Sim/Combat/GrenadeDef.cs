namespace Nmf.Sim.Combat;

/// <summary>Static hand grenade data (content/core/grenades/*.yaml). Times are ticks, distances centimetres.</summary>
public sealed record GrenadeDef(
    string Id,
    string Name,
    int FuseTicks,
    int ThrowRangeCm,
    int ScatterPct,
    int BlastRadiusCm,
    int LethalRadiusCm,
    int Suppression,
    int LethalityPct)
{
    /// <summary>Returns this grenade, or throws <see cref="ArgumentException"/> naming the first invalid field.</summary>
    public GrenadeDef Validated()
    {
        Require(!string.IsNullOrWhiteSpace(Id), "id must not be empty");
        Require(FuseTicks > CombatRules.GrenadeFlightTicks, $"fuse_ticks must be more than the {CombatRules.GrenadeFlightTicks}-tick flight");
        Require(ThrowRangeCm >= 500, "throw_range_m must be at least 5");
        Require(ScatterPct is >= 0 and <= 100, "scatter_pct must be 0..100");
        Require(LethalRadiusCm >= 100, "lethal_radius_m must be at least 1");
        Require(BlastRadiusCm >= LethalRadiusCm, "lethal_radius_m must not exceed blast_radius_m");
        Require(Suppression >= 0, "suppression must not be negative");
        Require(LethalityPct is >= 0 and <= 100, "lethality_pct must be 0..100");
        return this;
    }

    private void Require(bool ok, string problem)
    {
        if (!ok)
            throw new ArgumentException($"grenade '{Id}': {problem}");
    }
}
