namespace Nmf.Client;

/// <summary>Where the mission being started was chosen.</summary>
public enum LaunchSource
{
    /// <summary>The command line (also <c>--campaign-play</c>): all its options apply, screenshots and demos too.</summary>
    CommandLine,
    /// <summary>The menu, the campaign view or a restart: of the command line only the window and the language still apply.</summary>
    InGame,
}

public static class LaunchArgs
{
    public static string[] Effective(string[] commandLine, LaunchSource source) => source == LaunchSource.CommandLine
        ? commandLine
        : commandLine.Where(a => a.StartsWith("--window=", StringComparison.Ordinal) || a.StartsWith("--lang=", StringComparison.Ordinal)).ToArray();

    /// <summary><c>--campaign-play=id</c>: straight into that campaign's next mission.</summary>
    public static string? CampaignPlay(string[] commandLine) =>
        commandLine.Where(a => a.StartsWith("--campaign-play=", StringComparison.Ordinal)).Select(a => a["--campaign-play=".Length..]).FirstOrDefault();
}
