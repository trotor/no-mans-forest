using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>A thrown grenade: in flight until <see cref="LandTick"/>, lying there until <see cref="ExplodeTick"/>.</summary>
public sealed class Grenade
{
    internal Grenade(int id, UnitId thrower, Side side, GrenadeDef def, Vec2 from, Vec2 landing, long throwTick)
    {
        Id = id;
        Thrower = thrower;
        Side = side;
        Def = def;
        From = from;
        Landing = landing;
        ThrowTick = throwTick;
    }

    public int Id { get; }
    public UnitId Thrower { get; }
    public Side Side { get; }
    public GrenadeDef Def { get; }
    public Vec2 From { get; }
    public Vec2 Landing { get; }
    public long ThrowTick { get; }
    public long LandTick => ThrowTick + CombatRules.GrenadeFlightTicks;
    public long ExplodeTick => ThrowTick + Def.FuseTicks;
    public bool Exploded { get; internal set; }
}
