using Nmf.Client;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

/// <summary>Spec 2026-10-11-campaign-design §9b: specialties and first aid from the player's side.</summary>
public class FirstAidClientTests
{
    /// <summary>Three Finns: a medic, a badly wounded man 10 m up, a third man; the enemy far off.</summary>
    private static (GameSession Session, Unit Medic, Unit Wounded, Unit Other) Field()
    {
        var points = new List<MapPoint>
        {
            new("b0", "blue", new Vec2(4050, 9050)), new("b1", "blue", new Vec2(4350, 9050)), new("b2", "blue", new Vec2(4050, 8050)),
            new("r0", "red", new Vec2(9550, 550)),
        };
        var session = new GameSession(SkirmishScenario.Create(new GridMap(100, 100, ["none"], new MapFeatures([], points, [])), 1));
        foreach (var u in session.Sim.Units)
            u.FirePolicy = FirePolicy.HoldFire;
        var medic = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 9050));
        session.Sim.SetSpecialty(medic, Specialty.Medic);
        var wounded = session.OwnUnits.Single(u => u.Position == new Vec2(4050, 8050));
        wounded.Wound = WoundLevel.Serious;
        var other = session.OwnUnits.Single(u => u.Position == new Vec2(4350, 9050));
        return (session, medic, wounded, other);
    }

    [Fact]
    public void Menu_BindRow_ForTheSelectedMedic_OnABadlyWoundedMan()
    {
        var (s, medic, wounded, _) = Field();
        s.Selection.Add(medic.Id);
        var row = Assert.Single(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FirstAid);
        Assert.Equal("Sido haava", row.Text);
        Assert.Equal("Bind the wound", Assert.Single(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "en"), i => i.Action == MenuAction.FirstAid).Text);
        int before = s.Sim.OrderLog.Count;
        ContextMenu.Run(s, row);
        s.StepOnce();
        var order = Assert.IsType<FirstAidOrder>(Assert.Single(s.Sim.OrderLog.Skip(before)).Order);
        Assert.Equal((medic.Id, wounded.Id), (order.Unit, order.Patient));
    }

    [Fact]
    public void Menu_NoBindRow_WithoutAMedicSelected_OrForAManWhoDoesNotBleed()
    {
        var (s, medic, wounded, other) = Field();
        s.Selection.Add(other.Id);
        Assert.DoesNotContain(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FirstAid);
        s.Selection.Clear();
        s.Selection.Add(medic.Id);
        wounded.Bandaged = true;
        Assert.DoesNotContain(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FirstAid);
        wounded.Bandaged = false;
        wounded.Wound = WoundLevel.Light;
        Assert.DoesNotContain(ContextMenu.Items(s, wounded.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FirstAid);
    }

    [Fact]
    public void Menu_TheMedicBindsHimself()
    {
        var (s, medic, _, _) = Field();
        medic.Wound = WoundLevel.Serious;
        s.Selection.Add(medic.Id);
        Assert.Single(ContextMenu.Items(s, medic.Position, GameSession.ClickRadiusCm, "fi"), i => i.Action == MenuAction.FirstAid);
    }

    [Fact]
    public void Card_SaysBinding_GoingToBind_AndBound()
    {
        var (s, medic, wounded, _) = Field();
        s.Sim.Submit(s.PlayerSide, new FirstAidOrder(medic.Id, wounded.Id));
        for (int i = 0; i < 10; i++) s.StepOnce();
        Assert.Equal("Menossa sitomaan", UnitStatus.Describe(medic, "fi"));
        for (int i = 0; i < 400 && medic.Action != CombatAction.FirstAid; i++) s.StepOnce();
        Assert.Equal("Sitoo haavaa", UnitStatus.Describe(medic, "fi"));
        Assert.Equal("Binding a wound", UnitStatus.Describe(medic, "en"));
        for (int i = 0; i < 400 && !wounded.Bandaged; i++) s.StepOnce();
        Assert.Equal("Vakavasti haavoittunut, sidottu", UnitStatus.Condition(wounded, "fi"));
        Assert.Equal("Serious wound, bound", UnitStatus.Condition(wounded, "en"));
    }

    [Theory]
    [InlineData(Specialty.Marksman, "tarkka-ampuja", "marksman")]
    [InlineData(Specialty.Scout, "partiomies", "scout")]
    [InlineData(Specialty.Sapper, "pioneeri", "sapper")]
    [InlineData(Specialty.Medic, "lääkintämies", "medic")]
    public void SpecialtyNames_InBothLanguages_WithWhatTheyDo(Specialty specialty, string fi, string en)
    {
        Assert.Equal(fi, SpecialtyText.Name(specialty, "fi"));
        Assert.Equal(en, SpecialtyText.Name(specialty, "en"));
        Assert.False(string.IsNullOrWhiteSpace(SpecialtyText.Effect(specialty, "fi")));
        Assert.False(string.IsNullOrWhiteSpace(SpecialtyText.Effect(specialty, "en")));
    }

    [Fact]
    public void Card_ShowsTheSpecialty_NoneSaysNothing()
    {
        var (_, medic, _, other) = Field();
        Assert.Equal("Ehjä · lääkintämies", SpecialtyText.WithCondition(medic, "fi"));
        Assert.Equal("Ehjä", SpecialtyText.WithCondition(other, "fi"));
    }

    [Fact]
    public void Toast_WhenOurMedicHasBoundAWound_NotTheEnemys()
    {
        var (s, medic, wounded, _) = Field();
        var bound = new Nmf.Sim.Events.UnitBandaged(5, medic.Id, wounded.Id);
        wounded.Name = "Sotm. Saarinen";
        Assert.Equal("Haava sidottu: Sotm. Saarinen", Nmf.Client.Mission.Alerts.Bandaged(bound, s.Sim, s.PlayerSide, "fi"));
        Assert.Equal("Wound bound: Sotm. Saarinen", Nmf.Client.Mission.Alerts.Bandaged(bound, s.Sim, s.PlayerSide, "en"));
        Assert.Null(Nmf.Client.Mission.Alerts.Bandaged(bound, s.Sim, Side.Red, "fi"));
    }
}
