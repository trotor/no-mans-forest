namespace Nmf.Client;

/// <summary>
/// Following the men without taking the camera away from the player: he may pan and zoom as he likes while the men he
/// commands stay in the inner part of the view; when they walk out of it the camera is drawn after them, and a pan that
/// would lose them is held back.
/// </summary>
public static class FollowLeash
{
    /// <summary>Share of the half view kept clear at each edge.</summary>
    public const float InsetShare = 0.3f;

    /// <summary>
    /// The camera centre on one axis: the nearest to <paramref name="centre"/> that keeps the men (<paramref name="min"/>..
    /// <paramref name="max"/>) <paramref name="inset"/> inside the view's edges; the men's middle when they are spread wider.
    /// </summary>
    public static float Centre(float centre, float half, float min, float max, float inset)
    {
        float lowest = max - (half - inset);
        float highest = min + (half - inset);
        return lowest > highest ? (min + max) / 2f : Math.Clamp(centre, lowest, highest);
    }

    public static string ButtonText(bool on, string language) => language == "fi"
        ? $"Seuraa: {(on ? "päällä" : "pois")} (L)"
        : $"Follow: {(on ? "on" : "off")} (L)";
}
