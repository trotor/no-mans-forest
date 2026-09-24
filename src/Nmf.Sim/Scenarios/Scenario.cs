using Nmf.Sim.AI;

namespace Nmf.Sim.Scenarios;

/// <summary>A simulation plus the scripted behaviours that drive it.</summary>
public sealed class Scenario
{
    internal Scenario(Simulation sim, IReadOnlyList<PatrolBehavior> patrols)
    {
        Sim = sim;
        Patrols = patrols;
    }

    public Simulation Sim { get; }
    public IReadOnlyList<PatrolBehavior> Patrols { get; }

    /// <summary>Runs scripted behaviours; call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick()
    {
        foreach (var patrol in Patrols)
            patrol.Tick(Sim);
    }
}
