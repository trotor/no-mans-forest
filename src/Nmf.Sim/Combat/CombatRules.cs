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
    /// <summary>Suppression fades by this many points per second; slow enough that sustained fire keeps men down.</summary>
    public const int SuppressionDecayPerSecond = 20;
    public const int ProneDecayBonusPerSecond = 10;
    public const int LeaderDecayBonusPerSecond = 10;
    public const int NearMissRadiusCm = 250;
    /// <summary>The man being aimed at is suppressed by misses this close; bystanders only within NearMissRadiusCm.</summary>
    public const int AimedMissRadiusCm = 500;
    /// <summary>A man suppressed within this many ticks counts as under fire.</summary>
    public const int UnderFireTicks = 60;
    public const int WalkingFireSpreadPct = 200;
    public const int RunningFireSpreadPct = 300;
    public const int RunSpeedPct = 225;
    public const int LootRangeCm = 150;
    public const int LootTicks = 40;
    public const int AutoLootRangeCm = 1000;
    /// <summary>A man with this many spare magazines or fewer looks for more on his own.</summary>
    public const int LowOnMagazines = 1;
    /// <summary>Men with this much nerve or more hold their ground when the enemy opens fire.</summary>
    public const int ToughNerve = 75;
    public const int DefaultNerve = 50;
    /// <summary>Fire after this long without any counts as the enemy opening fire.</summary>
    public const int FireQuietTicks = 200;
    public const int CoverSearchCm = 800;
    public const int CoverDistancePenaltyPerM = 20;
    public const int CoverObstacleMinCm = 50;
    public const int CoverOccupiedCm = 100;
    /// <summary>A* node budget when checking a cover cell can be reached (a few times the search area).</summary>
    public const int CoverPathNodeBudget = 4000;
    /// <summary>Height above the enemy's ground a man in cover must see to fire back (a crouched enemy's chest).</summary>
    public const int CoverSightTargetCm = 100;
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
    public const int FriendlyLineClearanceCm = 100;

    // grenades (spec 2026-09-24-grenades-melee-design §3)
    public const int GrenadesPerSoldier = 2;
    public const int ThrowTicks = 20;
    public const int GrenadeFlightTicks = 12;
    public const int MinThrowCm = 800;
    public const int CloseThrowCm = 2000;
    public const int ProneThrowPct = 60;
    public const int GrenadeFriendSafetyCm = 800;
    public const int ThrowCooldownTicks = 100;
    public const int HardCover = 128;
    public const int ShieldObstacleMinCm = 50;
    public const int ShieldHillMarginCm = 50;

    // melee (§4)
    public const int MeleeRangeCm = 200;
    public const int MeleeTicks = 40;
    public const int MeleeSurpriseBonus = 30;
    public const int MeleeWinMorale = 50;

    // pace and stance (§2) and assault (§5)
    public const int SneakRangeCm = 6000;
    public const int AutoCrouchRangeCm = 6000;
    public const int SneakSpeedPct = 65;
    public const int AssaultRepathCm = 200;
    /// <summary>Assaulting men stop this far beyond the lethal radius of their own live grenade.</summary>
    public const int AssaultGrenadeClearanceCm = 300;

    public static int FragmentStancePct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 80,
        _ => 50,
    };

    public static int MeleeSkill(WeaponClass? weapon) => weapon switch
    {
        WeaponClass.Smg => 60,
        WeaponClass.Rifle => 50,
        WeaponClass.Lmg => 30,
        _ => 20,
    };

    public static int MeleeWoundPenalty(WoundLevel wound) => wound switch
    {
        WoundLevel.Light => 10,
        WoundLevel.Serious => 30,
        _ => 0,
    };

    public static int StanceSpreadPct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 80,
        _ => 60,
    };

    /// <summary>Weapon spread after stance and suppression, in microradians (keeps precision a milliradian integer would lose).</summary>
    public static int EffectiveSpreadMicroRad(int spreadMrad, Stance stance, int suppression, int movingPct = 100) =>
        (int)((long)spreadMrad * 1000 * StanceSpreadPct(stance) * (100 + suppression / 5) / 10_000 * movingPct / 100);

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
