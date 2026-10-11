using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>A specialty's name and what it does, for the cards and the campaign view (spec 2026-10-11-campaign-design §9b).</summary>
public static class SpecialtyText
{
    public static string Name(Specialty specialty, string language) => (specialty, language == "fi") switch
    {
        (Specialty.Marksman, true) => "tarkka-ampuja",
        (Specialty.Marksman, false) => "marksman",
        (Specialty.Scout, true) => "partiomies",
        (Specialty.Scout, false) => "scout",
        (Specialty.Sapper, true) => "pioneeri",
        (Specialty.Sapper, false) => "sapper",
        (Specialty.Medic, true) => "lääkintämies",
        (Specialty.Medic, false) => "medic",
        _ => "",
    };

    /// <summary>What it does, in a line.</summary>
    public static string Effect(Specialty specialty, string language) => (specialty, language == "fi") switch
    {
        (Specialty.Marksman, true) => "Ampuu kiväärillä tarkemmin (hajonta 70 %).",
        (Specialty.Marksman, false) => "Shoots straighter with a rifle (spread 70 %).",
        (Specialty.Scout, true) => "Havaitsee vihollisen aiemmin; liikkuessaan näkyy ja kuuluu vähemmän.",
        (Specialty.Scout, false) => "Spots the enemy sooner; seen and heard less on the move.",
        (Specialty.Sapper, true) => "Kaksi käsikranaattia enemmän, heittää tarkemmin, katkaisee puhelinjohdon puolessa ajassa.",
        (Specialty.Sapper, false) => "Two more grenades, throws truer, cuts a telephone wire in half the time.",
        (Specialty.Medic, true) => "Sitoo vakavan haavan 15 sekunnissa, niin ettei mies vuoda taistelukyvyttömäksi.",
        (Specialty.Medic, false) => "Binds a bad wound in 15 seconds, so the man does not bleed out of the fight.",
        _ => "",
    };

    /// <summary>The card's condition line with his specialty after it: "Ehjä · lääkintämies".</summary>
    public static string WithCondition(Unit unit, string language) =>
        unit.Specialty == Specialty.None ? UnitStatus.Condition(unit, language) : $"{UnitStatus.Condition(unit, language)} · {Name(unit.Specialty, language)}";
}
