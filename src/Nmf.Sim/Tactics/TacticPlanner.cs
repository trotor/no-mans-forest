using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tactics;

/// <summary>
/// Works out an attack on a position from what the side knows (spec 2026-10-10-tactics-design, "Suunnittelija"): the fire
/// base, the strike team's form-up off the line of fire, and its way there round the ground the enemy sees. Reads the
/// simulation, never changes it; integers only.
/// </summary>
public static class TacticPlanner
{
    /// <summary>The known enemies this close to the target make up the position attacked.</summary>
    public const int PositionRadiusCm = 3_000;

    private const int MinSightClarity = 60;
    private const int AimHeightCm = 60, PitAimAboveRimCm = 30, KneelEyeCm = 100;
    private const int SideCos70 = 342;            // cos 70° × 1000: how far to the side of our approach a fire base may lie
    private const int FireBaseSpreadCm = 1_200, SpotGapCm = 300;
    private const int ClosestFireBaseCm = 2_500;
    private const int ExposurePenalty = 60;
    private const int RouteMarginCm = 4_000;
    private const int ExposureStepCost = 300, FireLaneCm = 800, FireLaneStepCost = 2_000;
    private const int RouteLegCm = 1_000;
    private const int FormUpSpreadCm = 600;
    private const int MinMenForManeuver = 4;
    private const int FurthestFormUpCm = 6_000, FormUpStepOutCm = 500;

    /// <summary>cos(d°) × 1000 for d = 0..180.</summary>
    private static readonly int[] Cos = Enumerable.Range(0, 181).Select(d => (int)Math.Round(Math.Cos(d * Math.PI / 180) * 1000)).ToArray();

    /// <summary>Where the side believes the target is: where he is when seen, else where the contact puts him; null when unknown.</summary>
    public static Vec2? AimOf(Simulation sim, Side side, UnitId target)
    {
        var contact = sim.Knowledge(side).Get(target);
        return contact?.Level switch
        {
            ContactLevel.Visible => sim.FindUnit(target)?.Position,
            ContactLevel.LastKnown or ContactLevel.Suspected => contact.Position,
            _ => null,
        };
    }

    /// <summary>The known enemies (seen, lost or heard, still fighting) within <see cref="PositionRadiusCm"/> of a point, in id order.</summary>
    public static IReadOnlyList<UnitId> PositionOf(Simulation sim, Side side, Vec2 centre)
    {
        long radiusSq = (long)PositionRadiusCm * PositionRadiusCm;
        return sim.Knowledge(side).Contacts
            .Where(c => c.Level != ContactLevel.Unknown && sim.FindUnit(c.Target) is { IsOutOfAction: false })
            .Where(c => (KnownPlace(sim, c) - centre).LengthSquared <= radiusSq)
            .Select(c => c.Target)
            .OrderBy(id => id.Value)
            .ToList();
    }

    /// <summary>Where the side believes this enemy is.</summary>
    public static Vec2 KnownPlace(Simulation sim, Contact c) =>
        c.Level == ContactLevel.Visible && sim.FindUnit(c.Target) is { } u ? u.Position : c.Position;

    /// <summary>
    /// The plan for the first tactic that suits: one with a strike team is passed over when the side knows of as many
    /// enemies as it has men, or when its fire base would see nothing, unless no other tactic gives a plan.
    /// </summary>
    public static TacticPlan? Suggest(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, IReadOnlyList<TacticDef> tactics)
    {
        int fit = men.Count(id => sim.FindUnit(id) is { IsOutOfAction: false });
        int known = sim.Knowledge(side).Contacts.Count(c => c.Level != ContactLevel.Unknown && sim.FindUnit(c.Target) is { IsOutOfAction: false });
        TacticPlan? fallback = null;
        foreach (var tactic in tactics)
        {
            if (Propose(sim, side, men, target, tactic) is not { } plan)
                continue;
            bool risky = tactic.Maneuver is not null && (known >= fit || plan.FireBaseBlind);
            if (!risky)
                return plan;
            fallback ??= plan;
        }
        return fallback;
    }

    public static TacticPlan? Propose(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, TacticDef tactic)
    {
        if (AimOf(sim, side, target) is not { } aim)
            return null;
        var group = men.Select(sim.FindUnit).Where(u => u is { IsOutOfAction: false } && u.Side == side).Cast<Unit>().OrderBy(u => u.Id.Value).ToList();
        if (group.Count == 0 || (tactic.Maneuver is not null && group.Count < MinMenForManeuver))
            return null;
        var (fireTeam, strikeTeam) = Teams(group, tactic);
        if (fireTeam.Count == 0 || (tactic.Maneuver is not null && strikeTeam.Count == 0))
            return null;

        var map = sim.Map;
        var position = PositionOf(sim, side, aim);
        var groupCentre = Centre(group.Select(u => u.Position));
        int windowCm = (int)Math.Max(tactic.FireBaseMaxCm, IntMath.Isqrt((groupCentre - aim).LengthSquared)) + 4_000;
        var threat = ThreatMap.Build(sim, side, aim, windowCm, possibleShootersNear: aim);
        int aimHeight = map.CellAt(aim).GroundHeightCm + AimHeightCm;
        if (CoverFinder.InPit(map, aim))
            aimHeight = map.CellAt(aim).GroundHeightCm + CoverFinder.RimAboveCm(map, aim.ToCell()) + PitAimAboveRimCm;
        long rangeCm = fireTeam.Min(u => (long)(u.Weapon?.RangeCm ?? 30_000));

        // The fire base: as far off as the band allows while still seeing the position, in cover, not under other eyes.
        var fireCentre = Centre(fireTeam.Select(u => u.Position));
        var candidates = FireBaseCandidates(sim, fireTeam[0], aim, aimHeight, groupCentre, fireCentre, tactic.FireBaseMinCm, tactic.FireBaseMaxCm, rangeCm, threat);
        if (candidates.Count == 0)
            candidates = FireBaseCandidates(sim, fireTeam[0], aim, aimHeight, groupCentre, fireCentre, ClosestFireBaseCm, tactic.FireBaseMinCm - 1, rangeCm, threat);
        bool blind = candidates.Count == 0;
        List<FirePosition> fireSpots;
        int score;
        if (blind)
        {
            fireSpots = fireTeam.Select(u => new FirePosition(u.Id, CoverFinder.Find(sim, u, aim) ?? u.Position)).ToList();
            score = 0;
        }
        else
        {
            var anchor = candidates[0];
            score = anchor.Score;
            var taken = new List<Vec2>();
            fireSpots = [];
            foreach (var man in fireTeam)
            {
                var spot = taken.Count == 0 ? anchor.At : candidates
                    .Where(c => (c.At - anchor.At).LengthSquared <= (long)FireBaseSpreadCm * FireBaseSpreadCm
                                && taken.All(t => (t - c.At).LengthSquared >= (long)SpotGapCm * SpotGapCm))
                    .Select(c => (Vec2?)c.At)
                    .FirstOrDefault() ?? anchor.At;
                taken.Add(spot);
                fireSpots.Add(new FirePosition(man.Id, spot));
            }
        }
        var fireBase = Centre(fireSpots.Select(p => p.At));
        int fireBaseM = (int)(IntMath.Isqrt((fireBase - aim).LengthSquared) / 100);

        if (tactic.Maneuver is not { } maneuver)
            return new TacticPlan(tactic.Id, target, aim, position, fireSpots, [], [], [], blind, score, new PlanReason(fireBaseM, 0, blind));

        // The form-up: off the line of fire by the flank angle, in cover from the position, out of sight, near the strike team.
        var strikeCentre = Centre(strikeTeam.Select(u => u.Position));
        var start = strikeTeam.OrderBy(u => (u.Position - strikeCentre).LengthSquared).ThenBy(u => u.Id.Value).First().Position;
        // Hidden from the position (and from the foxholes round it): further out if the band has no such spot.
        var formUps = new List<Candidate>();
        for (int maxCm = maneuver.FormUpMaxCm; formUps.Count == 0 && maxCm <= Math.Max(maneuver.FormUpMaxCm, FurthestFormUpCm); maxCm += FormUpStepOutCm)
            formUps = FormUpCandidates(sim, strikeTeam[0], aim, fireBase, strikeCentre, maneuver, maxCm, threat);
        if (formUps.Count == 0)
            return null;
        int ExtraCost(int x, int y)
        {
            var c = new CellCoord(x, y).CenterCm;
            return ExposureStepCost * threat.ExposureAt(c) + (InFireLane(fireBase, aim, c) ? FireLaneStepCost : 0);
        }
        // One cost flood from the strike team over the ground round the position gives the way to every form-up at once.
        var flood = CostFlood.Run(map, start, aim, Math.Max(maneuver.FormUpMaxCm, FurthestFormUpCm) + RouteMarginCm, ExtraCost);
        (Candidate Spot, int Score)? best = null;
        foreach (var candidate in formUps)
        {
            if (flood.ExtraTo(candidate.At) is not { } extra)
                continue;
            int total = candidate.Score - (int)(extra / 100);
            if (best is null || total > best.Value.Score)
                best = (candidate, total);
        }
        if (best is not { } chosen)
            return null;
        var path = flood.PathTo(chosen.Spot.At);

        var formUp = new List<FirePosition>();
        var used = new List<Vec2>();
        foreach (var man in strikeTeam)
        {
            var spot = used.Count == 0 ? chosen.Spot.At : formUps
                .Where(c => (c.At - chosen.Spot.At).LengthSquared <= (long)FormUpSpreadCm * FormUpSpreadCm
                            && used.All(t => (t - c.At).LengthSquared >= (long)SpotGapCm * SpotGapCm))
                .OrderBy(c => (c.At - chosen.Spot.At).LengthSquared).ThenBy(c => c.At.Y).ThenBy(c => c.At.X)
                .Select(c => (Vec2?)c.At)
                .FirstOrDefault() ?? chosen.Spot.At;
            used.Add(spot);
            formUp.Add(new FirePosition(man.Id, spot));
        }
        var u = fireBase - aim;
        var v = chosen.Spot.At - aim;
        int side2 = Math.Sign((long)u.X * v.Y - (long)u.Y * v.X);
        return new TacticPlan(tactic.Id, target, aim, position, fireSpots, strikeTeam.Select(m => m.Id).ToList(), formUp,
            Legs(start, path), blind, score + chosen.Score, new PlanReason(fireBaseM, side2, blind));
    }

    private readonly record struct Candidate(Vec2 At, int Score);

    /// <summary>
    /// The fire team and the strike team. Two squads or more: the squad with the machine gun gives the fire, the rest go round.
    /// One squad: the machine gunner and the best shots, half the men, give the fire; submachine gunners go in. Without a
    /// maneuver everyone is the fire team.
    /// </summary>
    private static (List<Unit> Fire, List<Unit> Strike) Teams(List<Unit> men, TacticDef tactic)
    {
        if (tactic.Maneuver is null)
            return (men, []);
        var squads = men.Select(m => m.Squad).Distinct().Order().ToList();
        if (squads.Count >= 2)
        {
            int fireSquad = men.Where(m => m.Weapon?.Class == WeaponClass.Lmg).Select(m => m.Squad).DefaultIfEmpty(squads[^1]).Min();
            return (men.Where(m => m.Squad == fireSquad).ToList(), men.Where(m => m.Squad != fireSquad).ToList());
        }
        int size = (men.Count + 1) / 2;
        var fire = men
            .OrderBy(m => m.Weapon?.Class switch { WeaponClass.Lmg => 0, WeaponClass.Smg => 2, _ => 1 })
            .ThenByDescending(m => m.Marksmanship)
            .ThenBy(m => m.Id.Value)
            .Take(size)
            .OrderBy(m => m.Id.Value)
            .ToList();
        return (fire, men.Where(m => !fire.Contains(m)).ToList());
    }

    /// <summary>Fire-base cells within the band, on our side, that see the position; best first.</summary>
    private static List<Candidate> FireBaseCandidates(Simulation sim, Unit first, Vec2 aim, int aimHeight, Vec2 groupCentre, Vec2 fireCentre,
        int minCm, int maxCm, long rangeCm, ThreatMap threat)
    {
        var map = sim.Map;
        var result = new List<(Candidate C, long Dist)>();
        if (maxCm < minCm)
            return [];
        var reachable = Pathfinder.ReachableFrom(map, first.Position);
        var toMen = groupCentre - aim;
        long toMenLength = IntMath.Isqrt(toMen.LengthSquared);
        var centre = aim.ToCell();
        int r = maxCm / SimConstants.CentimetersPerCell + 1;
        for (int y = centre.Y - r; y <= centre.Y + r; y++)
        {
            for (int x = centre.X - r; x <= centre.X + r; x++)
            {
                var cell = new CellCoord(x, y);
                if (!map.InBounds(cell) || !map[cell].IsPassable)
                    continue;
                var at = cell.CenterCm;
                var a = at - aim;
                long dist = IntMath.Isqrt(a.LengthSquared);
                if (dist < minCm || dist > maxCm || dist > rangeCm)
                    continue;
                if (toMenLength > 0 && a.Dot(toMen) * 1000 < dist * toMenLength * SideCos70)
                    continue;
                int clarity = LineOfSight.Clarity(map, at, map[cell].GroundHeightCm + KneelEyeCm, aim, aimHeight);
                if (clarity < MinSightClarity || !reachable(at))
                    continue;
                int cover = CoverFinder.CoveredAt(map, cell, aim);
                int exposure = threat.ExposureAt(at, aim, PositionRadiusCm);
                long travelM = IntMath.Isqrt((at - fireCentre).LengthSquared) / 100;
                int score = clarity + cover + (int)(2 * dist / 100) - ExposurePenalty * exposure - (int)(travelM / 2);
                result.Add((new Candidate(at, score), dist));
            }
        }
        return result.OrderByDescending(c => c.C.Score).ThenByDescending(c => c.Dist).ThenBy(c => c.C.At.Y).ThenBy(c => c.C.At.X)
            .Select(c => c.C).ToList();
    }

    /// <summary>Form-up cells within the band, at least the flank angle off the line of fire; best first (before routes).</summary>
    private static List<Candidate> FormUpCandidates(Simulation sim, Unit first, Vec2 aim, Vec2 fireBase, Vec2 strikeCentre, TacticManeuver maneuver,
        int maxCm, ThreatMap threat)
    {
        var map = sim.Map;
        var fireLine = fireBase - aim;
        long fireLineLength = Math.Max(1, IntMath.Isqrt(fireLine.LengthSquared));
        int cos = Cos[Math.Clamp(maneuver.FlankDeg, 0, 180)];
        var centre = aim.ToCell();
        int r = maxCm / SimConstants.CentimetersPerCell + 1;
        var result = new List<Candidate>();
        var reachable = Pathfinder.ReachableFrom(map, first.Position);
        for (int y = centre.Y - r; y <= centre.Y + r; y++)
        {
            for (int x = centre.X - r; x <= centre.X + r; x++)
            {
                var cell = new CellCoord(x, y);
                if (!map.InBounds(cell) || !map[cell].IsPassable)
                    continue;
                var at = cell.CenterCm;
                var v = at - aim;
                long dist = IntMath.Isqrt(v.LengthSquared);
                if (dist < maneuver.FormUpMinCm || dist > maxCm)
                    continue;
                if (fireLine.Dot(v) * 1000 > fireLineLength * dist * cos)
                    continue;
                if (!reachable(at))
                    continue;
                if (threat.ExposureAt(at) > 0)
                    continue; // the form-up is where nobody of the position can see the strike team gather
                int cover = CoverFinder.CoveredAt(map, cell, aim);
                long straightM = IntMath.Isqrt((at - strikeCentre).LengthSquared) / 100;
                result.Add(new Candidate(at, cover - (int)(straightM / 2)));
            }
        }
        return result.OrderByDescending(c => c.Score).ThenBy(c => c.At.Y).ThenBy(c => c.At.X).ToList();
    }

    /// <summary>Between the fire base and the position and within <see cref="FireLaneCm"/> of the line: our own fire's way.</summary>
    private static bool InFireLane(Vec2 fireBase, Vec2 aim, Vec2 p)
    {
        var d = aim - fireBase;
        long lengthSq = Math.Max(1, d.LengthSquared);
        long along = (p - fireBase).Dot(d);
        if (along <= 0 || along >= lengthSq)
            return false;
        var rel = p - fireBase;
        long cross = Math.Abs((long)rel.X * d.Y - (long)rel.Y * d.X);
        return cross <= FireLaneCm * IntMath.Isqrt(lengthSq);
    }

    /// <summary>The path as points at most <see cref="RouteLegCm"/> apart, ending at its goal.</summary>
    private static List<Vec2> Legs(Vec2 start, List<Vec2> path)
    {
        var legs = new List<Vec2>();
        var at = start;
        foreach (var next in path)
        {
            var d = next - at;
            long length = IntMath.Isqrt(d.LengthSquared);
            for (long walked = RouteLegCm; walked < length; walked += RouteLegCm)
                legs.Add(at + new Vec2((int)(d.X * walked / length), (int)(d.Y * walked / length)));
            legs.Add(next);
            at = next;
        }
        // Waypoints closer together than a leg are thinned out, keeping the last.
        var thinned = new List<Vec2>();
        var last = start;
        for (int i = 0; i < legs.Count; i++)
        {
            bool final = i == legs.Count - 1;
            bool nextTooFar = !final && (legs[i + 1] - last).LengthSquared > (long)RouteLegCm * RouteLegCm;
            if (final || nextTooFar)
            {
                thinned.Add(legs[i]);
                last = legs[i];
            }
        }
        return thinned;
    }

    private static Vec2 Centre(IEnumerable<Vec2> points)
    {
        long x = 0, y = 0;
        int n = 0;
        foreach (var p in points)
        {
            x += p.X;
            y += p.Y;
            n++;
        }
        return n == 0 ? Vec2.Zero : new Vec2((int)(x / n), (int)(y / n));
    }

    /// <summary>Dijkstra from one cell over a window: the cheapest way, with extra costs, to every cell in it.</summary>
    private sealed class CostFlood
    {
        private const int Straight = 100, Diagonal = 141;
        private static readonly (int Dx, int Dy)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

        private int _x0, _y0, _w, _h;
        private long[] _cost = [];
        private long[] _extra = [];
        private int[] _parent = [];
        private CellCoord _start;

        /// <summary>The window holds the start and everything within <paramref name="aroundCm"/> of <paramref name="centre"/>.</summary>
        public static CostFlood Run(GridMap map, Vec2 start, Vec2 centre, int aroundCm, Func<int, int, int> extraCost)
        {
            var s = start.ToCell();
            var c = centre.ToCell();
            int r = aroundCm / SimConstants.CentimetersPerCell;
            int x0 = Math.Max(0, Math.Min(s.X, c.X - r)), y0 = Math.Max(0, Math.Min(s.Y, c.Y - r));
            int x1 = Math.Min(map.Width - 1, Math.Max(s.X, c.X + r)), y1 = Math.Min(map.Height - 1, Math.Max(s.Y, c.Y + r));
            var flood = new CostFlood { _x0 = x0, _y0 = y0, _w = x1 - x0 + 1, _h = y1 - y0 + 1, _start = s };
            int n = flood._w * flood._h;
            flood._cost = new long[n];
            flood._extra = new long[n];
            flood._parent = new int[n];
            Array.Fill(flood._cost, long.MaxValue);
            Array.Fill(flood._parent, -1);
            var cells = map.Cells;
            var open = new PriorityQueue<int, (long Cost, int Index)>();
            int startIndex = flood.Index(s.X, s.Y);
            flood._cost[startIndex] = 0;
            open.Enqueue(startIndex, (0, startIndex));
            while (open.TryDequeue(out int current, out var key))
            {
                if (key.Cost != flood._cost[current])
                    continue;
                int cx = current % flood._w + x0, cy = current / flood._w + y0;
                foreach (var (dx, dy) in Steps)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < x0 || ny < y0 || nx > x1 || ny > y1)
                        continue;
                    ref readonly var next = ref cells[ny * map.Width + nx];
                    if (!next.IsPassable)
                        continue;
                    bool diagonal = dx != 0 && dy != 0;
                    if (diagonal && (!cells[cy * map.Width + nx].IsPassable || !cells[ny * map.Width + cx].IsPassable))
                        continue;
                    int extra = extraCost(nx, ny);
                    long cost = key.Cost + (diagonal ? Diagonal : Straight) * next.MoveCostPct / 100 + extra;
                    int ni = flood.Index(nx, ny);
                    if (cost >= flood._cost[ni])
                        continue;
                    flood._cost[ni] = cost;
                    flood._extra[ni] = flood._extra[current] + extra;
                    flood._parent[ni] = current;
                    open.Enqueue(ni, (cost, ni));
                }
            }
            return flood;
        }

        private int Index(int x, int y) => (y - _y0) * _w + (x - _x0);

        private bool Inside(CellCoord c) => c.X >= _x0 && c.Y >= _y0 && c.X < _x0 + _w && c.Y < _y0 + _h;

        /// <summary>The extra cost of the cheapest way there; null when it cannot be reached inside the window.</summary>
        public long? ExtraTo(Vec2 p)
        {
            var c = p.ToCell();
            return Inside(c) && _cost[Index(c.X, c.Y)] != long.MaxValue ? _extra[Index(c.X, c.Y)] : null;
        }

        /// <summary>The cheapest way there, cell centres from the first step to the goal (the goal point itself last).</summary>
        public List<Vec2> PathTo(Vec2 goal)
        {
            var cells = new List<Vec2>();
            int startIndex = Index(_start.X, _start.Y);
            for (int i = Index(goal.ToCell().X, goal.ToCell().Y); i != startIndex && i >= 0; i = _parent[i])
                cells.Add(new CellCoord(i % _w + _x0, i / _w + _y0).CenterCm);
            cells.Reverse();
            if (cells.Count > 0)
                cells[^1] = goal;
            return cells;
        }
    }
}
