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
        var briefingFiles = y.Briefing ?? throw new ArgumentException("missing field 'briefing'");
        var player = (y.Forces?.Player ?? []).Select((s, i) => Soldier(s, $"forces.player[{i}]", items)).ToList();
        var enemy = (y.Forces?.Enemy ?? []).Select((s, i) => Soldier(s, $"forces.enemy[{i}]", items)).ToList();
        if (player.Count == 0)
            throw new ArgumentException("forces.player needs at least one soldier");
        var objectives = (y.Objectives ?? []).Select((o, i) => Objective(o, $"objectives[{i}]", items)).ToList();
        CheckRequires(objectives);
        return new MissionSpec(
            Required(y.Id, "id"),
            Text(y.Title, "title"),
            Text(y.Date, "date"),
            Required(y.Map, "map"),
            new Localized(Briefing(directory, Required(briefingFiles.GetValueOrDefault("en"), "briefing.en")),
                briefingFiles.GetValueOrDefault("fi") is { Length: > 0 } fi ? Briefing(directory, fi) : null),
            player, enemy, objectives, items);
    }

    private static string Briefing(string directory, string file)
    {
        string path = Path.Combine(directory, file);
        if (!File.Exists(path))
            throw new ArgumentException($"briefing file '{file}' not found");
        return File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
    }

    private static SoldierSpec Soldier(SoldierYaml s, string where, IReadOnlyDictionary<string, Localized> items)
    {
        foreach (var item in s.Items ?? [])
            if (!items.ContainsKey(item))
                throw new ArgumentException($"{where}: item '{item}' is not declared under 'items'");
        return new SoldierSpec(
            Required(s.Name, $"{where}.name"),
            Required(s.Weapon, $"{where}.weapon"),
            string.IsNullOrWhiteSpace(s.Grenade) ? null : s.Grenade,
            s.Leader ?? false,
            Range(s.Nerve ?? 50, 0, 100, $"{where}.nerve"),
            s.Morale is { } m ? Range(m, 0, 1000, $"{where}.morale") : null,
            Range(s.Marksmanship ?? 50, 0, 100, $"{where}.marksmanship"),
            Range(s.Leadership ?? 100, 0, 100, $"{where}.leadership"),
            s.Items ?? []);
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
        public int? Leadership { get; set; }
        public List<string>? Items { get; set; }
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
}
