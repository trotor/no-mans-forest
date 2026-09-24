using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Time;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Client;

/// <summary>
/// Everything the player-facing game needs on top of the simulation: time, selection,
/// group orders, fog of war and smooth positions. Engine independent, so it is unit tested.
/// </summary>
public sealed class GameSession
{
    public const int FogRangeCm = 15_000;

    private readonly Dictionary<UnitId, Vec2> _previousPositions = [];
    private readonly List<SimEvent> _events = [];

    public GameSession(Scenario scenario, Side playerSide = Side.Blue)
    {
        Scenario = scenario;
        PlayerSide = playerSide;
        VisibleCells = new bool[Sim.Map.Width * Sim.Map.Height];
        SnapshotPositions();
        RefreshFog();
    }

    public Scenario Scenario { get; }
    public Simulation Sim => Scenario.Sim;
    public Side PlayerSide { get; }
    public FixedStepClock Clock { get; } = new();
    public Selection Selection { get; } = new();

    /// <summary>Cells the player's units can currently see, indexed y * width + x.</summary>
    public bool[] VisibleCells { get; }

    /// <summary>Changes whenever <see cref="VisibleCells"/> is recomputed.</summary>
    public int FogVersion { get; private set; }

    public SideKnowledge Knowledge => Sim.Knowledge(PlayerSide);
    public IEnumerable<Unit> OwnUnits => Sim.Units.Where(u => u.Side == PlayerSide);
    public TimeSpan GameTime => TimeSpan.FromSeconds((double)Sim.Tick / SimConstants.TicksPerSecond);

    /// <summary>The player may see own units and enemies their men currently see; the debug reveal shows everyone.</summary>
    public bool IsShownToPlayer(Unit unit, bool revealAll) =>
        unit.Side == PlayerSide || revealAll || Knowledge.LevelOf(unit.Id) == ContactLevel.Visible;

    public int Update(double realDeltaSeconds)
    {
        int steps = Clock.Advance(realDeltaSeconds);
        for (int i = 0; i < steps; i++)
            StepOnce();
        return steps;
    }

    public void StepOnce()
    {
        SnapshotPositions();
        Scenario.Tick();
        _events.AddRange(Sim.Step());
        if (_events.Count > 20_000)
            _events.RemoveRange(0, _events.Count - 20_000);
        if (Sim.Tick % VisionRules.IntervalTicks == 0)
            RefreshFog();
    }

    public (double X, double Y) InterpolatedPositionCm(Unit unit)
    {
        var current = unit.Position;
        var previous = _previousPositions.TryGetValue(unit.Id, out var p) ? p : current;
        double a = Math.Clamp(Clock.Alpha, 0, 1);
        return (previous.X + (current.X - previous.X) * a, previous.Y + (current.Y - previous.Y) * a);
    }

    public void OrderMove(Vec2 target, MoveMode mode)
    {
        var ids = Selection.Ids;
        var offsets = Formation.Offsets(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            var spot = target + offsets[i];
            if (!Sim.Map.Contains(spot) || !Sim.Map.CellAt(spot).IsPassable)
                spot = target;
            Sim.Submit(PlayerSide, new MoveOrder(ids[i], spot, mode));
        }
    }

    public IReadOnlyList<SimEvent> TakeEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public void OrderFireAt(UnitId target)
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new FireAtOrder(id, target));
    }

    public void CycleFirePolicy()
    {
        var ids = Selection.Ids;
        if (ids.Count == 0 || Sim.FindUnit(ids[0]) is not { } first)
            return;
        var next = (FirePolicy)(((int)first.FirePolicy + 1) % 3);
        foreach (var id in ids)
            Sim.Submit(PlayerSide, new SetFirePolicyOrder(id, next));
    }

    public Unit? EnemyAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        return Sim.Units
            .Where(u => u.Side != PlayerSide && u.IsAlive && IsShownToPlayer(u, revealAll: false)
                        && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    public void OrderStance(Stance stance)
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new SetStanceOrder(id, stance));
    }

    public void OrderStop()
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new StopOrder(id));
    }

    private void SnapshotPositions()
    {
        foreach (var unit in Sim.Units)
            _previousPositions[unit.Id] = unit.Position;
    }

    private void RefreshFog()
    {
        Viewshed.Compute(
            Sim.Map,
            OwnUnits.Select(u => (u.Position, VisionRules.EyeHeightAbsCm(Sim.Map, u))),
            FogRangeCm,
            VisibleCells);
        FogVersion++;
    }
}
