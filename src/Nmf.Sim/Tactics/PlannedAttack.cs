using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Tactics;

public enum PlannedPhase : byte
{
    /// <summary>The fire team going to its fire base, the strike team round to its form-up.</summary>
    Deploy,
    /// <summary>The fire base keeping the position down; the strike team on its way or waiting.</summary>
    Suppress,
    /// <summary>The strike team at the form-up, going in once the position is down.</summary>
    Assault,
}

public enum HaltReason : byte { Casualties, LeaderDown, StrikePinned, NotSuppressed, NoFire, TargetMoved }

/// <summary>A planned attack under way (spec 2026-10-10-tactics-design, "Toteutus"), run by <see cref="PlannedAttackRunner"/>.</summary>
public sealed class PlannedAttack
{
    internal PlannedAttack(int id, Side side, TacticPlan plan, TacticDef tactic, long tick)
    {
        Id = id;
        Side = side;
        Plan = plan;
        Tactic = tactic;
        PhaseTick = tick;
        Centre = plan.Aim;
        PositionList.AddRange(plan.Position);
        foreach (var spot in plan.FireTeam)
            Spots[spot.Man] = spot.At;
        foreach (var spot in plan.FormUp)
            Spots[spot.Man] = spot.At;
    }

    public int Id { get; }
    public Side Side { get; }
    public TacticPlan Plan { get; }
    public TacticDef Tactic { get; }
    public string TacticId => Plan.TacticId;
    public PlannedPhase Phase { get; internal set; }
    public bool Ended { get; internal set; }

    internal readonly List<UnitId> MemberList = [];
    public IReadOnlyList<UnitId> Members => MemberList;
    public IEnumerable<UnitId> FireTeam => MemberList.Where(id => !Strike.Contains(id));
    public IEnumerable<UnitId> StrikeTeam => MemberList.Where(Strike.Contains);

    /// <summary>The enemies of the position as the side now knows them.</summary>
    internal readonly List<UnitId> PositionList = [];
    public IReadOnlyList<UnitId> Position => PositionList;
    /// <summary>The middle of the position as the side now knows it.</summary>
    public Vec2 Centre { get; internal set; }

    internal readonly HashSet<UnitId> Strike = [];
    internal readonly Dictionary<UnitId, Vec2> Spots = [];
    internal readonly HashSet<UnitId> Sent = [];
    internal readonly HashSet<UnitId> Settled = [];
    internal UnitId Leader;
    internal int Strength;
    internal int AverageNerve;
    internal int Fallen;
    internal long PhaseTick;
    internal long? PinnedSince;
    internal long? FirstFireTick;
    internal bool AssaultLaunched;
}
