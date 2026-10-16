using Nmf.Content.Campaigns;
using Nmf.Content.Missions;

namespace Nmf.Content.Tests;

/// <summary>The campaign file (spec 2026-10-11-campaign-design §9a).</summary>
public class CampaignLoaderTests
{
    private const string Good = """
        id: koe
        title: { en: "Test", fi: "Koe" }
        intro: { en: "Off we go.", fi: "Lähdetään." }
        roster:
          - { name: "Kers. Lehto", nerve: 90, morale: 920, marksmanship: 70, leadership: 85, experience: 85 }
          - { name: "Sotm. Aho", nerve: 85, marksmanship: 60, experience: 70 }
        chapters:
          - { mission: iskuosasto, before: { en: "Before.", fi: "Ennen." }, after: { en: "After.", fi: "Jälkeen." } }
        """;

    private static CampaignSpec Load(string yaml)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-campaign-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "campaign.yaml"), yaml);
            return CampaignLoader.Load(dir.FullName);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Loads_TheFile()
    {
        var c = Load(Good);
        Assert.Equal("koe", c.Id);
        Assert.Equal("Koe", c.Title.In("fi"));
        Assert.Equal("Lähdetään.", c.Intro.In("fi"));
        Assert.Equal(["Kers. Lehto", "Sotm. Aho"], c.Roster.Select(m => m.Name));
        Assert.Equal(new CampaignMan("Kers. Lehto", 90, 920, 70, 85, 85), c.Roster[0]);
        var chapter = Assert.Single(c.Chapters);
        Assert.Equal("iskuosasto", chapter.Mission);
        Assert.Equal("Jälkeen.", chapter.After.In("fi"));
    }

    [Fact]
    public void Leadership_NotGiven_IsLow()
    {
        // The mission default (100) would rank every private above his corporal.
        var aho = Load(Good).Roster[1];
        Assert.Equal(40, aho.Leadership);
        Assert.Null(aho.Morale);
        Assert.Equal(50, Load(Good.Replace("nerve: 85, ", "")).Roster[1].Nerve);
    }

    [Theory]
    [InlineData("chapters:\n  - { mission", "chapters: []\n#", "a campaign needs at least one chapter")]
    [InlineData("\"Sotm. Aho\"", "\"Kers. Lehto\"", "duplicate")]
    [InlineData("leadership: 85", "leadership: 50", "leader")]
    [InlineData("nerve: 90", "nerve: 190", "nerve")]
    [InlineData("mission: iskuosasto, ", "", "mission")]
    public void Rejects_BadFiles(string from, string to, string reason)
    {
        var ex = Assert.Throws<ContentLoadException>(() => Load(Good.Replace(from, to)));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Rejects_AnEmptyRoster()
    {
        string yaml = string.Join("\n", Good.Split('\n').Where(l => !l.TrimStart().StartsWith("- { name", StringComparison.Ordinal)));
        Assert.Contains("roster", Assert.Throws<ContentLoadException>(() => Load(yaml)).Message);
    }

    [Fact]
    public void Roster_Specialty_IsRead_UnknownRejected()
    {
        var c = Load(Good.Replace("nerve: 85,", "specialty: medic, nerve: 85,"));
        Assert.Equal(Nmf.Sim.Units.Specialty.Medic, c.Roster[1].Specialty);
        Assert.Equal(Nmf.Sim.Units.Specialty.None, c.Roster[0].Specialty);
        Assert.Contains("specialty", Assert.Throws<ContentLoadException>(() => Load(Good.Replace("nerve: 85,", "specialty: cook, nerve: 85,"))).Message);
    }

    [Fact]
    public void Rejects_AWeaponInTheRoster()
    {
        // The slot's weapon applies; a campaign man brings none (spec §9a).
        Assert.Throws<ContentLoadException>(() => Load(Good.Replace("nerve: 85,", "weapon: mosin_m39, nerve: 85,")));
    }

    private const string Recruits = """
        armoury: { ppsh41: 1, mosin_m39: 2 }
        recruits:
          rank: "Sotm."
          names: [Heikkinen, Koskinen]
          nerve: [55, 75]
          morale: 840
          marksmanship: [50, 70]
          leadership: [15, 45]
          experience: [30, 50]
        """;

    [Fact]
    public void Armoury_AndRecruits_AreRead()
    {
        var c = Load(Good + "\n" + Recruits);
        Assert.Equal(new Dictionary<string, int> { ["ppsh41"] = 1, ["mosin_m39"] = 2 }, c.Armoury);
        var r = c.Recruits!;
        Assert.Equal("Sotm.", r.Rank);
        Assert.Equal(["Heikkinen", "Koskinen"], r.Names);
        Assert.Equal((55, 75), r.Nerve);
        Assert.Equal(840, r.Morale);
        Assert.Equal((50, 70), r.Marksmanship);
        Assert.Equal((15, 45), r.Leadership);
        Assert.Equal((30, 50), r.Experience);
    }

    [Fact]
    public void Armoury_AndRecruits_AreOptional()
    {
        var c = Load(Good);
        Assert.Empty(c.Armoury);
        Assert.Null(c.Recruits);
    }

    [Theory]
    [InlineData("ppsh41: 1", "ppsh41: -1", "armoury")]
    [InlineData("nerve: [55, 75]", "nerve: [75, 55]", "recruits.nerve")]
    [InlineData("nerve: [55, 75]", "nerve: [55, 175]", "recruits.nerve")]
    [InlineData("nerve: [55, 75]", "nerve: [55]", "recruits.nerve")]
    [InlineData("names: [Heikkinen, Koskinen]", "names: []", "recruits.names")]
    public void Rejects_BadRecruits(string from, string to, string reason)
    {
        var ex = Assert.Throws<ContentLoadException>(() => Load(Good + "\n" + Recruits.Replace(from, to)));
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Core_Campaigns_PointAtRealMissions_AndHoldTheirNamedMen()
    {
        string content = Path.Combine(CoreContentTests.RepoRoot(), "content", "core");
        var campaigns = CampaignLoader.LoadAll(Path.Combine(content, "campaigns"));
        Assert.Contains(campaigns, c => c.Id == "jatkosota");
        var weapons = Directory.GetFiles(Path.Combine(content, "weapons"), "*.yaml").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        foreach (var campaign in campaigns)
        {
            foreach (var weapon in campaign.Armoury.Keys)
                Assert.True(weapons.Contains(weapon), $"{campaign.Id}: the armoury's {weapon} is no weapon");
            var roster = campaign.Roster.Select(m => m.Name).ToHashSet();
            foreach (var chapter in campaign.Chapters)
            {
                var mission = MissionLoader.Load(Path.Combine(content, "missions", chapter.Mission));
                Assert.False(mission.Debug, $"{campaign.Id}: {chapter.Mission} is a testbed");
                // The orders and the event texts name the mission's men: the campaign has them all.
                foreach (var man in mission.Player.Where(s => !s.Stranded))
                    Assert.True(roster.Contains(man.Name), $"{campaign.Id}: {man.Name} of {chapter.Mission} is not in the roster");
            }
        }
    }

    [Fact]
    public void Jatkosota_LossesCarryOver_TheWreckPatrolIsFromTheStrikeDetachment()
    {
        string content = Path.Combine(CoreContentTests.RepoRoot(), "content", "core");
        var campaign = CampaignLoader.Load(Path.Combine(content, "campaigns", "jatkosota"));
        Assert.Equal(["iskuosasto", "hylky"], campaign.Chapters.Select(c => c.Mission));
        var first = MissionLoader.Load(Path.Combine(content, "missions", "iskuosasto")).Player.Select(s => s.Name);
        var second = MissionLoader.Load(Path.Combine(content, "missions", "hylky")).Player.Select(s => s.Name);
        Assert.True(first.Intersect(second).Count() >= 3);
    }
}
