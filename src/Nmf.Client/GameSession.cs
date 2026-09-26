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
    /// <summary>Up to ten steps a frame, so ×8 keeps up even at 20 frames a second.</summary>
    public FixedStepClock Clock { get; } = new(maxStepsPerFrame: 10);

    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 8;

    public void SetSpeed(double speed) => Clock.TimeScale = Math.Clamp(speed, MinSpeed, MaxSpeed);

    public void SpeedUp() => SetSpeed(Clock.TimeScale * 2);

    public void SlowDown() => SetSpeed(Clock.TimeScale / 2);
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
    /// Context click (spec 2026-09-24-grenades-melee-design §2). With nothing selected the whole platoon is commanded.
    /// Own soldier: command only him (Shift adds); double click: his whole squad.
    /// Seen enemy: fire at him; double click: attack him by fire and movement; Shift + double click: assault him straight. Seen unsearched body, double click: the nearest commanded man searches it.
    /// Where an enemy was last seen or heard ("?"), or any ground with <paramref name="area"/> (Ctrl/Cmd): area fire there.
    /// Ground: move there at their own pace; double click: run; Alt: crawl.
    /// </summary>
    /// <param name="radiusCm">How far from a man a click still picks him (grows when zoomed far out).</param>
    public ClickOutcome HandleLeftClick(Vec2 point, bool doubleClick, bool shift, bool alt, int radiusCm = ClickRadiusCm, bool area = false)
    {
        if (area)
            return Sim.Map.Contains(point) && CommandedIds.Count > 0 && OrderAreaFire(point) > 0
                ? new ClickOutcome(ClickResult.AreaFireOrdered, point)
                : new ClickOutcome(ClickResult.None, point);
        var own = NearestOwnAt(point, radiusCm);
        var enemy = EnemyAt(point, radiusCm);
        bool ownWins = own is not null && (enemy is null || (own.Position - point).LengthSquared <= (enemy.Position - point).LengthSquared);

        if (ownWins)
        {
            if (doubleClick)
            {
                SelectSquad(own!.Squad);
                return new ClickOutcome(ClickResult.SelectedSquad, own.Position);
            }
            Selection.SelectAt(Sim.Units.Where(u => !u.IsOutOfAction), PlayerSide, point, radiusCm, shift);
            return new ClickOutcome(ClickResult.Selected, own!.Position);
        }
        if (CommandedIds.Count == 0)
            return new ClickOutcome(ClickResult.None, point);
        // A body is searched on a double click right on it (like an attack): one click there is only a move, and far
        // out a wider reach would turn move clicks into searches.
        if (enemy is null && doubleClick && BodyAt(point, ClickRadiusCm) is { } body)
        {
            return OrderLoot(body)
                ? new ClickOutcome(ClickResult.LootOrdered, body.Position)
                : new ClickOutcome(ClickResult.None, body.Position);
        }
        if (enemy is not null)
        {
            if (doubleClick && shift)
            {
                OrderAssault(enemy.Id);
                return new ClickOutcome(ClickResult.AssaultOrdered, enemy.Position);
            }
            if (doubleClick)
            {
                OrderAttack(enemy.Id);
                return new ClickOutcome(ClickResult.AttackOrdered, enemy.Position);
            }
            OrderFireAt(enemy.Id);
            return new ClickOutcome(ClickResult.FireOrdered, enemy.Position);
        }
        if (!Sim.Map.Contains(point))
            return new ClickOutcome(ClickResult.None, point);
        if (!doubleClick && ContactMarkAt(point, radiusCm) is { } mark && OrderAreaFire(mark) > 0)
            return new ClickOutcome(ClickResult.AreaFireOrdered, mark);
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
        LastLooter = free[0].Id;
        return true;
    }

    /// <summary>The man the last loot click sent.</summary>
    public UnitId? LastLooter { get; private set; }

    /// <summary>Names of the papers own men still in action carry.</summary>
    public IReadOnlyList<string> CarriedPapers =>
        OwnUnits.Where(u => !u.IsOutOfAction).SelectMany(u => u.Items)
            .Select(i => Mission?.Items.TryGetValue(i.Id, out var name) == true ? name!.In(Language) : i.Name).Distinct().ToList();

    /// <summary>Attack by fire and movement: the men plan it themselves (spec 2026-09-26-attack-design).</summary>
    public void OrderAttack(UnitId target)
    {
        foreach (var id in CommandedIds)
            Sim.Submit(PlayerSide, new AttackOrder(id, target));
    }

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

    private Unit? NearestOwnAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
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

    /// <summary>The seen enemy under the cursor for the hover tip: one still fighting before a body.</summary>
    public Unit? InspectAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        return Sim.Units
            .Where(u => u.Side != PlayerSide && IsShownToPlayer(u, revealAll: false) && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => u.IsOutOfAction ? 1 : 0)
            .ThenBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>The "?" of an enemy last seen or heard under the pointer: where he was.</summary>
    public Vec2? ContactMarkAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)Math.Max(radiusCm, MarkRadiusCm) * Math.Max(radiusCm, MarkRadiusCm);
        return Knowledge.Contacts
            .Where(c => c.Level is ContactLevel.LastKnown or ContactLevel.Suspected && (c.Position - point).LengthSquared <= radiusSq)
            .OrderBy(c => (c.Position - point).LengthSquared)
            .ThenBy(c => c.Target.Value)
            .Select(c => (Vec2?)c.Position)
            .FirstOrDefault();
    }

    /// <summary>The "?" is drawn larger than a man.</summary>
    public const int MarkRadiusCm = 250;

    /// <summary>Area fire at a place by every commanded man with a spare magazine; how many were told.</summary>
    public int OrderAreaFire(Vec2 place)
    {
        int told = 0;
        foreach (var id in CommandedIds)
        {
            if (Sim.FindUnit(id) is not { Weapon: not null, Magazines: > 0 })
                continue;
            Sim.Submit(PlayerSide, new AreaFireOrder(id, place));
            told++;
        }
        return told;
    }

    /// <summary>Select a whole squad — or, with only one squad in the field, command the whole platoon.</summary>
    public void SelectSquad(int squad)
    {
        var men = OwnUnits.Where(u => !u.IsOutOfAction).ToList();
        Selection.Clear();
        if (men.All(u => u.Squad == squad))
            return;
        foreach (var man in men.Where(u => u.Squad == squad))
            Selection.Add(man.Id);
    }

    /// <summary>A squad's name from the mission, else "Squad 2".</summary>
    public string SquadName(int squad, string? language = null)
    {
        language ??= Language;
        return Mission?.PlayerSquadName(squad)?.In(language) ?? (language == "fi" ? $"Ryhmä {squad + 1}" : $"Squad {squad + 1}");
    }

    /// <summary>Who orders go to: the platoon, a squad by name, or the men.</summary>
    public string CommandingText(string language, Func<Unit, string>? name = null)
    {
        bool fi = language == "fi";
        if (IsSquadCommanded)
            return fi ? "joukkue" : "platoon";
        var ids = CommandedIds;
        var men = ids.Select(id => Sim.FindUnit(id)!).ToList();
        var squads = men.Select(m => m.Squad).Distinct().ToList();
        if (squads.Count == 1 && OwnUnits.Where(u => !u.IsOutOfAction && u.Squad == squads[0]).Count() == men.Count)
            return SquadName(squads[0], language);
        if (men.Count > 2)
            return fi ? $"{men.Count} miestä" : $"{men.Count} men";
        return string.Join(", ", men.Select(m => name?.Invoke(m) ?? m.Name ?? m.Id.ToString()));
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
