using Nmf.Client;
using Nmf.Client.Mission;
using Nmf.Content.Missions;
using Nmf.Sim.Mission;

namespace Nmf.Client.Tests.Mission;

/// <summary>The mission menu: missions grouped as the mission files say, the testbeds apart and only with --debug.</summary>
public class MissionMenuTests
{
    private static MissionLoader.Entry Mission(string id, string? group = null, int order = 0, bool debug = false) =>
        new(id, new MissionSpec(id, new Localized(id), new Localized("today"), "map", new Localized("go"), [], [], [],
            new Dictionary<string, Localized>(), Debug: debug,
            Group: group is null ? null : new Localized(group + " (en)", group), Order: order), null);

    [Fact]
    public void Groups_InOrder_TestbedsOnlyWithDebug_LastAndApart()
    {
        MissionLoader.Entry[] all =
        [
            Mission("hylky", "Talvi 1943", order: 1), Mission("iskuosasto", "Kesä 1942", order: 1), Mission("vartiokoe", debug: true),
            Mission("loose"), new MissionLoader.Entry("broken", null, "bad yaml"),
        ];
        var plain = MissionMenu.Groups(all, debug: false, "fi");
        Assert.Equal(["Kesä 1942", "Talvi 1943", "Muut tehtävät"], plain.Select(g => g.Heading));
        Assert.Equal(["iskuosasto"], plain[0].Missions.Select(m => m.Id));
        Assert.Equal(["broken", "loose"], plain[2].Missions.Select(m => m.Id));
        Assert.DoesNotContain(plain.SelectMany(g => g.Missions), m => m.Id == "vartiokoe");

        var debug = MissionMenu.Groups(all, debug: true, "en");
        Assert.Equal("Test scenarios", debug[^1].Heading);
        Assert.Equal(["vartiokoe"], debug[^1].Missions.Select(m => m.Id));
        Assert.Equal("Kesä 1942 (en)", debug[0].Heading);
    }

    [Fact]
    public void CoreMissions_AreGrouped_TheTestbedsMarkedDebug()
    {
        var root = Path.Combine(ContentLocator.FindContentRoot(AppContext.BaseDirectory)!, "core", "missions");
        var groups = MissionMenu.Groups(MissionLoader.LoadAll(root), debug: false, "fi");
        Assert.DoesNotContain(groups, g => g.Heading is "Muut tehtävät" or "Tehtävät"); // every real mission has its group
        Assert.Equal(["iskuosasto"], groups[0].Missions.Select(m => m.Id));
        Assert.Equal(["hylky"], groups[1].Missions.Select(m => m.Id));
        Assert.DoesNotContain(groups.SelectMany(g => g.Missions), m => m.Id is "testikentta" or "talvikoe" or "vartiokoe" or "tapahtumakoe");
    }
}
