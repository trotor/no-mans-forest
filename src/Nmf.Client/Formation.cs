using Nmf.Sim.Core;

namespace Nmf.Client;

/// <summary>Spreads a group move over nearby spots so soldiers do not stack on one point.</summary>
public static class Formation
{
    public const int SpacingCm = 200;

    public static IReadOnlyList<Vec2> Offsets(int count)
    {
        var result = new List<Vec2>(count);
        for (int ring = 0; result.Count < count; ring++)
        {
            var cells = new List<Vec2>();
            for (int y = -ring; y <= ring; y++)
                for (int x = -ring; x <= ring; x++)
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == ring)
                        cells.Add(new Vec2(x * SpacingCm, y * SpacingCm));

            foreach (var offset in cells.OrderBy(o => o.LengthSquared).ThenBy(o => o.Y).ThenBy(o => o.X))
            {
                if (result.Count == count)
                    break;
                result.Add(offset);
            }
        }
        return result;
    }
}
