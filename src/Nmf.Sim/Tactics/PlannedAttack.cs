using Nmf.Sim.Combat;
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

public enum HaltReason : byte { Casualties, LeaderDown, StrikePinned, NotSuppressed, NoFire, TargetMoved, Discovered }

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
        StartTick = tick;
        Centre = plan.Aim;
        PositionList.AddRange(plan.Position);
        foreach (var spot in plan.FireTeam)
            Spots[spot.Man] = spot.At;
        foreach (var spot in plan.FormUp)
            Spots[spot.Man] = spot.At;
    }

    public int Id { get; }
    public Side Side { get; }
    public TacticPlan Plan { get; internal set; }
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

    /// <summary>He is at his spot (or has no way to it and stays where he is).</summary>
    public bool IsSettled(UnitId man) => Settled.Contains(man) || Stuck.Contains(man);
    /// <summary>The strike team has gone in.</summary>
    public bool Launched => AssaultLaunched;

    internal readonly HashSet<UnitId> Strike = [];
    internal readonly Dictionary<UnitId, Vec2> Spots = [];
    internal readonly HashSet<UnitId> Sent = [];
    internal readonly HashSet<UnitId> Settled = [];
    /// <summary>Sent but with no way to his spot: he stays where he is and counts as there.</summary>
    internal readonly HashSet<UnitId> Stuck = [];
    /// <summary>Men already badly hurt when the order came: not casualties of this attack.</summary>
    internal readonly HashSet<UnitId> WoundedAtStart = [];
    /// <summary>Where each man of the position was believed to be when he first counted: the position moving is all of them gone from there.</summary>
    internal readonly Dictionary<UnitId, Vec2> PositionAt = [];
    /// <summary>The middle of the position when the order came; where its men are looked for all along.</summary>
    internal Vec2 PositionHome;
    internal long? SightTick;
    /// <summary>Where each strike man waits during the deploy, short of the first open ground on his way (spec 6a-2 §4).</summary>
    internal readonly Dictionary<UnitId, Vec2> HoldSpots = [];
    /// <summary>The route point the strike team waits short of (exclusive); -1 when the whole way is hidden.</summary>
    internal int HoldBefore = -1;
    /// <summary>How many times the attack has been planned again for a position that moved (spec 6a-2 §6).</summary>
    internal int Replans;
    internal UnitId Leader;
    internal int Strength;
    internal int AverageNerve;
    internal int Fallen;
    internal long PhaseTick;
    /// <summary>When it began (or was planned again): fire taken since then has found an infiltration out.</summary>
    internal long StartTick;
    /// <summary>Infiltration: each man's own fire policy, held while they creep in and given back after.</summary>
    internal readonly Dictionary<UnitId, FirePolicy> PolicyBefore = [];
    /// <summary>Infiltration: when the opening grenades were thrown.</summary>
    internal long? GrenadeTick;
    internal long? PinnedSince;
    internal long? FirstFireTick;
    internal bool AssaultLaunched;
}
