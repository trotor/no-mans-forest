using Nmf.Sim.Combat;

namespace Nmf.Sim.Tests.Combat;

/// <summary>Weapons with easy numbers for tests.</summary>
internal static class TestWeapons
{
    public static WeaponDef Rifle(int spread = 0, int lethality = 70, int aim = 4, int magazine = 5, int recover = 2, int reload = 10, int suppression = 80) =>
        new WeaponDef("test_rifle", "Test rifle", WeaponClass.Rifle, magazine, aim, 1, 0, recover, reload, spread, 30_000, lethality, suppression, 30_000).Validated();

    public static WeaponDef Smg(int spread = 30) =>
        new WeaponDef("test_smg", "Test SMG", WeaponClass.Smg, 71, 4, 5, 1, 4, 20, spread, 12_000, 45, 50, 20_000).Validated();

    public static IReadOnlyDictionary<string, WeaponDef> SkirmishSet()
    {
        var rifle = Rifle(spread: 6);
        var smg = Smg();
        var lmg = new WeaponDef("test_lmg", "Test LMG", WeaponClass.Lmg, 20, 8, 5, 2, 6, 30, 12, 40_000, 65, 100, 40_000).Validated();
        return new Dictionary<string, WeaponDef>
        {
            ["mosin_m39"] = rifle with { Id = "mosin_m39" },
            ["mosin_9130"] = rifle with { Id = "mosin_9130" },
            ["suomi_kp31"] = smg with { Id = "suomi_kp31" },
            ["ppsh41"] = smg with { Id = "ppsh41" },
            ["lahti_saloranta"] = lmg with { Id = "lahti_saloranta" },
            ["dp27"] = lmg with { Id = "dp27" },
        };
    }
}
