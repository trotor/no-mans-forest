using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>
/// Field telephones (spec 2026-10-10-alertness-design §4), inside the step: an alarmed post sends its nearest man to the
/// phone (moved directly, as the carry system moves a helper, not by orders) and he calls; a man told to cut the wire goes
/// to it and cuts. Either is undone by the man going down, losing his nerve or being sent elsewhere.
/// </summary>
internal static class PhoneSystem
{
    public const int PostRadiusCm = 3_000;
    public const int CallTicks = 15 * SimConstants.TicksPerSecond, CutTicks = 10 * SimConstants.TicksPerSecond;
    /// <summary>A cut order must point this near the wire.</summary>
    public const int CutReachCm = 500;
    public const int AtPhoneCm = 200, AtCutCm = 150;

    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var phone in sim.FieldPhones)
        {
            Cutting(sim, phone, tick, events);
            Calling(sim, phone, tick, events);
        }
    }

    /// <summary>A cut order: null when taken, else why not.</summary>
    public static string? Cut(Simulation sim, Unit unit, CutLineOrder order, long tick, List<SimEvent> events)
    {
        var phone = sim.FieldPhones.FirstOrDefault(p => p.Id == order.Phone);
        if (phone is null)
            return "no such telephone wire";
        if (phone.Side == unit.Side)
            return "our own wire";
        if (phone.CutAt is not null)
            return "already cut";
        var (point, distance) = phone.Nearest(order.At);
        if (distance > CutReachCm)
            return "not by the wire";
        var path = Pathfinder.FindPath(sim.Map, unit.Position, point);
        if (path is null)
            return "not reachable";
        CarrySystem.LetGo(sim, unit, tick, events);
        Simulation.LeaveAttack(unit);
        unit.AssaultTarget = null;
        unit.AreaTarget = null;
        LootSystem.Abandon(unit);
        SoldierBrain.ForgetCover(unit);
        unit.StanceOrdered = false;
        unit.AutoPace = true;
        Movement.StartPath(unit, point, SoldierBrain.ChoosePace(sim, unit), path);
        phone.Cutter = unit.Id;
        phone.CutPoint = point;
        phone.CutDoneTick = -1;
        return null;
    }

    private static void Cutting(Simulation sim, FieldPhone phone, long tick, List<SimEvent> events)
    {
        if (phone.Cutter is not { } id)
            return;
        var man = sim.FindUnit(id);
        bool there = man is not null && (man.Position - phone.CutPoint).LengthSquared <= (long)AtCutCm * AtCutCm && man.MoveTarget is null;
        bool onHisWay = man is not null && man.MoveTarget == phone.CutPoint;
        if (man is null || man.IsOutOfAction || man.MoraleState != MoraleState.Steady || phone.CutAt is not null || !(there || onHisWay))
        {
            phone.Cutter = null; // down, shaken or sent elsewhere: the wire stays whole
            phone.CutDoneTick = -1;
            return;
        }
        if (!there)
            return;
        if (phone.CutDoneTick < 0)
            phone.CutDoneTick = tick + CutTicks;
        else if (tick >= phone.CutDoneTick)
        {
            phone.CutAt = phone.CutPoint;
            phone.Cutter = null;
            phone.CutDoneTick = -1;
            events.Add(new LineCut(tick, phone.Id, man.Id, phone.CutPoint));
        }
    }

    private static void Calling(Simulation sim, FieldPhone phone, long tick, List<SimEvent> events)
    {
        if (phone.Called || phone.Failed || !sim.AlertnessOn(phone.Side))
            return;
        if (phone.Caller is { } id)
        {
            var man = sim.FindUnit(id);
            bool there = man is not null && (man.Position - phone.At).LengthSquared <= (long)AtPhoneCm * AtPhoneCm && man.MoveTarget is null;
            bool onHisWay = man is not null && man.MoveTarget == phone.At;
            if (man is null || man.IsOutOfAction || man.MoraleState == MoraleState.Broken || !(there || onHisWay))
            {
                phone.Caller = null; // the next man tries
                phone.CallDoneTick = -1;
                return;
            }
            if (!there)
                return;
            if (phone.CallDoneTick < 0)
                phone.CallDoneTick = tick + CallTicks;
            else if (tick >= phone.CallDoneTick)
            {
                phone.Caller = null;
                phone.CallDoneTick = -1;
                if (phone.CutAt is null)
                {
                    phone.Called = true;
                    events.Add(new PhoneCalled(tick, phone.Id, phone.Side, phone.At));
                }
                else
                {
                    phone.Failed = true;
                    events.Add(new PhoneCallFailed(tick, phone.Id, phone.Side, phone.At));
                }
            }
            return;
        }
        long postSq = (long)PostRadiusCm * PostRadiusCm;
        var post = sim.Units.Where(u => u.Side == phone.Side && !u.IsOutOfAction && (u.Position - phone.At).LengthSquared <= postSq).ToList();
        if (!post.Any(u => u.Alertness >= Alertness.Alerted))
            return;
        var caller = post
            .Where(u => u.MoraleState == MoraleState.Steady && !u.HandsBusy && u.CarryTarget is null && u.PlannedAttackId is null && u.AssaultTarget is null)
            .OrderBy(u => (u.Position - phone.At).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
        if (caller is null)
            return;
        if ((caller.Position - phone.At).LengthSquared > (long)AtPhoneCm * AtPhoneCm)
        {
            if (Pathfinder.FindPath(sim.Map, caller.Position, phone.At) is not { } path)
                return;
            SoldierBrain.ForgetCover(caller);
            LootSystem.Abandon(caller);
            caller.AreaTarget = null;
            caller.AutoPace = false;
            Movement.StartPath(caller, phone.At, MoveMode.Run, path);
        }
        else
        {
            Movement.ClearPath(caller);
            SoldierBrain.ForgetCover(caller);
        }
        phone.Caller = caller.Id;
    }
}
