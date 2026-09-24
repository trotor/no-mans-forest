using Nmf.Sim.Combat;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Weapons;

/// <summary>Loads content/core/grenades/*.yaml (spec 2026-09-24-grenades-melee-design §3).</summary>
public static class GrenadeLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static IReadOnlyDictionary<string, GrenadeDef> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new ContentLoadException(directory, "grenade directory not found");
        var result = new Dictionary<string, GrenadeDef>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
        {
            var grenade = Load(path);
            if (!result.TryAdd(grenade.Id, grenade))
                throw new ContentLoadException(path, $"duplicate grenade id '{grenade.Id}'");
        }
        return result;
    }

    public static GrenadeDef Load(string path)
    {
        GrenadeYaml? y;
        try
        {
            y = Deserializer.Deserialize<GrenadeYaml>(File.ReadAllText(path));
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
            return new GrenadeDef(
                id,
                y.Name ?? id,
                Required(y.FuseTicks, "fuse_ticks"),
                Required(y.ThrowRangeM, "throw_range_m") * 100,
                Required(y.ScatterPct, "scatter_pct"),
                Required(y.BlastRadiusM, "blast_radius_m") * 100,
                Required(y.LethalRadiusM, "lethal_radius_m") * 100,
                Required(y.Suppression, "suppression"),
                Required(y.LethalityPct, "lethality_pct")).Validated();
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    private static int Required(int? value, string field) => value ?? throw new ArgumentException($"missing field '{field}'");

    private sealed class GrenadeYaml
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public int? FuseTicks { get; set; }
        public int? ThrowRangeM { get; set; }
        public int? ScatterPct { get; set; }
        public int? BlastRadiusM { get; set; }
        public int? LethalRadiusM { get; set; }
        public int? Suppression { get; set; }
        public int? LethalityPct { get; set; }
    }
}
