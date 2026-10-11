using Nmf.Sim.Combat;
using Nmf.Sim.Core;

namespace Nmf.Sim.Units;

public enum Side : byte
{
    Blue = 0,
    Red = 1,
}

public enum Stance : byte
{
    Standing = 0,
    Crouching = 1,
    Prone = 2,
}

public enum MoveMode : byte
{
    Walk = 0,
    Run = 1,
    Crawl = 2,

    /// <summary>Crouched walk at reduced speed.</summary>
    Sneak = 3,

    /// <summary>Orders only: the soldier picks walk, sneak or run himself (resolved when the order is applied).</summary>
    Auto = 4,
}

/// <summary>What a man has learned to do best (spec 2026-10-11-campaign-design §9b).</summary>
public enum Specialty : byte
{
    None = 0,
    /// <summary>Tarkka-ampuja: a steadier rifle.</summary>
    Marksman = 1,
    /// <summary>Partiomies: sees sooner, is seen and heard less on the move.</summary>
    Scout = 2,
    /// <summary>Pioneeri: more grenades thrown truer, a wire cut quicker.</summary>
    Sapper = 3,
    /// <summary>Lääkintämies: binds a bad wound so it no longer bleeds.</summary>
    Medic = 4,
}

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"U{Value}";
}

public sealed class Unit
{
    internal Unit(UnitId id, Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader, GrenadeDef? grenade = null)
    {
        GrenadeType = grenade;
        Grenades = grenade is null ? 0 : CombatRules.GrenadesPerSoldier;
        Id = id;
        Side = side;
        Position = position;
        SpeedCmPerTick = speedCmPerTick;
        Weapon = weapon;
        Ammo = weapon?.MagazineSize ?? 0;
        Magazines = weapon?.SpareMagazines ?? 0;
        IsLeader = isLeader;
        LeaderQualityPct = isLeader ? 100 : 0;
        BaseMorale = isLeader ? CombatRules.LeaderMorale : CombatRules.BaseMorale;
        Morale = BaseMorale;
    }

    public UnitId Id { get; }
    public Side Side { get; }

    /// <summary>Walking speed on open ground.</summary>
    public int SpeedCmPerTick { get; }

    public Vec2 Position { get; internal set; }

    /// <summary>Final destination of the current move order, or null when not moving.</summary>
    public Vec2? MoveTarget { get; internal set; }

    public MoveMode MoveMode { get; internal set; }
    public Stance Stance { get; internal set; }

    /// <summary>Stance the unit is changing to, or null.</summary>
    public Stance? TargetStance { get; internal set; }

    public int StanceTicksLeft { get; internal set; }

    /// <summary>True if the unit changed position during the last step.</summary>
    public bool IsMoving { get; internal set; }

    /// <summary>Waypoints of the current path; <see cref="PathIndex"/> is the next one.</summary>
    public IReadOnlyList<Vec2> Path => PathPoints;

    public int PathIndex { get; internal set; }

    internal List<Vec2> PathPoints { get; } = [];

    /// <summary>Set by movement, cleared by the vision update; used for hearing.</summary>
    internal bool MovedSinceVisionUpdate { get; set; }

    public const long NeverShot = -1_000_000;

    public WeaponDef? Weapon { get; internal set; }
    public int Ammo { get; internal set; }
    /// <summary>Spare magazines; a reload uses one.</summary>
    public int Magazines { get; internal set; }
    public bool OutOfAmmo => Weapon is null || (Ammo <= 0 && Magazines <= 0);
    public bool IsLeader { get; internal set; }

    /// <summary>His squad within his side (0, 1, …): a leader leads his own squad only.</summary>
    public int Squad { get; internal set; }

    /// <summary>Area fire: the place he keeps firing at (spec 2026-09-26-squads-area-fire-design §3).</summary>
    public Vec2? AreaTarget { get; internal set; }

    /// <summary>100 for the original leader, 50 for a man who took over.</summary>
    public int LeaderQualityPct { get; internal set; }
    /// <summary>The morale he starts with and recovers toward (0–1000).</summary>
    public int BaseMorale { get; internal set; }
    /// <summary>Shooting skill 0–100: spread × (150 − skill) / 100, so 50 is average.</summary>
    public int Marksmanship { get; internal set; } = 50;

    /// <summary>0–100, 50 average: how many fights he has come through (spec 2026-09-26-experience-prone-design).</summary>
    public int Experience { get; internal set; } = 50;
    /// <summary>What he has learned to do best; set through <see cref="Simulation.SetSpecialty"/>.</summary>
    public Specialty Specialty { get; internal set; }

    /// <summary>His bad wound is bound: it no longer bleeds (a new wound tears the bandage away).</summary>
    public bool Bandaged { get; internal set; }

    /// <summary>The man whose wound he goes to bind, or binds (a medic).</summary>
    public UnitId? FirstAidTarget { get; internal set; }

    /// <summary>Name and rank from the mission roster, if any.</summary>
    public string? Name { get; internal set; }

    public WoundLevel Wound { get; internal set; }
    public long WoundTick { get; internal set; }
    public int Suppression { get; internal set; }
    public int Morale { get; internal set; }
    public MoraleState MoraleState { get; internal set; }
    public FirePolicy FirePolicy { get; internal set; }
    public CombatAction Action { get; internal set; }
    public int ActionTicksLeft { get; internal set; }
    public int RoundsLeftInBurst { get; internal set; }
    public UnitId? Target { get; internal set; }
    public UnitId? OrderedTarget { get; internal set; }
    public long LastShotTick { get; internal set; } = NeverShot;

    public bool IsAlive => Wound != WoundLevel.Dead;
    public bool IsOutOfAction => Wound >= WoundLevel.Incapacitated || IsCaptured;

    public GrenadeDef? GrenadeType { get; internal set; }

    private readonly List<Item> _items = [];
    /// <summary>Papers and other items he carries.</summary>
    public IReadOnlyList<Item> Items => _items;
    /// <summary>Someone has already been through his pockets.</summary>
    public bool Looted { get; internal set; }
    private byte _searchedBy;
    /// <summary>Whether a man of <paramref name="side"/> has searched him — known to that side, whatever is left.</summary>
    public bool WasSearchedBy(Side side) => (_searchedBy & (1 << (int)side)) != 0;
    internal void MarkSearchedBy(Side side) => _searchedBy |= (byte)(1 << (int)side);
    internal byte SearchedBySides => _searchedBy;
    /// <summary>The fallen man he is on his way to search, or searching.</summary>
    public UnitId? LootTarget { get; internal set; }
    /// <summary>Toughness 0–100; the toughest hold their ground when the enemy opens fire.</summary>
    public int Nerve { get; internal set; } = CombatRules.DefaultNerve;
    public bool IsTough => Nerve >= CombatRules.ToughNerve;
    /// <summary>Running for cover after the enemy opened fire on him.</summary>
    public bool TakingCover { get; internal set; }
    internal bool CoverReactionPending { get; set; }
    internal Vec2? CoverThreat { get; set; }
    /// <summary>He picked his stance in cover to see the enemy over it; the automatic crouch leaves it alone.</summary>
    internal bool HoldsCoverStance { get; set; }
    /// <summary>His part in an attack by fire and movement.</summary>
    public AttackRole AttackRole { get; internal set; }
    /// <summary>The planned attack he is part of (spec 2026-10-10-tactics-design).</summary>
    internal int? PlannedAttackId { get; set; }
    internal void AddItem(Item item) => _items.Add(item);
    internal void ClearItems() => _items.Clear();
    public int Grenades { get; internal set; }
    public long LastThrowTick { get; internal set; } = NeverShot;
    public UnitId? ThrowTarget { get; internal set; }
    /// <summary>Cut off from his side (a downed pilot): not theirs to command until one of them reaches him (spec 2026-10-10-events-design §1).</summary>
    public bool Stranded { get; internal set; }

    /// <summary>The man he carries (spec 2026-10-10-carrying-design).</summary>
    public UnitId? Carrying { get; internal set; }
    /// <summary>The man carrying him.</summary>
    public UnitId? CarriedBy { get; internal set; }
    /// <summary>The carried man he helps to carry.</summary>
    public UnitId? HelpingCarry { get; internal set; }
    /// <summary>The man he is on his way to lift.</summary>
    public UnitId? CarryTarget { get; internal set; }
    /// <summary>A helper walks beside him: he goes faster.</summary>
    public bool CarryHelped { get; internal set; }
    /// <summary>His hands are taken by a man: he neither fires nor throws.</summary>
    public bool HandsBusy => Carrying is not null || HelpingCarry is not null || Action == Combat.CombatAction.Lifting;
    /// <summary>A lobbed grenade's aim: where the side believes the target is (null: at the target himself).</summary>
    public Vec2? ThrowPoint { get; internal set; }
    public UnitId? MeleeOpponent { get; internal set; }
    public bool IsCaptured { get; internal set; }
    public UnitId? AssaultTarget { get; internal set; }

    /// <summary>The soldier chooses walk, sneak or run himself while following his current move order.</summary>
    public bool AutoPace { get; internal set; }
    /// <summary>The player chose the stance; the soldier keeps it until the next move order.</summary>
    public bool StanceOrdered { get; internal set; }
    /// <summary>Last tick he took any suppression (a shot at him, a near miss, a blast).</summary>
    public long LastSuppressedTick { get; internal set; } = long.MinValue / 2;

    /// <summary>On skis: quick over the snow walking or running upright (spec 2026-10-10-winter-design §2).</summary>
    public bool OnSkis { get; internal set; }
    /// <summary>Hundredths of a centimetre of his last step through the snow not yet gone.</summary>
    internal int MoveRemainder { get; set; }
    /// <summary>He has an ahkio: in winter the man he carries goes on it.</summary>
    public bool HasAhkio { get; internal set; }

    /// <summary>How awake he is to the enemy (spec 2026-10-10-alertness-design); Combat on a side without alertness.</summary>
    public Alertness Alertness { get; internal set; } = Alertness.Combat;
    /// <summary>When something last made him wary (suspicion fades 30 s after it).</summary>
    public long AlertSinceTick { get; internal set; }
    /// <summary>Taken by surprise: he does not fire before this tick.</summary>
    public long ReadyTick { get; internal set; }
    /// <summary>When he will shout the alarm; -1: no shout coming.</summary>
    public long ShoutTick { get; internal set; } = -1;
    /// <summary>When he will fire a signal flare; -1: none coming.</summary>
    public long FlareTick { get; internal set; } = -1;
    /// <summary>He has shouted the alarm (once at most).</summary>
    public bool Shouted { get; internal set; }
    /// <summary>Where the alarm came from: what he turns to.</summary>
    public Vec2 AlarmAt { get; internal set; }

    internal bool MeleeSurprise { get; set; }
    internal Vec2 AssaultGoal { get; set; }

    /// <summary>A broken soldier has already started (or finished) his retreat.</summary>
    internal bool Retreated { get; set; }
}

public enum AttackRole
{
    None,
    /// <summary>Dashing forward to the next cover.</summary>
    Bounding,
    /// <summary>Down in a firing position, keeping the target's head down.</summary>
    Covering,
}
