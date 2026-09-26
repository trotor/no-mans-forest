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
        var objectives = (y.Objectives ?? []).Select((o, i) => Objective(NotEmpty(o, $"objectives[{i}]"), $"objectives[{i}]", items)).ToList();
        if (objectives.Count == 0)
            throw new ArgumentException("a mission needs at least one objective");
        CheckRequires(objectives);
        return new MissionSpec(
            Required(y.Id, "id"),
            Text(y.Title, "title"),
            Text(y.Date, "date"),
            Required(y.Map, "map"),
            new Localized(Briefing(directory, Required(briefingFiles.GetValueOrDefault("en"), "briefing.en")),
                briefingFiles.GetValueOrDefault("fi") is { Length: > 0 } fi ? Briefing(directory, fi) : null),
            player, enemy, objectives, items,
            (y.Plan ?? []).Select((a, i) => Arrow(NotEmpty(a, $"plan[{i}]"), $"plan[{i}]")).ToList(),
            new EnemyAiSpec(y.EnemyAi?.Counterattack ?? false, y.EnemyAi?.Investigate ?? false),
            squads,
            y.Debug ?? false,
            y.Patrols ?? true,
            y.Start is null ? null
                : DateTime.TryParseExact(y.Start, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var start) ? start
                : throw new ArgumentException($"start must be \"yyyy-MM-dd HH:mm\" (local time), was '{y.Start}'"));
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
            s.Searched ?? false);
    }

    private static ObjectiveSpec Objective(ObjectiveYaml o, string where, IReadOnlyDictionary<string, Localized> items)
    {
        string id = Required(o.Id, $"{where}.id");
        var type = o.Type switch
        {
            "pick_up" => ObjectiveType.PickUp,
            "reach_zone" => ObjectiveType.ReachZone,
            _ => throw new ArgumentException($"objective '{id}': type must be pick_up or reach_zone, was '{o.Type}'"),
        };
        if (type == ObjectiveType.PickUp && string.IsNullOrWhiteSpace(o.Item))
            throw new ArgumentException($"objective '{id}': pick_up needs an item");
        if (type == ObjectiveType.ReachZone && string.IsNullOrWhiteSpace(o.Zone))
            throw new ArgumentException($"objective '{id}': reach_zone needs a zone");
        foreach (var item in new[] { o.Item, o.Carrying })
            if (!string.IsNullOrWhiteSpace(item) && !items.ContainsKey(item))
                throw new ArgumentException($"objective '{id}': item '{item}' is not declared under 'items'");
        return new ObjectiveSpec(id, type, Text(o.Text, $"objective '{id}' text"), NullIfEmpty(o.Item), NullIfEmpty(o.Zone),
            NullIfEmpty(o.Carrying), o.Requires ?? []);
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
    }

    private sealed class ObjectiveYaml
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Item { get; set; }
        public string? Zone { get; set; }
        public string? Carrying { get; set; }
        public List<string>? Requires { get; set; }
        public Dictionary<string, string>? Text { get; set; }
    }

    private sealed class EnemyAiYaml
    {
        public bool? Counterattack { get; set; }
        public bool? Investigate { get; set; }
    }
}
