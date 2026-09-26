using Nmf.Client;

namespace Nmf.Client.Tests;

public class CardSizeTests
{
    [Fact]
    public void TheSizesGoRound_LargeSmallIconAndBack()
    {
        Assert.Equal(CardSize.Small, CardSizes.Next(CardSize.Large));
        Assert.Equal(CardSize.Icon, CardSizes.Next(CardSize.Small));
        Assert.Equal(CardSize.Large, CardSizes.Next(CardSize.Icon));
    }

    [Fact]
    public void EachSize_ShowsLessThanTheOneBefore()
    {
        var large = CardSizes.Layout(CardSize.Large);
        var small = CardSizes.Layout(CardSize.Small);
        var icon = CardSizes.Layout(CardSize.Icon);
        Assert.True(large.PortraitPx > small.PortraitPx && small.PortraitPx > icon.PortraitPx);
        Assert.True(large.BarHeightPx > small.BarHeightPx && small.BarHeightPx > icon.BarHeightPx);
        Assert.True(large.Details && !small.Details && !icon.Details); // condition and ammo lines
        Assert.True(small.Name && !icon.Name);
    }

    [Fact]
    public void TheButton_SaysWhatComesNext()
    {
        Assert.Equal("Kortit: pienet (K)", CardSizes.ButtonText(CardSize.Large, "fi"));
        Assert.Equal("Kortit: ikonit (K)", CardSizes.ButtonText(CardSize.Small, "fi"));
        Assert.Equal("Cards: large (K)", CardSizes.ButtonText(CardSize.Icon, "en"));
    }

    [Fact]
    public void ACompactCard_TellsTheRestInItsTooltip()
    {
        Assert.Equal("Alik. Korpela ★\nSeisoo\nEhjä\n71+2 · 2 kr",
            CardSizes.Tooltip("Alik. Korpela ★", "Seisoo", "Ehjä", "71+2 · 2 kr"));
    }
}
