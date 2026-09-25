namespace Nmf.Client;

/// <summary>Zoom limits that depend on the map: on a 1 km map the player can still zoom out to see all of it.</summary>
public static class ViewScale
{
    public const float DefaultMinZoom = 0.12f;
    /// <summary>Below this zoom trees, rocks and bushes are too small to matter and are not drawn (the ground still shows the forest).</summary>
    public const float DecorationMinZoom = 0.06f;

    /// <summary>The usual limit, or lower when that is needed to fit the whole world (with a small margin) in the view.</summary>
    public static float MinZoomToFit(float viewWidth, float viewHeight, float worldWidth, float worldHeight)
    {
        float fit = Math.Min(viewWidth / worldWidth, viewHeight / worldHeight) * 0.95f;
        return Math.Min(DefaultMinZoom, fit);
    }

    public static bool ShowsDecorations(float zoom) => zoom >= DecorationMinZoom;
}
