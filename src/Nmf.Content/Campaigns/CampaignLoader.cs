using Nmf.Sim.Mission;
using Nmf.Sim.Units;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Campaigns;

/// <summary>A man of the campaign's platoon: what he brings to any slot (the slot keeps the weapon and the kit).</summary>
public sealed record CampaignMan(string Name, int Nerve, int? Morale, int Marksmanship, int Leadership, int Experience,
    Specialty Specialty = Specialty.None);

/// <summary>One chapter: a mission, with the story before and after it.</summary>
public sealed record CampaignChapter(string Mission, Localized Before, Localized After);

/// <summary>A campaign as loaded from content/core/campaigns/&lt;id&gt;/campaign.yaml (spec 2026-10-11-campaign-design §9a).</summary>
public sealed record CampaignSpec(string Id, Localized Title, Localized Intro, IReadOnlyList<CampaignMan> Roster, IReadOnlyList<CampaignChapter> Chapters);

public static class CampaignLoader
{
    /// <summary>A roster man's leadership when none is given: a private, not a leader (the mission default of 100 would outrank them all).</summary>
    public const int DefaultLeadership = 40;

    /// <summary>At least one man of the roster must be fit to lead a squad.</summary>
    public const int LeaderLeadership = 60;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    /// <summary>Every campaign folder under <paramref name="campaignsDirectory"/>, in name order; a broken one throws.</summary>
    public static IReadOnlyList<CampaignSpec> LoadAll(string campaignsDirectory) =>
        Directory.Exists(campaignsDirectory)
            ? Directory.GetDirectories(campaignsDirectory).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal)
                .Where(d => File.Exists(Path.Combine(d, "campaign.yaml"))).Select(Load).ToList()
            : [];

    public static CampaignSpec Load(string directory)
    {
        string path = Path.Combine(directory, "campaign.yaml");
        CampaignYaml? y;
        try
        {
            y = Deserializer.Deserialize<CampaignYaml>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(path, ex.InnerException?.Message is { } inner ? $"{ex.Message} ({inner})" : ex.Message, ex);
        }
        if (y is null)
            throw new ContentLoadException(path, "file is empty");
        try
        {
            return Build(y);
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    private static CampaignSpec Build(CampaignYaml y)
    {
        var roster = (y.Roster ?? []).Select((m, i) => Man(m ?? throw new ArgumentException($"roster[{i}] is empty"), $"roster[{i}]")).ToList();
        if (roster.Count == 0)
            throw new ArgumentException("the roster needs at least one man");
        if (roster.GroupBy(m => m.Name).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new ArgumentException($"duplicate name '{twice.Key}' in the roster");
        if (roster.All(m => m.Leadership < LeaderLeadership))
            throw new ArgumentException($"the roster needs a leader (leadership {LeaderLeadership} or more)");
        var chapters = (y.Chapters ?? []).Select((c, i) => Chapter(c ?? throw new ArgumentException($"chapters[{i}] is empty"), $"chapters[{i}]")).ToList();
        if (chapters.Count == 0)
            throw new ArgumentException("a campaign needs at least one chapter");
        return new CampaignSpec(Required(y.Id, "id"), Text(y.Title, "title"), Text(y.Intro, "intro"), roster, chapters);
    }

    private static CampaignMan Man(ManYaml m, string where) => new(
        Required(m.Name, $"{where}.name"),
        Range(m.Nerve ?? 50, 0, 100, $"{where}.nerve"),
        m.Morale is { } morale ? Range(morale, 0, 1000, $"{where}.morale") : null,
        Range(m.Marksmanship ?? 50, 0, 100, $"{where}.marksmanship"),
        Range(m.Leadership ?? DefaultLeadership, 0, 100, $"{where}.leadership"),
        Range(m.Experience ?? 50, 0, 100, $"{where}.experience"),
        SpecialtyNames.Parse(m.Specialty, $"{where}.specialty"));

    private static CampaignChapter Chapter(ChapterYaml c, string where) =>
        new(Required(c.Mission, $"{where}.mission"), Text(c.Before, $"{where}.before"), Text(c.After, $"{where}.after"));

    private static Localized Text(Dictionary<string, string>? text, string field)
    {
        if (text is null || !text.TryGetValue("en", out var en) || string.IsNullOrWhiteSpace(en))
            throw new ArgumentException($"{field} needs an English text ('en')");
        return new Localized(en.TrimEnd(), text.GetValueOrDefault("fi") is { Length: > 0 } fi ? fi.TrimEnd() : null);
    }

    private static int Range(int value, int min, int max, string field) =>
        value >= min && value <= max ? value : throw new ArgumentException($"{field} must be {min}..{max}, was {value}");

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"missing field '{field}'") : value;

    private sealed class CampaignYaml
    {
        public string? Id { get; set; }
        public Dictionary<string, string>? Title { get; set; }
        public Dictionary<string, string>? Intro { get; set; }
        public List<ManYaml?>? Roster { get; set; }
        public List<ChapterYaml?>? Chapters { get; set; }
    }

    private sealed class ManYaml
    {
        public string? Name { get; set; }
        public int? Nerve { get; set; }
        public int? Morale { get; set; }
        public int? Marksmanship { get; set; }
        public int? Leadership { get; set; }
        public int? Experience { get; set; }
        public string? Specialty { get; set; }
    }

    private sealed class ChapterYaml
    {
        public string? Mission { get; set; }
        public Dictionary<string, string>? Before { get; set; }
        public Dictionary<string, string>? After { get; set; }
    }
}
