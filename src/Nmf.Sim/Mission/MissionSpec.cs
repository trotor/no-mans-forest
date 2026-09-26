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
    IReadOnlyList<string>? Items = null,
    int Experience = 50,
    string? Squad = null);

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
    IReadOnlyDictionary<string, Localized> Items,
    IReadOnlyList<PlanArrow>? PlanArrows = null,
    EnemyAiSpec? EnemyAiRules = null,
    IReadOnlyDictionary<string, Localized>? Squads = null)
{
    /// <summary>Squad names by id, in the order they are declared.</summary>
    public IReadOnlyDictionary<string, Localized> SquadNames => Squads ?? new Dictionary<string, Localized>();

    /// <summary>A man's squad number on his side: the order his side's squads first appear in its roster.</summary>
    public int SquadIndex(SoldierSpec man)
    {
        var roster = Player.Contains(man) ? Player : Enemy;
        return Math.Max(0, SquadIds(roster).IndexOf(SquadOf(man)));
    }

    /// <summary>A man's squad id; with none given, the first squad declared.</summary>
    private string SquadOf(SoldierSpec man) => man.Squad ?? SquadNames.Keys.FirstOrDefault() ?? "";

    /// <summary>The squad ids a roster uses, in the order they are declared under <c>squads</c> (undeclared ones after, as they come).</summary>
    public List<string> SquadIds(IReadOnlyList<SoldierSpec> roster)
    {
        var declared = SquadNames.Keys.ToList();
        return roster.Select(SquadOf).Distinct()
            .OrderBy(id => declared.IndexOf(id) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(id => roster.Select(SquadOf).ToList().IndexOf(id))
            .ToList();
    }

    /// <summary>The name of a player squad by its number, or null when the mission names none.</summary>
    public Localized? PlayerSquadName(int squad) =>
        SquadIds(Player) is var ids && squad < ids.Count && SquadNames.TryGetValue(ids[squad], out var name) ? name : null;

    /// <summary>Whether the enemy may counterattack and scout on his own.</summary>
    public EnemyAiSpec EnemyAi => EnemyAiRules ?? EnemyAiSpec.None;

    /// <summary>The routes drawn on the mission map (attack and withdrawal).</summary>
    public IReadOnlyList<PlanArrow> Plan => PlanArrows ?? [];
}

public enum PlanKind
{
    Attack,
    Withdraw,
}

/// <summary>A route on the mission map, points in centimetres.</summary>
public sealed record PlanArrow(PlanKind Kind, IReadOnlyList<Nmf.Sim.Core.Vec2> Points);

/// <summary>What the mission lets the computer side do on its own (spec 2026-09-26-enemy-initiative-design §2).</summary>
public sealed record EnemyAiSpec(bool Counterattack = false, bool Investigate = false)
{
    public static readonly EnemyAiSpec None = new();

    public bool Any => Counterattack || Investigate;
}
