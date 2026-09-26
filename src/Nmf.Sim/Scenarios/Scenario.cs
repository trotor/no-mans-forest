using Nmf.Sim.AI;

namespace Nmf.Sim.Scenarios;

/// <summary>A simulation plus the scripted behaviours that drive it.</summary>
public sealed class Scenario
{
    internal Scenario(Simulation sim, IReadOnlyList<PatrolBehavior> patrols, IReadOnlyList<EnemyCommander>? commanders = null)
    {
        Sim = sim;
        Patrols = patrols;
        Commanders = commanders ?? [];
    }

    public Simulation Sim { get; }
    public IReadOnlyList<PatrolBehavior> Patrols { get; }
    public IReadOnlyList<EnemyCommander> Commanders { get; }

    /// <summary>Runs scripted behaviours; call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick()
    {
        foreach (var patrol in Patrols)
            patrol.Tick(Sim);
        foreach (var commander in Commanders)
            commander.Tick(Sim);
    }
}
