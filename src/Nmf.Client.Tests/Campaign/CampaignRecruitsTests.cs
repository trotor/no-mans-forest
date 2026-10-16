using Nmf.Client.Campaign;
using Nmf.Content.Campaigns;
using static Nmf.Client.Tests.Campaign.CampaignTests;

namespace Nmf.Client.Tests.Campaign;

/// <summary>Replacements for the fallen (spec 2026-10-11-campaign-design §9c, "Täydennys").</summary>
public class CampaignRecruitsTests
{
    private static readonly RecruitSpec Recruits = new("Sotm.", ["Heikkinen", "Koskinen", "Järvinen", "Aho", "Lehtonen", "Salo"],
        (55, 75), 840, (50, 70), (15, 45), (30, 50));

    private static readonly CampaignSpec Spec = new("jatkosota", new("C"), new("intro"),
        [Man("Kers. Lehto", 85, 85), Man("Sotm. Aho"), Man("Sotm. Laine"), Man("Sotm. Hakala")],
        [new("m", new("b"), new("a"))], new Dictionary<string, int> { ["ppsh41"] = 1 }, Recruits);

    private static CampaignState Fresh() => CampaignState.New(Spec);

    private static void Fall(CampaignState state, string name) => state.Men.Single(m => m.Name == name).Status = ManStatus.Fallen;

    [Fact]
    public void New_TakesTheArmouryFromTheFile()
    {
        Assert.Equal(new Dictionary<string, int> { ["ppsh41"] = 1 }, Fresh().Armoury);
    }

    [Fact]
    public void Vacancies_AreTheFallenBelowTheStartingStrength()
    {
        var state = Fresh();
        Assert.Equal(0, CampaignRecruits.Vacancies(Spec, state));
        Assert.Empty(CampaignRecruits.Candidates(Spec, state));
        Fall(state, "Sotm. Laine");
        Fall(state, "Sotm. Hakala");
        state.Men.Single(m => m.Name == "Sotm. Aho").Status = ManStatus.Recovering; // still the platoon's
        Assert.Equal(2, CampaignRecruits.Vacancies(Spec, state));
    }

    [Fact]
    public void Candidates_AreThree_WithFreeNames_AndSkillsInTheirRanges()
    {
        var state = Fresh();
        Fall(state, "Sotm. Laine");
        var candidates = CampaignRecruits.Candidates(Spec, state);
        Assert.Equal(3, candidates.Count);
        Assert.Equal(3, candidates.Select(c => c.Name).Distinct().Count());
        foreach (var c in candidates)
        {
            Assert.StartsWith("Sotm. ", c.Name);
            Assert.NotEqual("Sotm. Aho", c.Name); // his name is taken
            Assert.InRange(c.Nerve, 55, 75);
            Assert.InRange(c.Marksmanship, 50, 70);
            Assert.InRange(c.Leadership, 15, 45);
            Assert.InRange(c.Experience, 30, 50);
            Assert.Equal(840, c.Morale);
        }
    }

    [Fact]
    public void Candidates_AreTheSameEachTime_AndNewAfterARecruit()
    {
        var state = Fresh();
        Fall(state, "Sotm. Laine");
        Fall(state, "Sotm. Hakala");
        var first = CampaignRecruits.Candidates(Spec, state);
        Assert.Equal(first, CampaignRecruits.Candidates(Spec, state));
        Assert.True(state.Recruit(Spec, 1));
        var joined = state.Men.Last();
        Assert.Equal(first[1].Name, joined.Name);
        Assert.Equal(1, state.Recruited);
        var next = CampaignRecruits.Candidates(Spec, state);
        Assert.DoesNotContain(next, c => c.Name == joined.Name);
    }

    [Fact]
    public void Recruit_JoinsFit_WithNoPoints_AndNoSpecialty()
    {
        var state = Fresh();
        Fall(state, "Sotm. Laine");
        var c = CampaignRecruits.Candidates(Spec, state)[0];
        Assert.True(state.Recruit(Spec, 0));
        var man = state.Men.Last();
        Assert.Equal((c.Name, c.Nerve, c.Morale, c.Marksmanship, c.Leadership, c.Experience),
            (man.Name, man.Nerve, man.Morale, man.Marksmanship, man.Leadership, man.Experience));
        Assert.Equal((ManStatus.Fit, 0, Nmf.Sim.Units.Specialty.None, 0), (man.Status, man.Xp, man.Specialty, man.Missions));
        Assert.Equal(0, CampaignRecruits.Vacancies(Spec, state));
    }

    [Fact]
    public void Recruit_Refused_WithoutAVacancy_OrABadChoice()
    {
        var state = Fresh();
        Assert.False(state.Recruit(Spec, 0));
        Fall(state, "Sotm. Laine");
        Assert.False(state.Recruit(Spec, 3));
        Assert.False(state.Recruit(Spec, -1));
        Assert.Equal(4, state.Men.Count);
    }

    [Fact]
    public void NoRecruitsInTheFile_NoReplacements()
    {
        var spec = Spec with { Recruits = null };
        var state = CampaignState.New(spec);
        Fall(state, "Sotm. Laine");
        Assert.Equal(0, CampaignRecruits.Vacancies(spec, state));
        Assert.False(state.Recruit(spec, 0));
    }

    [Fact]
    public void CampaignOver_NoReplacements()
    {
        var state = Fresh();
        Fall(state, "Sotm. Laine");
        state.Completed = true;
        Assert.Equal(0, CampaignRecruits.Vacancies(Spec, state));
    }

    [Fact]
    public void NamesRunOut_FewerCandidates()
    {
        var spec = Spec with { Recruits = Recruits with { Names = ["Heikkinen", "Aho"] } };
        var state = CampaignState.New(spec);
        Fall(state, "Sotm. Laine");
        Assert.Equal(["Sotm. Heikkinen"], CampaignRecruits.Candidates(spec, state).Select(c => c.Name));
        Assert.True(state.Recruit(spec, 0));
        Fall(state, "Sotm. Hakala");
        Assert.Empty(CampaignRecruits.Candidates(spec, state));
        Assert.Equal(1, CampaignRecruits.Vacancies(spec, state));
    }
}
