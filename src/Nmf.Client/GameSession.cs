using Nmf.Sim.Mission;
using Nmf.Client.Fog;
using Nmf.Sim;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Time;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Client;

/// <summary>
/// Everything the player-facing game needs on top of the simulation: time, selection,
/// group orders, fog of war and smooth positions. Engine independent, so it is unit tested.
/// </summary>
public sealed class GameSession
{
    private readonly Dictionary<UnitId, Vec2> _previousPositions = [];
    private readonly List<SimEvent> _events = [];

    /// <summary>A session playing a mission: its objectives are tracked and its texts shown in <paramref name="language"/> (en or fi).</summary>
    public GameSession(Scenario scenario, MissionSpec mission, string language = "en", Side playerSide = Side.Blue) : this(scenario, playerSide)
    {
        Mission = mission;
        Language = language;
        Tracker = new MissionTracker(mission, scenario.Sim.Map, playerSide);
    }

    public MissionSpec? Mission { get; }
    public MissionTracker? Tracker { get; }
    public string Language { get; } = "en";

    public GameSession(Scenario scenario, Side playerSide = Side.Blue)
    {
        Scenario = scenario;
        PlayerSide = playerSide;
        Fog = new FogOfWar(Sim.Map);
        SnapshotPositions();
        RefreshFog();
    }

    public Scenario Scenario { get; }
    public Simulation Sim => Scenario.Sim;
    public Side PlayerSide { get; }
    public FixedStepClock Clock { get; } = new();
    public Selection Selection { get; } = new();

    /// <summary>What the player's men can see, for drawing the fog (4 m blocks; spotting itself is exact line of sight).</summary>
    public FogOfWar Fog { get; }

    /// <summary>Changes whenever the fog changes.</summary>
    public int FogVersion => Fog.Version;

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
        if (Tracker is not null)
            _events.AddRange(Tracker.Update(Sim));
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

    /// <summary>
    /// Sends the selection toward a point in formation. Each man takes cover near his spot if there is any, and a spot
    /// that cannot be reached (a rock, a closed pocket) is replaced by the nearest one that can.
    /// </summary>
    public void OrderMove(Vec2 target, MoveMode mode)
    {
        var ids = CommandedIds;
        var offsets = Formation.Offsets(ids.Count);
        var taken = new HashSet<CellCoord>();
        for (int i = 0; i < ids.Count; i++)
        {
            if (Sim.FindUnit(ids[i]) is not { } unit)
                continue;
            var spot = target + offsets[i];
            if (!Sim.Map.Contains(spot) || !Sim.Map.CellAt(spot).IsPassable)
                spot = target;
            spot = MovePlanner.SeekCover(Sim.Map, spot, taken);
            if (MovePlanner.Reachable(Sim.Map, unit.Position, spot) is not { } reachable)
                continue;
            taken.Add(reachable.ToCell());
            Sim.Submit(PlayerSide, new MoveOrder(ids[i], reachable, mode));
        }
    }

    public const int ClickRadiusCm = 150;

    /// <summary>
    /// Context click (spec 2026-09-24-grenades-melee-design §2). With nothing selected the whole squad is commanded.
    /// Own soldier: command only him (Shift adds); double click: the whole squad again.
    /// Seen enemy: fire at him; double click: assault him. Seen unsearched body: the nearest commanded man searches it. Ground: move there at their own pace; double click: run; Alt: crawl.
    /// </summary>
    public ClickOutcome HandleLeftClick(Vec2 point, bool doubleClick, bool shift, bool alt)
    {
        var own = NearestOwnAt(point);
        var enemy = EnemyAt(point, ClickRadiusCm);
        bool ownWins = own is not null && (enemy is null || (own.Position - point).LengthSquared <= (enemy.Position - point).LengthSquared);

        if (ownWins)
        {
            if (doubleClick)
            {
                Selection.Clear();
                return new ClickOutcome(ClickResult.SelectedSquad, own!.Position);
            }
            Selection.SelectAt(Sim.Units.Where(u => !u.IsOutOfAction), PlayerSide, point, ClickRadiusCm, shift);
            return new ClickOutcome(ClickResult.Selected, own!.Position);
        }
        if (CommandedIds.Count == 0)
            return new ClickOutcome(ClickResult.None, point);
        if (enemy is null && BodyAt(point, ClickRadiusCm) is { } body)
        {
            return OrderLoot(body)
                ? new ClickOutcome(ClickResult.LootOrdered, body.Position)
                : new ClickOutcome(ClickResult.None, body.Position);
        }
        if (enemy is not null)
        {
            if (doubleClick)
            {
                OrderAssault(enemy.Id);
                return new ClickOutcome(ClickResult.AssaultOrdered, enemy.Position);
            }
            OrderFireAt(enemy.Id);
            return new ClickOutcome(ClickResult.FireOrdered, enemy.Position);
        }
        if (!Sim.Map.Contains(point))
            return new ClickOutcome(ClickResult.None, point);
        OrderMove(point, alt ? MoveMode.Crawl : doubleClick ? MoveMode.Run : MoveMode.Auto);
        return new ClickOutcome(ClickResult.MoveOrdered, point);
    }

    /// <summary>The soldiers orders go to: the selection, or with nothing selected every own man still in action.</summary>
    /// <summary>The selected men still in action; the whole squad when there are none.</summary>
    public IReadOnlyList<UnitId> CommandedIds
    {
        get
        {
            var selected = InAction(Selection.Ids.Select(Sim.FindUnit));
            return selected.Count > 0 ? selected : InAction(OwnUnits);
        }
    }

    public bool IsSquadCommanded => InAction(Selection.Ids.Select(Sim.FindUnit)).Count == 0;

    private static List<UnitId> InAction(IEnumerable<Unit?> units) =>
        units.Where(u => u is { IsOutOfAction: false }).Select(u => u!.Id).ToList();

    /// <summary>A fallen man (either side) the player can see and nobody has searched yet.</summary>
    public Unit? BodyAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        return Sim.Units
            .Where(u => u.IsOutOfAction && !u.Looted && IsShownToPlayer(u, revealAll: false)
                        && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// Sends one commanded man who is free to move to search the body: the nearest who can use its ammo, else the nearest.
    /// Returns false when nobody can go (all pinned or broken).
    /// </summary>
    public bool OrderLoot(Unit body)
    {
        var free = CommandedIds.Select(Sim.FindUnit)
            .Where(u => u is not null && u != body && u.MoraleState == MoraleState.Steady)
            .Select(u => u!)
            .OrderBy(u => LootSystem.HasUsefulLoot(u, body) ? 0 : 1)
            .ThenBy(u => (u.Position - body.Position).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .ToList();
        if (free.Count == 0)
            return false;
        Sim.Submit(PlayerSide, new LootOrder(free[0].Id, body.Id));
        return true;
    }

    /// <summary>Names of the papers own men still in action carry.</summary>
    public IReadOnlyList<string> CarriedPapers =>
        OwnUnits.Where(u => !u.IsOutOfAction).SelectMany(u => u.Items).Select(i => i.Name).Distinct().ToList();

    public void OrderAssault(UnitId target)
    {
        foreach (var id in CommandedIds)
            Sim.Submit(PlayerSide, new AssaultOrder(id, target));
    }

    public ClickOutcome HandleRightClick()
    {
        Selection.Clear();
        return new ClickOutcome(ClickResult.Cleared, Vec2.Zero);
    }

    private Unit? NearestOwnAt(Vec2 point)
    {
        long radiusSq = (long)ClickRadiusCm * ClickRadiusCm;
        return OwnUnits
            .Where(u => !u.IsOutOfAction && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }


    public IReadOnlyList<SimEvent> TakeEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public void OrderFireAt(UnitId target)
    {
        foreach (var id in CommandedIds)
            Sim.Submit(PlayerSide, new FireAtOrder(id, target));
    }

    public void CycleFirePolicy()
    {
        var ids = CommandedIds;
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
            .Where(u => u.Side != PlayerSide && !u.IsOutOfAction && IsShownToPlayer(u, revealAll: false)
                        && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    public void OrderStance(Stance stance)
    {
        foreach (var id in CommandedIds)
            Sim.Submit(PlayerSide, new SetStanceOrder(id, stance));
    }

    public void OrderStop()
    {
        foreach (var id in CommandedIds)
            Sim.Submit(PlayerSide, new StopOrder(id));
    }

    private void SnapshotPositions()
    {
        foreach (var unit in Sim.Units)
            _previousPositions[unit.Id] = unit.Position;
    }

    private void RefreshFog()
    {
        // Only men still in action look; the fog recomputes just those who changed block or stance.
        Fog.Update(OwnUnits.Where(u => !u.IsOutOfAction).Select(u => (u.Id, u.Position, VisionRules.EyeHeightAbsCm(Sim.Map, u))));
    }
}
