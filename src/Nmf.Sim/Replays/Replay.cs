using Nmf.Sim.Orders;

namespace Nmf.Sim.Replays;

/// <summary>Everything needed to re-run a session besides the initial setup: seed, length and the order log.</summary>
public sealed record Replay(ulong Seed, long EndTick, IReadOnlyList<LoggedOrder> Orders);

public static class ReplayRunner
{
    public static Replay Capture(Simulation sim) => new(sim.Seed, sim.Tick, sim.OrderLog.ToList());

    /// <summary>Re-runs <paramref name="replay"/> on a freshly set-up simulation (same map and spawns, tick 0).</summary>
    public static void Run(Simulation freshSim, Replay replay)
    {
        if (freshSim.Tick != 0)
            throw new InvalidOperationException("A replay must start from a simulation at tick 0.");
        if (freshSim.Seed != replay.Seed)
            throw new ArgumentException($"Replay seed {replay.Seed} does not match simulation seed {freshSim.Seed}.", nameof(replay));
        Validate(replay);

        int next = 0;
        while (freshSim.Tick < replay.EndTick)
        {
            while (next < replay.Orders.Count && replay.Orders[next].Tick == freshSim.Tick)
            {
                var logged = replay.Orders[next++];
                freshSim.Submit(logged.Issuer, logged.Order);
            }
            freshSim.Step();
        }
    }

    private static void Validate(Replay replay)
    {
        for (int i = 0; i < replay.Orders.Count; i++)
        {
            long tick = replay.Orders[i].Tick;
            if (tick < 0 || tick >= replay.EndTick)
                throw new ArgumentException($"Replay order {i} has tick {tick}, outside 0..{replay.EndTick - 1}.", nameof(replay));
            if (i > 0 && tick < replay.Orders[i - 1].Tick)
                throw new ArgumentException($"Replay orders are not in tick order at index {i}.", nameof(replay));
        }
    }
}
