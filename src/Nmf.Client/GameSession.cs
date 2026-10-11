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
        Script = new MissionEvents(mission, scenario, Tracker, playerSide);
    }

    /// <summary>The mission's "when … then …" script (spec 2026-10-10-events-design); null outside a mission.</summary>
    public MissionEvents? Script { get; }

    private readonly List<MissionMessage> _messages = [];

    /// <summary>The mission's messages so far, oldest first (for the orders paper).</summary>
    public IReadOnlyList<MissionMessage> Messages => _messages;

    /// <summary>The men on the cards: the side's, but not one still waiting to be found.</summary>
    public IEnumerable<Unit> CardUnits => OwnUnits.Where(u => !u.Stranded);

    /// <summary>A stranded man of ours is made out this close by one of his side (until then he is not on the map).</summary>
    public const int StrandedSeenCm = 3_000;

    public MissionSpec? Mission { get; }

    /// <summary>A campaign mission's tally of what our men did (spec 2026-10-11-campaign-design §9a); null outside a campaign.</summary>
    public Campaign.CampaignTally? Tally { get; set; }
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
        revealAll || (unit.Side == PlayerSide ? !unit.Stranded || StrandedInSight(unit)
            : Knowledge.LevelOf(unit.Id) == ContactLevel.Visible && (!AnyStranded || Fog.IsVisible(unit.Position)));

    /// <summary>
    /// A man of ours still to be found: his side's knowledge includes what he sees (the fight is the same in a replay), so
    /// the player is shown an enemy only where the men he commands can see (the fog leaves the stranded man out).
    /// </summary>
    private bool AnyStranded => OwnUnits.Any(u => u.Stranded && u.IsAlive);

    /// <summary>One of the side close enough to make out a man waiting to be found.</summary>
    private bool StrandedInSight(Unit lost) =>
        OwnUnits.Any(u => !u.Stranded && !u.IsOutOfAction && (u.Position - lost.Position).LengthSquared <= (long)StrandedSeenCm * StrandedSeenCm);

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
        var stepped = Sim.Step();
        Tally?.Observe(Sim, stepped);
        _events.AddRange(stepped);
        if (Tracker is not null)
            _events.AddRange(Tracker.Update(Sim));
        if (Script is not null)
        {
            var scripted = Script.Tick(Sim);
            _events.AddRange(scripted);
            _messages.AddRange(scripted.OfType<MissionMessage>());
        }
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
        // A carried man is drawn on his carrier's shoulder, a little to the side, not hidden under him.
        double side = unit.CarriedBy is not null ? CarriedOffsetCm : 0;
        return (previous.X + (current.X - previous.X) * a + side, previous.Y + (current.Y - previous.Y) * a - side);
    }

    /// <summary>How far to the side a carried man is drawn from his carrier.</summary>
    public const int CarriedOffsetCm = 35;

    /// <summary>Squads moving together go side by side this far apart, each in its own formation (and by its own way).</summary>
    public const int SquadSpacingCm = 2000;

    /// <summary>
    /// Sends the selection toward a point in formation. Each man takes cover near his spot if there is any, and a spot
    /// that cannot be reached (a rock, a closed pocket) is replaced by the nearest one that can.
    /// </summary>
    public void OrderMove(Vec2 target, MoveMode mode)
    {
        var men = CommandedIds.Select(id => Sim.FindUnit(id)!).ToList();
        var squads = men.GroupBy(u => u.Squad).Select(g => g.ToList()).ToList();
        var taken = new HashSet<CellCoord>();
        // Only real squads go their own way: a straggler or two picked from another squad keep with the rest.
        if (squads.Count < 2 || squads.Any(s => s.Count < 2))
        {
            MoveGroup(men.Select(u => u.Id).ToList(), target, mode, taken);
            return;
        }
        // Side by side across the way they go, in the order they stand now so their ways do not cross.
        var centre = Centre(men);
        var ahead = target - centre;
        if (ahead.LengthSquared < 100L * 100)
            ahead = new Vec2(0, -1000); // clicked on themselves: side by side east–west
        long length = Math.Max(1, IntMath.Isqrt(ahead.LengthSquared));
        var across = new Vec2((int)(-ahead.Y * 1000 / length), (int)(ahead.X * 1000 / length)); // unit vector ×1000
        var ordered = squads.OrderBy(s => Centre(s).Dot(across)).ThenBy(s => s[0].Squad).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            long shift = (2L * i - (ordered.Count - 1)) * SquadSpacingCm / 2;
            var spot = target + new Vec2((int)(across.X * shift / 1000), (int)(across.Y * shift / 1000));
            if (!Sim.Map.Contains(spot) || !Sim.Map.CellAt(spot).IsPassable)
                spot = target;
            MoveGroup(ordered[i].Select(u => u.Id).ToList(), spot, mode, taken);
        }
    }

    private static Vec2 Centre(IReadOnlyCollection<Unit> men) =>
        new((int)(men.Sum(u => (long)u.Position.X) / men.Count), (int)(men.Sum(u => (long)u.Position.Y) / men.Count));

    private void MoveGroup(IReadOnlyList<UnitId> ids, Vec2 target, MoveMode mode, HashSet<CellCoord> taken)
    {
        var offsets = Formation.Offsets(ids.Count);
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
    /// Context click (spec 2026-10-10-controls-design §1). With nothing selected the whole platoon is commanded.
    /// Own soldier: command only him (Shift adds); double click: his whole squad.
    /// Seen enemy: fire at him; double click: attack his position (the suggested tactic); Alt + double: long fire;
    /// Ctrl + double: straight assault; Ctrl: area fire at him.
    /// A heard or last-seen mark ("?"): area fire there; double click: attack it.
    /// Seen unsearched body, double click: the nearest commanded man searches it.
    /// Ground: go there at their own pace; double click: run; Alt: sneak, Alt + double: crawl; Ctrl/Cmd (<paramref name="area"/>):
    /// area fire; <paramref name="attackForward"/> (A held): attack forward.
    /// </summary>
    /// <param name="radiusCm">How far from a man a click still picks him (grows when zoomed far out).</param>
    public ClickOutcome HandleLeftClick(Vec2 point, bool doubleClick, bool shift, bool alt, int radiusCm = ClickRadiusCm, bool area = false,
        bool attackForward = false)
    {
        var target = Resolve(point, radiusCm, doubleClick);
        switch (target.Kind)
        {
            case ClickTargetKind.Own:
                if (doubleClick)
                {
                    SelectSquad(target.Unit!.Squad);
                    return new ClickOutcome(ClickResult.SelectedSquad, target.Unit.Position);
                }
                Selection.SelectAt(Sim.Units.Where(u => !u.IsOutOfAction && !u.Stranded), PlayerSide, point, radiusCm, shift);
                return new ClickOutcome(ClickResult.Selected, target.Unit!.Position);
            case ClickTargetKind.None:
                return new ClickOutcome(ClickResult.None, point);
        }
        if (CommandedIds.Count == 0)
            return new ClickOutcome(ClickResult.None, point);
        // The attack is shown before it starts; nothing changes, the queue included, until the player starts it.
        if (!shift && doubleClick && PreviewOpensFor(alt, area) && PreviewTarget(target) is { } previewed && OpenPreview(previewed))
            return new ClickOutcome(ClickResult.AttackPreviewOpened, point);
        if (QueuedFor(target, point, doubleClick, alt, area, attackForward) is { } queued)
        {
            if (shift)
            {
                // Shift: after what they are doing now. A double click replaces what its own first click queued.
                var firstClick = QueuedFor(target, point, false, alt, area, attackForward);
                if (doubleClick && firstClick is not null && Queue.LastOf(CommandedIds) == firstClick)
                    Queue.ReplaceLast(this, CommandedIds, queued);
                else
                    Queue.Add(this, CommandedIds, queued);
                return new ClickOutcome(ClickResult.Queued, point);
            }
            Queue.ClearFor(CommandedIds); // an order of their own: any queue of theirs is over
        }
        switch (target.Kind)
        {
            case ClickTargetKind.Wounded:
                return OrderCarry(target.Unit!)
                    ? new ClickOutcome(ClickResult.CarryOrdered, target.Unit!.Position)
                    : new ClickOutcome(ClickResult.None, target.Unit!.Position);
            case ClickTargetKind.Body:
                return OrderLoot(target.Unit!)
                    ? new ClickOutcome(ClickResult.LootOrdered, target.Unit!.Position)
                    : new ClickOutcome(ClickResult.None, target.Unit!.Position);
            case ClickTargetKind.Enemy:
                var enemy = target.Unit!;
                if (doubleClick && area)
                {
                    OrderAssault(enemy.Id);
                    return new ClickOutcome(ClickResult.AssaultOrdered, enemy.Position);
                }
                if (area)
                    return OrderAreaFire(enemy.Position) > 0 ? new ClickOutcome(ClickResult.AreaFireOrdered, enemy.Position) : new ClickOutcome(ClickResult.None, point);
                if (doubleClick)
                {
                    OrderAttack(enemy.Id, alt ? LongFireId : null);
                    return new ClickOutcome(ClickResult.AttackOrdered, enemy.Position);
                }
                OrderFireAt(enemy.Id);
                return new ClickOutcome(ClickResult.FireOrdered, enemy.Position);
            case ClickTargetKind.Mark:
                var mark = target.Contact!;
                if (doubleClick && !area)
                {
                    OrderAttack(mark.Target, alt ? LongFireId : null);
                    return new ClickOutcome(ClickResult.AttackOrdered, mark.Position);
                }
                if (OrderAreaFire(mark.Position) > 0)
                    return new ClickOutcome(ClickResult.AreaFireOrdered, mark.Position);
                break; // nobody can fire there: a click on the mark is a move like any other
        }
        // Ground.
        if (area)
            return OrderAreaFire(point) > 0 ? new ClickOutcome(ClickResult.AreaFireOrdered, point) : new ClickOutcome(ClickResult.None, point);
        if (attackForward)
        {
            OrderAttackForward(point);
            return new ClickOutcome(ClickResult.AttackForwardOrdered, point);
        }
        OrderMove(point, alt ? (doubleClick ? MoveMode.Crawl : MoveMode.Sneak) : doubleClick ? MoveMode.Run : MoveMode.Auto);
        return new ClickOutcome(ClickResult.MoveOrdered, point);
    }

    /// <summary>Show the attack plan before it starts (setting "Näytä hyökkäyssuunnitelma ensin"); off: a double click attacks at once.</summary>
    public bool PreviewAttacks { get; set; }

    /// <summary>The attack being looked at before it starts; null when none (spec 2026-10-10-plan-preview-design).</summary>
    public AttackPreview? Preview { get; private set; }

    /// <summary>Whether a plain double click on an enemy or a mark opens the preview with these modifiers.</summary>
    public bool PreviewOpensFor(bool alt, bool area) => PreviewAttacks && Tactics.Count > 0 && !alt && !area;

    private static UnitId? PreviewTarget(ClickTarget target) => target.Kind switch
    {
        ClickTargetKind.Enemy => target.Unit!.Id,
        ClickTargetKind.Mark => target.Contact!.Target,
        _ => null,
    };

    /// <summary>Open the preview of an attack on the target by the commanded men; false when there is nothing to show.</summary>
    public bool OpenPreview(UnitId target)
    {
        Preview = AttackPreview.Open(this, target);
        return Preview is not null;
    }

    public void ClosePreview() => Preview = null;

    /// <summary>Start the attack the preview shows and close it; false when it could not start (the target is gone).</summary>
    public bool ConfirmPreview()
    {
        if (Preview is not { } preview)
            return false;
        Preview = null;
        return preview.Confirm(this);
    }

    /// <summary>Attack forward: go to the point, attacking every enemy seen on the way (the queue carries it out).</summary>
    public void OrderAttackForward(Vec2 point)
    {
        Queue.ClearFor(CommandedIds);
        Queue.Add(this, CommandedIds, new QueuedOrder(QueuedKind.AttackForward, point));
    }

    /// <summary>Orders queued with Shift, or given now (spec 2026-10-10-controls-design §4).</summary>
    public OrderQueue Queue { get; } = new();

    /// <summary>The order a click would give, as a queue item (null when the click gives none: a selection, nothing to do).</summary>
    public QueuedOrder? QueuedFor(ClickTarget target, Vec2 point, bool doubleClick, bool alt, bool area, bool attackForward) => target.Kind switch
    {
        ClickTargetKind.Body => new QueuedOrder(QueuedKind.Loot, target.Unit!.Position, target.Unit.Id),
        ClickTargetKind.Enemy when doubleClick && area => new QueuedOrder(QueuedKind.Assault, target.Unit!.Position, target.Unit.Id),
        ClickTargetKind.Enemy when area => new QueuedOrder(QueuedKind.AreaFire, target.Unit!.Position),
        ClickTargetKind.Enemy when doubleClick => new QueuedOrder(QueuedKind.Attack, target.Unit!.Position, target.Unit.Id, Tactic: alt ? LongFireId : null),
        ClickTargetKind.Enemy => new QueuedOrder(QueuedKind.FireAt, target.Unit!.Position, target.Unit.Id),
        ClickTargetKind.Mark when doubleClick && !area => new QueuedOrder(QueuedKind.Attack, target.Contact!.Position, target.Contact.Target, Tactic: alt ? LongFireId : null),
        ClickTargetKind.Mark => new QueuedOrder(QueuedKind.AreaFire, target.Contact!.Position),
        ClickTargetKind.Ground when area => new QueuedOrder(QueuedKind.AreaFire, point),
        ClickTargetKind.Ground when attackForward => new QueuedOrder(QueuedKind.AttackForward, point),
        ClickTargetKind.Ground => new QueuedOrder(QueuedKind.Move, point, Mode: alt ? (doubleClick ? MoveMode.Crawl : MoveMode.Sneak) : doubleClick ? MoveMode.Run : MoveMode.Auto),
        _ => null,
    };

    /// <summary>The long fire tactic's id (content/core/tactics).</summary>
    public const string LongFireId = "long_fire";

    /// <summary>
    /// What a click at the point is on, in the order a click picks (spec 2026-10-10-controls-design §2): an own man before
    /// an enemy when nearer, a body only on a double click right on it, a mark, else the ground (None off the map).
    /// </summary>
    public ClickTarget Resolve(Vec2 point, int radiusCm, bool doubleClick)
    {
        var own = NearestOwnAt(point, radiusCm);
        var enemy = EnemyAt(point, radiusCm);
        if (own is not null && (enemy is null || (own.Position - point).LengthSquared <= (enemy.Position - point).LengthSquared))
            return new ClickTarget(ClickTargetKind.Own, point, own, null);
        if (enemy is not null)
            return new ClickTarget(ClickTargetKind.Enemy, point, enemy, null);
        // A body is searched on a double click right on it (like an attack): one click there is only a move, and far
        // out a wider reach would turn move clicks into searches.
        // One of ours down but alive is carried, not searched.
        if (doubleClick && WoundedAt(point, ClickRadiusCm) is { } wounded)
            return new ClickTarget(ClickTargetKind.Wounded, point, wounded, null);
        if (doubleClick && BodyAt(point, ClickRadiusCm) is { } body)
            return new ClickTarget(ClickTargetKind.Body, point, body, null);
        if (!Sim.Map.Contains(point))
            return new ClickTarget(ClickTargetKind.None, point, null, null);
        if (ContactAt(point, radiusCm) is { } mark)
            return new ClickTarget(ClickTargetKind.Mark, point, null, mark);
        return new ClickTarget(ClickTargetKind.Ground, point, null, null);
    }

    /// <summary>
    /// Take up a position facing the point: each man to the nearest cover against it (or stays), at his own pace, and
    /// ready to fire (spec 2026-10-10-controls-design §3).
    /// </summary>
    public void OrderTakePosition(Vec2 facing)
    {
        foreach (var id in CommandedIds)
        {
            if (Sim.FindUnit(id) is not { } man)
                continue;
            if (CoverFinder.Find(Sim, man, facing) is { } cover && cover.ToCell() != man.Position.ToCell())
                Sim.Submit(PlayerSide, new MoveOrder(id, cover, MoveMode.Auto));
        }
    }

    /// <summary>The soldiers orders go to: the selection, or with nothing selected every own man still in action.</summary>
    /// <summary>The selected men still in action; the whole squad when there are none.</summary>
    public IReadOnlyList<UnitId> CommandedIds
    {
        get
        {
            if (_orderMen is { } men)
                return InAction(men.Select(Sim.FindUnit));
            var selected = InAction(Selection.Ids.Select(Sim.FindUnit));
            return selected.Count > 0 ? selected : InAction(OwnUnits);
        }
    }

    private IReadOnlyList<UnitId>? _orderMen;

    /// <summary>Gives an order to these men instead of the selection (a queued order goes to the men it was queued for).</summary>
    public void IssueAs(IReadOnlyList<UnitId> men, Action order)
    {
        var before = _orderMen;
        _orderMen = men;
        try
        {
            order();
        }
        finally
        {
            _orderMen = before;
        }
    }

    public bool IsSquadCommanded => InAction(Selection.Ids.Select(Sim.FindUnit)).Count == 0;

    private static List<UnitId> InAction(IEnumerable<Unit?> units) =>
        units.Where(u => u is { IsOutOfAction: false, Stranded: false }).Select(u => u!.Id).ToList();

    /// <summary>A fallen man (either side) the player can see and nobody has searched yet.</summary>
    /// <summary>One of ours who is down but alive (he can be carried), shown to the player, near the point.</summary>
    public Unit? WoundedAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        return OwnUnits
            .Where(u => u.IsOutOfAction && u.IsAlive && IsShownToPlayer(u, revealAll: false) && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// The nearest commanded man with free hands goes to carry him; if he is carried already (or someone is on his way),
    /// the nearest other one goes to help (spec 2026-10-10-carrying-design §2–§3). False when nobody can.
    /// </summary>
    public bool OrderCarry(Unit wounded)
    {
        var busy = Sim.Units.Where(u => u.CarryTarget == wounded.Id || u.Carrying == wounded.Id || u.HelpingCarry == wounded.Id).Select(u => u.Id).ToHashSet();
        if (busy.Count >= 2)
            return false;
        var free = CommandedIds.Select(id => Sim.FindUnit(id)!)
            .Where(u => u != wounded && !busy.Contains(u.Id) && u.Carrying is null && u.HelpingCarry is null && u.MoraleState != Nmf.Sim.Combat.MoraleState.Pinned)
            .OrderBy(u => (u.Position - wounded.Position).LengthSquared).ThenBy(u => u.Id.Value)
            .FirstOrDefault();
        if (free is null)
            return false;
        Queue.ClearFor([free.Id]);
        Sim.Submit(PlayerSide, new CarryOrder(free.Id, wounded.Id));
        return true;
    }

    /// <summary>The player's mortar by radio (spec 2026-10-10-mortar-design); null when the mission gives none.</summary>
    public Nmf.Sim.Combat.MortarSupport? Mortar => Sim.MortarSupports.FirstOrDefault(m => m.Side == PlayerSide);

    /// <summary>A new fire mission can be called now: there is a mortar, it has rounds, the radioman can, it is not firing.</summary>
    public bool CanCallMortar => Mortar is { } m && m.Available(Sim) && m.Phase is not (Nmf.Sim.Combat.MortarPhase.Ranging or Nmf.Sim.Combat.MortarPhase.Effect);

    /// <summary>The radioman calls the mortar onto the point, whoever is selected. False when it cannot be called.</summary>
    public bool OrderFireMission(Vec2 point, int rounds = 4)
    {
        if (!CanCallMortar || !Sim.Map.Contains(point))
            return false;
        Sim.Submit(PlayerSide, new FireMissionOrder(Mortar!.Radio, point, rounds));
        return true;
    }

    /// <summary>The enemy's telephone wire, whole, within reach of a cut order at <paramref name="point"/>; null: none.</summary>
    public FieldPhone? WireAt(Vec2 point) =>
        Sim.FieldPhones.Where(p => p.Side != PlayerSide && p.CutAt is null && p.Nearest(point).DistanceCm <= WireReachCm)
            .OrderBy(p => p.Nearest(point).DistanceCm).FirstOrDefault();

    /// <summary>A cut order must point this near the wire (the sim's own reach).</summary>
    public const int WireReachCm = 500;

    /// <summary>The commanded man nearest the wire goes to cut it (spec 2026-10-10-alertness-design §4).</summary>
    public bool OrderCutLine(Vec2 point)
    {
        if (WireAt(point) is not { } phone)
            return false;
        var man = CommandedIds.Select(Sim.FindUnit).OfType<Unit>().Where(u => u.MoraleState == MoraleState.Steady)
            .OrderBy(u => (u.Position - point).LengthSquared).ThenBy(u => u.Id.Value).FirstOrDefault();
        if (man is null)
            return false;
        Queue.ClearFor([man.Id]); // an order of his own: his queue is over (it would walk him off the wire)
        Sim.Submit(PlayerSide, new CutLineOrder(man.Id, phone.Id, point));
        return true;
    }

    /// <summary>The "?" or "!" over an enemy the player sees (spec 2026-10-10-alertness-design §7).</summary>
    public string? AlertMark(Unit unit, bool revealAll) =>
        unit.Side != PlayerSide && IsShownToPlayer(unit, revealAll) ? AlertnessText.Mark(Sim, unit) : null;

    /// <summary>The commanded men who carry someone lay him down.</summary>
    public void OrderDrop()
    {
        foreach (var id in CommandedIds.Concat(Selection.Ids).Distinct())
            if (Sim.FindUnit(id) is { } man && (man.Carrying is not null || man.HelpingCarry is not null))
                Sim.Submit(PlayerSide, new DropOrder(id));
    }

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

    /// <summary>The ways of attacking the men know (content/core/tactics); none = the old fire and movement order.</summary>
    public IReadOnlyList<Nmf.Sim.Tactics.TacticDef> Tactics { get; set; } = [];

    /// <summary>The plan the last attack order made, for the player to read; null when none was made.</summary>
    public Nmf.Sim.Tactics.TacticPlan? LastPlan { get; private set; }

    /// <summary>The last attack order could not be planned, and the men fire instead.</summary>
    public bool LastPlanFailed { get; private set; }

    /// <summary>
    /// Attack the enemy's position: the men work out the plan themselves, from what they know (spec
    /// 2026-10-10-tactics-design). Without tactics nothing can be planned: they fire at him.
    /// </summary>
    public void OrderAttack(UnitId target, string? tacticId = null)
    {
        LastPlan = null;
        LastPlanFailed = false;
        if (Tactics.Count == 0)
        {
            LastPlanFailed = true;
            OrderFireAt(target);
            return;
        }
        var forced = tacticId is null ? null : Tactics.FirstOrDefault(t => t.Id == tacticId);
        var plan = forced is not null
            ? Nmf.Sim.Tactics.TacticPlanner.Propose(Sim, PlayerSide, CommandedIds.ToList(), target, forced)
            : Nmf.Sim.Tactics.TacticPlanner.Suggest(Sim, PlayerSide, CommandedIds.ToList(), target, Tactics);
        if (plan is null)
        {
            LastPlanFailed = true;
            OrderFireAt(target);
            return;
        }
        IssuePlan(plan, Tactics.First(t => t.Id == plan.TacticId));
    }

    /// <summary>An order without a plan was given: there is no plan to tell of.</summary>
    public void ForgetPlan()
    {
        LastPlan = null;
        LastPlanFailed = false;
    }

    /// <summary>Start a planned attack (the one the preview shows, or the one just worked out).</summary>
    public void IssuePlan(Nmf.Sim.Tactics.TacticPlan plan, Nmf.Sim.Tactics.TacticDef tactic)
    {
        LastPlan = plan;
        LastPlanFailed = false;
        Sim.Submit(PlayerSide, new PlannedAttackOrder(plan.FirstMan, plan, tactic));
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
            .Where(u => !u.IsOutOfAction && !u.Stranded && (u.Position - point).LengthSquared <= radiusSq)
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
    public Vec2? ContactMarkAt(Vec2 point, int radiusCm) => ContactAt(point, radiusCm)?.Position;

    /// <summary>The heard ("?") or last-seen mark under the point, nearest first.</summary>
    public Contact? ContactAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)MarkRadiusCm * MarkRadiusCm; // the "?" is drawn at a fixed size on the ground
        return Knowledge.Contacts
            .Where(c => c.Level is ContactLevel.LastKnown or ContactLevel.Suspected && (c.Position - point).LengthSquared <= radiusSq)
            .OrderBy(c => (c.Position - point).LengthSquared)
            .ThenBy(c => c.Target.Value)
            .FirstOrDefault();
    }

    /// <summary>The "?" is drawn larger than a man.</summary>
    public const int MarkRadiusCm = 250;

    /// <summary>Area fire at a place by every commanded man with a spare magazine and the range; how many were told.</summary>
    public int OrderAreaFire(Vec2 place)
    {
        var men = AreaFirers(place);
        foreach (var id in men)
            Sim.Submit(PlayerSide, new AreaFireOrder(id, place));
        return men.Count;
    }

    /// <summary>The commanded men who could area-fire at the place: a spare magazine and the range.</summary>
    public IReadOnlyList<UnitId> AreaFirers(Vec2 place) =>
        !Sim.Map.Contains(place) ? [] : CommandedIds.Where(id => Sim.FindUnit(id) is { Weapon: { } weapon, Magazines: > 0 } man
                                                              && (man.Position - place).LengthSquared <= (long)weapon.RangeCm * weapon.RangeCm).ToList();

    /// <summary>Select a whole squad — or, with only one squad in the field, command the whole platoon.</summary>
    public void SelectSquad(int squad)
    {
        var men = CardUnits.Where(u => !u.IsOutOfAction).ToList();
        if (!men.Any(u => u.Squad == squad))
            return; // nobody of his squad left: keep what is selected
        Selection.Clear();
        if (men.All(u => u.Squad == squad))
            return;
        foreach (var man in men.Where(u => u.Squad == squad))
            Selection.Add(man.Id);
    }

    /// <summary>The squad selected as a whole (its men still in action, no one else), if any.</summary>
    public int? SelectedSquad
    {
        get
        {
            if (IsSquadCommanded)
                return null;
            var men = CommandedIds.Select(id => Sim.FindUnit(id)!).ToList();
            int squad = men[0].Squad;
            return men.All(m => m.Squad == squad) && CardUnits.Count(u => !u.IsOutOfAction && u.Squad == squad) == men.Count ? squad : null;
        }
    }

    /// <summary>How many squads the player has (for the number keys).</summary>
    public int SquadCount => CardUnits.Select(u => u.Squad).DefaultIfEmpty(0).Max() + 1;

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
        if (squads.Count == 1 && CardUnits.Where(u => !u.IsOutOfAction && u.Squad == squads[0]).Count() == men.Count)
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
        Queue.ClearFor(CommandedIds); // a halt is a halt: nothing queued carries on
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
        Fog.Update(OwnUnits.Where(u => !u.IsOutOfAction && !u.Stranded).Select(u => (u.Id, u.Position, VisionRules.EyeHeightAbsCm(Sim.Map, u))));
        if (Mission is { } mission)
            RefreshSeenProps(mission);
    }

    private readonly SortedSet<int> _seenProps = [];

    /// <summary>The mission's props our men have seen: a wreck is on the map once found by eye, and stays there.</summary>
    public IReadOnlyList<PropSpec> ShownProps()
    {
        if (Mission is not { } mission)
            return [];
        RefreshSeenProps(mission);
        return _seenProps.Select(i => mission.Props[i]).ToList();
    }

    private void RefreshSeenProps(MissionSpec mission)
    {
        for (int i = 0; i < mission.Props.Count; i++)
            if (Fog.IsVisible(mission.Props[i].At))
                _seenProps.Add(i);
    }
}
