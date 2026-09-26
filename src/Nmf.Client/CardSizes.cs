namespace Nmf.Client;

/// <summary>How big the men's cards along the bottom are drawn: the player picks, to see more of the map.</summary>
public enum CardSize
{
    /// <summary>Portrait, name, what he does, his condition, ammo and bars.</summary>
    Large,
    /// <summary>A smaller portrait, name, what he does and the bars; the rest in the tooltip.</summary>
    Small,
    /// <summary>Only the portrait and the bars; everything else in the tooltip.</summary>
    Icon,
}

/// <param name="PortraitPx">Portrait side in base pixels.</param>
/// <param name="Name">Name and status lines shown.</param>
/// <param name="Details">Condition and ammo lines shown.</param>
/// <param name="FontSize">Name font size; the status line is one smaller.</param>
/// <param name="BarHeightPx">Approximate height of the whole card bar, for the camera's overscroll.</param>
public readonly record struct CardLayout(int PortraitPx, bool Name, bool Details, int FontSize, float BarHeightPx);

public static class CardSizes
{
    public static CardSize Next(CardSize size) => size switch
    {
        CardSize.Large => CardSize.Small,
        CardSize.Small => CardSize.Icon,
        _ => CardSize.Large,
    };

    public static CardLayout Layout(CardSize size) => size switch
    {
        CardSize.Small => new CardLayout(56, Name: true, Details: false, FontSize: 13, BarHeightPx: 130f),
        CardSize.Icon => new CardLayout(36, Name: false, Details: false, FontSize: 13, BarHeightPx: 90f),
        _ => new CardLayout(96, Name: true, Details: true, FontSize: 15, BarHeightPx: 190f),
    };

    /// <summary>The toggle's label: the size a press switches to.</summary>
    public static string ButtonText(CardSize current, string language)
    {
        bool fi = language == "fi";
        return Next(current) switch
        {
            CardSize.Small => fi ? "Kortit: pienet (K)" : "Cards: small (K)",
            CardSize.Icon => fi ? "Kortit: ikonit (K)" : "Cards: icons (K)",
            _ => fi ? "Kortit: isot (K)" : "Cards: large (K)",
        };
    }

    /// <summary>What a compact card leaves out, for its tooltip.</summary>
    public static string Tooltip(string name, string status, string condition, string ammo) =>
        string.Join("\n", name, status, condition, ammo);
}
