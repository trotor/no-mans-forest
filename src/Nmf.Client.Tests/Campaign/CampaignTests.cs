using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Sim.Mission;
using Nmf.Client;

namespace Nmf.Client.Tests.Campaign;

/// <summary>The campaign's state and how it fills a mission's slots (spec 2026-10-11-campaign-design §9a).</summary>
public class CampaignTests
{
    internal static SoldierSpec Slot(string name, string squad = "a", bool leader = false, string weapon = "mosin_m39", bool ahkio = false,
        IReadOnlyList<string>? items = null, bool stranded = false) =>
        new(name, weapon, "m32", leader, Nerve: 10, Morale: 100, Marksmanship: 10, Leadership: 100, Items: items ?? [], Experience: 10, Squad: squad,
            At: (10, 10), Stranded: stranded, Skis: true, Ahkio: ahkio);

    internal static MissionSpec Mission(IReadOnlyList<SoldierSpec> player, string? radio = null) =>
        new("m", new Localized("M"), new Localized("today"), "map", new Localized("go"), player, [], [], new Dictionary<string, Localized>(),
            Squads: new Dictionary<string, Localized> { ["a"] = new("A"), ["b"] = new("B"), ["p"] = new("P") },
            Support: radio is null ? null : new SupportSpec(new MortarSupportSpec(8, 60, 120, radio)));

    internal static CampaignMan Man(string name, int leadership = 40, int experience = 50) => new(name, 70, 800, 60, leadership, experience);

    internal static CampaignState State(params CampaignMan[] roster) =>
        CampaignState.New(new CampaignSpec("c", new Localized("C"), new Localized("intro"), roster,
            [new CampaignChapter("m", new Localized("b"), new Localized("a")), new CampaignChapter("n", new Localized("b2"), new Localized("a2"))]));

    private static void Set(CampaignState state, string name, ManStatus status) => state.Men.Single(m => m.Name == name).Status = status;

    // Korpela leads squad a, Virtanen squad b (and has the radio), two riflemen each; Aho's slot has the ahkio.
    private static readonly SoldierSpec[] Squads =
    [
        Slot("Alik. Korpela", "a", leader: true, weapon: "suomi_kp31"),
        Slot("Sotm. Mäkinen", "a"),
        Slot("Kpl. Virtanen", "b", leader: true, weapon: "lahti_saloranta"),
        Slot("Sotm. Nieminen", "b"),
        Slot("Sotm. Aho", "b", ahkio: true),
    ];

    private static CampaignState Platoon() => State(
        Man("Kers. Lehto", 85, 85), Man("Alik. Korpela", 90, 90), Man("Sotm. Mäkinen", 40, 80), Man("Kpl. Virtanen", 80, 85),
        Man("Sotm. Nieminen", 35, 70), Man("Sotm. Aho", 30, 70), Man("Sotm. Kivelä", 30, 75), Man("Sotm. Hakala", 40, 60));

    private static string[] Names(MissionSpec spec) => spec.Player.Select(s => s.Name).ToArray();

    [Fact]
    public void Fill_NamedMenKeepTheirSlots()
    {
        var applied = CampaignMission.Apply(Mission(Squads), Platoon());
        Assert.Equal(Squads.Select(s => s.Name), Names(applied));
    }

    [Fact]
    public void Slot_KeepsWeaponAndKit_ManBringsHisSkills()
    {
        var state = Platoon();
        Set(state, "Sotm. Mäkinen", ManStatus.Fallen);
        Set(state, "Kers. Lehto", ManStatus.Recovering);
        var kivela = CampaignMission.Apply(Mission(Squads), state).Player[1];
        Assert.Equal("Sotm. Kivelä", kivela.Name); // the most experienced left (75) before Hakala (60)
        Assert.Equal(("mosin_m39", "m32", "a", false, true, (10, 10)), (kivela.Weapon, kivela.Grenade, kivela.Squad, kivela.Leader, kivela.Skis, kivela.At));
        Assert.Equal((70, 800, 60, 30, 75), (kivela.Nerve, kivela.Morale, kivela.Marksmanship, kivela.Leadership, kivela.Experience));
    }

    [Fact]
    public void Fill_LeaderSlotsTakeBestLeaders()
    {
        var state = Platoon();
        Set(state, "Alik. Korpela", ManStatus.Fallen);
        Set(state, "Kpl. Virtanen", ManStatus.Recovering);
        var applied = CampaignMission.Apply(Mission(Squads), state);
        Assert.Equal("Kers. Lehto", applied.Player[0].Name); // 85: the best leader left
        Assert.Equal("Sotm. Hakala", applied.Player[2].Name); // 40, before the more experienced Kivelä (30)
        Assert.DoesNotContain("Sotm. Kivelä", Names(applied));
    }

    [Fact]
    public void Fill_PrivateWithoutLeadership_DoesNotOutrankLeaders()
    {
        // A roster loaded without leadership values: every private is 40, and Virtanen (80) is the best leader left.
        var state = State(Man("Kpl. Virtanen", 80, 50), Man("Sotm. Hakala"), Man("Sotm. Kivelä", experience: 99));
        var applied = CampaignMission.Apply(Mission([Slot("Alik. Korpela", "a", leader: true), Slot("Sotm. Mäkinen", "a")]), state);
        Assert.Equal(["Kpl. Virtanen", "Sotm. Kivelä"], Names(applied));
    }

    [Fact]
    public void Fill_TooFewMen_LastSlotsDropped_SquadGetsALeader()
    {
        var state = State(Man("Sotm. Nieminen", 35, 70), Man("Sotm. Hakala", 45, 60));
        var applied = CampaignMission.Apply(Mission(Squads), state);
        // Nieminen keeps his own slot in squad b; Hakala takes the first leader slot (squad a); the rest are dropped.
        Assert.Equal(["Sotm. Hakala", "Sotm. Nieminen"], Names(applied));
        Assert.True(applied.Player[0].Leader);
        Assert.True(applied.Player[1].Leader); // squad b has no leader slot filled: its best man leads
    }

    [Fact]
    public void Fill_TooFewMen_AhkioKeptBeforeRifle()
    {
        var state = State(Man("Kers. Lehto", 85), Man("Kpl. Virtanen", 80), Man("Sotm. Hakala", 40, 60));
        var applied = CampaignMission.Apply(Mission(Squads), state);
        Assert.Equal(["Kers. Lehto", "Kpl. Virtanen", "Sotm. Hakala"], Names(applied));
        Assert.True(applied.Player[2].Ahkio); // Aho's slot, not Mäkinen's or Nieminen's rifle
    }

    [Fact]
    public void Fill_NoFitMan_Throws()
    {
        var state = Platoon();
        foreach (var man in state.Men)
            man.Status = ManStatus.Fallen;
        Assert.Throws<CampaignException>(() => CampaignMission.Apply(Mission(Squads), state));
        Assert.False(CampaignMission.CanPlay(state));
    }

    [Fact]
    public void Fill_OnlyRecovering_TheyComeBack()
    {
        // No soft lock: recovery only happens when a mission is recorded.
        var state = Platoon();
        foreach (var man in state.Men)
            man.Status = ManStatus.Fallen;
        Set(state, "Sotm. Aho", ManStatus.Recovering);
        Assert.True(CampaignMission.CanPlay(state));
        Assert.Equal(["Sotm. Aho"], Names(CampaignMission.Apply(Mission(Squads), state)));
    }

    [Fact]
    public void Radio_FollowsTheSlot_WhenVirtanenIsOut()
    {
        var state = Platoon();
        Set(state, "Kpl. Virtanen", ManStatus.Fallen);
        var applied = CampaignMission.Apply(Mission(Squads, radio: "Kpl. Virtanen"), state);
        Assert.Equal("Kers. Lehto", applied.Player[2].Name);
        Assert.Equal("Kers. Lehto", applied.Support!.Mortar!.Radio);
        Assert.Equal(8, applied.Support.Mortar.He);
    }

    [Fact]
    public void Radio_SlotEmpty_NoMortar()
    {
        // The radio on a rifleman's slot: a kit slot, filled before the ahkio (slot order) and before any plain rifle.
        var three = CampaignMission.Apply(Mission(Squads, radio: "Sotm. Nieminen"), State(Man("Kers. Lehto", 85), Man("Kpl. Virtanen", 80), Man("Sotm. Hakala")));
        Assert.Equal("Sotm. Hakala", three.Support!.Mortar!.Radio);

        var two = CampaignMission.Apply(Mission(Squads, radio: "Sotm. Nieminen"), State(Man("Kers. Lehto", 85), Man("Kpl. Virtanen", 80)));
        Assert.Equal(["Kers. Lehto", "Kpl. Virtanen"], Names(two));
        Assert.Null(two.Support?.Mortar);
    }

    [Fact]
    public void Stranded_Untouched()
    {
        var pilot = Slot("Luutn. Saarinen", "p", stranded: true, items: ["map_case"]);
        var applied = CampaignMission.Apply(Mission([.. Squads, pilot]), Platoon());
        Assert.Same(pilot, applied.Player[^1]);
    }

    [Fact]
    public void Json_RoundTrip()
    {
        var state = Platoon();
        Set(state, "Sotm. Aho", ManStatus.Recovering);
        state.Men[0].Xp = 37;
        state.Chapter = 1;
        state.History.Add(new CampaignHistoryEntry("m", true, 300));
        var back = CampaignState.Load(state.ToJson(), Spec(state));
        Assert.Equal(1, back.Chapter);
        Assert.Equal(37, back.Men[0].Xp);
        Assert.Equal(ManStatus.Recovering, back.Men.Single(m => m.Name == "Sotm. Aho").Status);
        Assert.Equal(new CampaignHistoryEntry("m", true, 300), Assert.Single(back.History));
        Assert.Contains("\"Recovering\"", state.ToJson());
        Assert.DoesNotContain("weapon", state.ToJson(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"Campaign\":\"other\",\"Chapter\":0,\"Men\":[{\"Name\":\"X\"}]}")]
    [InlineData("{\"Campaign\":\"c\",\"Chapter\":7,\"Men\":[{\"Name\":\"X\"}]}")]
    [InlineData("{\"Campaign\":\"c\",\"Chapter\":0,\"Men\":[]}")]
    public void Json_BrokenOrForeign_IsFresh(string json)
    {
        var spec = Spec(Platoon());
        var state = CampaignState.Load(json, spec);
        Assert.Equal(0, state.Chapter);
        Assert.Equal(spec.Roster.Select(m => m.Name), state.Men.Select(m => m.Name));
        Assert.All(state.Men, m => Assert.Equal((ManStatus.Fit, 0), (m.Status, m.Xp)));
    }

    private static CampaignSpec Spec(CampaignState state) =>
        new("c", new Localized("C"), new Localized("intro"), state.Men.Select(m => Man(m.Name)).ToList(),
            [new CampaignChapter("m", new Localized("b"), new Localized("a")), new CampaignChapter("n", new Localized("b2"), new Localized("a2"))]);
}

/// <summary>The campaign on the shipped content: the missions start with the platoon's men in them.</summary>
public class CampaignContentTests
{
    private static readonly string Content = ContentLocator.FindContentRoot(AppContext.BaseDirectory)!;

    private static (CampaignSpec Campaign, MissionSpec Mission) Load(string mission) =>
        (CampaignLoader.Load(Path.Combine(Content, "core", "campaigns", "jatkosota")),
            Nmf.Content.Missions.MissionLoader.Load(Path.Combine(Content, "core", "missions", mission)));

    private static Nmf.Sim.Scenarios.Scenario Start(MissionSpec spec) => MissionScenario.Create(
        Nmf.Content.Tiled.TmxMapLoader.Load(Path.Combine(Content, "core", "maps", spec.Map + ".tmx")), spec,
        Nmf.Content.Weapons.WeaponLoader.LoadDirectory(Path.Combine(Content, "core", "weapons")),
        Nmf.Content.Weapons.GrenadeLoader.LoadDirectory(Path.Combine(Content, "core", "grenades")), 1942,
        Nmf.Content.Tactics.TacticLoader.LoadDirectory(Path.Combine(Content, "core", "tactics")));

    [Fact]
    public void FreshPlatoon_PlaysEachMissionAsWritten()
    {
        foreach (var id in new[] { "iskuosasto", "hylky" })
        {
            var (campaign, mission) = Load(id);
            var applied = CampaignMission.Apply(mission, CampaignState.New(campaign));
            Assert.Equal(mission.Player.Select(s => (s.Name, s.Weapon, s.Leader, s.Squad)), applied.Player.Select(s => (s.Name, s.Weapon, s.Leader, s.Squad)));
            Assert.Equal(mission.Support, applied.Support);
        }
    }

    [Fact]
    public void Virtanen_Fallen_TheMortarGoesWithHisSlot_AndTheMissionStarts()
    {
        var (campaign, mission) = Load("iskuosasto");
        var state = CampaignState.New(campaign);
        state.Men.Single(m => m.Name == "Kpl. Virtanen").Status = ManStatus.Fallen;
        var applied = CampaignMission.Apply(mission, state);
        Assert.Equal("Kers. Lehto", applied.Support!.Mortar!.Radio); // the best leader left takes Virtanen's leader slot
        var sim = Start(applied).Sim;
        var radio = sim.MortarSupports.Single();
        Assert.Equal("Kers. Lehto", sim.FindUnit(radio.Radio)!.Name);
    }
}
