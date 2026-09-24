namespace Nmf.Sim.Combat;

public enum WeaponClass : byte
{
    Rifle,
    Smg,
    Lmg,
}

/// <summary>Static weapon data (content/core/weapons/*.yaml). Times are ticks, distances centimetres.</summary>
public sealed record WeaponDef(
    string Id,
    string Name,
    WeaponClass Class,
    int MagazineSize,
    int AimTicks,
    int RoundsPerBurst,
    int RoundIntervalTicks,
    int RecoverTicks,
    int ReloadTicks,
    int SpreadMrad,
    int RangeCm,
    int LethalityPct,
    int SuppressionPerRound,
    int NoiseRadiusCm)
{
    /// <summary>Returns this weapon, or throws <see cref="ArgumentException"/> naming the first invalid field.</summary>
    public WeaponDef Validated()
    {
        Require(!string.IsNullOrWhiteSpace(Id), "id must not be empty");
        Require(MagazineSize >= 1, "magazine must be at least 1");
        Require(RoundsPerBurst >= 1, "burst must be at least 1");
        Require(AimTicks >= 0 && RoundIntervalTicks >= 0 && RecoverTicks >= 0 && ReloadTicks >= 0, "tick values must not be negative");
        Require(SpreadMrad >= 0, "spread_mrad must not be negative");
        Require(RangeCm >= 100, "range_m must be at least 1");
        Require(LethalityPct is >= 0 and <= 100, "lethality_pct must be 0..100");
        Require(SuppressionPerRound >= 0, "suppression must not be negative");
        Require(NoiseRadiusCm >= 0, "noise_m must not be negative");
        return this;
    }

    private void Require(bool ok, string problem)
    {
        if (!ok)
            throw new ArgumentException($"weapon '{Id}': {problem}");
    }
}
