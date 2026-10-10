using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Tactics;

/// <summary>Where one man of the plan goes: his spot in the fire base, or at the form-up.</summary>
public sealed record FirePosition(UnitId Man, Vec2 At);

/// <summary>What the plan says to the player: how far the fire base is, which way the strike team goes round (-1 left, 1 right, 0 none).</summary>
public sealed record PlanReason(int FireBaseM, int Side, bool Blind);

/// <summary>
/// What the player changed in the preview (spec 2026-10-10-plan-preview-design §4): the fire base and the form-up dragged
/// somewhere, the strike team's way through these points in order. The planner fills in the rest round them.
/// </summary>
public sealed record PlanEdits(Vec2? FireBase = null, Vec2? FormUp = null, IReadOnlyList<Vec2>? Via = null)
{
    public bool IsEmpty => FireBase is null && FormUp is null && (Via is null || Via.Count == 0);
}

/// <summary>
/// An attack on a position, worked out beforehand (spec 2026-10-10-tactics-design): pure data, carried by the order, so the
/// player can look at it first and a replay makes the same attack.
/// </summary>
/// <param name="Position">The known enemies of the position attacked, in unit-id order.</param>
/// <param name="FireTeam">The fire base: every man's spot.</param>
/// <param name="StrikeTeam">The men who go round and in; empty for long fire.</param>
/// <param name="FormUp">Where the strike men wait before going in, one spot each.</param>
/// <param name="Route">The strike team's way to the form-up, points at most 10 m apart, ending at the first form-up spot.</param>
/// <param name="FireRoute">The fire team's way to the fire base round what the enemy sees (null: straight there).</param>
/// <param name="RouteExposed">For each point of <paramref name="Route"/>: whether a known enemy (or a foxhole) sees it.</param>
public sealed record TacticPlan(string TacticId, UnitId Target, Vec2 Aim, IReadOnlyList<UnitId> Position,
    IReadOnlyList<FirePosition> FireTeam, IReadOnlyList<UnitId> StrikeTeam, IReadOnlyList<FirePosition> FormUp,
    IReadOnlyList<Vec2> Route, bool FireBaseBlind, int Score, PlanReason Reason,
    IReadOnlyList<Vec2>? FireRoute = null, IReadOnlyList<bool>? RouteExposed = null);
