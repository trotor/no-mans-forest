using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class LootOrderTests
{
    private static (Simulation Sim, Unit Looter, Unit Body) Setup()
    {
        var sim = new Simulation(new GridMap(40, 20, ["none"]), 1);
        var looter = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle());
        var body = sim.SpawnUnit(Side.Red, new Vec2(1850, 1050), 7, TestWeapons.Rifle());
        body.Wound = WoundLevel.Dead;
        return (sim, looter, body);
    }

    private static string? RejectionOf(Simulation sim, Order order)
    {
        sim.Submit(Side.Blue, order);
        return sim.Step().OfType<OrderRejected>().FirstOrDefault()?.Reason;
    }

    private static List<SimEvent> StepUntilLooted(Simulation sim, int max = 300)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < max && !all.Any(e => e is UnitLooted); i++)
            all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void Rejections()
    {
        var (sim, looter, body) = Setup();
        var alive = sim.SpawnUnit(Side.Red, new Vec2(3050, 1850), 7);
        Assert.Equal("invalid target", RejectionOf(sim, new LootOrder(looter.Id, alive.Id)));
        Assert.Equal("invalid target", RejectionOf(sim, new LootOrder(looter.Id, looter.Id)));
        body.Looted = true;
        Assert.Equal("already looted", RejectionOf(sim, new LootOrder(looter.Id, body.Id)));
        body.Looted = false;
        looter.Morale = CombatRules.MaxMorale;
        MoraleSystem.AddSuppression(sim, looter, 450, sim.Tick, []);
        Assert.Equal("unit is pinned", RejectionOf(sim, new LootOrder(looter.Id, body.Id)));
    }

    [Fact]
    public void Looter_WalksOver_TakesTwoSeconds_ThenGetsTheGoods()
    {
        var (sim, looter, body) = Setup();
        sim.Submit(Side.Blue, new LootOrder(looter.Id, body.Id));
        long started = -1;
        var events = new List<SimEvent>();
        for (int i = 0; i < 300 && !events.Any(e => e is UnitLooted); i++)
        {
            events.AddRange(sim.Step());
            if (started < 0 && looter.Action == CombatAction.Looting)
                started = sim.Tick;
        }
        var looted = Assert.Single(events.OfType<UnitLooted>());
        Assert.True(started > 0);
        Assert.InRange(looted.Tick - started, CombatRules.LootTicks - 2, CombatRules.LootTicks);
        Assert.True((looter.Position - body.Position).LengthSquared <= (long)CombatRules.LootRangeCm * CombatRules.LootRangeCm);
        Assert.Equal(looter.Id, looted.Looter);
        Assert.Equal(5, looted.Magazines); // 4 spares + the full magazine
        Assert.Null(looter.LootTarget);
        Assert.Equal(CombatAction.None, looter.Action);
    }

    [Fact]
    public void MoveOrder_CancelsTheLoot()
    {
        var (sim, looter, body) = Setup();
        sim.Submit(Side.Blue, new LootOrder(looter.Id, body.Id));
        for (int i = 0; i < 300 && looter.Action != CombatAction.Looting; i++) sim.Step();
        Assert.Equal(CombatAction.Looting, looter.Action);
        sim.Submit(Side.Blue, new MoveOrder(looter.Id, new Vec2(1050, 1050)));
        var events = new List<SimEvent>();
        for (int i = 0; i < 80; i++) events.AddRange(sim.Step());
        Assert.DoesNotContain(events, e => e is UnitLooted);
        Assert.Null(looter.LootTarget);
        Assert.False(body.Looted);
    }

    [Fact]
    public void MeleeInterruptsLooting()
    {
        var (sim, looter, body) = Setup();
        sim.Submit(Side.Blue, new LootOrder(looter.Id, body.Id));
        for (int i = 0; i < 300 && looter.Action != CombatAction.Looting; i++) sim.Step();
        sim.SpawnUnit(Side.Red, looter.Position + new Vec2(0, 150), 7);
        sim.Step();
        Assert.Equal(CombatAction.Melee, looter.Action);
        Assert.Null(looter.LootTarget);
    }

    [Fact]
    public void TwoLootersSameTick_OnlyFirstGetsTheGoods()
    {
        var (sim, first, body) = Setup();
        first.Position = body.Position + new Vec2(-100, 0);
        var second = sim.SpawnUnit(Side.Blue, body.Position + new Vec2(100, 0), 7, TestWeapons.Rifle());
        sim.Submit(Side.Blue, new LootOrder(first.Id, body.Id));
        sim.Submit(Side.Blue, new LootOrder(second.Id, body.Id));
        var events = new List<SimEvent>();
        for (int i = 0; i < 100; i++) events.AddRange(sim.Step());
        var loots = events.OfType<UnitLooted>().ToList();
        Assert.Equal(2, loots.Count);
        Assert.Equal(first.Id, loots[0].Looter);
        Assert.Equal(5, loots[0].Magazines);
        Assert.Equal(0, loots[1].Magazines);
    }

    [Fact]
    public void LooterKilledWhileLooting_NoTransfer()
    {
        var (sim, looter, body) = Setup();
        sim.Submit(Side.Blue, new LootOrder(looter.Id, body.Id));
        for (int i = 0; i < 300 && looter.Action != CombatAction.Looting; i++) sim.Step();
        Damage.SetWound(sim, looter, WoundLevel.Dead, sim.Tick, []);
        var events = new List<SimEvent>();
        for (int i = 0; i < 80; i++) events.AddRange(sim.Step());
        Assert.DoesNotContain(events, e => e is UnitLooted);
        Assert.False(body.Looted);
        Assert.False(looter.Looted);
        Assert.Null(looter.LootTarget);
    }

    [Fact]
    public void Prisoner_CanBeLooted_ButDoesNotLoot()
    {
        var (sim, looter, body) = Setup();
        body.Wound = WoundLevel.None;
        body.IsCaptured = true;
        sim.Submit(Side.Blue, new LootOrder(looter.Id, body.Id));
        Assert.Contains(StepUntilLooted(sim), e => e is UnitLooted l && l.Body == body.Id);

        var (sim2, looter2, body2) = Setup();
        looter2.IsCaptured = true;
        Assert.Equal("unit is out of action", RejectionOf(sim2, new LootOrder(looter2.Id, body2.Id)));
    }
}
