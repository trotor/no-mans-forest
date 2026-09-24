using Nmf.Sim.Core;

namespace Nmf.Sim.Units;

public enum Side : byte
{
    Blue = 0,
    Red = 1,
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
    public int SpeedCmPerTick { get; }
    public Vec2 Position { get; internal set; }
    public Vec2? MoveTarget { get; internal set; }
}
