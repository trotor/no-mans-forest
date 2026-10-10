using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;

namespace Nmf.Client;

public enum PlanHandleKind { FireBase, FormUp, Via }

/// <summary>A point of the plan the player can drag: the fire base, the form-up, or a point of the strike team's way.</summary>
public sealed record PlanHandle(PlanHandleKind Kind, int Index, Vec2 At);

public enum PlanWhyNot { TooFewMen, NoPlace }

/// <summary>One way of attacking the preview offers: a tactic and its plan, or the straight assault (no plan).</summary>
public sealed record PreviewOption(string Id, string NameFi, string NameEn, TacticDef? Tactic, TacticPlan? Plan, PlanWhyNot? WhyNot, bool Suggested)
{
    public string Name(string language) => language == "fi" ? NameFi : NameEn;
    public bool Possible => WhyNot is null;
}

/// <summary>
/// The attack before it starts (spec 2026-10-10-plan-preview-design): the plan for each tactic, the one chosen, what the
/// player dragged. Nothing reaches the simulation until <see cref="Confirm"/>.
/// </summary>
public sealed class AttackPreview
{
    public const string AssaultId = "assault";
    public const int MaxVia = 3;
    private const int MinMenForManeuver = 4;

    private readonly Simulation _sim;
    private readonly Side _side;
    private readonly List<PreviewOption> _options;
    private readonly int _suggested;

    private AttackPreview(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, List<PreviewOption> options, int suggested)
    {
        _sim = sim;
        _side = side;
        Men = men;
        Target = target;
        _options = options;
        _suggested = suggested;
        Selected = suggested;
    }

    /// <summary>The men it was opened for; they get the order whatever is selected by then.</summary>
    public IReadOnlyList<UnitId> Men { get; }
    public UnitId Target { get; }
    public IReadOnlyList<PreviewOption> Options => _options;
    public int Selected { get; private set; }
    public PreviewOption Option => _options[Selected];
    /// <summary>The plan of the chosen tactic as it now stands; null for the straight assault.</summary>
    public TacticPlan? Plan => Option.Plan;
    public PlanEdits Edits { get; private set; } = new();
    /// <summary>The last drag went where the men cannot get to; the plan before it stands.</summary>
    public bool CannotGoThere { get; private set; }

    /// <summary>The preview of an attack by the commanded men; null when nobody is commanded or the target is not known.</summary>
    public static AttackPreview? Open(GameSession session, UnitId target)
    {
        var sim = session.Sim;
        var men = session.CommandedIds.ToList();
        if (men.Count == 0 || TacticPlanner.AimOf(sim, session.PlayerSide, target) is null)
            return null;
        int fit = men.Count(id => sim.FindUnit(id) is { IsOutOfAction: false });
        var suggestion = TacticPlanner.Suggest(sim, session.PlayerSide, men, target, session.Tactics);
        var options = new List<PreviewOption>();
        foreach (var tactic in session.Tactics)
        {
            var plan = TacticPlanner.Propose(sim, session.PlayerSide, men, target, tactic);
            PlanWhyNot? whyNot = plan is not null ? null
                : tactic.Maneuver is not null && fit < MinMenForManeuver ? PlanWhyNot.TooFewMen
                : PlanWhyNot.NoPlace;
            options.Add(new PreviewOption(tactic.Id, tactic.NameFi, tactic.NameEn, tactic, plan, whyNot, suggestion?.TacticId == tactic.Id));
        }
        options.Add(new PreviewOption(AssaultId, "Suora rynnäkkö", "Straight assault", null, null, null, Suggested: false));
        int suggested = options.FindIndex(o => o.Suggested);
        if (suggested < 0)
            suggested = options.FindIndex(o => o.Possible);
        return new AttackPreview(sim, session.PlayerSide, men, target, options, suggested);
    }

    /// <summary>Choose another way; false when it is not possible. The fire base the player put carries over, the rest does not.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= _options.Count || !_options[index].Possible)
            return false;
        Selected = index;
        CannotGoThere = false;
        var option = _options[index];
        if (option.Tactic is null)
        {
            Edits = new PlanEdits(); // the straight assault has nothing to drag
            return true;
        }
        // What the new tactic has handles for carries over: no fire base when creeping in, no way round for long fire.
        var kept = option.Tactic.Infiltrate is not null ? Edits with { FireBase = null }
            : option.Tactic.Maneuver is null ? new PlanEdits(FireBase: Edits.FireBase)
            : Edits;
        if (kept.IsEmpty)
        {
            Edits = new PlanEdits();
            _options[index] = option with { Plan = Base(option) };
            return true;
        }
        if (Replan(option, kept) is { } plan)
        {
            Edits = kept;
            _options[index] = option with { Plan = plan };
        }
        else
        {
            Edits = new PlanEdits();
            _options[index] = option with { Plan = Base(option) };
        }
        return true;
    }

    /// <summary>The suggestion again, nothing dragged.</summary>
    public void Reset()
    {
        Edits = new PlanEdits();
        for (int i = 0; i < _options.Count; i++)
            if (_options[i].Tactic is not null && _options[i].Possible)
                _options[i] = _options[i] with { Plan = Base(_options[i]) };
        Selected = _suggested;
        CannotGoThere = false;
    }

    public IReadOnlyList<PlanHandle> Handles
    {
        get
        {
            if (Plan is not { } plan)
                return [];
            var handles = new List<PlanHandle>();
            if (plan.FireTeam.Count > 0)
                handles.Add(new PlanHandle(PlanHandleKind.FireBase, 0, Centre(plan.FireTeam.Select(p => p.At))));
            if (plan.FormUp.Count > 0)
            {
                handles.Add(new PlanHandle(PlanHandleKind.FormUp, 0, Centre(plan.FormUp.Select(p => p.At))));
                var via = Edits.Via ?? [];
                for (int i = 0; i < via.Count; i++)
                    handles.Add(new PlanHandle(PlanHandleKind.Via, i, via[i]));
            }
            return handles;
        }
    }

    /// <summary>The handle under a point, the nearest within the radius; null when none.</summary>
    public PlanHandle? HandleAt(Vec2 point, int radiusCm) =>
        Handles.Where(h => (h.At - point).LengthSquared <= (long)radiusCm * radiusCm)
            .OrderBy(h => (h.At - point).LengthSquared).FirstOrDefault();

    /// <summary>
    /// A point on the strike team's way: where among the player's own points a new one there would go (0 = before the
    /// first); null when the point is not on the way or no more can be added.
    /// </summary>
    public int? ViaSlotAt(Vec2 point, int radiusCm)
    {
        if (Plan is not { StrikeTeam.Count: > 0 } plan || (Edits.Via?.Count ?? 0) >= MaxVia)
            return null;
        var route = plan.Route;
        int best = -1;
        long bestDistSq = (long)radiusCm * radiusCm;
        for (int i = 0; i < route.Count; i++)
        {
            long d = (route[i] - point).LengthSquared;
            if (d <= bestDistSq)
            {
                bestDistSq = d;
                best = i;
            }
        }
        if (best < 0)
            return null;
        // The player's points lie on the way in order; count those before this one.
        var via = Edits.Via ?? [];
        int slot = 0;
        for (int v = 0; v < via.Count; v++)
        {
            int at = IndexOn(route, via[v]);
            if (at >= 0 && at < best)
                slot = v + 1;
        }
        return slot;
    }

    /// <summary>Move a handle there and plan round it; false (the old plan stands) when the men cannot get there.</summary>
    public bool Drag(PlanHandle handle, Vec2 to)
    {
        var via = (Edits.Via ?? []).ToList();
        var edits = handle.Kind switch
        {
            PlanHandleKind.FireBase => Edits with { FireBase = to },
            PlanHandleKind.FormUp => Edits with { FormUp = to },
            _ when handle.Index >= 0 && handle.Index < via.Count => Edits with { Via = Replace(via, handle.Index, to) },
            _ => null,
        };
        return edits is not null && TryEdits(edits);
    }

    public bool AddVia(Vec2 at, int slot)
    {
        var via = (Edits.Via ?? []).ToList();
        if ((Option.Tactic?.Maneuver is null && Option.Tactic?.Infiltrate is null) || via.Count >= MaxVia)
            return false;
        via.Insert(Math.Clamp(slot, 0, via.Count), at);
        return TryEdits(Edits with { Via = via });
    }

    public bool RemoveVia(int index)
    {
        var via = (Edits.Via ?? []).ToList();
        if (index < 0 || index >= via.Count)
            return false;
        via.RemoveAt(index);
        return TryEdits(Edits with { Via = via });
    }

    /// <summary>Give the order to the men it was opened for; false (no order) when the target is gone or nobody can go.</summary>
    public bool Confirm(GameSession session)
    {
        // Only what the side knows: a man killed out of sight still stands as a mark, and his position may still hold.
        if (TacticPlanner.AimOf(_sim, _side, Target) is null
            || (_sim.Knowledge(_side).Get(Target)?.Level == Nmf.Sim.Vision.ContactLevel.Visible && _sim.FindUnit(Target) is not { IsOutOfAction: false }))
            return false;
        var fit = Men.Where(id => _sim.FindUnit(id) is { IsOutOfAction: false }).ToList();
        if (fit.Count == 0 || !Option.Possible)
            return false;
        session.Queue.ClearFor(Men);
        if (Option.Tactic is null)
        {
            session.ForgetPlan();
            foreach (var id in fit)
                _sim.Submit(_side, new AssaultOrder(id, Target));
            return true;
        }
        if (Plan is not { } plan)
            return false;
        session.IssuePlan(plan, Option.Tactic);
        return true;
    }

    private bool TryEdits(PlanEdits edits)
    {
        if (Option.Tactic is null || Replan(Option, edits) is not { } plan)
        {
            CannotGoThere = true;
            return false;
        }
        Edits = edits;
        CannotGoThere = false;
        _options[Selected] = Option with { Plan = plan };
        return true;
    }

    private TacticPlan? Base(PreviewOption option) => TacticPlanner.Propose(_sim, _side, Men, Target, option.Tactic!);

    private TacticPlan? Replan(PreviewOption option, PlanEdits edits) => TacticPlanner.Propose(_sim, _side, Men, Target, option.Tactic!, edits);

    private static List<Vec2> Replace(List<Vec2> list, int index, Vec2 value)
    {
        list[index] = value;
        return list;
    }

    private static int IndexOn(IReadOnlyList<Vec2> route, Vec2 p)
    {
        int best = -1;
        long bestSq = long.MaxValue;
        for (int i = 0; i < route.Count; i++)
        {
            long d = (route[i] - p).LengthSquared;
            if (d < bestSq)
            {
                bestSq = d;
                best = i;
            }
        }
        return best;
    }

    internal static Vec2 Centre(IEnumerable<Vec2> points)
    {
        long x = 0, y = 0;
        int n = 0;
        foreach (var p in points)
        {
            x += p.X;
            y += p.Y;
            n++;
        }
        return n == 0 ? Vec2.Zero : new Vec2((int)(x / n), (int)(y / n));
    }
}
