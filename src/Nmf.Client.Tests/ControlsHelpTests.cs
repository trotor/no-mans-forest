using Nmf.Client;

namespace Nmf.Client.Tests;

public class ControlsHelpTests
{
    [Theory]
    [InlineData("fi")]
    [InlineData("en")]
    public void EveryKeyTheGameHandles_IsInTheHelp(string language)
    {
        var keys = string.Join("\n", ControlsHelp.Lines(language).Select(l => l.Keys));
        foreach (var key in ControlsHelp.HandledKeys)
            Assert.True(keys.Contains(key, StringComparison.Ordinal), $"'{key}' missing from the {language} help");
    }

    [Fact]
    public void BothLanguages_HaveTheSameRows() =>
        Assert.Equal(ControlsHelp.Lines("fi").Count, ControlsHelp.Lines("en").Count);

    [Fact]
    public void TheNewControls_AreExplained()
    {
        var fi = string.Join("\n", ControlsHelp.Lines("fi").Select(l => l.Keys + " " + l.What));
        Assert.Contains("hyökkää eteenpäin", fi);
        Assert.Contains("jonoon", fi);
        Assert.Contains("Oikea nappi", fi);
        Assert.Contains("hiivi", fi);
        Assert.Contains("suora rynnäkkö", fi);
        Assert.Contains("suunnitelma", fi);
        Assert.Contains("R", fi);
    }
}
