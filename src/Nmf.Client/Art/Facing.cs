namespace Nmf.Client.Art;

/// <summary>Eight facings, 0 = north (screen up), clockwise.</summary>
public static class Facing
{
    public const int North = 0;
    public const int East = 2;
    public const int South = 4;

    public static int FromDelta(double dx, double dy)
    {
        double degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        int index = (int)Math.Round(degrees / 45.0);
        return ((index % 8) + 8) % 8;
    }
}
