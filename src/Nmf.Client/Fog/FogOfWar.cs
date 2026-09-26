using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client.Fog;

/// <summary>
/// The fog of war the player sees: where a standing man could be seen by one of his men. Presentation only — spotting
/// itself is exact line of sight in <see cref="VisionSystem"/> — so it is computed on 4 × 4 m blocks, per observer,
/// and only again when that observer changes block or eye height (spec 2026-09-26-coarse-fog-design).
/// </summary>
public sealed class FogOfWar
{
    public const int BlockCells = 4;
    public const int RangeCm = 15_000;
    private const int StandingTargetCm = 170;
    private const int BlockCm = BlockCells * SimConstants.CentimetersPerCell;

    private readonly int[] _groundCm;
    private readonly int[] _concealment; // per block crossed, out of LineOfSight.Clear
    private readonly Dictionary<UnitId, (int Block, int Eye, int[] Seen)> _observers = [];

    public FogOfWar(GridMap map)
    {
        Width = (map.Width + BlockCells - 1) / BlockCells;
        Height = (map.Height + BlockCells - 1) / BlockCells;
        _groundCm = new int[Width * Height];
        _concealment = new int[Width * Height];
        var counts = new int[Width * Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                int b = y / BlockCells * Width + x / BlockCells;
                _groundCm[b] += cell.GroundHeightCm;
                // Only growth taller than a standing man hides him, as in the exact viewshed.
                if (cell.ObstacleHeightCm > StandingTargetCm)
                    _concealment[b] += cell.ConcealmentPerM;
                counts[b]++;
            }
        }
        for (int b = 0; b < counts.Length; b++)
        {
            _groundCm[b] /= counts[b];
            _concealment[b] = _concealment[b] * BlockCells / counts[b];
        }
        Visible = new bool[Width * Height];
        _scratch = new bool[Width * Height];
        _mapWidthCm = map.WidthCm;
        _mapHeightCm = map.HeightCm;
    }

    private readonly bool[] _scratch;
    private readonly int _mapWidthCm;
    private readonly int _mapHeightCm;

    /// <summary>Blocks across and down.</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>Row-major, one entry per block.</summary>
    public bool[] Visible { get; }

    /// <summary>Changes whenever <see cref="Visible"/> changes.</summary>
    public int Version { get; private set; }

    /// <summary>How many observers the last <see cref="Update"/> had to recompute (for tests and profiling).</summary>
    public int LastRecomputed { get; private set; }

    public bool IsVisible(Vec2 positionCm)
    {
        if (positionCm.X < 0 || positionCm.Y < 0 || positionCm.X >= _mapWidthCm || positionCm.Y >= _mapHeightCm)
            return false;
        return Visible[positionCm.Y / BlockCm * Width + positionCm.X / BlockCm];
    }

    /// <summary>Brings the fog up to date with the given observers (position and absolute eye height, cm).</summary>
    public void Update(IEnumerable<(UnitId Id, Vec2 Position, int EyeHeightAbsCm)> observers)
    {
        int recomputed = 0;
        bool changed = false;
        var present = new HashSet<UnitId>();
        foreach (var (id, position, eye) in observers)
        {
            present.Add(id);
            int bx = Math.Clamp(position.X / BlockCm, 0, Width - 1), by = Math.Clamp(position.Y / BlockCm, 0, Height - 1);
            int block = by * Width + bx;
            if (_observers.TryGetValue(id, out var cached) && cached.Block == block && cached.Eye == eye)
                continue;
            _observers[id] = (block, eye, Cast(bx, by, eye));
            recomputed++;
            changed = true;
        }
        foreach (var gone in _observers.Keys.Where(k => !present.Contains(k)).ToList())
        {
            _observers.Remove(gone);
            changed = true;
        }
        LastRecomputed = recomputed;
        if (!changed)
            return;
        Array.Clear(_scratch);
        foreach (var (_, _, seen) in _observers.Values)
            foreach (int b in seen)
                _scratch[b] = true;
        if (_scratch.AsSpan().SequenceEqual(Visible))
            return; // recomputed, but the same ground is seen: nothing to redraw
        _scratch.CopyTo(Visible, 0);
        Version++;
    }

    /// <summary>Blocks one observer sees: rays to the edge of the range, stopped by rising ground and thick growth.</summary>
    private int[] Cast(int ox, int oy, int eye)
    {
        var seen = new HashSet<int>();
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (ox + dx >= 0 && oy + dy >= 0 && ox + dx < Width && oy + dy < Height)
                    seen.Add((oy + dy) * Width + ox + dx);
        int radius = (RangeCm + BlockCm - 1) / BlockCm;
        for (int i = -radius; i <= radius; i++)
        {
            Ray(ox, oy, eye, ox + i, oy - radius, radius, seen);
            Ray(ox, oy, eye, ox + i, oy + radius, radius, seen);
            Ray(ox, oy, eye, ox - radius, oy + i, radius, seen);
            Ray(ox, oy, eye, ox + radius, oy + i, radius, seen);
        }
        return [.. seen];
    }

    private void Ray(int fx, int fy, int eye, int tx, int ty, int radius, HashSet<int> seen)
    {
        int dx = Math.Abs(tx - fx), dy = Math.Abs(ty - fy);
        int sx = Math.Sign(tx - fx), sy = Math.Sign(ty - fy);
        int err = dx - dy;
        int x = fx, y = fy;
        long radiusSq = (long)radius * radius;
        bool hasSlope = false;
        long slopeNum = 0, slopeDen = 1;
        int concealment = 0;
        while (x != tx || y != ty)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            long cx = x - fx, cy = y - fy;
            long distanceSq = cx * cx + cy * cy;
            if (distanceSq > radiusSq)
                return;
            long distance = IntMath.Isqrt(distanceSq * BlockCm * BlockCm);
            int b = y * Width + x;
            long targetNum = _groundCm[b] + StandingTargetCm - eye;
            if (!hasSlope || targetNum * slopeDen >= slopeNum * distance)
                seen.Add(b);
            long groundNum = _groundCm[b] - eye;
            if (!hasSlope || groundNum * slopeDen > slopeNum * distance)
            {
                hasSlope = true;
                slopeNum = groundNum;
                slopeDen = distance;
            }
            concealment += _concealment[b];
            if (concealment >= LineOfSight.Clear)
                return;
        }
    }
}
