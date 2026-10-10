using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tactics;

/// <summary>
/// Where the enemies a side knows of can see, on 4 m blocks round a point (spec 2026-10-10-tactics-design, "Uhkakartta").
/// Built from the side's contacts only: a seen man where he is, a lost or heard one where he was believed to be.
/// </summary>
public sealed class ThreatMap
{
    public const int BlockCm = 400;
    /// <summary>Beyond this a man in the woods is not a threat worth planning round.</summary>
    public const int SightCm = 15_000;
    /// <summary>The enemy's eyes (kneeling or over a foxhole rim) and the height of a man moving low.</summary>
    public const int EyeCm = 100, TargetCm = 100;
    public const int MaxRadiusCm = 25_000;
    /// <summary>A man standing in a foxhole: his eyes this high above its floor, over the rim.</summary>
    public const int PitEyeCm = 150;
    /// <summary>Foxholes this close to the position may hold men nobody has seen yet.</summary>
    public const int PossibleShootersCm = 3_000;
    private const int MaxSources = 64;

    private readonly int _originX, _originY, _blocks;
    private readonly ulong[] _seenBy;
    private readonly Vec2[] _sources;
    private readonly int[] _eyes;
    private readonly GridMap _map;

    private ThreatMap(GridMap map, int originX, int originY, int blocks, ulong[] seenBy, Vec2[] sources, int[] eyes)
    {
        _map = map;
        _eyes = eyes;
        _originX = originX;
        _originY = originY;
        _blocks = blocks;
        _seenBy = seenBy;
        _sources = sources;
    }

    /// <param name="possibleShootersNear">The position attacked: the foxholes round it count as shooters too (spec 6a-2 §1).</param>
    public static ThreatMap Build(Simulation sim, Side side, Vec2 centre, int radiusCm, Vec2? possibleShootersNear = null)
    {
        int radius = Math.Min(radiusCm, MaxRadiusCm);
        int blocks = 2 * radius / BlockCm + 1;
        int originX = centre.X - radius, originY = centre.Y - radius;
        var map = sim.Map;
        var sources = sim.Knowledge(side).Contacts
            .Where(c => c.Level != ContactLevel.Unknown && sim.FindUnit(c.Target) is { IsOutOfAction: false })
            .Select(c => c.Level == ContactLevel.Visible ? sim.FindUnit(c.Target)!.Position : c.Position)
            .Where(map.Contains)
            .OrderBy(p => (p - centre).LengthSquared)
            .Take(MaxSources)
            .Select(p => (At: p, Eye: map.CellAt(p).GroundHeightCm + EyeCm))
            .ToList();
        if (possibleShootersNear is { } near)
        {
            // Foxholes are there to be seen on the ground: one near the position may hold a man, seen or not.
            var c = near.ToCell();
            int r = PossibleShootersCm / SimConstants.CentimetersPerCell;
            long nearSq = (long)PossibleShootersCm * PossibleShootersCm;
            for (int y = c.Y - r; y <= c.Y + r && sources.Count < MaxSources; y++)
            {
                for (int x = c.X - r; x <= c.X + r && sources.Count < MaxSources; x++)
                {
                    var cell = new CellCoord(x, y);
                    if (!map.InBounds(cell) || (cell.CenterCm - near).LengthSquared > nearSq || !CoverFinder.InPit(map, cell.CenterCm))
                        continue;
                    if (sources.Any(s => (s.At - cell.CenterCm).LengthSquared <= 200L * 200))
                        continue;
                    sources.Add((cell.CenterCm, map[cell].GroundHeightCm + PitEyeCm));
                }
            }
        }
        var seenBy = new ulong[blocks * blocks];
        long sightSq = (long)SightCm * SightCm;
        for (int s = 0; s < sources.Count; s++)
        {
            var (eye, eyeHeight) = sources[s];
            for (int by = 0; by < blocks; by++)
            {
                for (int bx = 0; bx < blocks; bx++)
                {
                    var p = new Vec2(originX + bx * BlockCm + BlockCm / 2, originY + by * BlockCm + BlockCm / 2);
                    if (!map.Contains(p) || (p - eye).LengthSquared > sightSq)
                        continue;
                    if (LineOfSight.Clarity(map, eye, eyeHeight, p, map.CellAt(p).GroundHeightCm + TargetCm) > 0)
                        seenBy[by * blocks + bx] |= 1UL << s;
                }
            }
        }
        return new ThreatMap(map, originX, originY, blocks, seenBy, sources.Select(s => s.At).ToArray(), sources.Select(s => s.Eye).ToArray());
    }

    /// <summary>
    /// Whether any source sees a man kneeling exactly at <paramref name="p"/>: a sight line of its own, not the 4 m block
    /// (on the edge of a wood the block's middle may lie deeper in than the spot).
    /// </summary>
    public bool SeenExactly(Vec2 p)
    {
        if (!_map.Contains(p))
            return false;
        long sightSq = (long)SightCm * SightCm;
        int height = _map.CellAt(p).GroundHeightCm + TargetCm;
        for (int s = 0; s < _sources.Length; s++)
            if ((p - _sources[s]).LengthSquared <= sightSq && LineOfSight.Clarity(_map, _sources[s], _eyes[s], p, height) > 0)
                return true;
        return false;
    }

    /// <summary>Whether a known enemy is within the radius of the point (would hear a man moving there).</summary>
    public bool NearSource(Vec2 p, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        for (int s = 0; s < _sources.Length; s++)
            if ((p - _sources[s]).LengthSquared <= radiusSq)
                return true;
        return false;
    }

    /// <summary>How many known enemies see the block of <paramref name="p"/> (0 outside the window).</summary>
    public int ExposureAt(Vec2 p) => System.Numerics.BitOperations.PopCount(Mask(p));

    /// <summary>As <see cref="ExposureAt(Vec2)"/>, not counting the enemies within the radius of a point (the position attacked).</summary>
    public int ExposureAt(Vec2 p, Vec2 excludeNear, int excludeRadiusCm)
    {
        ulong mask = Mask(p);
        long radiusSq = (long)excludeRadiusCm * excludeRadiusCm;
        for (int s = 0; s < _sources.Length; s++)
            if ((_sources[s] - excludeNear).LengthSquared <= radiusSq)
                mask &= ~(1UL << s);
        return System.Numerics.BitOperations.PopCount(mask);
    }

    private ulong Mask(Vec2 p)
    {
        int bx = IntMath.FloorDiv(p.X - _originX, BlockCm), by = IntMath.FloorDiv(p.Y - _originY, BlockCm);
        return bx < 0 || by < 0 || bx >= _blocks || by >= _blocks ? 0 : _seenBy[by * _blocks + bx];
    }
}
