using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.AI;

/// <summary>Men attacking one enemy together by fire and movement (spec 2026-09-26-attack-design §2).</summary>
public sealed class AttackGroup
{
    internal AttackGroup(int id, Side side, UnitId target, long createdTick)
    {
        Id = id;
        Side = side;
        Target = target;
        CreatedTick = createdTick;
    }

    public int Id { get; }
    public Side Side { get; }
    /// <summary>The enemy under attack now; when he falls the attack moves on to the next seen man of his position.</summary>
    public UnitId Target { get; internal set; }
    public long CreatedTick { get; }

    internal readonly List<UnitId> MemberList = [];
    public IReadOnlyList<UnitId> Members => MemberList;

    internal readonly Dictionary<UnitId, int> Team = [];
    internal bool TeamsAssigned;
    /// <summary>The half now dashing forward (0 = A, 1 = B); the other half covers it with fire.</summary>
    public int BoundingTeam { get; internal set; }
    internal long BoundStartTick;
    internal Vec2? LastKnown;
    /// <summary>When a man first came within assault range; held up there too long, they go in anyway.</summary>
    internal long? CloseSince;
    internal int StalledSwaps;
    internal bool MovedThisBound;
    /// <summary>The way round when the straight line is blocked (found once per target position).</summary>
    internal List<Vec2>? Route;
    public bool Ended { get; internal set; }
}
