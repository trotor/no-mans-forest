namespace Nmf.Sim.Tactics;

/// <summary>The strike team's way round: how far off the fire line its form-up lies, and how close to the position.</summary>
public sealed record TacticManeuver(int FlankDeg, int FormUpMinCm, int FormUpMaxCm);

/// <summary>When the strike team goes in: the suppression it waits for, how long, and when it gives up.</summary>
public sealed record TacticAssault(int Suppression, int WaitTicks, int GiveUpTicks);

/// <summary>
/// The whole group creeps unseen to a form-up this close and goes in with grenades, with no fire base (spec
/// 2026-10-10-infiltration-enemy-design §1); suggested only for men this experienced on average.
/// </summary>
public sealed record TacticInfiltration(int FormUpMinCm, int FormUpMaxCm, int MinExperience);

/// <summary>
/// One way of attacking a position (content/core/tactics, spec 2026-10-10-tactics-design). Without a maneuver the whole
/// group is the fire base (long fire); without an assault nobody goes in.
/// </summary>
public sealed record TacticDef(string Id, string NameEn, string NameFi, int FireBaseMinCm, int FireBaseMaxCm,
    TacticManeuver? Maneuver, TacticAssault? Assault, int AbortCasualtiesPct, int AbortPinnedTicks, TacticInfiltration? Infiltrate = null)
{
    private const int MaxRangeCm = 30_000;
    private const int MaxInfiltrateCm = 6_000;

    public string Name(string language) => language == "fi" ? NameFi : NameEn;

    public TacticDef Validated()
    {
        // First: infiltration's band also stands in for the fire base, so its own message is the one that helps.
        if (Infiltrate is { } i)
        {
            Require(Maneuver is null && Assault is null, "infiltrate has no maneuver nor assault: the whole group goes in");
            Require(i.FormUpMinCm > 0 && i.FormUpMinCm <= i.FormUpMaxCm && i.FormUpMaxCm <= MaxInfiltrateCm,
                "infiltrate.form_up must have 0 < min_m <= max_m <= 60");
            Require(i.MinExperience is >= 0 and <= 100, "infiltrate.min_experience must be 0..100");
        }
        Require(FireBaseMinCm > 0, "fire_base.min_m must be positive");
        Require(FireBaseMaxCm >= FireBaseMinCm && FireBaseMaxCm <= MaxRangeCm, "fire_base.max_m must be at least min_m and at most 300");
        if (Maneuver is { } m)
        {
            Require(m.FlankDeg is >= 0 and <= 150, "maneuver.flank_deg must be 0..150");
            Require(m.FormUpMinCm > 0 && m.FormUpMinCm <= m.FormUpMaxCm && m.FormUpMaxCm <= FireBaseMaxCm,
                "maneuver.form_up must have 0 < min_m <= max_m <= fire_base.max_m");
        }
        Require(Assault is null || Maneuver is not null, "an assault needs a maneuver");
        if (Assault is { } a)
            Require(a.Suppression > 0 && a.WaitTicks >= 0 && a.GiveUpTicks >= a.WaitTicks, "assault: suppression > 0 and give_up_s >= wait_s");
        Require(AbortCasualtiesPct is >= 1 and <= 100, "abort.casualties_pct must be 1..100");
        Require(AbortPinnedTicks > 0, "abort.pinned_s must be positive");
        return this;
    }

    private static void Require(bool ok, string message)
    {
        if (!ok)
            throw new ArgumentException(message);
    }
}
