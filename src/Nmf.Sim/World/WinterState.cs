using Nmf.Sim.Units;

namespace Nmf.Sim.World;

/// <summary>
/// A winter mission's snow (spec 2026-10-10-winter-design §2): the same depth everywhere; on foot it slows by its factor
/// (trodden roads excepted), on skis it is quicker than summer ground.
/// </summary>
public sealed class WinterState
{
    public const int SkiOpenPct = 70, SkiForestPct = 85, MaxFootPct = 250;

    private readonly int _road, _forest;

    public WinterState(int snowCm, GridMap map)
    {
        SnowCm = snowCm;
        FootPct = Math.Min(MaxFootPct, 100 + snowCm * 5 / 4);
        var names = map.TerrainNames.ToList();
        _road = names.IndexOf("road");
        _forest = names.IndexOf("forest");
    }

    public int SnowCm { get; }

    /// <summary>How much harder every step on foot is through the snow, in per cent.</summary>
    public int FootPct { get; }

    /// <summary>The snow's factor on a cell's cost of going, for this man as he goes now.</summary>
    public int FactorPct(Unit unit, CellData cell)
    {
        if (unit.OnSkis && unit.MoveMode is MoveMode.Walk or MoveMode.Run or MoveMode.Auto && unit.Stance == Stance.Standing)
            return cell.TerrainId == _forest ? SkiForestPct : SkiOpenPct;
        return cell.TerrainId == _road ? 100 : FootPct;
    }
}
