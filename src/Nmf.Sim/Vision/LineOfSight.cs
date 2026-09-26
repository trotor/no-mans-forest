using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

public static class LineOfSight
{
    public const int Clear = 255;

    /// <summary>
    /// How clearly a point at <paramref name="toHeightCm"/> can be seen from <paramref name="fromHeightCm"/>:
    /// 0 = blocked, 255 = clear. Heights are absolute (ground + body).
    /// </summary>
    public static int Clarity(GridMap map, Vec2 from, int fromHeightCm, Vec2 to, int toHeightCm)
    {
        var a = from.ToCell();
        var b = to.ToCell();
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int steps = Math.Max(dx, dy);
        if (steps == 0)
            return Clear;

        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        int err = dx - dy;
        int x = a.X, y = a.Y;
        int accumulated = 0;

        while (true)
        {
            int e2 = 2 * err;
            bool movedX = false, movedY = false;
            if (e2 > -dy) { err -= dy; x += sx; movedX = true; }
            if (e2 < dx) { err += dx; y += sy; movedY = true; }

            var cell = map[new CellCoord(x, y)];
            bool isTarget = x == b.X && y == b.Y;
            int progress = dx >= dy ? Math.Abs(x - a.X) : Math.Abs(y - a.Y);
            int rayHeight = fromHeightCm + (toHeightCm - fromHeightCm) * progress / steps;

            if (!isTarget && rayHeight <= cell.GroundHeightCm)
                return 0;
            int step = movedX && movedY ? 141 : 100;
            if (rayHeight < cell.GroundHeightCm + cell.ObstacleHeightCm)
                accumulated += cell.ConcealmentPerM * step / 100;
            if (rayHeight < cell.GroundHeightCm + cell.LowCoverHeightCm)
                accumulated += cell.LowConcealmentPerM * step / 100; // a fallen tree or a boulder hides what is low
            if (accumulated >= Clear)
                return 0;
            if (isTarget)
                return Clear - accumulated;
        }
    }
}
