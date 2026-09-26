namespace Nmf.Client;

/// <summary>How strongly the men are outlined in their side's colour: stronger when they are hard to find on the ground.</summary>
public enum OutlineStrength
{
    Normal,
    Strong,
}

/// <param name="WidthScale">Glow width against the normal one.</param>
/// <param name="Alpha">Opacity of the side's colour.</param>
/// <param name="DarkRim">A dark rim outside the colour, for contrast on light ground.</param>
/// <param name="GroundRing">A ring of the side's colour on the ground under him.</param>
public readonly record struct OutlineLook(float WidthScale, float Alpha, bool DarkRim, bool GroundRing);

public static class UnitOutlines
{
    public static OutlineLook Of(OutlineStrength strength) => strength == OutlineStrength.Strong
        ? new OutlineLook(1.9f, 1f, DarkRim: true, GroundRing: true)
        : new OutlineLook(1f, 0.85f, DarkRim: false, GroundRing: false);

    public static OutlineStrength Next(OutlineStrength strength) =>
        strength == OutlineStrength.Normal ? OutlineStrength.Strong : OutlineStrength.Normal;

    public static string Toast(OutlineStrength now, string language) => language == "fi"
        ? $"Selkeät reunat: {(now == OutlineStrength.Strong ? "päällä" : "pois")} (O)"
        : $"Clear outlines: {(now == OutlineStrength.Strong ? "on" : "off")} (O)";
}
