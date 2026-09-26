using Nmf.Sim.Units;

namespace Nmf.Client;

public enum BodyMark
{
    /// <summary>Still in the fight.</summary>
    None,
    /// <summary>Fallen, and none of our men has been through his pockets.</summary>
    Unsearched,
    /// <summary>Fallen, and our men have searched him: no need to go back.</summary>
    Searched,
}

/// <summary>How a man out of the fight is marked for the player: the ones our men have searched apart from the rest.</summary>
public static class BodyMarks
{
    public static BodyMark Of(Unit unit, Side player) =>
        !unit.IsOutOfAction ? BodyMark.None
        : unit.WasSearchedBy(player) ? BodyMark.Searched
        : BodyMark.Unsearched;
}
