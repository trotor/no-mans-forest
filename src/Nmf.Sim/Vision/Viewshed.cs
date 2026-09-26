using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

/// <summary>Cells in which a standing man could be seen by at least one observer, exactly per 1 m cell (the reference the block fog in Nmf.Client is tested against).</summary>
public static class Viewshed
{
    public const int StandingTargetHeightCm = 170;
    // Only obstacles taller than a standing man hide him; lower ones (rocks, bushes) are seen over, as in LineOfSight.
    private const int BlockingObstacleHeightCm = StandingTargetHeightCm;

    public static void Compute(GridMap map, IEnumerable<(Vec2 Position, int EyeHeightAbsCm)> observers, int rangeCm, bool[] visible)
    {
        if (visible.Length != map.Width * map.Height)
            throw new ArgumentException("Buffer size must match the map.", nameof(visible));
        Array.Clear(visible);
        int radius = rangeCm / SimConstants.CentimetersPerCell;

        foreach (var (position, eye) in observers)
        {
            var origin = position.ToCell();
            if (!map.InBounds(origin))
                continue;
            visible[origin.Y * map.Width + origin.X] = true;
            for (int i = -radius; i <= radius; i++)
            {
                CastRay(map, origin, eye, new CellCoord(origin.X + i, origin.Y - radius), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X + i, origin.Y + radius), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X - radius, origin.Y + i), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X + radius, origin.Y + i), radius, visible);
            }
        }
    }

    private static void CastRay(GridMap map, CellCoord from, int eye, CellCoord to, int radius, bool[] visible)
    {
        int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        int sx = Math.Sign(to.X - from.X), sy = Math.Sign(to.Y - from.Y);
        int err = dx - dy;
        int x = from.X, y = from.Y;
        long radiusSquared = (long)radius * radius;

        // Steepest ground slope seen so far along the ray, as slopeNum / slopeDen (cm / cm).
        bool hasSlope = false;
        long slopeNum = 0, slopeDen = 1;
        int concealment = 0;

        while (x != to.X || y != to.Y)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }

            var cellCoord = new CellCoord(x, y);
            if (!map.InBounds(cellCoord))
                return;
            long cx = x - from.X, cy = y - from.Y;
            long distanceSquared = cx * cx + cy * cy;
            if (distanceSquared > radiusSquared)
                return;

            long distance = IntMath.Isqrt(distanceSquared * 10_000); // centimetres
            var cell = map[cellCoord];
            long targetNum = cell.GroundHeightCm + StandingTargetHeightCm - eye;
            if (!hasSlope || targetNum * slopeDen >= slopeNum * distance)
                visible[y * map.Width + x] = true;

            long groundNum = cell.GroundHeightCm - eye;
            if (!hasSlope || groundNum * slopeDen > slopeNum * distance)
            {
                hasSlope = true;
                slopeNum = groundNum;
                slopeDen = distance;
            }

            if (cell.ObstacleHeightCm > BlockingObstacleHeightCm)
            {
                concealment += cell.ConcealmentPerM;
                if (concealment >= LineOfSight.Clear)
                    return;
            }
        }
    }
}
