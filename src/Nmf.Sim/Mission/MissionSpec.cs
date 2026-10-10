namespace Nmf.Sim.Mission;

/// <summary>A text in English (required) and optionally Finnish.</summary>
public sealed record Localized(string En, string? Fi = null)
{
    public string In(string language) => language == "fi" && !string.IsNullOrWhiteSpace(Fi) ? Fi : En;
}

/// <summary>How a man starts a mission: a testbed may set the fallen and the wounded out beforehand.</summary>
public enum SoldierState
{
    Fit,
    Wounded,
    Incapacitated,
    Dead,
}

/// <summary>
/// One man of a mission roster (spec 2026-09-26-missions-design §2–§3). <paramref name="At"/> (metres on the map), <paramref name="State"/>
/// and <paramref name="Searched"/> (the other side has been through his pockets) set a scene up for testing.
/// </summary>
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
    string? Squad = null,
    (int X, int Y)? At = null,
    SoldierState State = SoldierState.Fit,
    bool Searched = false,
    bool Stranded = false);

public enum ObjectiveType
{
    PickUp,
    ReachZone,
    /// <summary>A man of ours within <see cref="ObjectiveSpec.RadiusCm"/> of a place (spec 2026-10-10-events-design §5).</summary>
    Find,
    /// <summary>A named man of ours alive in a zone; his death fails the mission.</summary>
    Rescue,
}

/// <param name="Place">Find: the place looked for (<see cref="MissionSpec.Places"/>).</param>
/// <param name="Area">Find: the zone the search is in, shown by the guide (the place itself never is until found).</param>
/// <param name="Unit">Rescue: the man to bring back, by name.</param>
/// <param name="Hidden">Not shown and not counted until an event reveals it.</param>
public sealed record ObjectiveSpec(
    string Id,
    ObjectiveType Type,
    Localized Text,
    string? Item = null,
    string? Zone = null,
    string? Carrying = null,
    IReadOnlyList<string>? Requires = null,
    string? Place = null,
    string? Area = null,
    string? Unit = null,
    int RadiusCm = 1_500,
    bool Hidden = false);

/// <summary>When an event fires (spec 2026-10-10-events-design §3): every condition given must hold.</summary>
public sealed record EventCondition(
    long? AfterTicks = null,
    string? Objective = null,
    string? Event = null,
    Units.Side? ContactBy = null,
    string? ZoneName = null,
    Units.Side? ZoneSide = null,
    Units.Side? LossesSide = null,
    int LossesAtLeast = 0,
    string? Phone = null);

public enum EventActionKind { Wait, Message, Send, Release, Reveal, End }

/// <summary>One thing an event does (spec 2026-10-10-events-design §4); the fields its kind uses are set.</summary>
public sealed record EventAction(
    EventActionKind Kind,
    long Ticks = 0,
    Localized? Text = null,
    string? Squad = null,
    string? To = null,
    Units.MoveMode Mode = Units.MoveMode.Auto,
    string? Objective = null,
    bool Success = false);

public sealed record EventSpec(string Id, EventCondition When, IReadOnlyList<EventAction> Do);

/// <summary>The 81 mm mortar a mission gives the player (spec 2026-10-10-mortar-design §1): rounds, delay, the radioman by name.</summary>
public sealed record MortarSupportSpec(int He, int DelayMinS, int DelayMaxS, string Radio);

/// <summary>A post's field telephone and its wire (spec 2026-10-10-alertness-design §4), in centimetres; the enemy's.</summary>
public sealed record PhoneSpec(string Id, Core.Vec2 At, IReadOnlyList<Core.Vec2> Line);

/// <summary>Fire support a mission gives the player.</summary>
public sealed record SupportSpec(MortarSupportSpec? Mortar = null);

/// <summary>
/// A mission as loaded from content/core/missions/&lt;id&gt;/mission.yaml. <paramref name="Debug"/>: a testbed, shown
/// whole (no fog) and listed only with --debug; <paramref name="Patrols"/>: whether the map's patrol routes are walked;
/// <paramref name="Start"/>: the day and time of day the mission begins (local time), for the in-game watch.
/// </summary>
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
    IReadOnlyDictionary<string, Localized>? Squads = null,
    bool Debug = false,
    bool Patrols = true,
    DateTime? Start = null,
    MissionTactics? TacticRules = null,
    IReadOnlyDictionary<string, Core.Vec2>? PlaceList = null,
    IReadOnlyList<World.MapZone>? ZoneList = null,
    IReadOnlyList<EventSpec>? EventList = null,
    SupportSpec? Support = null,
    IReadOnlyList<PhoneSpec>? PhoneList = null)
{
    /// <summary>The enemy posts' field telephones.</summary>
    public IReadOnlyList<PhoneSpec> Phones => PhoneList ?? [];

    /// <summary>Named places of the mission, in centimetres (spec 2026-10-10-events-design §2).</summary>
    public IReadOnlyDictionary<string, Core.Vec2> Places => PlaceList ?? new Dictionary<string, Core.Vec2>();

    /// <summary>The mission's own zones, besides the map's.</summary>
    public IReadOnlyList<World.MapZone> MissionZones => ZoneList ?? [];

    public IReadOnlyList<EventSpec> Events => EventList ?? [];

    /// <summary>A zone by name: the mission's own first, then the map's.</summary>
    public World.MapZone? ZoneNamed(World.GridMap map, string name) =>
        MissionZones.FirstOrDefault(z => z.Name == name) ?? map.Features.Zones.FirstOrDefault(z => z.Name == name);

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

    /// <summary>The ways of attacking this mission favours or rules out (spec 2026-10-10-infiltration-enemy-design §3).</summary>
    public MissionTactics Tactics => TacticRules ?? MissionTactics.None;

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
/// <summary>Tactic ids a mission puts first (in this order) or rules out for the player.</summary>
public sealed record MissionTactics(IReadOnlyList<string> Prefer, IReadOnlyList<string> Forbid)
{
    public static readonly MissionTactics None = new([], []);
}

/// <remarks><paramref name="Tactic"/>: how the counterattack goes (a tactic id); null: fire and movement.
/// <paramref name="Calm"/>: his men start calm, each with his own alertness (spec 2026-10-10-alertness-design); else they are
/// ready from the start, as before. <paramref name="Flares"/>: signal flares his leaders fire on the alarm.</remarks>
public sealed record EnemyAiSpec(bool Counterattack = false, bool Investigate = false, string? Tactic = null, bool Calm = false, int Flares = 0)
{
    public static readonly EnemyAiSpec None = new();

    public bool Any => Counterattack || Investigate;
}
