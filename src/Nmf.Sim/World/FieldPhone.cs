using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.World;

/// <summary>
/// A post's field telephone and its wire to the rear (spec 2026-10-10-alertness-design §4): once the post is alarmed a man
/// calls through, unless the wire has been cut. It calls once.
/// </summary>
public sealed class FieldPhone(string id, Side side, Vec2 at, IReadOnlyList<Vec2> line)
{
    public string Id { get; } = id;
    public Side Side { get; } = side;
    public Vec2 At { get; } = at;
    /// <summary>The wire, from the phone to the rear.</summary>
    public IReadOnlyList<Vec2> Line { get; } = line;

    /// <summary>The call got through.</summary>
    public bool Called { get; internal set; }
    /// <summary>The call was made on a cut wire: nobody tries again.</summary>
    public bool Failed { get; internal set; }
    /// <summary>Where the wire was cut; null: whole.</summary>
    public Vec2? CutAt { get; internal set; }

    public UnitId? Caller { get; internal set; }
    /// <summary>When the call ends; -1: not calling yet.</summary>
    public long CallDoneTick { get; internal set; } = -1;

    public UnitId? Cutter { get; internal set; }
    public Vec2 CutPoint { get; internal set; }
    /// <summary>When the wire parts; -1: not at it yet.</summary>
    public long CutDoneTick { get; internal set; } = -1;

    /// <summary>The point of the wire nearest <paramref name="p"/>, and how far it is.</summary>
    public (Vec2 Point, long DistanceCm) Nearest(Vec2 p)
    {
        Vec2 best = Line[0];
        long bestSq = (p - best).LengthSquared;
        for (int i = 1; i < Line.Count; i++)
        {
            var a = Line[i - 1];
            var b = Line[i];
            var ab = b - a;
            long lenSq = ab.LengthSquared;
            long t = lenSq == 0 ? 0 : Math.Clamp(((long)(p.X - a.X) * ab.X + (long)(p.Y - a.Y) * ab.Y) * 1000 / lenSq, 0, 1000);
            var q = new Vec2(a.X + (int)(ab.X * t / 1000), a.Y + (int)(ab.Y * t / 1000));
            long dSq = (p - q).LengthSquared;
            if (dSq < bestSq)
            {
                best = q;
                bestSq = dSq;
            }
        }
        return (best, IntMath.Isqrt(bestSq));
    }
}
