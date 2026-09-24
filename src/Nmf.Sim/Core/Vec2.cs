namespace Nmf.Sim.Core;

/// <summary>Position or offset in whole centimetres.</summary>
public readonly record struct Vec2(int X, int Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public long LengthSquared => (long)X * X + (long)Y * Y;

    public int Length => (int)IntMath.Isqrt(LengthSquared);

    public CellCoord ToCell() => new(
        IntMath.FloorDiv(X, SimConstants.CentimetersPerCell),
        IntMath.FloorDiv(Y, SimConstants.CentimetersPerCell));

    public override string ToString() => $"({X},{Y})cm";
}

/// <summary>Index of a 1 m map cell.</summary>
public readonly record struct CellCoord(int X, int Y)
{
    public Vec2 CenterCm => new(
        X * SimConstants.CentimetersPerCell + SimConstants.CentimetersPerCell / 2,
        Y * SimConstants.CentimetersPerCell + SimConstants.CentimetersPerCell / 2);
}
