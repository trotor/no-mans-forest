using Nmf.Client.Campaign;
using Nmf.Sim.Mission;
using static Nmf.Client.Tests.Campaign.CampaignTests;

namespace Nmf.Client.Tests.Campaign;

/// <summary>The player's lineup: who goes into which slot with which weapon (spec 2026-10-11-campaign-design §9c).</summary>
public class CampaignLineupTests
{
    // Korpela leads a with the SMG, Mäkinen rifle; Virtanen leads b with the LMG, Nieminen rifle.
    private static readonly MissionSpec Four = CampaignTests.Mission(
    [
        Slot("Alik. Korpela", "a", leader: true, weapon: "suomi_kp31"),
        Slot("Sotm. Mäkinen", "a"),
        Slot("Kpl. Virtanen", "b", leader: true, weapon: "lahti_saloranta"),
        Slot("Sotm. Nieminen", "b"),
    ]);

    private static CampaignState Platoon() => State(
        Man("Alik. Korpela", 90, 90), Man("Sotm. Mäkinen", 40, 80), Man("Kpl. Virtanen", 80, 85), Man("Sotm. Nieminen", 35, 70),
        Man("Sotm. Kivelä", 30, 75), Man("Sotm. Hakala", 40, 60));

    private static void Same(IEnumerable<string?> expected, IEnumerable<string?> actual) => Assert.Equal(expected, actual);

    private static string?[] Going(CampaignState state) => CampaignMission.Fill(Four, state).Select(m => m?.Name).ToArray();

    [Fact]
    public void PickedMan_TakesTheSlot_TheOneThereMovesOn()
    {
        var state = Platoon();
        state.PickMan(Four, 1, "Sotm. Hakala");
        Same(["Alik. Korpela", "Sotm. Hakala", "Kpl. Virtanen", "Sotm. Nieminen"], Going(state));
    }

    [Fact]
    public void PickedMan_LeavesTheSlotHeWasPickedForBefore()
    {
        var state = Platoon();
        state.PickMan(Four, 1, "Sotm. Hakala");
        state.PickMan(Four, 3, "Sotm. Hakala");
        Assert.Equal("Sotm. Hakala", Going(state)[3]);
        Assert.Equal("Sotm. Mäkinen", Going(state)[1]); // slot 1 is automatic again: its own man
        Assert.Single(state.Lineup, p => p.Man == "Sotm. Hakala");
    }

    [Fact]
    public void NamedMan_PickedElsewhere_HisOwnSlotIsFilledByTheRules()
    {
        var state = Platoon();
        state.PickMan(Four, 3, "Alik. Korpela");
        var going = Going(state);
        Assert.Equal("Alik. Korpela", going[3]);
        Assert.Equal("Kpl. Virtanen", going[2]);
        Assert.Equal("Sotm. Hakala", going[0]); // the best leader left (Hakala 40, Nieminen 35, Kivelä 30)
    }

    [Fact]
    public void LeftEmpty_NobodyGoesThere_HeStaysInTheLine()
    {
        var state = Platoon();
        state.LeaveEmpty(Four, 1);
        var going = Going(state);
        Assert.Null(going[1]);
        Assert.DoesNotContain("Sotm. Mäkinen", going);
        var applied = CampaignMission.Apply(Four, state);
        Assert.Equal(["Alik. Korpela", "Kpl. Virtanen", "Sotm. Nieminen"], applied.Player.Select(s => s.Name));
    }

    [Fact]
    public void Automatic_ClearsThePick()
    {
        var state = Platoon();
        state.PickMan(Four, 1, "Sotm. Hakala");
        state.PickMan(Four, 1, null);
        Assert.Equal("Sotm. Mäkinen", Going(state)[1]);
        state.LeaveEmpty(Four, 1);
        state.PickMan(Four, 1, null);
        Assert.Equal("Sotm. Mäkinen", Going(state)[1]);
    }

    [Fact]
    public void PickedMan_WhoCannotGo_IsPassedOver()
    {
        var state = Platoon();
        state.PickMan(Four, 1, "Sotm. Hakala");
        state.Men.Single(m => m.Name == "Sotm. Hakala").Status = ManStatus.Recovering;
        Assert.Equal("Sotm. Mäkinen", Going(state)[1]);
    }

    [Fact]
    public void AllLeftEmpty_TheMissionCannotStart()
    {
        var state = Platoon();
        for (int i = 0; i < 4; i++)
            state.LeaveEmpty(Four, i);
        Assert.False(CampaignMission.CanPlay(Four, state));
        Assert.Throws<CampaignException>(() => CampaignMission.Apply(Four, state));
        state.PickMan(Four, 2, null);
        Assert.True(CampaignMission.CanPlay(Four, state));
    }

    [Fact]
    public void Weapon_PickedFromTheArmoury_WhenOneIsFree()
    {
        var state = Platoon();
        state.Armoury["ppsh41"] = 1;
        Assert.True(state.PickWeapon(Four, 1, "ppsh41"));
        Same(["suomi_kp31", "ppsh41", "lahti_saloranta", "mosin_m39"], CampaignLineup.Weapons(Four, state));
        Assert.Equal("ppsh41", CampaignMission.Apply(Four, state).Player[1].Weapon);
    }

    [Fact]
    public void Weapon_NoneFree_SwapsWithTheSlotThatHasIt()
    {
        var state = Platoon();
        Assert.True(state.PickWeapon(Four, 1, "lahti_saloranta"));
        Same(["suomi_kp31", "lahti_saloranta", "mosin_m39", "mosin_m39"], CampaignLineup.Weapons(Four, state));
    }

    [Fact]
    public void Weapon_NotInThePool_Refused()
    {
        var state = Platoon();
        Assert.False(state.PickWeapon(Four, 1, "dp27"));
        Same(["suomi_kp31", "mosin_m39", "lahti_saloranta", "mosin_m39"], CampaignLineup.Weapons(Four, state));
    }

    [Fact]
    public void Weapon_OfAnEmptySlot_IsFreeForAnother()
    {
        var state = Platoon();
        state.LeaveEmpty(Four, 0);
        Assert.True(state.PickWeapon(Four, 3, "suomi_kp31"));
        var weapons = CampaignLineup.Weapons(Four, state);
        Assert.Null(weapons[0]);
        Assert.Equal("mosin_m39", weapons[1]); // nobody had to give his up
        Assert.Equal("suomi_kp31", weapons[3]);
    }

    [Fact]
    public void Apply_NeverHandsOutMoreThanThereAre()
    {
        var state = Platoon();
        // Picks written by hand (an old save, a lost armoury weapon): both want the one SMG.
        state.Lineup.Add(new SlotPick { Slot = 1, Weapon = "suomi_kp31" });
        state.Lineup.Add(new SlotPick { Slot = 3, Weapon = "ppsh41" });
        var weapons = CampaignMission.Apply(Four, state).Player.Select(s => s.Weapon).ToList();
        Assert.Single(weapons, w => w == "suomi_kp31");
        Assert.DoesNotContain("ppsh41", weapons);
        Assert.Equal(4, weapons.Count);
    }

    [Fact]
    public void Pool_IsTheSlotsWeaponsAndTheArmoury()
    {
        var state = Platoon();
        state.Armoury["ppsh41"] = 2;
        state.Armoury["mosin_m39"] = 1;
        var pool = CampaignLineup.Pool(Four, state);
        Assert.Equal(["suomi_kp31", "mosin_m39", "lahti_saloranta", "ppsh41"], pool.Keys);
        Assert.Equal([1, 3, 1, 2], pool.Values);
    }

    [Fact]
    public void StrandedSlot_IsNeitherPickedNorArmed()
    {
        var mission = CampaignTests.Mission([Slot("Alik. Korpela", "a", leader: true), Slot("Luutn. Saarinen", "p", stranded: true, weapon: "suomi_kp31")]);
        var state = Platoon();
        state.PickMan(mission, 1, "Sotm. Hakala");
        Assert.False(state.PickWeapon(mission, 0, "suomi_kp31"));
        Assert.Equal("Luutn. Saarinen", CampaignMission.Apply(mission, state).Player[1].Name);
    }

    [Fact]
    public void Lineup_RoundTripsInJson_OldSavesHaveNone()
    {
        var state = Platoon();
        state.PickMan(Four, 1, "Sotm. Hakala");
        state.LeaveEmpty(Four, 3);
        state.Armoury["ppsh41"] = 1;
        state.PickWeapon(Four, 1, "ppsh41");
        state.Recruited = 2;
        var spec = new Nmf.Content.Campaigns.CampaignSpec("c", new("C"), new("intro"),
            state.Men.Select(m => Man(m.Name)).ToList(), [new("m", new("b"), new("a"))]);
        var back = CampaignState.Load(state.ToJson(), spec);
        Assert.Equal(Going(state), Going(back));
        Assert.Equal(CampaignLineup.Weapons(Four, state), CampaignLineup.Weapons(Four, back));
        Assert.Equal(2, back.Recruited);

        var old = CampaignState.Load("""{ "Campaign": "c", "Chapter": 0, "Men": [ { "Name": "Alik. Korpela" } ] }""", spec);
        Assert.Empty(old.Lineup);
        Assert.Empty(old.Armoury);
        Assert.Equal(0, old.Recruited);
    }

    private static readonly Nmf.Content.Campaigns.CampaignSpec Spec = new("c", new("C"), new("intro"), [Man("Alik. Korpela")],
        [new("m", new("b"), new("a")), new("n", new("b2"), new("a2"))]);

    private static CampaignOutcome Ended(bool success, params string[] captured) =>
        new(success, 300, [new CampaignOutcome.Man("Alik. Korpela", Nmf.Sim.Combat.WoundLevel.None, false)],
            new CampaignTally(Nmf.Sim.Units.Side.Blue), captured);

    [Fact]
    public void Record_ClearsTheLineup()
    {
        var state = Platoon();
        state.LeaveEmpty(Four, 1);
        state.Record(Spec, Ended(false));
        Assert.Empty(state.Lineup);
    }

    [Fact]
    public void Record_Success_TheSearchedEnemiesWeaponsGoToTheArmoury()
    {
        var state = Platoon();
        state.Armoury["ppsh41"] = 1;
        state.Record(Spec, Ended(true, "ppsh41", "dp27", "mosin_9130", "mosin_9130"));
        Assert.Equal(new Dictionary<string, int> { ["ppsh41"] = 2, ["dp27"] = 1, ["mosin_9130"] = 2 }, state.Armoury);
    }

    [Fact]
    public void Record_Failure_NothingIsBroughtBack()
    {
        var state = Platoon();
        state.Record(Spec, Ended(false, "ppsh41"));
        Assert.Empty(state.Armoury);
    }
}
