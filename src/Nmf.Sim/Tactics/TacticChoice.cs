using Nmf.Sim.Mission;

namespace Nmf.Sim.Tactics;

/// <summary>The tactics a mission leaves the player (spec 2026-10-10-infiltration-enemy-design §3).</summary>
public static class TacticChoice
{
    /// <summary>The forbidden left out, the preferred first in the order given, then the rest in their own order.</summary>
    public static IReadOnlyList<TacticDef> For(IReadOnlyList<TacticDef> all, MissionTactics rules)
    {
        var allowed = all.Where(t => !rules.Forbid.Contains(t.Id)).ToList();
        var preferred = rules.Prefer.Select(id => allowed.FirstOrDefault(t => t.Id == id)).OfType<TacticDef>().Distinct().ToList();
        return preferred.Concat(allowed.Where(t => !preferred.Contains(t))).ToList();
    }

    /// <summary>Ids the mission names that no tactic has, in the order named (prefer first): a content error.</summary>
    public static IReadOnlyList<string> Unknown(IReadOnlyList<TacticDef> all, MissionTactics rules) =>
        rules.Prefer.Concat(rules.Forbid).Where(id => all.All(t => t.Id != id)).Distinct().ToList();
}
