using Nmf.Sim.Mission;
using Nmf.Sim.Tactics;

namespace Nmf.Sim.Tests.Tactics;

public class TacticChoiceTests
{
    private static readonly TacticDef[] All = [TacticPlannerTests.FireAndMovement, TacticPlannerTests.Infiltration, TacticPlannerTests.LongFire];

    private static string[] Ids(IReadOnlyList<TacticDef> tactics) => tactics.Select(t => t.Id).ToArray();

    [Fact]
    public void NoRules_AllInOrder() =>
        Assert.Equal(["fire_and_movement", "infiltration", "long_fire"], Ids(TacticChoice.For(All, MissionTactics.None)));

    [Fact]
    public void Forbid_Removes() =>
        Assert.Equal(["fire_and_movement", "infiltration"], Ids(TacticChoice.For(All, new MissionTactics([], ["long_fire"]))));

    [Fact]
    public void Prefer_First_InOrder() =>
        Assert.Equal(["long_fire", "infiltration", "fire_and_movement"],
            Ids(TacticChoice.For(All, new MissionTactics(["long_fire", "infiltration"], []))));

    [Fact]
    public void ForbidAll_Empty() =>
        Assert.Empty(TacticChoice.For(All, new MissionTactics([], ["fire_and_movement", "infiltration", "long_fire"])));

    [Fact]
    public void Unknown_Listed() =>
        Assert.Equal(["pincer", "ambush"], TacticChoice.Unknown(All, new MissionTactics(["pincer", "infiltration"], ["ambush"])));
}
