namespace Nmf.Sim.Mission;

/// <summary>A text in English (required) and optionally Finnish.</summary>
public sealed record Localized(string En, string? Fi = null)
{
    public string In(string language) => language == "fi" && !string.IsNullOrWhiteSpace(Fi) ? Fi : En;
}

/// <summary>One man of a mission roster (spec 2026-09-26-missions-design §2–§3).</summary>
public sealed record SoldierSpec(
    string Name,
    string Weapon,
    string? Grenade,
    bool Leader = false,
    int Nerve = 50,
    int? Morale = null,
    int Marksmanship = 50,
    int Leadership = 100,
    IReadOnlyList<string>? Items = null);

public enum ObjectiveType
{
    PickUp,
    ReachZone,
}

public sealed record ObjectiveSpec(
    string Id,
    ObjectiveType Type,
    Localized Text,
    string? Item = null,
    string? Zone = null,
    string? Carrying = null,
    IReadOnlyList<string>? Requires = null);

/// <summary>A mission as loaded from content/core/missions/&lt;id&gt;/mission.yaml.</summary>
public sealed record MissionSpec(
    string Id,
    Localized Title,
    Localized Date,
    string Map,
    Localized Briefing,
    IReadOnlyList<SoldierSpec> Player,
    IReadOnlyList<SoldierSpec> Enemy,
    IReadOnlyList<ObjectiveSpec> Objectives,
    IReadOnlyDictionary<string, Localized> Items);
