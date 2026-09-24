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
        IsLeader = isLeader;
        LeaderQualityPct = isLeader ? 100 : 0;
        Morale = isLeader ? CombatRules.LeaderMorale : CombatRules.BaseMorale;
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

    public WeaponDef? Weapon { get; }
    public int Ammo { get; internal set; }
    public bool IsLeader { get; internal set; }

    /// <summary>100 for the original leader, 50 for a man who took over.</summary>
    public int LeaderQualityPct { get; internal set; }

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

    public GrenadeDef? GrenadeType { get; }
    public int Grenades { get; internal set; }
    public long LastThrowTick { get; internal set; } = NeverShot;
    public UnitId? ThrowTarget { get; internal set; }
    public UnitId? MeleeOpponent { get; internal set; }
    public bool IsCaptured { get; internal set; }
    public UnitId? AssaultTarget { get; internal set; }

    /// <summary>The soldier chooses walk, sneak or run himself while following his current move order.</summary>
    public bool AutoPace { get; internal set; }

    internal bool MeleeSurprise { get; set; }
    internal Vec2 AssaultGoal { get; set; }

    /// <summary>A broken soldier has already started (or finished) his retreat.</summary>
    internal bool Retreated { get; set; }
}
