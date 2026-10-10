using Nmf.Sim;
using Nmf.Sim.Tactics;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Tactics;

/// <summary>Loads content/core/tactics/*.yaml (spec 2026-10-10-tactics-design); file-name order, the first is the default.</summary>
public static class TacticLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static IReadOnlyList<TacticDef> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new ContentLoadException(directory, "tactics directory not found");
        var result = new List<TacticDef>();
        foreach (var path in Directory.GetFiles(directory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
        {
            var tactic = Load(path);
            if (result.Any(t => t.Id == tactic.Id))
                throw new ContentLoadException(path, $"duplicate tactic id '{tactic.Id}'");
            result.Add(tactic);
        }
        return result;
    }

    public static TacticDef Load(string path)
    {
        TacticYaml? y;
        try
        {
            y = Deserializer.Deserialize<TacticYaml>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(path, ex.InnerException?.Message is { } inner ? $"{ex.Message} ({inner})" : ex.Message, ex);
        }
        if (y is null)
            throw new ContentLoadException(path, "file is empty");
        try
        {
            string id = string.IsNullOrWhiteSpace(y.Id) ? throw new ArgumentException("missing field 'id'") : y.Id;
            var fireBase = y.FireBase ?? throw new ArgumentException("missing field 'fire_base'");
            var abort = y.Abort ?? throw new ArgumentException("missing field 'abort'");
            var maneuver = y.Maneuver is { } m
                ? new TacticManeuver(m.FlankDeg ?? 60, Metres(m.FormUp?.MinM, "maneuver.form_up.min_m"), Metres(m.FormUp?.MaxM, "maneuver.form_up.max_m"))
                : null;
            var assault = y.Assault is { } a
                ? new TacticAssault(a.Suppression ?? 250, Seconds(a.WaitS ?? 20), Seconds(a.GiveUpS ?? 60))
                : null;
            return new TacticDef(id, y.Name?.En ?? id, y.Name?.Fi ?? y.Name?.En ?? id,
                Metres(fireBase.MinM, "fire_base.min_m"), Metres(fireBase.MaxM, "fire_base.max_m"),
                maneuver, assault, abort.CasualtiesPct ?? 30, Seconds(abort.PinnedS ?? 10)).Validated();
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    private static int Metres(int? value, string field) => (value ?? throw new ArgumentException($"missing field '{field}'")) * 100;

    private static int Seconds(int value) => value * SimConstants.TicksPerSecond;

    private sealed class TacticYaml
    {
        public string? Id { get; set; }
        public NameYaml? Name { get; set; }
        public BandYaml? FireBase { get; set; }
        public ManeuverYaml? Maneuver { get; set; }
        public AssaultYaml? Assault { get; set; }
        public AbortYaml? Abort { get; set; }
    }

    private sealed class NameYaml
    {
        public string? En { get; set; }
        public string? Fi { get; set; }
    }

    private sealed class BandYaml
    {
        public int? MinM { get; set; }
        public int? MaxM { get; set; }
    }

    private sealed class ManeuverYaml
    {
        public int? FlankDeg { get; set; }
        public BandYaml? FormUp { get; set; }
    }

    private sealed class AssaultYaml
    {
        public int? Suppression { get; set; }
        public int? WaitS { get; set; }
        public int? GiveUpS { get; set; }
    }

    private sealed class AbortYaml
    {
        public int? CasualtiesPct { get; set; }
        public int? PinnedS { get; set; }
    }
}
