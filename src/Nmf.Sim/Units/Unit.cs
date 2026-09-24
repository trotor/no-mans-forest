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
}

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"U{Value}";
}

public sealed class Unit
{
    internal Unit(UnitId id, Side side, Vec2 position, int speedCmPerTick)
    {
        Id = id;
        Side = side;
        Position = position;
        SpeedCmPerTick = speedCmPerTick;
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
}
