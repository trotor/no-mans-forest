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

/// <summary>What a man took from a fallen one; all zero / empty when there was nothing for him.</summary>
/// <remarks><paramref name="LeftOver"/>: he would have taken more, but carries all he can (the rest stays on the body).</remarks>
public sealed record UnitLooted(long Tick, UnitId Looter, UnitId Body, int Magazines, int Grenades, string? WeaponTaken, IReadOnlyList<Item> Items,
    bool LeftOver = false) : SimEvent(Tick);

/// <summary>A mission objective was completed, or undone (e.g. the man carrying the papers fell).</summary>
/// <summary>The radio call got through: the mortar will fire after its delay (spec 2026-10-10-mortar-design).</summary>
public sealed record FireMissionCalled(long Tick, Side Side) : SimEvent(Tick);

/// <summary>The radio call did not get through: the mission is dropped.</summary>
public sealed record FireMissionFailed(long Tick, Side Side) : SimEvent(Tick);

public sealed record FireMissionPhaseChanged(long Tick, Side Side, Nmf.Sim.Combat.MortarPhase Phase) : SimEvent(Tick);

/// <summary>A shell on its way down (it lands at <paramref name="ImpactTick"/>).</summary>
public sealed record ShellIncoming(long Tick, Side Side, Vec2 At, long ImpactTick, bool Ranging) : SimEvent(Tick);

public sealed record ShellExploded(long Tick, Side Side, Vec2 At, bool Ranging) : SimEvent(Tick);

/// <summary>The fire is over: all rounds fired, or none left.</summary>
public sealed record FireMissionEnded(long Tick, Side Side, bool OutOfRounds, bool RadioLost = false) : SimEvent(Tick);

/// <summary>A man who cannot move has been lifted onto a carrier's back.</summary>
public sealed record UnitLifted(long Tick, UnitId Carrier, UnitId Wounded) : SimEvent(Tick);

/// <summary>A carried man laid down (ordered, the carrier needs his hands, falls, is pinned or breaks).</summary>
public sealed record UnitDropped(long Tick, UnitId Carrier, UnitId Wounded) : SimEvent(Tick);

public sealed record ObjectiveChanged(long Tick, string Id, bool Done) : SimEvent(Tick);

/// <summary>A hidden objective has been revealed by a mission event (spec 2026-10-10-events-design §4).</summary>
public sealed record ObjectiveRevealed(long Tick, string Id) : SimEvent(Tick);

/// <summary>A mission event's message to the player.</summary>
public sealed record MissionMessage(long Tick, Nmf.Sim.Mission.Localized Text) : SimEvent(Tick);

/// <summary>A stranded man has been reached by his own side and is theirs to command now.</summary>
public sealed record StrandedFound(long Tick, UnitId Unit) : SimEvent(Tick);

public sealed record MissionEnded(long Tick, bool Success) : SimEvent(Tick);

/// <summary>A leader called a counterattack: his shout ("Urraa!") is heard some way off.</summary>
public sealed record CounterattackStarted(long Tick, Side Side, UnitId Leader, Vec2 At, UnitId Target) : SimEvent(Tick);

/// <summary>A planned attack moved on: the fire base set and firing, or the strike team in place for the assault.</summary>
public sealed record PlannedAttackPhaseChanged(long Tick, Side Side, int Id, Nmf.Sim.Tactics.PlannedPhase Phase) : SimEvent(Tick);

/// <summary>A planned attack stopped short (spec 2026-10-10-tactics-design, "Keskeytys"); the fire base keeps firing.</summary>
public sealed record PlannedAttackHalted(long Tick, Side Side, int Id, Nmf.Sim.Tactics.HaltReason Reason, int Casualties) : SimEvent(Tick);

/// <summary>A planned attack is over: the position taken, its men gone, or (Disbanded) ours all given other orders or down.</summary>
public sealed record PlannedAttackEnded(long Tick, Side Side, int Id, bool Success, bool Disbanded = false) : SimEvent(Tick);
