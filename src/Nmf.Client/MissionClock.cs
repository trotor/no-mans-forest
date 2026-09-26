using System.Globalization;

namespace Nmf.Client;

/// <summary>The day and the time of day in the mission (its start plus the time fought), for the watch under the top bar.</summary>
public static class MissionClock
{
    private static readonly string[] FinnishDays = ["su", "ma", "ti", "ke", "to", "pe", "la"];

    /// <returns>"ti 14.7.1942 klo 03:10" / "Tue 14 July 1942, 03:10"; null when the mission gives no start.</returns>
    public static string? Text(DateTime? start, TimeSpan elapsed, string language)
    {
        if (start is not { } s)
            return null;
        var now = s + TimeSpan.FromMinutes(Math.Floor(elapsed.TotalMinutes));
        return language == "fi"
            ? $"{FinnishDays[(int)now.DayOfWeek]} {now.Day}.{now.Month}.{now.Year} klo {now.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : now.ToString("ddd d MMMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    }
}
