using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tactics;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class AttackPreviewTests
{
    private static readonly TacticDef FireAndMovement = new("fire_and_movement", "Fire and movement", "Tuli ja liike", 3_500, 10_000,
        new TacticManeuver(60, 2_500, 4_500), new TacticAssault(250, 400, 1_200), 30, 200);
    private static readonly TacticDef LongFire = new("long_fire", "Long fire", "Pitkä tuli", 5_000, 15_000, null, null, 30, 200);
    private static readonly TacticDef Infiltration = new("infiltration", "Infiltration", "Soluttautuminen", 1_800, 3_000, null, null, 30, 200,
        new TacticInfiltration(1_800, 3_000, 60));

    private static Vec2 M(int x, int y) => new(x * 100 + 50, y * 100 + 50);

    /// <summary>Six Finns at the south of a 200 m field and a Soviet 120 m north whom they see, woods on his flanks.</summary>
    private static (GameSession Session, Unit Red) Field()
    {
        var points = Enumerable.Range(0, 6).Select(i => new MapPoint($"b{i}", "blue", new Vec2(9050 + i * 300, 18_050)))
            .Append(new MapPoint("r1", "red", new Vec2(10_050, 6_050))).ToList();
        var map = new GridMap(200, 200, ["none"], new MapFeatures([], points, []));
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 200; x++)
                if (x < 85 || x > 115)
                {
                    ref var cell = ref map[new CellCoord(x, y)];
                    cell.ConcealmentPerM = 60;
                    cell.ObstacleHeightCm = 1500;
                }
        var session = new GameSession(SkirmishScenario.Create(map, 1)) { Tactics = [FireAndMovement, LongFire] };
        var red = session.Sim.Units.Single(u => u.Side == Side.Red);
        var contact = session.Sim.Knowledge(Side.Blue).GetOrAdd(red.Id);
        contact.Level = Nmf.Sim.Vision.ContactLevel.Visible;
        contact.Position = red.Position;
        return (session, red);
    }

    private static List<Order> Submitted(GameSession session, int before)
    {
        session.StepOnce();
        return session.Sim.OrderLog.Skip(before).Select(o => o.Order).ToList();
    }

    private static int DistM(Vec2 a, Vec2 b) => (int)(IntMath.Isqrt((a - b).LengthSquared) / 100);

    [Fact]
    public void Open_SelectsTheSuggestion()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.Equal(["fire_and_movement", "long_fire", AttackPreview.AssaultId], preview.Options.Select(o => o.Id));
        Assert.Equal("fire_and_movement", preview.Option.Id);
        Assert.True(preview.Option.Suggested);
        Assert.False(preview.Options[1].Suggested);
        Assert.NotNull(preview.Plan);
        Assert.Equal(6, preview.Men.Count);
        Assert.Contains(preview.Handles, h => h.Kind == PlanHandleKind.FireBase);
        Assert.Contains(preview.Handles, h => h.Kind == PlanHandleKind.FormUp);
    }

    [Fact]
    public void Open_NoMenOrUnknownTarget_Null()
    {
        var (session, red) = Field();
        session.Sim.Knowledge(Side.Blue).Get(red.Id)!.Level = Nmf.Sim.Vision.ContactLevel.Unknown;
        Assert.Null(AttackPreview.Open(session, red.Id));
    }

    [Fact]
    public void TooFewMen_FireAndMovementGreyed()
    {
        var (session, red) = Field();
        foreach (var man in session.OwnUnits.Skip(3).ToList())
            man.Wound = WoundLevel.Dead;
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.Null(preview.Options[0].Plan);
        Assert.Equal(PlanWhyNot.TooFewMen, preview.Options[0].WhyNot);
        Assert.False(preview.Select(0));
        Assert.Equal("long_fire", preview.Option.Id);
        Assert.Contains("vähintään 4", PlanText.WhyNot(preview.Options[0], "fi"));
    }

    [Fact]
    public void Select_SwitchesThePlan()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.True(preview.Select(1));
        Assert.Equal("long_fire", preview.Plan!.TacticId);
        Assert.DoesNotContain(preview.Handles, h => h.Kind == PlanHandleKind.FormUp);
        Assert.True(preview.Select(2));
        Assert.Null(preview.Plan);
        Assert.Empty(preview.Handles);
    }

    [Fact]
    public void DragFireBase_Replans()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Select(1);
        var handle = preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase);
        var to = M(60, 140);
        Assert.True(DistM(handle.At, to) > 20);
        Assert.True(preview.Drag(handle, to));
        Assert.True(DistM(preview.Plan!.FireTeam[0].At, to) <= 12);
        Assert.Equal(to, preview.Edits.FireBase);
        Assert.False(preview.CannotGoThere);
        Assert.Equal(preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase), preview.HandleAt(preview.Handles[0].At, 200));
    }

    [Fact]
    public void BadDrag_KeepsTheOldPlan()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        var before = preview.Plan;
        var handle = preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase);
        Assert.False(preview.Drag(handle, new Vec2(-5_000, 30_000))); // off the map
        Assert.Same(before, preview.Plan);
        Assert.True(preview.CannotGoThere);
        Assert.Contains("Sinne ei pääse.", PlanText.Warnings(preview, session, "fi"));
    }

    [Fact]
    public void SwitchTactic_KeepsTheFireBaseEdit_DropsTheRest()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        var fireBase = preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase);
        Assert.True(preview.Drag(fireBase, M(60, 140)));
        Assert.True(preview.AddVia(M(70, 170), 0));
        Assert.True(preview.Select(1));
        Assert.Equal(M(60, 140), preview.Edits.FireBase);
        Assert.Empty(preview.Edits.Via ?? []);
        Assert.True(DistM(preview.Plan!.FireTeam[0].At, M(60, 140)) <= 12);
    }

    [Fact]
    public void AddAndRemoveVia()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        var via = M(60, 160);
        Assert.True(preview.AddVia(via, 0));
        Assert.Contains(preview.Plan!.Route, p => (p - via).LengthSquared <= 150L * 150);
        var handle = preview.Handles.Single(h => h.Kind == PlanHandleKind.Via);
        Assert.Equal(0, handle.Index);
        // A second point after the first on the way: the slot past it.
        var late = preview.Plan.Route[^3];
        Assert.Equal(1, preview.ViaSlotAt(late, 150));
        Assert.Null(preview.ViaSlotAt(M(190, 10), 150));
        for (int i = 0; i < AttackPreview.MaxVia - 1; i++)
            preview.AddVia(preview.Plan.Route[^3], preview.Edits.Via!.Count);
        Assert.False(preview.AddVia(M(60, 150), 0)); // three at most
        Assert.True(preview.RemoveVia(0));
        Assert.Equal(AttackPreview.MaxVia - 1, preview.Edits.Via!.Count);
    }

    [Fact]
    public void Reset_BackToTheSuggestion()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        var suggested = preview.Plan!;
        preview.Select(1);
        preview.Drag(preview.Handles[0], M(60, 140));
        preview.Reset();
        Assert.Equal(0, preview.Selected);
        Assert.True(preview.Edits.IsEmpty);
        Assert.Equal(suggested.FireTeam, preview.Plan!.FireTeam);
        Assert.False(preview.CannotGoThere);
    }

    [Fact]
    public void Confirm_OrdersTheEditedPlan()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Drag(preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase), M(60, 140));
        int before = session.Sim.OrderLog.Count;
        Assert.True(preview.Confirm(session));
        var order = Assert.IsType<PlannedAttackOrder>(Assert.Single(Submitted(session, before)));
        Assert.Same(preview.Plan, order.Plan);
        Assert.Same(preview.Plan, session.LastPlan);
        Assert.Single(session.Sim.PlannedAttacks);
    }

    [Fact]
    public void Confirm_Assault_GivesAssaultOrders()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Select(2);
        int before = session.Sim.OrderLog.Count;
        Assert.True(preview.Confirm(session));
        var orders = Submitted(session, before);
        Assert.Equal(6, orders.Count);
        Assert.All(orders, o => Assert.Equal(red.Id, Assert.IsType<AssaultOrder>(o).Target));
    }

    [Fact]
    public void Confirm_OrdersTheMenItWasOpenedFor()
    {
        var (session, red) = Field();
        var two = session.OwnUnits.Take(2).Select(u => u.Id).ToList();
        foreach (var id in two)
            session.Selection.Add(id);
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.Equal(two, preview.Men);
        Assert.Equal("long_fire", preview.Option.Id); // two men are too few to go round
        session.Selection.Clear(); // the whole platoon now
        int before = session.Sim.OrderLog.Count;
        Assert.True(preview.Confirm(session));
        var order = Assert.IsType<PlannedAttackOrder>(Assert.Single(Submitted(session, before)));
        Assert.Equal(two.OrderBy(i => i.Value), order.Plan.FireTeam.Select(p => p.Man).OrderBy(i => i.Value));
    }

    [Fact]
    public void Confirm_TargetGone_NoOrder()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        red.Wound = WoundLevel.Dead;
        int before = session.Sim.OrderLog.Count;
        Assert.False(preview.Confirm(session));
        Assert.Empty(Submitted(session, before));
    }

    [Fact]
    public void Confirm_ClearsTheirQueue()
    {
        var (session, red) = Field();
        session.Queue.Add(session, session.CommandedIds, new QueuedOrder(QueuedKind.Move, M(100, 150)));
        session.Queue.Add(session, session.CommandedIds, new QueuedOrder(QueuedKind.Move, M(100, 140)));
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Confirm(session);
        Assert.Empty(session.Queue.Groups);
    }

    [Fact]
    public void Texts_InBothLanguages()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        var plan = preview.Plan!;
        string fi = PlanText.Reason(preview.Option, "fi");
        Assert.Contains($"Tuliasema {plan.Reason.FireBaseM} m päässä ({plan.FireTeam.Count} miestä)", fi);
        Assert.Contains($"Iskuryhmä ({plan.StrikeTeam.Count})", fi);
        Assert.Contains(plan.Reason.Side < 0 ? "vasemmalta" : "oikealta", fi);
        Assert.Contains($"Fire base {plan.Reason.FireBaseM} m off", PlanText.Reason(preview.Option, "en"));
        preview.Select(1);
        Assert.Contains("Kukaan ei mene sisään", PlanText.Reason(preview.Option, "fi"));
        preview.Select(2);
        Assert.Contains("suoraan kohteeseen", PlanText.Reason(preview.Option, "fi"));
        Assert.Equal("Hyökkäys: asema, 1 tunnettu vihollinen", PlanText.Title(preview, session, "fi"));
        Assert.Equal("Attack: a position, 1 known enemy", PlanText.Title(preview, session, "en"));
        Assert.Equal("Tuli ja liike (ehdotus)", PlanText.Button(preview.Options[0], 0, "fi").Split(' ', 2)[1]);
    }

    [Fact]
    public void Warnings_SayWhatIsWrong()
    {
        var (session, red) = Field();
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Select(1);
        preview.Drag(preview.Handles[0], M(30, 50)); // deep in the western wood
        Assert.True(preview.Plan!.FireBaseBlind);
        Assert.Contains("Tuliasema ei näe kohdetta: tuli on arvailua.", PlanText.Warnings(preview, session, "fi"));
        Assert.Contains("The fire base cannot see the target: the fire is guesswork.", PlanText.Warnings(preview, session, "en"));
    }

    [Fact]
    public void Confirm_ManKilledOutOfSight_TheMarkIsStillAttacked()
    {
        var (session, red) = Field();
        session.Sim.Knowledge(Side.Blue).Get(red.Id)!.Level = Nmf.Sim.Vision.ContactLevel.LastKnown;
        var preview = AttackPreview.Open(session, red.Id)!;
        red.Wound = WoundLevel.Dead; // unseen: the side does not know (VisionSystem keeps him as last known)
        int before = session.Sim.OrderLog.Count;
        Assert.True(preview.Confirm(session));
        Assert.NotEmpty(Submitted(session, before));
    }

    [Fact]
    public void Confirm_Assault_LeavesNoOldPlanToTellOf()
    {
        var (session, red) = Field();
        session.OrderAttack(red.Id);
        Assert.NotNull(session.LastPlan);
        var preview = AttackPreview.Open(session, red.Id)!;
        preview.Select(2);
        preview.Confirm(session);
        Assert.Null(session.LastPlan);
    }

    [Fact]
    public void Infiltration_NoFireBaseHandle_FormUpAndWayDraggable()
    {
        var (session, red) = Field();
        session.Tactics = [FireAndMovement, Infiltration, LongFire];
        var preview = AttackPreview.Open(session, red.Id)!;
        int index = preview.Options.ToList().FindIndex(o => o.Id == "infiltration");
        Assert.True(preview.Select(index), "infiltration not possible here");
        Assert.Empty(preview.Plan!.FireTeam);
        Assert.DoesNotContain(preview.Handles, h => h.Kind == PlanHandleKind.FireBase);
        Assert.Contains(preview.Handles, h => h.Kind == PlanHandleKind.FormUp);
        Assert.NotNull(preview.ViaSlotAt(preview.Plan.Route[preview.Plan.Route.Count / 2], 150));
    }

    [Fact]
    public void SwitchToInfiltration_DropsTheFireBaseEdit()
    {
        var (session, red) = Field();
        session.Tactics = [FireAndMovement, Infiltration, LongFire];
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.True(preview.Drag(preview.Handles.Single(h => h.Kind == PlanHandleKind.FireBase), M(60, 140)));
        preview.Select(preview.Options.ToList().FindIndex(o => o.Id == "infiltration"));
        Assert.Null(preview.Edits.FireBase);
    }

    [Fact]
    public void Texts_Infiltration()
    {
        var (session, red) = Field();
        session.Tactics = [Infiltration];
        var preview = AttackPreview.Open(session, red.Id)!;
        var plan = preview.Plan!;
        Assert.StartsWith($"Koko ryhmä ({plan.StrikeTeam.Count}) hiipii piilossa kokoontumispaikkaan", PlanText.Reason(preview.Option, "fi"));
        Assert.Contains("rynnäköi kranaatein", PlanText.Reason(preview.Option, "fi"));
        Assert.StartsWith($"The whole group ({plan.StrikeTeam.Count}) creeps unseen", PlanText.Reason(preview.Option, "en"));
        Assert.Equal(plan.StrikeTeam[0], plan.FirstMan);
        int before = session.Sim.OrderLog.Count;
        Assert.True(preview.Confirm(session));
        session.StepOnce();
        Assert.IsType<PlannedAttackOrder>(Assert.Single(session.Sim.OrderLog.Skip(before)).Order);
    }

    [Fact]
    public void Warnings_Infiltration_UnderFireAndGreenMen()
    {
        var (session, red) = Field();
        session.Tactics = [Infiltration];
        var preview = AttackPreview.Open(session, red.Id)!;
        Assert.DoesNotContain(PlanText.Warnings(preview, session, "fi"), w => w.Contains("tulen alla"));
        session.OwnUnits.First().LastSuppressedTick = session.Sim.Tick;
        foreach (var man in session.OwnUnits)
            man.Experience = 40;
        var warnings = PlanText.Warnings(preview, session, "fi");
        Assert.Contains("Miehiä on tulen alla: hiipiminen paljastuu heti.", warnings);
        Assert.Contains("Miehet ovat kokemattomia hiipimään (kokemus 40, tarvitaan 60).", warnings);
        Assert.Contains("Men are under fire: creeping in will be found out at once.", PlanText.Warnings(preview, session, "en"));
    }
}
