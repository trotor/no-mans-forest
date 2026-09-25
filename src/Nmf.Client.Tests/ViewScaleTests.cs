using Nmf.Client;

namespace Nmf.Client.Tests;

public class ViewScaleTests
{
    [Fact]
    public void MinZoom_FitsTheWholeKilometreMap()
    {
        float zoom = ViewScale.MinZoomToFit(1280, 800, 32_000, 32_000);
        Assert.True(1280 / zoom >= 32_000 && 800 / zoom >= 32_000, $"zoom {zoom} does not show the whole map");
    }

    [Fact]
    public void MinZoom_SmallMapKeepsTheUsualLimit()
    {
        Assert.Equal(ViewScale.DefaultMinZoom, ViewScale.MinZoomToFit(1280, 800, 4096, 3072));
    }

    [Fact]
    public void Decorations_HiddenOnlyWhenZoomedFarOut()
    {
        Assert.True(ViewScale.ShowsDecorations(ViewScale.DefaultMinZoom));
        Assert.False(ViewScale.ShowsDecorations(0.03f));
    }
}
