using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>
/// What our men can tell about an enemy they see, for the hover tip: what sort of man he looks like, his weapon, what he
/// is doing, how he looks, how far he is. Only what shows from the outside — never his name, papers or morale figures.
/// </summary>
public static class EnemyInfo
{
    /// <param name="bodyInSight">Whether our men see the spot right now: only then can they tell what is left on a body.</param>
    /// <param name="sim">With it, how awake he looks (spec 2026-10-10-alertness-design §7).</param>
    public static IReadOnlyList<string> Describe(Unit enemy, IEnumerable<Unit> own, string language, bool bodyInSight = true, Nmf.Sim.Simulation? sim = null)
    {
        bool fi = language == "fi";
        var lines = new List<string>();
        if (enemy.IsOutOfAction)
        {
            lines.Add(enemy.IsCaptured ? (fi ? "Antautunut" : "Surrendered")
                : enemy.Wound == WoundLevel.Dead ? (fi ? "Kaatunut" : "Fallen")
                : fi ? "Haavoittunut, taistelukyvytön" : "Wounded, out of the fight");
            bool searched = own.FirstOrDefault() is { } ours && enemy.WasSearchedBy(ours.Side);
            if (!enemy.IsCaptured && searched)
                lines.Add(!bodyInSight ? (fi ? "Tutkittu" : "Searched")
                    : enemy.Looted ? (fi ? "Tutkittu — ei mitään otettavaa" : "Searched — nothing left to take")
                    : fi ? "Tutkittu — jäi tavaraa, josta voi olla hyötyä toiselle" : "Searched — something is left another man could use");
            else if (!enemy.IsCaptured && bodyInSight)
                lines.Add(enemy.Looted ? (fi ? "Ei mitään otettavaa" : "Nothing left to take")
                                       : fi ? "Voi olla tavaraa — tuplaklikkaa, niin lähin mies tutkii hänet" : "May have something on him — double click and the nearest man searches him");
        }
        else
        {
            lines.Add(Role(enemy, fi));
            if (enemy.Weapon is { } weapon)
                lines.Add((fi ? "Ase: " : "Weapon: ") + weapon.Name);
            lines.Add(Doing(enemy, fi));
            if (sim is not null && AlertnessText.Tip(sim, enemy, language) is { } awake)
                lines.Add(awake);
            if (Looks(enemy, fi) is { } looks)
                lines.Add(looks);
        }
        if (Nearest(enemy, own) is { } metres)
            lines.Add(fi ? $"{metres} m lähimmästä omasta" : $"{metres} m from our nearest man");
        return lines;
    }

    internal static string Role(Unit enemy, bool fi) => Role(enemy.IsLeader, enemy.Weapon?.Class, fi);

    /// <summary>What sort of man he looks like, from what was seen of him.</summary>
    internal static string Role(bool leader, WeaponClass? weapon, bool fi)
    {
        if (leader)
            return fi ? "Johtaja — jakaa käskyjä" : "Leader — giving orders";
        return weapon switch
        {
            WeaponClass.Rifle => fi ? "Kiväärimies" : "Rifleman",
            WeaponClass.Smg => fi ? "Konepistoolimies" : "Submachine gunner",
            WeaponClass.Lmg => fi ? "Pikakivääriampuja" : "Machine gunner",
            _ => fi ? "Aseeton sotilas" : "Unarmed soldier",
        };
    }

    private static string Doing(Unit enemy, bool fi)
    {
        bool moving = enemy.MoveTarget is not null;
        return enemy.Action switch
        {
            CombatAction.Melee => fi ? "Lähitaistelussa" : "In hand-to-hand fighting",
            CombatAction.Throwing => fi ? "Heittää kranaattia" : "Throwing a grenade",
            CombatAction.Reloading => fi ? "Lataa asettaan" : "Reloading",
            CombatAction.Looting => fi ? "Tutkii kaatunutta" : "Searching a body",
            CombatAction.Aiming or CombatAction.Firing => fi ? "Ampuu" : "Firing",
            _ when moving && enemy.Stance == Stance.Prone => fi ? "Ryömii" : "Crawling",
            _ when moving && enemy.MoveMode == MoveMode.Run => fi ? "Juoksee" : "Running",
            _ when moving => fi ? "Liikkuu" : "Moving",
            _ => enemy.Stance switch
            {
                Stance.Prone => fi ? "Makaa maassa" : "Lying down",
                Stance.Crouching => fi ? "Kyykyssä" : "Crouching",
                _ => fi ? "Seisoo" : "Standing",
            },
        };
    }

    private static string? Looks(Unit enemy, bool fi)
    {
        var parts = new List<string>();
        if (enemy.Wound is WoundLevel.Light or WoundLevel.Serious)
            parts.Add(fi ? "haavoittunut" : "wounded");
        if (enemy.MoraleState == MoraleState.Broken)
            parts.Add(fi ? "pakenee" : "running away");
        else if (enemy.MoraleState == MoraleState.Pinned)
            parts.Add(fi ? "painuu maahan, lamautunut" : "hugging the ground, pinned");
        else if (enemy.Suppression >= CombatRules.GoProneAt)
            parts.Add(fi ? "tulen alla" : "under fire");
        if (parts.Count == 0)
            return null;
        string text = string.Join(", ", parts);
        return (fi ? "Näyttää: " : "Looks: ") + text;
    }

    private static long? Nearest(Unit enemy, IEnumerable<Unit> own)
    {
        long best = long.MaxValue;
        foreach (var man in own)
            if (!man.IsOutOfAction)
                best = Math.Min(best, (man.Position - enemy.Position).LengthSquared);
        return best == long.MaxValue ? null : (IntMath.Isqrt(best) + 50) / 100;
    }
}
