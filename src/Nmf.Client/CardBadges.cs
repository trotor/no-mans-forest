using Nmf.Sim.Combat;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>How bad a man's trouble is: the colour behind his badge on the card.</summary>
public enum BadgeTone
{
    None,
    /// <summary>Lightly wounded, out of ammo: yellow.</summary>
    Warning,
    /// <summary>Pinned down: orange.</summary>
    Bad,
    /// <summary>Badly wounded, out of the fight or broken: red.</summary>
    Critical,
    /// <summary>Dead or taken prisoner: grey.</summary>
    Gone,
}

public readonly record struct CardBadge(string Glyph, BadgeTone Tone);

/// <summary>The one sign on a man's portrait that tells at a glance what is wrong with him, the worst first.</summary>
public static class CardBadges
{
    public static CardBadge For(Unit unit)
    {
        if (unit.Wound == WoundLevel.Dead)
            return new("✖", BadgeTone.Gone);
        if (unit.IsCaptured)
            return new("⚑", BadgeTone.Gone);
        if (unit.Wound == WoundLevel.Incapacitated)
            return new("✚", BadgeTone.Critical);
        if (unit.MoraleState == MoraleState.Broken)
            return new("!!", BadgeTone.Critical);
        if (unit.Wound == WoundLevel.Serious)
            return new("✚", BadgeTone.Critical);
        if (unit.MoraleState == MoraleState.Pinned)
            return new("!", BadgeTone.Bad);
        if (unit.Wound == WoundLevel.Light)
            return new("✚", BadgeTone.Warning);
        if (unit.Weapon is not null && unit.Ammo <= 0 && unit.Magazines <= 0)
            return new("∅", BadgeTone.Warning);
        return new("", BadgeTone.None);
    }

    /// <summary>The morale bar's colour: green while he holds, yellow when shaken, red near breaking (0–1000).</summary>
    public static BadgeTone MoraleTone(int morale) =>
        morale >= 600 ? BadgeTone.None : morale >= 300 ? BadgeTone.Warning : BadgeTone.Critical;
}
