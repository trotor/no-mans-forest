using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class VisionSystemTests
{
    private static readonly Vec2 BluePos = new(50, 550);

    private static (Simulation Sim, Unit Blue, Unit Red) Pair(GridMap map, Vec2 redPos)
    {
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, BluePos, 7);
        var red = sim.SpawnUnit(Side.Red, redPos, 7);
        return (sim, blue, red);
    }

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < n; i++) events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void StandingEnemyInOpen_IsSpottedAfterFiveUpdates()
    {
        var (sim, _, red) = Pair(new GridMap(60, 10, ["none"]), new Vec2(2050, 550));

        StepN(sim, 16); // updates at ticks 0, 5, 10, 15: 4 x 237 = 948
        var knowledge = sim.Knowledge(Side.Blue);
        Assert.Equal(ContactLevel.Unknown, knowledge.LevelOf(red.Id));
        Assert.Equal(948, knowledge.Get(red.Id)!.Progress);

        var events = StepN(sim, 5); // update at tick 20
        Assert.Equal(ContactLevel.Visible, knowledge.LevelOf(red.Id));
        Assert.Contains<SimEvent>(new ContactChanged(20, Side.Blue, red.Id, ContactLevel.Visible, red.Position), events);
    }

    [Fact]
    public void EnemyBehindHill_IsNeverSpotted()
    {
        var map = new GridMap(60, 10, ["none"]);
        for (int y = 0; y < 10; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));

        StepN(sim, 200);

        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        Assert.Equal(0, sim.Knowledge(Side.Blue).Get(red.Id)!.Progress);
    }

    [Fact]
    public void ProneEnemyInBush_TakesMuchLongerThanStandingInOpen()
    {
        var bushMap = new GridMap(120, 10, ["none"]);
        bushMap[new CellCoord(100, 5)] = new CellData(0, 80, 153, 0, 0);
        var (hidden, _, proneRed) = Pair(bushMap, new Vec2(10050, 550));
        proneRed.Stance = Stance.Prone;

        var (open, _, standingRed) = Pair(new GridMap(120, 10, ["none"]), new Vec2(10050, 550));

        StepN(open, 30);
        Assert.Equal(ContactLevel.Visible, open.Knowledge(Side.Blue).LevelOf(standingRed.Id));

        StepN(hidden, 200);
        Assert.Equal(ContactLevel.Unknown, hidden.Knowledge(Side.Blue).LevelOf(proneRed.Id));
        Assert.InRange(hidden.Knowledge(Side.Blue).Get(proneRed.Id)!.Progress, 1, 999);
    }

    [Fact]
    public void LosingSight_TurnsContactIntoLastKnownAtLastSeenPosition()
    {
        var map = new GridMap(60, 10, ["none"]);
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        StepN(sim, 21);
        Assert.Equal(ContactLevel.Visible, sim.Knowledge(Side.Blue).LevelOf(red.Id));

        for (int y = 0; y < 10; y++)
            map[new CellCoord(10, y)] = map[new CellCoord(10, y)] with { ObstacleHeightCm = 300, ConcealmentPerM = 255 };
        var events = StepN(sim, 5);

        var contact = sim.Knowledge(Side.Blue).Get(red.Id)!;
        Assert.Equal(ContactLevel.LastKnown, contact.Level);
        Assert.Equal(new Vec2(2050, 550), contact.Position);
        Assert.Equal(VisionRules.ReacquireProgress, contact.Progress);
        Assert.Contains(events, e => e is ContactChanged { Observer: Side.Blue, Level: ContactLevel.LastKnown });
    }

    [Fact]
    public void RunningEnemyBehindHill_IsHeardAsRoughPositionThenForgotten()
    {
        var map = new GridMap(60, 10, ["none"]);
        for (int y = 0; y < 10; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        sim.Submit(Side.Red, new MoveOrder(red.Id, new Vec2(2850, 550), MoveMode.Run));

        StepN(sim, 11);
        var contact = sim.Knowledge(Side.Blue).Get(red.Id)!;
        Assert.Equal(ContactLevel.Suspected, contact.Level);
        Assert.Equal(new Vec2(2500, 500), contact.Position);

        sim.Submit(Side.Red, new StopOrder(red.Id));
        StepN(sim, 220);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
    }

    [Fact]
    public void EnemyBeyondMaxRange_StaysUnknownWithZeroProgress()
    {
        var (sim, _, red) = Pair(new GridMap(420, 10, ["none"]), new Vec2(41050, 550));
        StepN(sim, 100);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        Assert.Equal(0, sim.Knowledge(Side.Blue).Get(red.Id)!.Progress);
    }

    [Fact]
    public void Contacts_AreEnumeratedInUnitIdOrder()
    {
        var sim = new Simulation(new GridMap(60, 10, ["none"]), 1);
        sim.SpawnUnit(Side.Blue, BluePos, 7);
        var ids = new[] { new Vec2(4050, 550), new Vec2(2050, 550), new Vec2(3050, 550) }
            .Select(p => sim.SpawnUnit(Side.Red, p, 7).Id).ToList();
        sim.Step();
        Assert.Equal(ids, sim.Knowledge(Side.Blue).Contacts.Select(c => c.Target));
    }

    [Fact]
    public void OwnSide_IsNeverAContact()
    {
        var sim = new Simulation(new GridMap(60, 10, ["none"]), 1);
        var a = sim.SpawnUnit(Side.Blue, BluePos, 7);
        var b = sim.SpawnUnit(Side.Blue, new Vec2(250, 550), 7);
        StepN(sim, 30);
        Assert.Empty(sim.Knowledge(Side.Blue).Contacts);
        Assert.Null(sim.Knowledge(Side.Blue).Get(b.Id));
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(a.Id));
    }

    [Fact]
    public void EnemyWalkingOutOfSightWithinEarshot_KeepsLastSeenMarker()
    {
        var map = new GridMap(60, 10, ["none"]);
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        StepN(sim, 21);
        Assert.Equal(ContactLevel.Visible, sim.Knowledge(Side.Blue).LevelOf(red.Id));

        for (int y = 0; y < 10; y++)
            map[new CellCoord(10, y)] = map[new CellCoord(10, y)] with { ObstacleHeightCm = 300, ConcealmentPerM = 255 };
        sim.Submit(Side.Red, new MoveOrder(red.Id, new Vec2(2550, 550)));
        StepN(sim, 10);

        var contact = sim.Knowledge(Side.Blue).Get(red.Id)!;
        Assert.Equal(ContactLevel.LastKnown, contact.Level);
        Assert.Equal(new Vec2(2050, 550), contact.Position);
    }

    [Fact]
    public void HeardEnemyWhoDies_IsForgottenAfterTheTimeout()
    {
        var map = new GridMap(60, 10, ["none"]);
        for (int y = 0; y < 10; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        sim.Submit(Side.Red, new MoveOrder(red.Id, new Vec2(2850, 550), MoveMode.Run));
        StepN(sim, 11);
        Assert.Equal(ContactLevel.Suspected, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        red.Wound = Nmf.Sim.Combat.WoundLevel.Dead;
        StepN(sim, 250);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
    }

    [Fact]
    public void EnemyKilledUnseen_IsFoundWhenSomeoneComesInSight()
    {
        var sim = new Simulation(new GridMap(40, 20, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        Nmf.Sim.Combat.Damage.SetWound(sim, red, Nmf.Sim.Combat.WoundLevel.Dead, 0, []);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        StepN(sim, 200);
        Assert.Equal(ContactLevel.Visible, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        Assert.Equal(red.Position, sim.Knowledge(Side.Blue).GetOrAdd(red.Id).Position);
    }

    [Fact]
    public void SeenCorpse_StaysVisibleOutOfSight()
    {
        var sim = new Simulation(new GridMap(40, 20, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        StepN(sim, 60);
        Nmf.Sim.Combat.Damage.SetWound(sim, red, Nmf.Sim.Combat.WoundLevel.Dead, sim.Tick, []);
        Nmf.Sim.Combat.Damage.SetWound(sim, blue, Nmf.Sim.Combat.WoundLevel.Dead, sim.Tick, []); // nobody left to look
        StepN(sim, 60);
        Assert.Equal(ContactLevel.Visible, sim.Knowledge(Side.Blue).LevelOf(red.Id));
    }
}
