using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Events;

public abstract record SimEvent(long Tick);

public sealed record UnitMoved(long Tick, UnitId Unit, Vec2 From, Vec2 To) : SimEvent(Tick);

public sealed record UnitArrived(long Tick, UnitId Unit, Vec2 Position) : SimEvent(Tick);

public sealed record OrderRejected(long Tick, Order Order, string Reason) : SimEvent(Tick);

public sealed record StanceChanged(long Tick, UnitId Unit, Stance Stance) : SimEvent(Tick);

public sealed record ContactChanged(long Tick, Side Observer, UnitId Target, ContactLevel Level, Vec2 Position) : SimEvent(Tick);

public sealed record ShotFired(long Tick, UnitId Shooter, Vec2 From, Vec2 To, UnitId? Hit) : SimEvent(Tick);

public sealed record UnitWounded(long Tick, UnitId Unit, WoundLevel Level) : SimEvent(Tick);

public sealed record MoraleChanged(long Tick, UnitId Unit, MoraleState State) : SimEvent(Tick);

public sealed record LeaderChanged(long Tick, Side Side, UnitId Leader) : SimEvent(Tick);

public sealed record GrenadeThrown(long Tick, UnitId Thrower, Vec2 From, Vec2 To, long ExplodeTick) : SimEvent(Tick);

public sealed record GrenadeExploded(long Tick, int Grenade, Vec2 At) : SimEvent(Tick);

public sealed record MeleeStarted(long Tick, UnitId A, UnitId B) : SimEvent(Tick);

public sealed record MeleeEnded(long Tick, UnitId Winner, UnitId Loser) : SimEvent(Tick);

public sealed record UnitCaptured(long Tick, UnitId Unit) : SimEvent(Tick);
