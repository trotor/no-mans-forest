using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Sim.Combat;
using Nmf.Sim.Mission;

namespace Nmf.Client.Tests.Campaign;

/// <summary>What the campaign view, the menu and the end screen say (spec 2026-10-11-campaign-design §9a, "Pelaajalle").</summary>
public class CampaignTextTests
{
    private static readonly CampaignSpec Spec = new("c", new Localized("Lehto's Platoon", "Lehdon joukkue"), new Localized("Summer.", "Kesä."),
        [CampaignTests.Man("Alik. Korpela", 90, 90), CampaignTests.Man("Sotm. Mäkinen", 40, 80), CampaignTests.Man("Sotm. Laine", 40, 75),
            CampaignTests.Man("Sotm. Aho", 30, 70), CampaignTests.Man("Sotm. Kivelä", 30, 60)],
        [new CampaignChapter("m", new Localized("Before one.", "Ennen yhtä."), new Localized("After one.", "Yhden jälkeen.")),
            new CampaignChapter("n", new Localized("Before two.", "Ennen kahta."), new Localized("After two.", "Kahden jälkeen."))]);

    private static readonly MissionSpec Mission = CampaignTests.Mission(
        [CampaignTests.Slot("Alik. Korpela", "a", leader: true, weapon: "suomi_kp31"), CampaignTests.Slot("Sotm. Mäkinen", "a"),
            CampaignTests.Slot("Sotm. Laine", "a")]);

    private static Localized? Title(string id) => id switch { "m" => new Localized("Strike", "Isku"), "n" => new Localized("Wreck", "Hylky"), _ => null };

    private static string Weapon(string id) => id == "suomi_kp31" ? "Suomi KP/-31" : "Kivääri";

    [Fact]
    public void MenuButton_StartOrGoOn()
    {
        Assert.Equal("Aloita kampanja: Lehdon joukkue\nLuku 1/2: Isku", CampaignText.MenuButton(Spec, null, Title, "fi"));
        var state = CampaignState.New(Spec);
        state.Chapter = 1;
        state.History.Add(new CampaignHistoryEntry("m", true, 300));
        Assert.Equal("Jatka kampanjaa: Lehdon joukkue\nLuku 2/2: Hylky", CampaignText.MenuButton(Spec, state, Title, "fi"));
        Assert.Equal("Continue the campaign: Lehto's Platoon\nChapter 2/2: Wreck", CampaignText.MenuButton(Spec, state, Title, "en"));
        state.Completed = true;
        Assert.Equal("Lehdon joukkue: kampanja on päättynyt\nLuku 2/2: Hylky", CampaignText.MenuButton(Spec, state, Title, "fi"));
    }

    [Fact]
    public void Story_IntroFirst_ThenWhatCameBefore_AndARepeatSaysSo()
    {
        var state = CampaignState.New(Spec);
        Assert.Equal("Kesä.\n\nEnnen yhtä.", CampaignText.Story(Spec, state, "fi"));
        state.History.Add(new CampaignHistoryEntry("m", false, 300));
        Assert.Equal("Edellinen yritys epäonnistui. Tappiot jäävät, ja luku pelataan uudelleen.\n\nEnnen yhtä.", CampaignText.Story(Spec, state, "fi"));
        state.Chapter = 1;
        state.History.Add(new CampaignHistoryEntry("m", true, 300));
        Assert.Equal("Yhden jälkeen.\n\nEnnen kahta.", CampaignText.Story(Spec, state, "fi"));
        state.Completed = true;
        Assert.Equal("Kahden jälkeen.", CampaignText.Story(Spec, state, "fi"));
    }

    [Fact]
    public void Lineup_SlotWeapon_NameAndPoints_TheRestBelow()
    {
        var state = CampaignState.New(Spec);
        state.Men[0].Xp = 33;
        state.Men[0].Specialty = Nmf.Sim.Units.Specialty.Marksman;
        state.Men.Single(m => m.Name == "Sotm. Mäkinen").Status = ManStatus.Fallen;
        state.Men.Single(m => m.Name == "Sotm. Aho").Status = ManStatus.Recovering;
        Assert.Equal(
        [
            "Alik. Korpela (johtaja) — Suomi KP/-31 — Kokenut (33 p), tarkka-ampuja",
            "Sotm. Kivelä — Kivääri — Alokas (0 p)",
            "Sotm. Laine — Kivääri — Alokas (0 p)",
        ], CampaignText.Lineup(Mission, state, Weapon, "fi"));
        Assert.Equal("Alik. Korpela (leader) — Suomi KP/-31 — Seasoned (33 pts), marksman", CampaignText.Lineup(Mission, state, Weapon, "en")[0]);
        Assert.Equal(["Toipuu: Sotm. Aho", "Kaatuneet: Sotm. Mäkinen"], CampaignText.Others(Mission, state, "fi"));
        state.Men.Single(m => m.Name == "Sotm. Aho").Status = ManStatus.Fit; // back: more experienced than Kivelä, he takes the slot
        Assert.Equal(["Odottaa: Sotm. Kivelä", "Kaatuneet: Sotm. Mäkinen"], CampaignText.Others(Mission, state, "fi"));
        Assert.Equal(["Waiting: Sotm. Kivelä", "Fallen: Sotm. Mäkinen"], CampaignText.Others(Mission, state, "en"));
    }

    [Fact]
    public void Report_PointsAndWhereEachStands()
    {
        var reports = new[]
        {
            new ManReport("Alik. Korpela", 33, ManStatus.Fit, 1, 1, WoundLevel.Light, 1, 2),
            new ManReport("Sotm. Laine", 10, ManStatus.Recovering, 0, 0, WoundLevel.Incapacitated),
            new ManReport("Sotm. Aho", 0, ManStatus.Fallen, 0, 0, WoundLevel.Dead),
        };
        Assert.Equal(
        [
            "Alik. Korpela: +33 kokemusta (osumia 1, kaatoja 1) — kunnossa — nyt Kokenut",
            "Sotm. Laine: +10 kokemusta — toipuu, ei mukana seuraavassa tehtävässä",
            "Sotm. Aho: kaatui",
        ], CampaignText.Report(reports, "fi"));
        Assert.Equal("Alik. Korpela: +33 XP (hits 1, kills 1) — fit — now Seasoned", CampaignText.Report(reports, "en")[0]);
    }

    [Fact]
    public void NobodyLeft_Says_StartAgain()
    {
        Assert.Contains("alusta", CampaignText.NobodyLeft("fi"));
        Assert.Contains("again", CampaignText.NobodyLeft("en"));
    }

    [Fact]
    public void Levels_Named()
    {
        Assert.Equal(["Alokas", "Kokenut", "Veteraani", "Konkari"], Enumerable.Range(1, 4).Select(l => CampaignText.LevelName(l, "fi")));
        Assert.Equal(["Recruit", "Seasoned", "Veteran", "Old hand"], Enumerable.Range(1, 4).Select(l => CampaignText.LevelName(l, "en")));
    }

    [Fact]
    public void Specialisable_TheSeasonedWithoutOne_NotTheFallen()
    {
        var state = CampaignState.New(Spec);
        state.Men[0].Xp = 30; // Korpela
        state.Men[1].Xp = 30; // Mäkinen, fallen
        state.Men[1].Status = ManStatus.Fallen;
        state.Men[2].Xp = 80; // Laine, has one
        state.Men[2].Specialty = Nmf.Sim.Units.Specialty.Scout;
        state.Men[3].Xp = 25; // Aho, recovering
        state.Men[3].Status = ManStatus.Recovering;
        Assert.Equal(["Alik. Korpela", "Sotm. Aho"], CampaignText.Specialisable(state).Select(m => m.Name));
        Assert.Equal("Alik. Korpela — Kokenut (30 p): valitse erikoistuminen", CampaignText.SpecialiseLine(state.Men[0], "fi"));
        Assert.Equal("Alik. Korpela — Seasoned (30 pts): choose a specialty", CampaignText.SpecialiseLine(state.Men[0], "en"));
    }
}
