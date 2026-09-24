namespace Nmf.Client.Art;

/// <summary>Which ground texture (shader slot) each terrain name uses; unknown terrain is drawn as grass.</summary>
public static class TerrainArt
{
    public static readonly IReadOnlyList<string> TextureNames = ["grass", "forest", "swamp", "road"];

    public static int SlotFor(string terrain)
    {
        for (int i = 0; i < TextureNames.Count; i++)
            if (TextureNames[i] == terrain)
                return i;
        return 0;
    }
}
