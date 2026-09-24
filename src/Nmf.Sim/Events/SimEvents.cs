using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;

namespace Nmf.Sim.Events;

public abstract record SimEvent(long Tick);

public sealed record UnitMoved(long Tick, UnitId Unit, Vec2 From, Vec2 To) : SimEvent(Tick);

public sealed record UnitArrived(long Tick, UnitId Unit, Vec2 Position) : SimEvent(Tick);

public sealed record OrderRejected(long Tick, Order Order, string Reason) : SimEvent(Tick);

public sealed record StanceChanged(long Tick, UnitId Unit, Stance Stance) : SimEvent(Tick);
