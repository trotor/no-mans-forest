using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Taking ammo, grenades, a weapon and papers from the fallen (spec 2026-09-25-looting-design §3–§4).</summary>
public static class LootSystem
{
    /// <summary>Moves everything the looter can use from the body to him, marks the body looted and reports what was taken.</summary>
    public static void Transfer(Simulation sim, Unit looter, Unit body, long tick, List<SimEvent> events)
    {
        int magazines = 0, grenades = 0;
        string? weaponTaken = null;
        var items = new List<Item>();
        if (!body.Looted)
        {
            if (CanSwapWeapon(looter, body))
            {
                weaponTaken = body.Weapon!.Id;
                SwapWeapons(looter, body);
            }
            if (SameWeapon(looter, body))
            {
                magazines = body.Magazines + (HasFullMagazine(body) ? 1 : 0);
                if (HasFullMagazine(body))
                    body.Ammo = 0;
                body.Magazines = 0;
                looter.Magazines += magazines;
            }
            if (body.Grenades > 0 && body.GrenadeType is { } type
                && (looter.GrenadeType?.Id == type.Id || looter.Grenades == 0))
            {
                grenades = body.Grenades;
                looter.GrenadeType = type;
                looter.Grenades += grenades;
                body.Grenades = 0;
            }
            items.AddRange(body.Items);
            foreach (var item in items)
                looter.AddItem(item);
            body.ClearItems();
            body.Looted = true;
        }
        events.Add(new UnitLooted(tick, looter.Id, body.Id, magazines, grenades, weaponTaken, items));
    }

    /// <summary>Counts down searching men and hands over the goods when done (after melee in the step).</summary>
    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var unit in sim.Units)
        {
            if (unit.Action != CombatAction.Looting)
                continue;
            if (unit.IsOutOfAction)
            {
                Abandon(unit);
                continue;
            }
            if (--unit.ActionTicksLeft > 0)
                continue;
            unit.Action = CombatAction.None;
            var body = unit.LootTarget is { } id ? sim.FindUnit(id) : null;
            unit.LootTarget = null;
            if (body is not null)
                Transfer(sim, unit, body, tick, events);
        }
    }

    /// <summary>Called by the brain for a man with a loot target: starts the search once he is there, re-paths if he stopped short.</summary>
    internal static bool Approach(Simulation sim, Unit unit)
    {
        if (unit.Action == CombatAction.Looting)
            return true;
        var body = sim.FindUnit(unit.LootTarget!.Value);
        if (body is null || body.Looted)
        {
            unit.LootTarget = null;
            return false;
        }
        bool there = (body.Position - unit.Position).LengthSquared <= (long)CombatRules.LootRangeCm * CombatRules.LootRangeCm;
        if (!there)
        {
            if (unit.MoveTarget is null && unit.TargetStance is null && unit.MoraleState != MoraleState.Pinned)
            {
                var path = World.Pathfinder.FindPath(sim.Map, unit.Position, body.Position);
                if (path is null)
                    unit.LootTarget = null;
                else
                    Movement.StartPath(unit, body.Position, AI.SoldierBrain.ChoosePace(sim, unit), path);
            }
            return false;
        }
        if (unit.MoveTarget is not null || unit.TargetStance is not null)
            return false;
        Firing.Cancel(unit);
        if (unit.Action != CombatAction.None)
            return true; // finish the reload first
        unit.Action = CombatAction.Looting;
        unit.ActionTicksLeft = CombatRules.LootTicks;
        return true;
    }

    /// <summary>Gives up a loot errand, stopping a search in progress.</summary>
    public static void Abandon(Unit unit)
    {
        if (unit.Action == CombatAction.Looting)
        {
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
        }
        unit.LootTarget = null;
    }

    /// <summary>The body holds ammo the man can use: magazines for his weapon, or a loaded weapon when his own is empty.</summary>
    public static bool HasUsefulLoot(Unit looter, Unit body) =>
        !body.Looted && body.IsOutOfAction
        && (CanSwapWeapon(looter, body) || (SameWeapon(looter, body) && (body.Magazines > 0 || HasFullMagazine(body))));

    private static bool SameWeapon(Unit looter, Unit body) =>
        looter.Weapon is not null && body.Weapon?.Id == looter.Weapon.Id;

    private static bool HasFullMagazine(Unit body) => body.Weapon is { } w && body.Ammo >= w.MagazineSize;

    private static bool CanSwapWeapon(Unit looter, Unit body) =>
        looter.OutOfAmmo && body.Weapon is not null && !SameWeapon(looter, body) && (body.Ammo > 0 || body.Magazines > 0);

    private static void SwapWeapons(Unit looter, Unit body)
    {
        Firing.Cancel(looter);
        if (looter.Action == CombatAction.Reloading)
        {
            looter.Action = CombatAction.None;
            looter.ActionTicksLeft = 0;
        }
        (looter.Weapon, body.Weapon) = (body.Weapon, looter.Weapon);
        (looter.Ammo, body.Ammo) = (body.Ammo, looter.Ammo);
        (looter.Magazines, body.Magazines) = (body.Magazines, looter.Magazines);
    }
}
