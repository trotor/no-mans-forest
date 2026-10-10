using Nmf.Content.Missions;
using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Cli;

/// <summary>What one probe run came to.</summary>
public sealed record ProbeRun(int Seed, int BlueOut, int BlueCount, int RedOut, int RedCount, bool LeaderDown, int Attacks,
    bool? MissionResult, IReadOnlyDictionary<string, int> Halts, int Counterattacks = 0, int BlueOutToCounterattacks = 0);

/// <summary>
/// Plays a mission's attack headless as a player would (spec 2026-10-10-tactics-design, "Mittari"): up the plan's attack
/// route, sneaking the last 160 m, then an attack on the first enemy seen (their leader when he is seen), again every 15 s
/// while enemies are in sight and nobody is attacking. Measures who is out after some minutes of fighting.
/// </summary>
public static class AttackProbe
{
    private const int TicksPerSecond = SimConstants.TicksPerSecond;
    private const int SneakFromCm = 16_000;
    private const int WaypointReachedCm = 1_000;
    private const int ReattackTicks = 15 * TicksPerSecond;
    private const int MaxTicks = 20 * 60 * TicksPerSecond;

    /// <summary>Gives the attack order; true when one was given.</summary>
    /// <summary>Nothing seen and nobody attacking this long after contact: they go on along the route.</summary>
    private const int QuietTicks = 20 * TicksPerSecond;

    public delegate bool AttackHook(Simulation sim, IReadOnlyList<Unit> men, Unit target);

    public static IReadOnlyList<ProbeRun> Run(string contentRoot, string missionId, int seeds, int minutes, AttackHook attack, TextWriter output, int? traceSeed = null)
    {
        var spec = MissionLoader.Load(Path.Combine(contentRoot, "core", "missions", missionId));
        var map = TmxMapLoader.Load(Path.Combine(contentRoot, "core", "maps", spec.Map + ".tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(contentRoot, "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(contentRoot, "core", "grenades"));
        var tactics = Nmf.Content.Tactics.TacticLoader.LoadDirectory(Path.Combine(contentRoot, "core", "tactics"));
        var route = spec.Plan.FirstOrDefault(a => a.Kind == PlanKind.Attack)?.Points ?? [];

        var runs = new List<ProbeRun>();
        for (int seed = 1; seed <= seeds; seed++)
        {
            var scenario = MissionScenario.Create(map, spec, weapons, grenades, (ulong)seed, tactics);
            var sim = scenario.Sim;
            var tracker = new MissionTracker(spec, map);
            var blues = sim.Units.Where(u => u.Side == Side.Blue).ToList();
            var reds = sim.Units.Where(u => u.Side == Side.Red).ToList();
            var leader = reds.FirstOrDefault(u => u.IsLeader);
            var halts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int waypoint = 0, attacks = 0, counterattacks = 0, lostToCounterattacks = 0;
            long? contactTick = null;
            long lastAttackTick = long.MinValue / 2;
            long lastSeenTick = long.MinValue / 2;

            for (long t = 0; t < MaxTicks; t++)
            {
                var fit = blues.Where(u => !u.IsOutOfAction && u.MoraleState != Nmf.Sim.Combat.MoraleState.Broken).ToList();
                var seen = reds.Where(r => !r.IsOutOfAction && sim.Knowledge(Side.Blue).LevelOf(r.Id) == ContactLevel.Visible).ToList();
                if (contactTick is null && seen.Count > 0)
                    contactTick = sim.Tick;
                if (seen.Count > 0)
                    lastSeenTick = sim.Tick;
                // Before contact, and after it whenever nothing is seen and nobody attacks for a while, on along the route
                // (as a player goes on to the next position).
                bool quiet = contactTick is not null && sim.Tick - lastSeenTick >= QuietTicks && fit.All(u => !Attacking(sim, u));
                if ((contactTick is null || quiet) && fit.Count > 0 && sim.Tick % 20 == 0)
                    waypoint = Approach(sim, fit, route, waypoint);
                if (contactTick is not null && fit.Count > 0 && seen.Count > 0 && sim.Tick - lastAttackTick >= ReattackTicks
                    && fit.All(u => !Attacking(sim, u)))
                {
                    var target = seen.Contains(leader!) ? leader! : seen.OrderBy(r => Centre(fit).DistanceSq(r.Position)).ThenBy(r => r.Id.Value).First();
                    if (attack(sim, fit, target))
                        attacks++;
                    lastAttackTick = sim.Tick;
                }
                scenario.Tick();
                // Who was in which attack before the step: the step that hits a man may also end his attack.
                var roles = traceSeed == seed ? Roles(sim) : null;
                // The other way: the enemy's counterattacks, and the Finns who fall while one is on.
                bool redAttacking = RedAttacking(sim);
                foreach (var e in sim.Step().Concat(tracker.Update(sim)))
                {
                    if (e is CounterattackStarted)
                        counterattacks++;
                    if (redAttacking && e is UnitWounded { Level: >= Nmf.Sim.Combat.WoundLevel.Incapacitated } down
                        && sim.FindUnit(down.Unit)?.Side == Side.Blue)
                        lostToCounterattacks++;
                    CountHalt(e, halts);
                    if (roles is not null)
                        Trace(sim, e, roles, output);
                }
                if (tracker.Result is not null || fit.Count == 0
                    || (contactTick is { } c && sim.Tick - c >= (long)minutes * 60 * TicksPerSecond))
                    break;
            }

            var run = new ProbeRun(seed, blues.Count(u => u.IsOutOfAction), blues.Count, reds.Count(u => u.IsOutOfAction), reds.Count,
                leader?.IsOutOfAction == true, attacks, tracker.Result, halts, counterattacks, lostToCounterattacks);
            runs.Add(run);
            output.WriteLine($"seed {seed}: blue out {run.BlueOut}/{run.BlueCount}, red out {run.RedOut}/{run.RedCount}, " +
                             $"leader down {(run.LeaderDown ? "yes" : "no")}, attacks {run.Attacks}" +
                             (contactTick is null ? ", no contact" : "") +
                             (halts.Count > 0 ? ", halts " + string.Join(" ", halts.Select(h => $"{h.Key}×{h.Value}")) : "") +
                             (counterattacks > 0 ? $", counterattacks {counterattacks} (blue down in them {lostToCounterattacks})" : ""));
        }
        output.WriteLine($"mean: blue out {runs.Average(r => r.BlueOut):F1}, red out {runs.Average(r => r.RedOut):F1}, " +
                         $"leader down {runs.Count(r => r.LeaderDown)}/{runs.Count}, attacks {runs.Average(r => r.Attacks):F1}, " +
                         $"counterattacks {runs.Average(r => r.Counterattacks):F1} (blue down in them {runs.Average(r => r.BlueOutToCounterattacks):F1})");
        return runs;
    }

    private static Dictionary<UnitId, string> Roles(Simulation sim)
    {
        var roles = new Dictionary<UnitId, string>();
        foreach (var attack in sim.PlannedAttacks)
            foreach (var id in attack.Members)
                roles[id] = $"{(attack.StrikeTeam.Contains(id) ? "strike" : "fire")} in {attack.Phase}";
        return roles;
    }

    /// <summary>One line per attack event and per man falling, with the role he had just before.</summary>
    private static void Trace(Simulation sim, SimEvent e, Dictionary<UnitId, string> roles, TextWriter output)
    {
        string time = $"{e.Tick / TicksPerSecond / 60}:{e.Tick / TicksPerSecond % 60:00}";
        switch (e)
        {
            case PlannedAttackPhaseChanged or PlannedAttackHalted or PlannedAttackEnded:
                output.WriteLine($"  {time} {e}");
                break;
            case UnitWounded { Level: >= Nmf.Sim.Combat.WoundLevel.Incapacitated } w when sim.FindUnit(w.Unit) is { } man:
                string role = roles.GetValueOrDefault(man.Id, "not attacking");
                output.WriteLine($"  {time} {man.Side} {man.Name} down ({w.Level}), {role}, at {man.Position}");
                break;
        }
    }

    private static bool RedAttacking(Simulation sim) => sim.PlannedAttacks.Any(a => !a.Ended && a.Side == Side.Red);

    private static bool Attacking(Simulation sim, Unit man) => sim.PlannedAttacks.Any(a => !a.Ended && a.Members.Contains(man.Id));

    /// <summary>Halts of planned attacks by reason, and taken positions.</summary>
    private static void CountHalt(SimEvent e, SortedDictionary<string, int> halts)
    {
        // The enemy's own counterattacks under "red ...".
        string Key(Side side, string what) => side == Side.Blue ? what : "red " + what;
        if (e is PlannedAttackHalted halted)
            halts[Key(halted.Side, halted.Reason.ToString())] = halts.GetValueOrDefault(Key(halted.Side, halted.Reason.ToString())) + 1;
        else if (e is PlannedAttackEnded { Success: true } taken)
            halts[Key(taken.Side, "Taken")] = halts.GetValueOrDefault(Key(taken.Side, "Taken")) + 1;
    }

    /// <summary>The planned attack: one tactic forced, or the planner's own pick from all (null tactic = suggest).</summary>
    public static AttackHook Planned(IReadOnlyList<Nmf.Sim.Tactics.TacticDef> tactics, string? tactic) => (sim, men, target) =>
    {
        var ids = men.Select(m => m.Id).ToList();
        var plan = tactic is null
            ? Nmf.Sim.Tactics.TacticPlanner.Suggest(sim, Side.Blue, ids, target.Id, tactics)
            : Nmf.Sim.Tactics.TacticPlanner.Propose(sim, Side.Blue, ids, target.Id, tactics.First(t => t.Id == tactic));
        if (plan is null)
            return false;
        sim.Submit(Side.Blue, new PlannedAttackOrder(plan.FirstMan, plan, tactics.First(t => t.Id == plan.TacticId)));
        return true;
    };

    /// <summary>Up the plan's route: walk, then sneak the last 160 m; returns the waypoint now headed for.</summary>
    private static int Approach(Simulation sim, List<Unit> men, IReadOnlyList<Vec2> route, int waypoint)
    {
        if (route.Count == 0)
            return waypoint;
        var centre = Centre(men);
        if (waypoint < route.Count - 1 && centre.DistanceSq(route[waypoint]) <= (long)WaypointReachedCm * WaypointReachedCm)
            waypoint++;
        var goal = route[waypoint];
        var mode = centre.DistanceSq(route[^1]) <= (long)SneakFromCm * SneakFromCm ? MoveMode.Sneak : MoveMode.Auto;
        foreach (var man in men.Where(m => m.MoveTarget is null || (m.MoveTarget.Value - goal).LengthSquared > 600 * 600 || (mode == MoveMode.Sneak && m.MoveMode != MoveMode.Sneak)))
        {
            // Each man to his own spot round the waypoint, so they do not all try for one cell.
            int i = man.Id.Value % 7;
            var spot = goal + new Vec2((i % 3 - 1) * 300, (i / 3 - 1) * 300);
            sim.Submit(Side.Blue, new MoveOrder(man.Id, sim.Map.Contains(spot) ? spot : goal, mode));
        }
        return waypoint;
    }

    private static Vec2 Centre(IReadOnlyList<Unit> men) =>
        new((int)men.Average(u => (long)u.Position.X), (int)men.Average(u => (long)u.Position.Y));

    private static long DistanceSq(this Vec2 a, Vec2 b) => (a - b).LengthSquared;
}
