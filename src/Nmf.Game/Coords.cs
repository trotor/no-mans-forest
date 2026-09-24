using Godot;
using Nmf.Sim.Core;

namespace Nmf.Game;

/// <summary>Conversions between simulation centimetres and Godot world pixels (32 px per 1 m cell).</summary>
public static class Coords
{
    public const float PixelsPerCell = 32f;
    public const float PixelsPerCm = PixelsPerCell / 100f;

    public static Vector2 ToPixels(Vec2 cm) => new Vector2(cm.X, cm.Y) * PixelsPerCm;

    public static Vector2 ToPixels(double xCm, double yCm) => new Vector2((float)xCm, (float)yCm) * PixelsPerCm;

    public static Vec2 ToCm(Vector2 px) => new((int)(px.X / PixelsPerCm), (int)(px.Y / PixelsPerCm));
}
