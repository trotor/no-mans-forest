using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Tests.Mission;

/// <summary>Spec 2026-10-10-events-design §1–§4: the "when … then …" script and the stranded man.</summary>
public class MissionEventsTests
{
    private static readonly Dictionary<string, WeaponDef> Weapons = new() { ["rifle"] = TestWeapons.Rifle(lethality: 0) };
    private static readonly Dictionary<string, GrenadeDef> Grenades = [];
    private static readonly ObjectiveSpec FindWreck = MissionTrackerEventsTests.FindWreck;
    private static readonly ObjectiveSpec BringBack = MissionTrackerEventsTests.BringBack;
    private static readonly ObjectiveSpec HiddenMaps = MissionTrackerEventsTests.HiddenMaps;

    private sealed class Run
    {
        public required Scenario Scenario { get; init; }
        public required MissionTracker Tracker { get; init; }
        public required MissionEvents Script { get; init; }
        public List<SimEvent> Events { get; } = [];
        public Simulation Sim => Scenario.Sim;
        public Unit Hero => Sim.Units[0];
        public Unit Pilot => Sim.Units[1];
        public Unit Belov => Sim.Units[2];
        public Unit Petrov => Sim.Units[3];

        public void Steps(int n, Action? everyTick = null)
        {
            for (int i = 0; i < n; i++)
            {
                everyTick?.Invoke();
                Scenario.Tick();
                Events.AddRange(Sim.Step());
                Events.AddRange(Tracker.Update(Sim));
                Events.AddRange(Script.Tick(Sim));
            }
        }
    }

    private static Run Start(IReadOnlyList<EventSpec> events, params ObjectiveSpec[] objectives) =>
        Start(events, s => s, objectives);

    private static Run Start(IReadOnlyList<EventSpec> events, Func<MissionSpec, MissionSpec> change, params ObjectiveSpec[] objectives)
    {
        var spec = change(MissionTrackerEventsTests.Spec(objectives.Length > 0 ? objectives : [FindWreck, BringBack, HiddenMaps], events)
            with { EnemyAiRules = new EnemyAiSpec(Counterattack: true) });
        var scenario = MissionScenario.Create(MissionTrackerEventsTests.Map(), spec, Weapons, Grenades, 1);
        foreach (var u in scenario.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire; // nobody fights: only the script moves anyone
        var tracker = new MissionTracker(spec, scenario.Sim.Map);
        return new Run { Scenario = scenario, Tracker = tracker, Script = new MissionEvents(spec, scenario, tracker) };
    }

    private static EventSpec Say(string id, EventCondition when, params EventAction[] more) =>
        new(id, when, [new EventAction(EventActionKind.Message, Text: new Localized(id)), .. more]);

    private static List<string> Said(Run run) => run.Events.OfType<MissionMessage>().Select(m => m.Text.En).ToList();

    [Fact]
    public void AfterSeconds()
    {
        var run = Start([Say("late", new EventCondition(AfterTicks: 100))]);
        run.Steps(90);
        Assert.Empty(Said(run));
        run.Steps(30);
        Assert.Equal(["late"], Said(run));
    }

    [Fact]
    public void Objective_ThenEvent_Chained_FiresOnce()
    {
        var run = Start([Say("found", new EventCondition(Objective: "find_wreck")), Say("after", new EventCondition(Event: "found"))]);
        run.Steps(20);
        Assert.Empty(Said(run));
        run.Hero.Position = new Vec2(1500, 1500);
        run.Steps(200);
        Assert.Equal(["found", "after"], Said(run));
    }

    [Fact]
    public void Contact_BySide()
    {
        var run = Start([Say("seen", new EventCondition(ContactBy: Side.Red))]);
        run.Steps(20, () => { run.Hero.Position = new Vec2(500, 3500); });
        var c = run.Sim.Knowledge(Side.Red).GetOrAdd(run.Hero.Id);
        c.Level = ContactLevel.Visible;
        run.Steps(20);
        Assert.Contains("seen", Said(run));
    }

    [Fact]
    public void Zone_BySide()
    {
        var run = Start([Say("in", new EventCondition(ZoneName: "home", ZoneSide: Side.Red))]);
        run.Steps(20); // the Finns are at home, the Russians are not
        Assert.Empty(Said(run));
        run.Petrov.Position = new Vec2(500, 3500);
        run.Steps(20);
        Assert.Equal(["in"], Said(run));
    }

    [Fact]
    public void Losses_BySide()
    {
        var run = Start([Say("losses", new EventCondition(LossesSide: Side.Red, LossesAtLeast: 1))]);
        run.Steps(20);
        Assert.Empty(Said(run));
        run.Belov.Wound = WoundLevel.Dead;
        run.Steps(20);
        Assert.Equal(["losses"], Said(run));
    }

    [Fact]
    public void Wait_DelaysTheRest()
    {
        var run = Start([new EventSpec("e", new EventCondition(AfterTicks: 1),
            [new EventAction(EventActionKind.Message, Text: new Localized("one")), new EventAction(EventActionKind.Wait, Ticks: 200),
             new EventAction(EventActionKind.Message, Text: new Localized("two"))])]);
        run.Steps(100);
        Assert.Equal(["one"], Said(run));
        run.Steps(150);
        Assert.Equal(["one", "two"], Said(run));
    }

    [Fact]
    public void Send_OrdersTheSquad()
    {
        var run = Start([new EventSpec("go", new EventCondition(AfterTicks: 1), [new EventAction(EventActionKind.Send, Squad: "reserve", To: "wreck", Mode: MoveMode.Run)])]);
        run.Steps(40);
        var order = Assert.Single(run.Sim.OrderLog.Select(o => o.Order).OfType<MoveOrder>(), o => o.Unit == run.Petrov.Id);
        Assert.Equal(new Vec2(2050, 2050), order.Target);
        Assert.Equal(MoveMode.Run, order.Mode);
        Assert.DoesNotContain(run.Sim.OrderLog.Select(o => o.Order).OfType<MoveOrder>(), o => o.Unit == run.Belov.Id);
    }

    [Fact]
    public void Send_AllDown_Nothing()
    {
        var run = Start([new EventSpec("go", new EventCondition(AfterTicks: 1), [new EventAction(EventActionKind.Send, Squad: "reserve", To: "home")])]);
        run.Petrov.Wound = WoundLevel.Dead;
        run.Steps(40);
        Assert.DoesNotContain(run.Sim.OrderLog.Select(o => o.Order), o => o is MoveOrder);
    }

    [Fact]
    public void Release_TheCommanderTakesThemOn()
    {
        var run = Start([new EventSpec("free", new EventCondition(AfterTicks: 100), [new EventAction(EventActionKind.Release, Squad: "reserve")])]);
        var commander = Assert.Single(run.Scenario.Commanders);
        Assert.DoesNotContain(run.Petrov.Id, commander.Men);
        run.Steps(120);
        Assert.Contains(run.Petrov.Id, commander.Men);
    }

    [Fact]
    public void Reveal_ShowsTheObjective()
    {
        var run = Start([new EventSpec("r", new EventCondition(AfterTicks: 1), [new EventAction(EventActionKind.Reveal, Objective: "maps")])]);
        run.Steps(20);
        Assert.True(run.Tracker.IsVisible("maps"));
        Assert.Single(run.Events.OfType<ObjectiveRevealed>());
    }

    [Fact]
    public void End_EndsTheMission_PendingActionsDropped()
    {
        var run = Start([new EventSpec("end", new EventCondition(AfterTicks: 1),
            [new EventAction(EventActionKind.End, Success: false), new EventAction(EventActionKind.Message, Text: new Localized("never"))]),
            Say("later", new EventCondition(AfterTicks: 100))]);
        run.Steps(200);
        Assert.False(run.Tracker.Result);
        Assert.Single(run.Events.OfType<MissionEnded>());
        Assert.Empty(Said(run));
    }

    [Fact]
    public void MissionOver_PendingActionsDropped()
    {
        var run = Start([new EventSpec("slow", new EventCondition(AfterTicks: 1),
            [new EventAction(EventActionKind.Wait, Ticks: 100), new EventAction(EventActionKind.Message, Text: new Localized("too late"))])],
            BringBack);
        run.Steps(20);
        run.Pilot.Position = new Vec2(500, 3500); // home: the mission is won
        run.Steps(200);
        Assert.True(run.Tracker.Result);
        Assert.Empty(Said(run));
    }

    [Fact]
    public void Stranded_HoldsFire_UntilFound_ThenJoins()
    {
        var run = Start([]);
        Assert.True(run.Pilot.Stranded);
        Assert.Equal(FirePolicy.HoldFire, run.Pilot.FirePolicy);
        run.Steps(20);
        Assert.True(run.Pilot.Stranded);
        run.Hero.Position = run.Pilot.Position + new Vec2(300, 0);
        run.Steps(20);
        Assert.False(run.Pilot.Stranded);
        Assert.Equal(run.Pilot.Id, Assert.Single(run.Events.OfType<StrandedFound>()).Unit);
        Assert.Equal(FirePolicy.FireAtWill, run.Pilot.FirePolicy);
    }

    [Fact]
    public void UnknownSendPlace_Throws()
    {
        var spec = MissionTrackerEventsTests.Spec([FindWreck],
            [new EventSpec("go", new EventCondition(AfterTicks: 1), [new EventAction(EventActionKind.Send, Squad: "reserve", To: "crashsite")])]);
        var scenario = MissionScenario.Create(MissionTrackerEventsTests.Map(), spec, Weapons, Grenades, 1);
        var ex = Assert.Throws<ArgumentException>(() => new MissionEvents(spec, scenario, new MissionTracker(spec, scenario.Sim.Map)));
        Assert.Contains("crashsite", ex.Message);
    }

    [Fact]
    public void SameRun_SameOrders()
    {
        static List<string> Log()
        {
            var run = Start([new EventSpec("go", new EventCondition(AfterTicks: 5),
                [new EventAction(EventActionKind.Send, Squad: "reserve", To: "wreck"), new EventAction(EventActionKind.Wait, Ticks: 50),
                 new EventAction(EventActionKind.Release, Squad: "reserve")])]);
            run.Steps(400);
            return run.Sim.OrderLog.Select(o => $"{o.Tick}:{o.Order}").ToList();
        }
        Assert.Equal(Log(), Log());
    }

    /// <summary>The enemy calm, with two flares and a phone where Belov stands.</summary>
    private static Run WithPhone(params EventSpec[] events) => Start(events, s => s with
    {
        EnemyAiRules = new EnemyAiSpec(Counterattack: true, Calm: true, Flares: 2),
        PhoneList = [new PhoneSpec("hq", new Vec2(3550, 550), [new Vec2(3550, 550), new Vec2(3950, 550)])], // by Belov
    });

    [Fact]
    public void Mission_EnemyCalm_FlaresAndPhones_InTheSim()
    {
        var run = WithPhone();
        Assert.True(run.Sim.AlertnessOn(Side.Red));
        Assert.False(run.Sim.AlertnessOn(Side.Blue));
        Assert.Equal(Alertness.Calm, run.Belov.Alertness);
        Assert.Equal(2, run.Sim.FlaresLeft(Side.Red));
        var phone = Assert.Single(run.Sim.FieldPhones);
        Assert.Equal(("hq", Side.Red), (phone.Id, phone.Side));
    }

    [Fact]
    public void PhoneCondition_FiresWhenTheCallGetsThrough()
    {
        var run = WithPhone(Say("called", new EventCondition(Phone: "hq")));
        run.Steps(100);
        Assert.Empty(Said(run));
        run.Sim.Submit(Side.Red, new AlertOrder(run.Belov.Id, Alertness.Alerted));
        run.Steps(60 * SimConstants.TicksPerSecond);
        Assert.Equal(["called"], Said(run));
    }

    [Fact]
    public void PhoneCondition_UnknownPhone_Throws() =>
        Assert.Throws<ArgumentException>(() => Start([Say("x", new EventCondition(Phone: "nope"))]));

    [Fact]
    public void Search_AlertsTheSquad_AndSendsItToTheZone()
    {
        var run = WithPhone(new EventSpec("hunt", new EventCondition(AfterTicks: 10), [new EventAction(EventActionKind.Search, Squad: "reserve", To: "home")]));
        Assert.DoesNotContain(run.Petrov.Id, run.Scenario.Commanders.Single().Men); // held for the search, not the commander's
        run.Steps(40);
        Assert.True(run.Petrov.Alertness >= Alertness.Alerted);
        Assert.Single(run.Scenario.Searches);
        Assert.True(run.Petrov.MoveTarget is { } to && to.X is >= 0 and < 1000 && to.Y is >= 3000 and < 4000, $"Petrov goes to {run.Petrov.MoveTarget}");
    }

    [Fact]
    public void Search_UnknownZone_Throws() =>
        Assert.Throws<ArgumentException>(() => Start([new EventSpec("x", new EventCondition(AfterTicks: 1), [new EventAction(EventActionKind.Search, Squad: "reserve", To: "nowhere")])]));

    [Fact]
    public void Mission_Winter_Light_Skis_InTheSim()
    {
        var run = Start([], s => s with
        {
            SnowCm = 40,
            Light = Nmf.Sim.World.LightLevel.Night,
            Player = [s.Player[0] with { Skis = true, Ahkio = true }, s.Player[1]],
        });
        Assert.Equal(40, run.Sim.Winter!.SnowCm);
        Assert.Equal(Nmf.Sim.World.LightLevel.Night, run.Sim.Light);
        Assert.True(run.Hero.OnSkis);
        Assert.True(run.Hero.HasAhkio);
        Assert.False(run.Pilot.OnSkis);
        var summer = Start([]);
        Assert.Null(summer.Sim.Winter);
        Assert.Equal(Nmf.Sim.World.LightLevel.Day, summer.Sim.Light);
    }
}
