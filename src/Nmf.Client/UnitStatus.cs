using Nmf.Sim.Combat;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>Short status text for portrait cards.</summary>
public static class UnitStatus
{
    /// <summary>What the man is doing, in the player's language.</summary>
    public static string Describe(Unit unit, string language = "en") => Translate(DescribeEnglish(unit), language);

    private static readonly Dictionary<string, string> Finnish = new()
    {
        ["Dead"] = "Kaatunut", ["Captured"] = "Vangittu", ["Down"] = "Taistelukyvytön", ["Broken"] = "Murtunut",
        ["Pinned"] = "Lamautunut", ["Melee"] = "Lähitaistelussa", ["Throwing"] = "Heittää kranaattia", ["Looting"] = "Tutkii",
        ["Reloading"] = "Lataa", ["Area fire"] = "Aluetuli", ["Firing"] = "Ampuu", ["Getting down"] = "Menee maahan",
        ["Getting up"] = "Nousee", ["Out of ammo"] = "Patruunat loppu", ["Bounding"] = "Syöksyy", ["Covering fire"] = "Suojatuli",
        ["Taking cover"] = "Hakee suojaa", ["Assaulting"] = "Rynnäköi", ["Sneaking"] = "Hiipii", ["Crawling"] = "Ryömii",
        ["Prone"] = "Maassa", ["Crouching"] = "Kyykyssä", ["Standing"] = "Seisoo", ["Running"] = "Juoksee", ["Walking"] = "Kävelee",
        ["Unhurt"] = "Ehjä", ["Light wound"] = "Lievästi haavoittunut", ["Serious wound"] = "Vakavasti haavoittunut",
        ["Free fire"] = "Vapaa tuli", ["Return fire"] = "Vastatuli", ["Hold fire"] = "Tulenavauskielto", ["Fire at will"] = "Vapaa tuli",
    };

    /// <summary>Every text the cards show (the Finnish table's keys).</summary>
    public static IReadOnlyCollection<string> States => Finnish.Keys;

    public static string Translate(string english, string language) =>
        language == "fi" && Finnish.TryGetValue(english, out var fi) ? fi : english;

    private static string DescribeEnglish(Unit unit)
    {
        if (unit.Wound == WoundLevel.Dead)
            return "Dead";
        if (unit.IsCaptured)
            return "Captured";
        if (unit.IsOutOfAction)
            return "Down";
        if (unit.MoraleState == MoraleState.Broken)
            return "Broken";
        if (unit.MoraleState == MoraleState.Pinned)
            return "Pinned";
        if (unit.Action == CombatAction.Melee)
            return "Melee";
        if (unit.Action == CombatAction.Throwing)
            return "Throwing";
        if (unit.Action == CombatAction.Looting)
            return "Looting";
        if (unit.Action == CombatAction.Reloading)
            return "Reloading";
        if (unit.AreaTarget is not null && unit.MoveTarget is null && unit.Target is null)
            return "Area fire";
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
            return "Firing";
        if (unit.TargetStance is { } target)
            return target > unit.Stance ? "Getting down" : "Getting up";
        bool moving = unit.MoveTarget is not null;
        if (!moving && unit.Weapon is not null && unit.OutOfAmmo)
            return "Out of ammo";
        if (unit.AttackRole != AttackRole.None && moving)
            return "Bounding";
        if (unit.AttackRole != AttackRole.None)
            return "Covering fire";
        if (moving && unit.TakingCover)
            return "Taking cover";
        if (moving && unit.AssaultTarget is not null)
            return "Assaulting";
        if (moving && unit.MoveMode == MoveMode.Sneak)
            return "Sneaking";
        return unit.Stance switch
        {
            Stance.Prone => moving ? "Crawling" : "Prone",
            Stance.Crouching => "Crouching",
            _ => !moving ? "Standing" : unit.MoveMode == MoveMode.Run ? "Running" : "Walking",
        };
    }

    /// <summary>"Ammo 5+12": rounds in the weapon + spare magazines.</summary>
    public static string AmmoText(Unit unit, string language = "en") =>
        $"{(language == "fi" ? "Patruunat" : "Ammo")} {unit.Ammo}+{unit.Magazines}";

    public static string Condition(Unit unit, string language = "en") => Translate(unit.Wound switch
    {
        WoundLevel.None => "Unhurt",
        WoundLevel.Light => "Light wound",
        WoundLevel.Serious => "Serious wound",
        WoundLevel.Incapacitated => "Down",
        _ => "Dead",
    }, language);

    /// <summary>The card's short line: rounds + spare magazines, grenades, fire policy ("71+2 · 2 gr · Free fire").</summary>
    public static string CardLine(Unit unit, string language = "en") =>
        $"{unit.Ammo}+{unit.Magazines} · {unit.Grenades} {(language == "fi" ? "kr" : "gr")} · {PolicyShort(unit.FirePolicy, language)}";

    public static string PolicyShort(FirePolicy policy, string language = "en") => Translate(policy switch
    {
        FirePolicy.FireAtWill => "Free fire",
        FirePolicy.ReturnFire => "Return fire",
        _ => "Hold fire",
    }, language);

    public static string PolicyName(FirePolicy policy, string language = "en") => Translate(policy switch
    {
        FirePolicy.FireAtWill => "Fire at will",
        FirePolicy.ReturnFire => "Return fire",
        _ => "Hold fire",
    }, language);
}
