using Nmf.Sim.Mission;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Missions;

/// <summary>Loads content/core/missions/&lt;id&gt;/mission.yaml and its briefings (spec 2026-09-26-missions-design §2).</summary>
public static class MissionLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>One mission folder: its spec, or why it could not be loaded.</summary>
    public sealed record Entry(string Id, MissionSpec? Spec, string? Error);

    /// <summary>Every mission folder under <paramref name="missionsDirectory"/>, by folder name; broken ones carry their error.</summary>
    public static IReadOnlyList<Entry> LoadAll(string missionsDirectory)
    {
        if (!Directory.Exists(missionsDirectory))
            return [];
        var entries = new List<Entry>();
        foreach (var dir in Directory.GetDirectories(missionsDirectory).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(dir, "mission.yaml")))
                continue;
            string id = Path.GetFileName(dir);
            try
            {
                entries.Add(new Entry(id, Load(dir), null));
            }
            catch (ContentLoadException ex)
            {
                entries.Add(new Entry(id, null, ex.Message));
            }
        }
        return entries;
    }

    public static MissionSpec Load(string directory)
    {
        string path = Path.Combine(directory, "mission.yaml");
        MissionYaml? y;
        try
        {
            y = Deserializer.Deserialize<MissionYaml>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(path, ex.InnerException?.Message is { } inner ? $"{ex.Message} ({inner})" : ex.Message, ex);
        }
        if (y is null)
            throw new ContentLoadException(path, "file is empty");
        try
        {
            return Build(y, directory);
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    private static MissionSpec Build(MissionYaml y, string directory)
    {
        var items = (y.Items ?? []).ToDictionary(kv => kv.Key, kv => Text(kv.Value, $"items.{kv.Key}"));
        var squads = (y.Squads ?? []).ToDictionary(kv => kv.Key, kv => Text(kv.Value, $"squads.{kv.Key}"));
        var briefingFiles = y.Briefing ?? throw new ArgumentException("missing field 'briefing'");
        var player = (y.Forces?.Player ?? []).Select((s, i) => Soldier(NotEmpty(s, $"forces.player[{i}]"), $"forces.player[{i}]", items, squads)).ToList();
        var enemy = (y.Forces?.Enemy ?? []).Select((s, i) => Soldier(NotEmpty(s, $"forces.enemy[{i}]"), $"forces.enemy[{i}]", items, squads)).ToList();
        if (player.Count == 0)
            throw new ArgumentException("forces.player needs at least one soldier");
        var places = (y.Places ?? []).ToDictionary(kv => kv.Key, kv => Point(kv.Value, $"places.{kv.Key}"));
        var zones = (y.Zones ?? []).Select(kv => Zone(kv.Key, kv.Value)).ToList();
        var objectives = (y.Objectives ?? []).Select((o, i) => Objective(NotEmpty(o, $"objectives[{i}]"), $"objectives[{i}]", items, places, player)).ToList();
        if (objectives.Count == 0)
            throw new ArgumentException("a mission needs at least one objective");
        CheckRequires(objectives);
        var calm = y.EnemyAi?.Alertness switch
        {
            null or "combat" => false,
            "calm" => true,
            var other => throw new ArgumentException($"enemy_ai.alertness must be calm or combat, was '{other}'"),
        };
        var phones = Phones(y.Phones ?? [], places, calm);
        var events = Events(y.Events ?? [], squads, objectives, places, phones);
        return new MissionSpec(
            Required(y.Id, "id"),
            Text(y.Title, "title"),
            Text(y.Date, "date"),
            Required(y.Map, "map"),
            new Localized(Briefing(directory, Required(briefingFiles.GetValueOrDefault("en"), "briefing.en")),
                briefingFiles.GetValueOrDefault("fi") is { Length: > 0 } fi ? Briefing(directory, fi) : null),
            player, enemy, objectives, items,
            (y.Plan ?? []).Select((a, i) => Arrow(NotEmpty(a, $"plan[{i}]"), $"plan[{i}]")).ToList(),
            new EnemyAiSpec(y.EnemyAi?.Counterattack ?? false, y.EnemyAi?.Investigate ?? false, y.EnemyAi?.Tactic, calm,
                Range(y.EnemyAi?.Flares ?? 0, 0, 20, "enemy_ai.flares")),
            squads,
            y.Debug ?? false,
            y.Patrols ?? true,
            y.Start is null ? null
                : DateTime.TryParseExact(y.Start, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var start) ? start
                : throw new ArgumentException($"start must be \"yyyy-MM-dd HH:mm\" (local time), was '{y.Start}'"),
            new MissionTactics(y.Tactics?.Prefer ?? [], y.Tactics?.Forbid ?? []),
            places, zones, events, Support(y.Support, player), phones,
            y.Winter is null ? null : Range(y.Winter.SnowCm ?? 0, 1, 150, "winter.snow_cm"),
            y.Light switch
            {
                null or "day" => Nmf.Sim.World.LightLevel.Day,
                "dusk" => Nmf.Sim.World.LightLevel.Dusk,
                "night" => Nmf.Sim.World.LightLevel.Night,
                _ => throw new ArgumentException($"light must be day, dusk or night, was '{y.Light}'"),
            },
            y.Group is null ? null : Text(y.Group, "group"),
            y.Order ?? 0,
            Props(y.Props ?? [], places));
    }

    private static string Briefing(string directory, string file)
    {
        string path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new ArgumentException($"briefing file '{file}' not found");
        return File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
    }

    private static SoldierSpec Soldier(SoldierYaml s, string where, IReadOnlyDictionary<string, Localized> items,
        IReadOnlyDictionary<string, Localized> squads)
    {
        if (s.Squad is { } squad && !squads.ContainsKey(squad))
            throw new ArgumentException($"{where}: squad '{squad}' is not declared under 'squads'");
        foreach (var item in s.Items ?? [])
            if (!items.ContainsKey(item))
                throw new ArgumentException($"{where}: item '{item}' is not declared under 'items'");
        if ((s.Leader ?? false) && s.State is "incapacitated" or "dead")
            throw new ArgumentException($"{where}: a leader cannot start {s.State} (his squad would be left without one)");
        return new SoldierSpec(
            Required(s.Name, $"{where}.name"),
            Required(s.Weapon, $"{where}.weapon"),
            string.IsNullOrWhiteSpace(s.Grenade) ? null : s.Grenade,
            s.Leader ?? false,
            Range(s.Nerve ?? 50, 0, 100, $"{where}.nerve"),
            s.Morale is { } m ? Range(m, 0, 1000, $"{where}.morale") : null,
            Range(s.Marksmanship ?? 50, 0, 100, $"{where}.marksmanship"),
            Range(s.Leadership ?? 100, 0, 100, $"{where}.leadership"),
            s.Items ?? [],
            Range(s.Experience ?? 50, 0, 100, $"{where}.experience"),
            string.IsNullOrWhiteSpace(s.Squad) ? null : s.Squad,
            s.At switch
            {
                null => null,
                [var x, var y] when x >= 0 && y >= 0 => (x, y),
                _ => throw new ArgumentException($"{where}.at must be [x, y] in metres from the map's top left, was [{string.Join(", ", s.At)}]"),
            },
            s.State switch
            {
                null or "fit" => SoldierState.Fit,
                "wounded" => SoldierState.Wounded,
                "incapacitated" => SoldierState.Incapacitated,
                "dead" => SoldierState.Dead,
                _ => throw new ArgumentException($"{where}.state must be fit, wounded, incapacitated or dead, was '{s.State}'"),
            },
            s.Searched ?? false,
            s.Stranded ?? false,
            s.Skis ?? false,
            s.Ahkio ?? false,
            SpecialtyNames.Parse(s.Specialty, $"{where}.specialty"));
    }

    /// <summary>[x, y] in metres from the map's top left: the centre of that metre, in centimetres.</summary>
    private static Nmf.Sim.Core.Vec2 Point(List<int>? p, string where) => p is [var x, var y] && x >= 0 && y >= 0
        ? new Nmf.Sim.Core.Vec2(x * 100 + 50, y * 100 + 50)
        : throw new ArgumentException($"{where} must be [x, y] in metres from the map's top left");

    private static Nmf.Sim.World.MapZone Zone(string name, ZoneYaml? z)
    {
        if (z?.Min is not [var x0, var y0] || z.Max is not [var x1, var y1] || x0 < 0 || y0 < 0 || x1 <= x0 || y1 <= y0)
            throw new ArgumentException($"zones.{name} must be {{ min: [x, y], max: [x, y] }} in metres, max beyond min");
        return new Nmf.Sim.World.MapZone(name, "mission", new Nmf.Sim.Core.Vec2(x0 * 100, y0 * 100), new Nmf.Sim.Core.Vec2(x1 * 100, y1 * 100));
    }

    /// <summary>The enemy's field telephones (spec 2026-10-10-alertness-design §4): at a place or a point, a wire of two points or more.</summary>
    private static List<PhoneSpec> Phones(List<PhoneYaml> phones, IReadOnlyDictionary<string, Nmf.Sim.Core.Vec2> places, bool calm)
    {
        if (phones.Count > 0 && !calm)
            throw new ArgumentException("phones need the enemy's alertness (enemy_ai: { alertness: calm }): a post ready from the start would call at once");
        var result = new List<PhoneSpec>();
        foreach (var (p, i) in phones.Select((p, i) => (p, i)))
        {
            string where = $"phones[{i}]";
            string id = Required(p?.Id, $"{where}.id");
            if (result.Any(r => r.Id == id))
                throw new ArgumentException($"duplicate phone id '{id}'");
            var at = p!.Place is { } place
                ? places.TryGetValue(place, out var spot) ? spot : throw new ArgumentException($"phone '{id}': place '{place}' is not declared under 'places'")
                : Point(p.At, $"phone '{id}'.at");
            var line = (p.Line ?? []).Select(q => Point(q, $"phone '{id}'.line points")).ToList();
            if (line.Count < 2)
                throw new ArgumentException($"phone '{id}': the line needs two points at least");
            result.Add(new PhoneSpec(id, at, line));
        }
        return result;
    }

    /// <summary>Things on the map for the look of it: a known kind, at a place or a point.</summary>
    private static List<PropSpec> Props(List<PropYaml> props, IReadOnlyDictionary<string, Nmf.Sim.Core.Vec2> places)
    {
        string[] kinds = ["plane_wreck"];
        var result = new List<PropSpec>();
        foreach (var (p, i) in props.Select((p, i) => (p, i)))
        {
            string where = $"props[{i}]";
            if (p?.Kind is not { } kind || !kinds.Contains(kind))
                throw new ArgumentException($"{where}.kind must be one of {string.Join(", ", kinds)}, was '{p?.Kind}'");
            var at = p.Place is { } place
                ? places.TryGetValue(place, out var spot) ? spot : throw new ArgumentException($"{where}: place '{place}' is not declared under 'places'")
                : Point(p.At, $"{where}.at");
            result.Add(new PropSpec(kind, at, ((p.Heading ?? 0) % 360 + 360) % 360));
        }
        return result;
    }

    private static SupportSpec? Support(SupportYaml? y, IReadOnlyList<SoldierSpec> player)
    {
        if (y?.Mortar is not { } m)
            return null;
        int he = Range(m.He ?? 0, 1, 99, "support.mortar.he");
        if (m.DelayS is not [var min, var max] || min < 0 || max < min)
            throw new ArgumentException("support.mortar.delay_s must be [min, max] seconds, max at least min");
        if (string.IsNullOrWhiteSpace(m.Radio) || player.All(s => s.Name != m.Radio))
            throw new ArgumentException($"support.mortar.radio must name a man of the player's forces, was '{m.Radio}'");
        return new SupportSpec(new MortarSupportSpec(he, min, max, m.Radio));
    }

    /// <summary>The "when … then …" events (spec 2026-10-10-events-design §3–§4), every name they use checked.</summary>
    private static List<EventSpec> Events(List<EventYaml> events, IReadOnlyDictionary<string, Localized> squads, IReadOnlyList<ObjectiveSpec> objectives,
        IReadOnlyDictionary<string, Nmf.Sim.Core.Vec2> places, IReadOnlyList<PhoneSpec> phones)
    {
        var ids = new HashSet<string>();
        foreach (var e in events)
            if (!ids.Add(Required(e?.Id, "events[].id")))
                throw new ArgumentException($"duplicate event id '{e!.Id}'");
        string Squad(string? name, string where) =>
            name is not null && squads.ContainsKey(name) ? name : throw new ArgumentException($"{where}: squad '{name}' is not declared under 'squads'");
        string ObjectiveId(string? id, string where) =>
            id is not null && objectives.Any(o => o.Id == id) ? id : throw new ArgumentException($"{where}: objective '{id}' does not exist");
        Nmf.Sim.Units.Side Side(string? side, string where) => side switch
        {
            "player" => Nmf.Sim.Units.Side.Blue,
            "enemy" => Nmf.Sim.Units.Side.Red,
            _ => throw new ArgumentException($"{where} must be player or enemy, was '{side}'"),
        };
        var result = new List<EventSpec>();
        foreach (var e in events)
        {
            string where = $"event '{e.Id}'";
            var w = e.When ?? throw new ArgumentException($"{where}: missing 'when'");
            var when = new EventCondition(
                w.AfterS is { } s ? s * (long)Nmf.Sim.SimConstants.TicksPerSecond : null,
                w.Objective is null ? null : ObjectiveId(w.Objective, $"{where} when"),
                w.Event is null ? null : ids.Contains(w.Event) && w.Event != e.Id ? w.Event : throw new ArgumentException($"{where} when: event '{w.Event}' does not exist"),
                w.Contact is null ? null : Side(w.Contact, $"{where} when.contact"),
                w.Zone is null ? null : Required(w.Zone.Name, $"{where} when.zone.name"),
                w.Zone is null ? null : Side(w.Zone.Side, $"{where} when.zone.side"),
                w.Losses is null ? null : Side(w.Losses.Side, $"{where} when.losses.side"),
                w.Losses?.AtLeast ?? 0,
                w.Phone is null ? null : phones.Any(p => p.Id == w.Phone) ? w.Phone : throw new ArgumentException($"{where} when: phone '{w.Phone}' is not declared under 'phones'"));
            if (when == new EventCondition())
                throw new ArgumentException($"{where}: 'when' needs at least one condition");
            var actions = new List<EventAction>();
            foreach (var (d, i) in (e.Do ?? []).Select((d, i) => (d, i)))
            {
                string at = $"{where} do[{i}]";
                int set = new object?[] { d?.WaitS, d?.Message, d?.Send, d?.Release, d?.Reveal, d?.End, d?.Search }.Count(v => v is not null);
                if (set != 1)
                    throw new ArgumentException($"{at}: each step is one action (wait_s, message, send, release, reveal, end or search)");
                actions.Add(d!.WaitS is { } wait ? new EventAction(EventActionKind.Wait, Ticks: wait * (long)Nmf.Sim.SimConstants.TicksPerSecond)
                    : d.Message is { } text ? new EventAction(EventActionKind.Message, Text: Text(text, $"{at}.message"))
                    : d.Send is { } send ? new EventAction(EventActionKind.Send, Squad: Squad(send.Squad, $"{at}.send"), To: Required(send.To, $"{at}.send.to"),
                        Mode: send.Mode switch
                        {
                            null or "walk" => Nmf.Sim.Units.MoveMode.Auto,
                            "run" => Nmf.Sim.Units.MoveMode.Run,
                            "sneak" => Nmf.Sim.Units.MoveMode.Sneak,
                            _ => throw new ArgumentException($"{at}.send.mode must be walk, run or sneak, was '{send.Mode}'"),
                        })
                    : d.Search is { } search ? new EventAction(EventActionKind.Search, Squad: Squad(search.Squad, $"{at}.search"), To: Required(search.Zone, $"{at}.search.zone"))
                    : d.Release is { } released ? new EventAction(EventActionKind.Release, Squad: Squad(released, $"{at}.release"))
                    : d.Reveal is { } revealed ? new EventAction(EventActionKind.Reveal, Objective: ObjectiveId(revealed, $"{at}.reveal"))
                    : new EventAction(EventActionKind.End, Success: d.End switch
                    {
                        "success" => true,
                        "failure" => false,
                        _ => throw new ArgumentException($"{at}.end must be success or failure, was '{d.End}'"),
                    }));
            }
            result.Add(new EventSpec(e.Id!, when, actions));
        }
        return result;
    }

    private static ObjectiveSpec Objective(ObjectiveYaml o, string where, IReadOnlyDictionary<string, Localized> items,
        IReadOnlyDictionary<string, Nmf.Sim.Core.Vec2> places, IReadOnlyList<SoldierSpec> player)
    {
        string id = Required(o.Id, $"{where}.id");
        var type = o.Type switch
        {
            "pick_up" => ObjectiveType.PickUp,
            "reach_zone" => ObjectiveType.ReachZone,
            "find" => ObjectiveType.Find,
            "rescue" => ObjectiveType.Rescue,
            _ => throw new ArgumentException($"objective '{id}': type must be pick_up, reach_zone, find or rescue, was '{o.Type}'"),
        };
        if (type == ObjectiveType.Find && (string.IsNullOrWhiteSpace(o.Place) || !places.ContainsKey(o.Place)))
            throw new ArgumentException($"objective '{id}': find needs a place declared under 'places', was place '{o.Place}'");
        if (type == ObjectiveType.Rescue && (string.IsNullOrWhiteSpace(o.Zone) || player.All(m => m.Name != o.Unit)))
            throw new ArgumentException($"objective '{id}': rescue needs a zone and a man of the player's forces, was unit '{o.Unit}'");
        if (type == ObjectiveType.PickUp && string.IsNullOrWhiteSpace(o.Item))
            throw new ArgumentException($"objective '{id}': pick_up needs an item");
        if (type == ObjectiveType.ReachZone && string.IsNullOrWhiteSpace(o.Zone))
            throw new ArgumentException($"objective '{id}': reach_zone needs a zone");
        foreach (var item in new[] { o.Item, o.Carrying })
            if (!string.IsNullOrWhiteSpace(item) && !items.ContainsKey(item))
                throw new ArgumentException($"objective '{id}': item '{item}' is not declared under 'items'");
        return new ObjectiveSpec(id, type, Text(o.Text, $"objective '{id}' text"), NullIfEmpty(o.Item), NullIfEmpty(o.Zone),
            NullIfEmpty(o.Carrying), o.Requires ?? [], NullIfEmpty(o.Place), NullIfEmpty(o.Area), NullIfEmpty(o.Unit),
            Range(o.RadiusM ?? 15, 1, 100, $"objective '{id}' radius_m") * 100, o.Hidden ?? false);
    }

    /// <summary>Every required objective exists, and nothing requires itself round a loop.</summary>
    private static void CheckRequires(IReadOnlyList<ObjectiveSpec> objectives)
    {
        var byId = new Dictionary<string, ObjectiveSpec>();
        foreach (var o in objectives)
            if (!byId.TryAdd(o.Id, o))
                throw new ArgumentException($"duplicate objective id '{o.Id}'");
        foreach (var o in objectives)
            foreach (var r in o.Requires ?? [])
                if (!byId.ContainsKey(r))
                    throw new ArgumentException($"objective '{o.Id}' requires unknown objective '{r}'");
        var state = new Dictionary<string, int>(); // 1 visiting, 2 done
        void Visit(string id)
        {
            if (state.GetValueOrDefault(id) == 2)
                return;
            if (state.GetValueOrDefault(id) == 1)
                throw new ArgumentException($"objective requires form a loop through '{id}'");
            state[id] = 1;
            foreach (var r in byId[id].Requires ?? [])
                Visit(r);
            state[id] = 2;
        }
        foreach (var o in objectives)
            Visit(o.Id);
    }

    private static Localized Text(Dictionary<string, string>? text, string field)
    {
        if (text is null || !text.TryGetValue("en", out var en) || string.IsNullOrWhiteSpace(en))
            throw new ArgumentException($"{field} needs an English text ('en')");
        return new Localized(en, text.GetValueOrDefault("fi") is { Length: > 0 } fi ? fi : null);
    }

    private static PlanArrow Arrow(PlanYaml a, string where)
    {
        var kind = a.Kind switch
        {
            "attack" => PlanKind.Attack,
            "withdraw" => PlanKind.Withdraw,
            _ => throw new ArgumentException($"{where}: kind must be attack or withdraw, was '{a.Kind}'"),
        };
        var points = (a.Points ?? []).Select(p => p is { Count: 2 }
                ? new Nmf.Sim.Core.Vec2(p[0] * 100 + 50, p[1] * 100 + 50)
                : throw new ArgumentException($"{where}: points are [x, y] pairs in metres")).ToList();
        if (points.Count < 2)
            throw new ArgumentException($"{where}: a route needs at least 2 points");
        return new PlanArrow(kind, points);
    }

    private static T NotEmpty<T>(T? entry, string where) where T : class =>
        entry ?? throw new ArgumentException($"{where} is empty");

    private static int Range(int value, int min, int max, string field) =>
        value >= min && value <= max ? value : throw new ArgumentException($"{field} must be {min}..{max}, was {value}");

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"missing field '{field}'") : value;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed class MissionYaml
    {
        public string? Id { get; set; }
        public Dictionary<string, string>? Title { get; set; }
        public Dictionary<string, string>? Date { get; set; }
        public string? Map { get; set; }
        public Dictionary<string, string>? Briefing { get; set; }
        public ForcesYaml? Forces { get; set; }
        public List<ObjectiveYaml>? Objectives { get; set; }
        public Dictionary<string, Dictionary<string, string>>? Items { get; set; }
        public List<PlanYaml>? Plan { get; set; }
        public EnemyAiYaml? EnemyAi { get; set; }
        public Dictionary<string, Dictionary<string, string>>? Squads { get; set; }
        public bool? Debug { get; set; }
        public bool? Patrols { get; set; }
        public string? Start { get; set; }
        public TacticsYaml? Tactics { get; set; }
        public Dictionary<string, List<int>>? Places { get; set; }
        public Dictionary<string, ZoneYaml>? Zones { get; set; }
        public List<EventYaml>? Events { get; set; }
        public SupportYaml? Support { get; set; }
        public List<PhoneYaml>? Phones { get; set; }
        public WinterYaml? Winter { get; set; }
        public string? Light { get; set; }
        public Dictionary<string, string>? Group { get; set; }
        public int? Order { get; set; }
        public List<PropYaml>? Props { get; set; }
    }

    private sealed class PlanYaml
    {
        public string? Kind { get; set; }
        public List<List<int>>? Points { get; set; }
    }

    private sealed class ForcesYaml
    {
        public List<SoldierYaml>? Player { get; set; }
        public List<SoldierYaml>? Enemy { get; set; }
    }

    private sealed class SoldierYaml
    {
        public bool? Stranded { get; set; }
        public string? Name { get; set; }
        public string? Weapon { get; set; }
        public string? Grenade { get; set; }
        public bool? Leader { get; set; }
        public int? Nerve { get; set; }
        public int? Morale { get; set; }
        public int? Marksmanship { get; set; }
        public int? Experience { get; set; }
        public int? Leadership { get; set; }
        public string? Squad { get; set; }
        public List<string>? Items { get; set; }
        public List<int>? At { get; set; }
        public string? State { get; set; }
        public bool? Searched { get; set; }
        public bool? Skis { get; set; }
        public bool? Ahkio { get; set; }
        public string? Specialty { get; set; }
    }

    private sealed class PropYaml
    {
        public string? Kind { get; set; }
        public string? Place { get; set; }
        public List<int>? At { get; set; }
        public int? Heading { get; set; }
    }

    private sealed class WinterYaml
    {
        public int? SnowCm { get; set; }
    }

    private sealed class ObjectiveYaml
    {
        public string? Place { get; set; }
        public string? Area { get; set; }
        public string? Unit { get; set; }
        public int? RadiusM { get; set; }
        public bool? Hidden { get; set; }
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Item { get; set; }
        public string? Zone { get; set; }
        public string? Carrying { get; set; }
        public List<string>? Requires { get; set; }
        public Dictionary<string, string>? Text { get; set; }
    }

    private sealed class SupportYaml
    {
        public MortarYaml? Mortar { get; set; }
    }

    private sealed class MortarYaml
    {
        public int? He { get; set; }
        public List<int>? DelayS { get; set; }
        public string? Radio { get; set; }
    }

    private sealed class ZoneYaml
    {
        public List<int>? Min { get; set; }
        public List<int>? Max { get; set; }
    }

    private sealed class EventYaml
    {
        public string? Id { get; set; }
        public WhenYaml? When { get; set; }
        public List<DoYaml>? Do { get; set; }
    }

    private sealed class WhenYaml
    {
        public int? AfterS { get; set; }
        public string? Objective { get; set; }
        public string? Event { get; set; }
        public string? Contact { get; set; }
        public WhenZoneYaml? Zone { get; set; }
        public WhenLossesYaml? Losses { get; set; }
        public string? Phone { get; set; }
    }

    private sealed class PhoneYaml
    {
        public string? Id { get; set; }
        public string? Place { get; set; }
        public List<int>? At { get; set; }
        public List<List<int>>? Line { get; set; }
    }

    private sealed class WhenZoneYaml
    {
        public string? Name { get; set; }
        public string? Side { get; set; }
    }

    private sealed class WhenLossesYaml
    {
        public string? Side { get; set; }
        public int? AtLeast { get; set; }
    }

    private sealed class DoYaml
    {
        public int? WaitS { get; set; }
        public Dictionary<string, string>? Message { get; set; }
        public SendYaml? Send { get; set; }
        public string? Release { get; set; }
        public string? Reveal { get; set; }
        public string? End { get; set; }
        public SearchYaml? Search { get; set; }
    }

    private sealed class SearchYaml
    {
        public string? Squad { get; set; }
        public string? Zone { get; set; }
    }

    private sealed class SendYaml
    {
        public string? Squad { get; set; }
        public string? To { get; set; }
        public string? Mode { get; set; }
    }

    private sealed class TacticsYaml
    {
        public List<string>? Prefer { get; set; }
        public List<string>? Forbid { get; set; }
    }

    private sealed class EnemyAiYaml
    {
        public bool? Counterattack { get; set; }
        public bool? Investigate { get; set; }
        public string? Tactic { get; set; }
        public string? Alertness { get; set; }
        public int? Flares { get; set; }
    }
}
