using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Mission;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Campaign;

/// <summary>Spec 2026-10-11-campaign-design §9b: levels from points, what grows, and specialising.</summary>
public class CampaignGrowthTests
{
    private static readonly CampaignSpec Spec = new("c", new Localized("C"), new Localized("intro"),
        [CampaignTests.Man("Alik. Korpela", 90, 90), CampaignTests.Man("Sotm. Mäkinen"), CampaignTests.Man("Sotm. Laine")],
        [new CampaignChapter("m", new Localized("b"), new Localized("a")), new CampaignChapter("n", new Localized("b2"), new Localized("a2"))]);

    private static CampaignManState Man(CampaignState state, string name) => state.Men.Single(m => m.Name == name);

    /// <summary>A tally where <paramref name="name"/> hit an enemy <paramref name="hits"/> times.</summary>
    private static CampaignTally Hits(string name, int hits)
    {
        var tally = new CampaignTally(Side.Blue);
        var ours = new UnitId(1);
        for (int i = 0; i < hits; i++)
        {
            var enemy = new UnitId(100 + i);
            tally.Observe([new ShotFired(i, ours, default, default, enemy), new UnitWounded(i, enemy, WoundLevel.Light)],
                id => id == ours ? new CampaignTally.Seen(Side.Blue, name, default) : new CampaignTally.Seen(Side.Red, "x", new Vec2(1, 1)), []);
        }
        return tally;
    }

    private static CampaignOutcome.Man M(string name, WoundLevel wound, bool captured, bool Led = false) => new(name, wound, captured, Led);

    private static CampaignOutcome Outcome(bool success, CampaignTally tally, params CampaignOutcome.Man[] men) => new(success, 300, men, tally);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(24, 1)]
    [InlineData(25, 2)]
    [InlineData(69, 2)]
    [InlineData(70, 3)]
    [InlineData(139, 3)]
    [InlineData(140, 4)]
    [InlineData(1000, 4)]
    public void Level_FromPoints(int xp, int level) => Assert.Equal(level, CampaignGrowth.Level(xp));

    [Fact]
    public void OneLevelUp_ExperienceAndNerveGrowFive()
    {
        var state = CampaignState.New(Spec);
        var laine = Man(state, "Sotm. Laine");
        var (experience, nerve) = (laine.Experience, laine.Nerve);
        var report = state.Record(Spec, Outcome(true, Hits("Sotm. Laine", 2), M("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal(26, laine.Xp); // 10 + 10 + 2 × 3
        Assert.Equal((experience + 5, nerve + 5), (laine.Experience, laine.Nerve));
        Assert.Equal((1, 2), (report[0].LevelBefore, report[0].LevelAfter));
    }

    [Fact]
    public void TwoLevelsInOneMission_GrowTwice()
    {
        var state = CampaignState.New(Spec);
        var laine = Man(state, "Sotm. Laine");
        laine.Xp = 22;
        var (experience, nerve) = (laine.Experience, laine.Nerve);
        var report = state.Record(Spec, Outcome(true, Hits("Sotm. Laine", 20), M("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal(22 + 20 + 60, laine.Xp);
        Assert.Equal((experience + 10, nerve + 10), (laine.Experience, laine.Nerve));
        Assert.Equal((1, 3), (report[0].LevelBefore, report[0].LevelAfter));
    }

    [Fact]
    public void NoLevelUp_NoGrowthOfExperienceOrNerve()
    {
        var state = CampaignState.New(Spec);
        var laine = Man(state, "Sotm. Laine");
        var (experience, nerve) = (laine.Experience, laine.Nerve);
        state.Record(Spec, Outcome(false, new CampaignTally(Side.Blue), M("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal((experience, nerve), (laine.Experience, laine.Nerve));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(5, 2)]
    [InlineData(8, 4)]
    [InlineData(15, 4)]
    public void Marksmanship_GrowsOnePerTwoHits_AtMostFour(int hits, int growth)
    {
        var state = CampaignState.New(Spec);
        var laine = Man(state, "Sotm. Laine");
        int before = laine.Marksmanship;
        state.Record(Spec, Outcome(false, Hits("Sotm. Laine", hits), M("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal(before + growth, laine.Marksmanship);
    }

    [Fact]
    public void Leader_WhoComesThrough_GainsLeadership_TheFallenDoNot()
    {
        var state = CampaignState.New(Spec);
        var (korpela, makinen, laine) = (Man(state, "Alik. Korpela"), Man(state, "Sotm. Mäkinen"), Man(state, "Sotm. Laine"));
        var before = (korpela.Leadership, makinen.Leadership, laine.Leadership);
        state.Record(Spec, Outcome(false, new CampaignTally(Side.Blue),
            M("Alik. Korpela", WoundLevel.Dead, false, Led: true),
            M("Sotm. Mäkinen", WoundLevel.Serious, false, Led: true), // took over, came out badly wounded
            M("Sotm. Laine", WoundLevel.None, false)));
        Assert.Equal((before.Item1, before.Item2 + 5, before.Item3), (korpela.Leadership, makinen.Leadership, laine.Leadership));
    }

    [Fact]
    public void Skills_StopAt100()
    {
        var state = CampaignState.New(Spec);
        var laine = Man(state, "Sotm. Laine");
        (laine.Xp, laine.Experience, laine.Nerve, laine.Marksmanship, laine.Leadership) = (22, 98, 97, 99, 100);
        state.Record(Spec, Outcome(true, Hits("Sotm. Laine", 20), M("Sotm. Laine", WoundLevel.None, false, Led: true)));
        Assert.Equal((100, 100, 100, 100), (laine.Experience, laine.Nerve, laine.Marksmanship, laine.Leadership));
    }

    [Fact]
    public void Specialise_FromLevelTwo_OnceOnly_NotTheFallen()
    {
        var state = CampaignState.New(Spec);
        var (korpela, makinen, laine) = (Man(state, "Alik. Korpela"), Man(state, "Sotm. Mäkinen"), Man(state, "Sotm. Laine"));
        Assert.False(CampaignGrowth.CanSpecialise(laine));
        Assert.False(state.Specialise("Sotm. Laine", Specialty.Medic)); // a recruit
        laine.Xp = 25;
        Assert.True(CampaignGrowth.CanSpecialise(laine));
        Assert.False(state.Specialise("Sotm. Laine", Specialty.None));
        Assert.True(state.Specialise("Sotm. Laine", Specialty.Medic));
        Assert.Equal(Specialty.Medic, laine.Specialty);
        Assert.False(state.Specialise("Sotm. Laine", Specialty.Scout)); // for good
        Assert.Equal(Specialty.Medic, laine.Specialty);
        (makinen.Xp, makinen.Status) = (30, ManStatus.Fallen);
        Assert.False(state.Specialise("Sotm. Mäkinen", Specialty.Scout));
        (korpela.Xp, korpela.Status) = (30, ManStatus.Recovering);
        Assert.True(state.Specialise("Alik. Korpela", Specialty.Marksman)); // a man in hospital can learn
        Assert.False(state.Specialise("Nobody", Specialty.Scout));
    }

    [Fact]
    public void Specialty_IsSaved_AndAnOldSaveHasNone()
    {
        var state = CampaignState.New(Spec);
        Man(state, "Sotm. Laine").Xp = 30;
        state.Specialise("Sotm. Laine", Specialty.Sapper);
        var json = state.ToJson();
        Assert.Contains("\"Sapper\"", json);
        Assert.Equal(Specialty.Sapper, Man(CampaignState.Load(json, Spec), "Sotm. Laine").Specialty);
        var old = json.Replace("\"Specialty\": \"Sapper\",", "").Replace("\"Specialty\": \"None\",", "");
        Assert.DoesNotContain("Specialty", old);
        Assert.All(CampaignState.Load(old, Spec).Men, m => Assert.Equal(Specialty.None, m.Specialty));
    }

    [Fact]
    public void RosterSpecialty_StartsWithTheMan()
    {
        var spec = Spec with { Roster = [CampaignTests.Man("Alik. Korpela", 90) with { Specialty = Specialty.Scout }] };
        Assert.Equal(Specialty.Scout, CampaignState.New(spec).Men[0].Specialty);
    }

    [Fact]
    public void Apply_TheMansSpecialty_ReplacesTheSlots()
    {
        var mission = CampaignTests.Mission(
        [
            CampaignTests.Slot("Alik. Korpela", leader: true) with { Specialty = Specialty.Sapper },
            CampaignTests.Slot("Sotm. Mäkinen"),
        ]);
        var state = CampaignState.New(Spec);
        Man(state, "Sotm. Mäkinen").Specialty = Specialty.Medic;
        var applied = CampaignMission.Apply(mission, state);
        Assert.Equal(Specialty.None, applied.Player.Single(p => p.Name == "Alik. Korpela").Specialty);
        Assert.Equal(Specialty.Medic, applied.Player.Single(p => p.Name == "Sotm. Mäkinen").Specialty);
    }
}
