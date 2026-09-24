namespace Nmf.Client.Art;

/// <summary>Checks that a loaded image is large enough for the layout that describes it.</summary>
public static class ArtSize
{
    public static void Require(string file, int width, int height, int requiredWidth, int requiredHeight)
    {
        if (width < requiredWidth || height < requiredHeight)
            throw new FormatException($"{file}: image is {width}x{height} but its layout needs at least {requiredWidth}x{requiredHeight}");
    }
}
