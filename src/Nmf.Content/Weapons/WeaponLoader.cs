using Nmf.Sim.Combat;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Nmf.Content.Weapons;

/// <summary>Loads content/core/weapons/*.yaml (spec 2026-09-24-combat-design §2).</summary>
public static class WeaponLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static IReadOnlyDictionary<string, WeaponDef> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
            throw new ContentLoadException(directory, "weapon directory not found");
        var result = new Dictionary<string, WeaponDef>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(directory, "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
        {
            var weapon = Load(path);
            if (!result.TryAdd(weapon.Id, weapon))
                throw new ContentLoadException(path, $"duplicate weapon id '{weapon.Id}'");
        }
        return result;
    }

    public static WeaponDef Load(string path)
    {
        WeaponYaml? y;
        try
        {
            y = Deserializer.Deserialize<WeaponYaml>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is YamlException or IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException(path, ex.InnerException?.Message is { } inner ? $"{ex.Message} ({inner})" : ex.Message, ex);
        }
        if (y is null)
            throw new ContentLoadException(path, "file is empty");

        try
        {
            string id = Required(y.Id, "id");
            return new WeaponDef(
                id,
                y.Name ?? id,
                ParseClass(Required(y.Class, "class")),
                Required(y.Magazine, "magazine"),
                Required(y.AimTicks, "aim_ticks"),
                Required(y.Burst, "burst"),
                y.RoundIntervalTicks ?? 0,
                Required(y.RecoverTicks, "recover_ticks"),
                Required(y.ReloadTicks, "reload_ticks"),
                Required(y.SpreadMrad, "spread_mrad"),
                Required(y.RangeM, "range_m") * 100,
                Required(y.LethalityPct, "lethality_pct"),
                Required(y.Suppression, "suppression"),
                Required(y.NoiseM, "noise_m") * 100).Validated();
        }
        catch (ArgumentException ex)
        {
            throw new ContentLoadException(path, ex.Message, ex);
        }
    }

    private static int Required(int? value, string field) => value ?? throw new ArgumentException($"missing field '{field}'");

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"missing field '{field}'") : value;

    private static WeaponClass ParseClass(string value) => value switch
    {
        "rifle" => WeaponClass.Rifle,
        "smg" => WeaponClass.Smg,
        "lmg" => WeaponClass.Lmg,
        _ => throw new ArgumentException($"class must be rifle, smg or lmg, was '{value}'"),
    };

    private sealed class WeaponYaml
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Class { get; set; }
        public int? Magazine { get; set; }
        public int? AimTicks { get; set; }
        public int? Burst { get; set; }
        public int? RoundIntervalTicks { get; set; }
        public int? RecoverTicks { get; set; }
        public int? ReloadTicks { get; set; }
        public int? SpreadMrad { get; set; }
        public int? RangeM { get; set; }
        public int? LethalityPct { get; set; }
        public int? Suppression { get; set; }
        public int? NoiseM { get; set; }
    }
}
