namespace Nmf.Client.Tests;

public class LaunchArgsTests
{
    private static readonly string[] CommandLine = ["--campaign-play=jatkosota", "--screenshot=x.png", "--window=1280x800", "--lang=fi"];

    [Fact]
    public void FromTheCommandLine_EverythingApplies()
    {
        // --campaign-play is for screenshots and checks: the screenshot (and with it no saving) must still apply.
        Assert.Equal(CommandLine, LaunchArgs.Effective(CommandLine, LaunchSource.CommandLine));
    }

    [Fact]
    public void ChosenInTheGame_OnlyTheWindowAndLanguage()
    {
        Assert.Equal(["--window=1280x800", "--lang=fi"], LaunchArgs.Effective(CommandLine, LaunchSource.InGame));
    }

    [Fact]
    public void CampaignPlay_TheCampaignAsked()
    {
        Assert.Equal("jatkosota", LaunchArgs.CampaignPlay(CommandLine));
        Assert.Null(LaunchArgs.CampaignPlay(["--mission=hylky"]));
    }
}
