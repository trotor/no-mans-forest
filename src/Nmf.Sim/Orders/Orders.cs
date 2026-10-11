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

/// <summary>Keep firing at a place: where the enemy was last seen or heard, or any point of the ground.</summary>
public sealed record AreaFireOrder(UnitId Unit, Vec2 Target) : Order(Unit);

public sealed record SetFirePolicyOrder(UnitId Unit, FirePolicy Policy) : Order(Unit);

public sealed record AssaultOrder(UnitId Unit, UnitId Target) : Order(Unit);

public sealed record LootOrder(UnitId Unit, UnitId Body) : Order(Unit);

/// <summary>Carry a man of his side who cannot move; a second man ordered to the same man helps (spec 2026-10-10-carrying-design).</summary>
public sealed record CarryOrder(UnitId Unit, UnitId Wounded) : Order(Unit);

/// <summary>The radioman calls the mortar onto a point: ranging, then this many rounds for effect (spec 2026-10-10-mortar-design).</summary>
public sealed record FireMissionOrder(UnitId Unit, Vec2 Target, int Rounds = 4) : Order(Unit);

/// <summary>Raise a man's alertness (a search party sets out on the alert); never lowers it, no shout (spec 2026-10-10-alertness-design §3).</summary>
public sealed record AlertOrder(UnitId Unit, Alertness Level) : Order(Unit);

/// <summary>Cut the enemy's telephone wire near <paramref name="At"/> (spec 2026-10-10-alertness-design §4).</summary>
public sealed record CutLineOrder(UnitId Unit, string Phone, Vec2 At) : Order(Unit);

/// <summary>The medic binds a badly wounded man's wound (spec 2026-10-11-campaign-design §9b).</summary>
public sealed record FirstAidOrder(UnitId Unit, UnitId Patient) : Order(Unit);

/// <summary>Lay the man he carries down where he is.</summary>
public sealed record DropOrder(UnitId Unit) : Order(Unit);



/// <summary>
/// Make the attack worked out beforehand (spec 2026-10-10-tactics-design): every man of the plan, not only <c>Unit</c>
/// (the plan's first man, who carries it). Men who cannot go (pinned, broken, down) are left out.
/// </summary>
/// <remarks><paramref name="Shout"/>: the man who orders it calls it out (the enemy's counterattack: <see cref="Events.CounterattackStarted"/>).</remarks>
public sealed record PlannedAttackOrder(UnitId Unit, Nmf.Sim.Tactics.TacticPlan Plan, Nmf.Sim.Tactics.TacticDef Tactic, bool Shout = false) : Order(Unit);
