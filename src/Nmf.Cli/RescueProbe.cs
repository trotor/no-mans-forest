using Nmf.Content.Missions;
using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;

namespace Nmf.Cli;

/// <summary>What one rescue run came to.</summary>
/// <remarks><paramref name="Routed"/>: nobody of the party was left fit to go on (down or broken) before the mission ended.</remarks>
public sealed record RescueRun(int Seed, bool? Result, long Seconds, int BlueOut, int BlueCount, int RedOut, bool Phoned, bool EnemyFirst, bool Found,
    bool Routed = false);

/// <summary>
/// Plays a find-and-rescue mission headless as a plain player would (spec 2026-10-10-wreck-mission-design §4): the whole
/// party to the middle of the search area (sneaking the last 80 m), the man with the ahkio (or the nearest) takes the
/// stranded man once he is found, and everyone goes back to the start area. The men fight whatever they see by themselves.
/// </summary>
public static class RescueProbe
{
    private const int TicksPerSecond = SimConstants.TicksPerSecond;
    private const int SneakFromCm = 8_000;
    private const int ThereCm = 1_500;

    /// <param name="traceSeed">Every 30 s of this seed: where everyone is and what he is about.</param>
    public static IReadOnlyList<RescueRun> Run(string contentRoot, string missionId, int seeds, int minutes, TextWriter output, int? traceSeed = null)
    {
        var spec = MissionLoader.Load(Path.Combine(contentRoot, "core", "missions", missionId));
        var map = TmxMapLoader.Load(Path.Combine(contentRoot, "core", "maps", spec.Map + ".tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(contentRoot, "core", "weapons"));
        var grenades = GrenadeLoader.LoadDirectory(Path.Combine(contentRoot, "core", "grenades"));
        var tactics = Nmf.Content.Tactics.TacticLoader.LoadDirectory(Path.Combine(contentRoot, "core", "tactics"));
        var find = spec.Objectives.FirstOrDefault(o => o.Type == ObjectiveType.Find)
                   ?? throw new ArgumentException($"mission '{missionId}' has no find objective");
        var rescue = spec.Objectives.FirstOrDefault(o => o.Type == ObjectiveType.Rescue)
                     ?? throw new ArgumentException($"mission '{missionId}' has no rescue objective");
        var area = spec.ZoneNamed(map, find.Area ?? "") ?? throw new ArgumentException($"find objective '{find.Id}' has no area");
        var home = spec.ZoneNamed(map, rescue.Zone!)!;
        var searchAt = Middle(area);
        var homeAt = Middle(home);

        var runs = new List<RescueRun>();
        for (int seed = 1; seed <= seeds; seed++)
        {
            var scenario = MissionScenario.Create(map, spec, weapons, grenades, (ulong)seed, tactics);
            var sim = scenario.Sim;
            var tracker = new MissionTracker(spec, map);
            var script = new MissionEvents(spec, scenario, tracker);
            var blues = sim.Units.Where(u => u.Side == Side.Blue).ToList();
            var party = blues.Where(u => !u.Stranded).ToList();
            var lost = blues.Single(u => u.Name == rescue.Unit);
            bool phoned = false, found = false;
            long? enemyAtWreck = null, foundTick = null, phoneTick = null, alarmTick = null, contactTick = null;
            long end = (long)minutes * 60 * TicksPerSecond;
            bool routed = false;

            for (long t = 0; t < end && tracker.Result is null; t++)
            {
                var fit = party.Where(u => !u.IsOutOfAction && u.MoraleState != Nmf.Sim.Combat.MoraleState.Broken && !u.Stranded).ToList();
                if (fit.Count == 0)
                {
                    routed = true;
                    break;
                }
                if (sim.Tick % TicksPerSecond == 0)
                    Command(sim, fit, lost, tracker.IsDone(find.Id), searchAt, homeAt);
                if (seed == traceSeed && sim.Tick % (30 * TicksPerSecond) == 0)
                    output.WriteLine($"  {Clock(sim.Tick)} " + string.Join(" | ", sim.Units.Where(u => !u.IsOutOfAction).Select(u =>
                        $"{u.Name?.Split(' ')[^1]} {u.Position.X / 100},{u.Position.Y / 100} {u.Alertness}{(u.MoveTarget is { } m ? $"→{m.X / 100},{m.Y / 100}" : "")}{(u.Carrying is not null ? " carrying" : "")}")));
                scenario.Tick();
                foreach (var e in sim.Step().Concat(tracker.Update(sim)).Concat(script.Tick(sim)))
                {
                    if (e is PhoneCalled)
                    {
                        phoned = true;
                        phoneTick ??= e.Tick;
                    }
                    if (e is AlarmRaised)
                        alarmTick ??= e.Tick;
                    if (e is ContactChanged { Observer: Side.Red, Level: Nmf.Sim.Vision.ContactLevel.Visible })
                        contactTick ??= e.Tick;
                }
                if (!found && tracker.IsDone(find.Id))
                {
                    found = true;
                    foundTick = sim.Tick;
                }
                if (enemyAtWreck is null && sim.Units.Any(u => u.Side == Side.Red && !u.IsOutOfAction && Inside(area, u.Position)))
                    enemyAtWreck = sim.Tick;
            }
            bool enemyFirst = enemyAtWreck is { } e1 && (foundTick is null || e1 < foundTick);
            var run = new RescueRun(seed, tracker.Result, sim.Tick / TicksPerSecond, blues.Count(u => u.IsOutOfAction && u != lost) + (lost.Wound == Nmf.Sim.Combat.WoundLevel.Dead ? 1 : 0),
                blues.Count, sim.Units.Count(u => u.Side == Side.Red && u.IsOutOfAction), phoned, enemyFirst, found, routed);
            runs.Add(run);
            output.WriteLine($"seed {run.Seed}: {(run.Result switch { true => "rescued", false => "failed", null => routed ? "routed" : "time out" })} at {run.Seconds / 60}:{run.Seconds % 60:00}, " +
                             $"blue out {run.BlueOut}/{run.BlueCount - 1}, red out {run.RedOut}, found {(found ? "yes" : "no")}" +
                             (phoned ? ", phoned" : "") + (enemyFirst ? ", enemy at the wreck first" : "")
                             + $" [seen {Clock(contactTick)}, alarm {Clock(alarmTick)}, call {Clock(phoneTick)}, found {Clock(foundTick)}, enemy at the wreck {Clock(enemyAtWreck)}]");
        }
        output.WriteLine($"mean: rescued {runs.Count(r => r.Result == true)}/{runs.Count}, failed {runs.Count(r => r.Result == false)}, routed {runs.Count(r => r.Routed)}, " +
                         $"blue out {runs.Average(r => r.BlueOut):F1}, red out {runs.Average(r => r.RedOut):F1}, phoned {runs.Count(r => r.Phoned)}, " +
                         $"enemy first {runs.Count(r => r.EnemyFirst)}, minutes {runs.Average(r => r.Seconds) / 60:F1}");
        return runs;
    }

    /// <summary>Once a second: out to the search area until it is found; then the ahkio man takes him and all go home.</summary>
    private static void Command(Simulation sim, List<Unit> fit, Unit lost, bool found, Vec2 searchAt, Vec2 homeAt)
    {
        bool aboard = lost.CarriedBy is not null;
        if (found && !aboard && !lost.Stranded && lost.Wound != Nmf.Sim.Combat.WoundLevel.Dead)
        {
            if (fit.All(u => u.CarryTarget != lost.Id))
            {
                var carrier = fit.Where(u => u.HasAhkio).Concat(fit.OrderBy(u => (u.Position - lost.Position).LengthSquared).ThenBy(u => u.Id.Value)).First();
                sim.Submit(Side.Blue, new CarryOrder(carrier.Id, lost.Id));
            }
            return;
        }
        var goal = found && aboard ? homeAt : found ? lost.Position : searchAt;
        foreach (var man in fit)
        {
            if (man.MoveTarget is not null || man.Carrying is not null && !aboard || man.CarryTarget is not null)
                continue;
            if ((man.Position - goal).LengthSquared <= (long)ThereCm * ThereCm)
                continue;
            var mode = !found && (man.Position - goal).LengthSquared <= (long)SneakFromCm * SneakFromCm ? MoveMode.Sneak : MoveMode.Auto;
            sim.Submit(Side.Blue, new MoveOrder(man.Id, goal, mode));
        }
    }

    private static string Clock(long? tick) => tick is { } t ? $"{t / TicksPerSecond / 60}:{t / TicksPerSecond % 60:00}" : "-";

    private static Vec2 Middle(Nmf.Sim.World.MapZone zone) => new((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);

    private static bool Inside(Nmf.Sim.World.MapZone zone, Vec2 p) =>
        p.X >= zone.Min.X && p.Y >= zone.Min.Y && p.X < zone.Max.X && p.Y < zone.Max.Y;
}
