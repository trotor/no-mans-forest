using Nmf.Sim.AI;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Scenarios;

/// <summary>Builds the phase 2 test skirmish from map points ("blue", "red") and paths ("patrol").</summary>
public static class SkirmishScenario
{
    public const int SoldierWalkSpeedCmPerTick = 7;
    public const string BluePointType = "blue";
    public const string RedPointType = "red";
    public const string PatrolPathType = "patrol";

    public static Scenario Create(GridMap map, ulong seed)
    {
        var sim = new Simulation(map, seed);
        foreach (var point in map.Features.Points)
        {
            if (point.Type == BluePointType)
                sim.SpawnUnit(Side.Blue, point.Position, SoldierWalkSpeedCmPerTick);
            else if (point.Type == RedPointType)
                sim.SpawnUnit(Side.Red, point.Position, SoldierWalkSpeedCmPerTick);
        }

        var patrols = new List<PatrolBehavior>();
        var assigned = new HashSet<UnitId>();
        foreach (var path in map.Features.Paths.Where(p => p.Type == PatrolPathType))
        {
            var start = path.Points[0];
            var unit = sim.Units
                .Where(u => u.Side == Side.Red && !assigned.Contains(u.Id))
                .OrderBy(u => (u.Position - start).LengthSquared)
                .ThenBy(u => u.Id.Value)
                .FirstOrDefault();
            if (unit is null)
                break;
            assigned.Add(unit.Id);
            patrols.Add(new PatrolBehavior(unit.Id, path.Points));
        }
        return new Scenario(sim, patrols);
    }
}
