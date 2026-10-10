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
/// party to the middle of the search area at their own pace (or sneaking the last <c>sneakCm</c>), then round its four
/// quarters until it is found; up to the stranded man, the man with the ahkio (or the nearest steady man) takes him, and
/// everyone goes back to the start area. The men fight whatever they see by themselves.
/// </summary>
public static class RescueProbe
{
    private const int TicksPerSecond = SimConstants.TicksPerSecond;
    private const int ThereCm = 1_500;
    /// <summary>Up to the man to be found: nearer than the mission's own reach for finding him.</summary>
    private const int BesideCm = MissionEvents.FoundCm - 200;

    /// <param name="traceSeed">Every 30 s of this seed: where everyone is and what he is about.</param>
    /// <param name="sneakCm">Sneak this last stretch to the search area (0: their own pace all the way).</param>
    public static IReadOnlyList<RescueRun> Run(string contentRoot, string missionId, int seeds, int minutes, TextWriter output, int? traceSeed = null,
        int sneakCm = 0)
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
        // Round the search area's four quarters when its middle shows nothing.
        int qx = (area.Max.X - area.Min.X) / 4, qy = (area.Max.Y - area.Min.Y) / 4;
        Vec2[] quarters = [new(area.Min.X + qx, area.Min.Y + qy), new(area.Max.X - qx, area.Min.Y + qy), new(area.Max.X - qx, area.Max.Y - qy), new(area.Min.X + qx, area.Max.Y - qy)];
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
            int leg = -1; // -1 the middle, then the quarters in turn

            for (long t = 0; t < end && tracker.Result is null; t++)
            {
                var fit = party.Where(u => !u.IsOutOfAction && u.MoraleState != Nmf.Sim.Combat.MoraleState.Broken && !u.Stranded).ToList();
                if (fit.Count == 0)
                {
                    routed = true;
                    break;
                }
                if (sim.Tick % TicksPerSecond == 0)
                {
                    bool foundYet = tracker.IsDone(find.Id);
                    var lookAt = leg < 0 ? searchAt : quarters[leg % quarters.Length];
                    if (!foundYet && fit.All(u => u.MoveTarget is null) && fit.Any(u => (u.Position - lookAt).LengthSquared <= (long)ThereCm * ThereCm))
                        lookAt = quarters[++leg % quarters.Length]; // nothing here: on to the next quarter
                    Command(sim, fit, lost, foundYet, lookAt, homeAt, sneakCm);
                }
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
                    if (e is ContactChanged { Observer: Side.Red, Level: Nmf.Sim.Vision.ContactLevel.Visible } seen
                        && sim.FindUnit(seen.Target) is { IsOutOfAction: false })
                        contactTick ??= e.Tick; // one of the party seen (not the man lying at the wreck)
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
            var run = new RescueRun(seed, tracker.Result, sim.Tick / TicksPerSecond, party.Count(u => u.IsOutOfAction),
                blues.Count, sim.Units.Count(u => u.Side == Side.Red && u.IsOutOfAction), phoned, enemyFirst, found, routed);
            runs.Add(run);
            output.WriteLine($"seed {run.Seed}: {(run.Result switch { true => "rescued", false => "failed", null => routed ? "routed" : "time out" })} at {run.Seconds / 60}:{run.Seconds % 60:00}, " +
                             $"blue out {run.BlueOut}/{party.Count}, red out {run.RedOut}, found {(found ? "yes" : "no")}" +
                             (lost.Wound == Nmf.Sim.Combat.WoundLevel.Dead ? ", the pilot dead" : "") +
                             (phoned ? ", phoned" : "") + (enemyFirst ? ", enemy at the wreck first" : "")
                             + $" [seen {Clock(contactTick)}, alarm {Clock(alarmTick)}, call {Clock(phoneTick)}, found {Clock(foundTick)}, enemy at the wreck {Clock(enemyAtWreck)}]");
        }
        if (runs.Count == 0)
            return runs;
        output.WriteLine($"mean: rescued {runs.Count(r => r.Result == true)}/{runs.Count}, failed {runs.Count(r => r.Result == false)}, routed {runs.Count(r => r.Routed)}, " +
                         $"blue out {runs.Average(r => r.BlueOut):F1}, red out {runs.Average(r => r.RedOut):F1}, phoned {runs.Count(r => r.Phoned)}, " +
                         $"enemy first {runs.Count(r => r.EnemyFirst)}, minutes {runs.Average(r => r.Seconds) / 60:F1}");
        return runs;
    }

    /// <summary>
    /// Once a second: out to the search area until it is found; then up beside him, the ahkio man (or the nearest steady
    /// man) takes him, and all go home.
    /// </summary>
    private static void Command(Simulation sim, List<Unit> fit, Unit lost, bool found, Vec2 lookAt, Vec2 homeAt, int sneakCm)
    {
        bool aboard = lost.CarriedBy is not null;
        bool alive = lost.Wound != Nmf.Sim.Combat.WoundLevel.Dead;
        if (found && !aboard && !lost.Stranded && alive && fit.All(u => u.CarryTarget != lost.Id))
        {
            var steady = fit.Where(u => u.MoraleState == Nmf.Sim.Combat.MoraleState.Steady).ToList();
            var carrier = steady.Where(u => u.HasAhkio).Concat(steady.OrderBy(u => (u.Position - lost.Position).LengthSquared).ThenBy(u => u.Id.Value)).FirstOrDefault();
            if (carrier is not null)
                sim.Submit(Side.Blue, new CarryOrder(carrier.Id, lost.Id));
        }
        var goal = found && aboard ? homeAt : found ? lost.Position : lookAt;
        int there = found && !aboard && lost.Stranded ? BesideCm : ThereCm; // he is found only when one of them is right beside him
        foreach (var man in fit)
        {
            if (man.Carrying is not null && !aboard || man.CarryTarget is not null)
                continue;
            long distSq = (man.Position - goal).LengthSquared;
            if (distSq <= (long)there * there)
                continue;
            bool sneak = !found && sneakCm > 0 && distSq <= (long)sneakCm * sneakCm;
            if (man.MoveTarget == goal && (!sneak || man.MoveMode == MoveMode.Sneak))
                continue; // on his way already, at the pace wanted
            if (man.MoveTarget is not null && man.MoveTarget != goal && !(found && !aboard))
                continue; // not to be turned while he is going somewhere of his own (into cover, say)
            sim.Submit(Side.Blue, new MoveOrder(man.Id, goal, sneak ? MoveMode.Sneak : MoveMode.Auto));
        }
    }

    private static string Clock(long? tick) => tick is { } t ? $"{t / TicksPerSecond / 60}:{t / TicksPerSecond % 60:00}" : "-";

    private static Vec2 Middle(Nmf.Sim.World.MapZone zone) => new((zone.Min.X + zone.Max.X) / 2, (zone.Min.Y + zone.Max.Y) / 2);

    private static bool Inside(Nmf.Sim.World.MapZone zone, Vec2 p) =>
        p.X >= zone.Min.X && p.Y >= zone.Min.Y && p.X < zone.Max.X && p.Y < zone.Max.Y;
}
