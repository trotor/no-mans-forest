using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Orders;

public abstract record Order(UnitId Unit);

public sealed record MoveOrder(UnitId Unit, Vec2 Target, MoveMode Mode = MoveMode.Walk) : Order(Unit);

public sealed record StopOrder(UnitId Unit) : Order(Unit);

public sealed record SetStanceOrder(UnitId Unit, Stance Stance) : Order(Unit);

/// <summary>An order as submitted: the tick it was submitted on and by which side.</summary>
public sealed record LoggedOrder(long Tick, Side Issuer, Order Order);

public sealed record FireAtOrder(UnitId Unit, UnitId Target) : Order(Unit);

public sealed record SetFirePolicyOrder(UnitId Unit, FirePolicy Policy) : Order(Unit);

public sealed record AssaultOrder(UnitId Unit, UnitId Target) : Order(Unit);

public sealed record LootOrder(UnitId Unit, UnitId Body) : Order(Unit);
