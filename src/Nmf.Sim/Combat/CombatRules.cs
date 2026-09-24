using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Every tunable number of the combat model (spec 2026-09-24-combat-design §3–§6).</summary>
public static class CombatRules
{
    public const int MaxSuppression = 1000;
    public const int PinnedAt = 400;
    public const int UnpinBelow = 250;
    public const int GoProneAt = 250;
    public const int MoraleCheckSuppression = 800;
    public const int CalmSuppression = 100;
    public const int SuppressionDecayPerTick = 3;
    public const int ProneDecayBonus = 2;
    public const int LeaderDecayBonus = 2;
    public const int NearMissRadiusCm = 250;
    public const int HitSuppression = 250;

    public const int MaxMorale = 1000;
    public const int BaseMorale = 700;
    public const int LeaderMorale = 800;
    public const int MoraleIntervalTicks = 20;
    public const int MoraleRecoveryPerInterval = 5;
    public const int WoundMoraleLoss = 100;
    public const int CasualtyMoraleLoss = 60;
    public const int LeaderLossMoraleLoss = 150;
    public const int CasualtyWitnessRadiusCm = 2000;
    public const int LeaderMoraleBonus = 150;
    public const int LeaderRallyBonus = 200;
    public const int NoLeaderRallyPenalty = 300;
    public const int RallyMoraleGain = 100;
    public const int CommandRadiusCm = 3000;
    public const int ActingLeaderQualityPct = 50;

    public const int SeriousBleedTicks = 1800;
    public const int FiringVisibilityTicks = 40;
    public const int FiringVisibilityPct = 400;
    public const int ReturnFireMemoryTicks = 200;
    public const int RetreatDistanceCm = 2000;
    public const int UnitHitRadiusCm = 25;
    public const int ProneHitRadiusCm = 30;
    public const int AimPointPct = 60;
    public const int PinnedAimPct = 150;

    public static int StanceSpreadPct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 80,
        _ => 60,
    };

    public static int WoundSpeedPct(WoundLevel wound) => wound switch
    {
        WoundLevel.None => 100,
        WoundLevel.Light => 85,
        WoundLevel.Serious => 50,
        _ => 0,
    };

    public static int WoundMoralePenalty(WoundLevel wound) => wound switch
    {
        WoundLevel.Light => 100,
        WoundLevel.Serious => 250,
        _ => 0,
    };
}
