using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

/// <summary>Hand grenades (spec 2026-09-24-grenades-melee-design §3): the throw action, flight and the shielded blast.</summary>
internal static class GrenadeSystem
{
    /// <summary>Would a sensible soldier throw a grenade at this enemy right now?</summary>
    public static bool CanThrowAt(Simulation sim, Unit unit, Unit target, long tick)
    {
        var grenade = unit.GrenadeType;
        if (grenade is null || unit.Grenades <= 0 || tick - unit.LastThrowTick < CombatRules.ThrowCooldownTicks)
            return false;
        long distanceSq = (target.Position - unit.Position).LengthSquared;
        long range = grenade.ThrowRangeCm * (unit.Stance == Stance.Prone ? CombatRules.ProneThrowPct : 100) / 100;
        if (distanceSq < (long)CombatRules.MinThrowCm * CombatRules.MinThrowCm || distanceSq > range * range)
            return false;
        // Close in, a grenade is always worth it; further out only against men a bullet cannot reach well.
        bool worthIt = distanceSq <= (long)CombatRules.CloseThrowCm * CombatRules.CloseThrowCm
                       || target.Stance == Stance.Prone || HardCoverNear(sim.Map, target) || unit.MoraleState == MoraleState.Pinned;
        if (!worthIt)
            return false;
        if (FriendNear(sim, unit, target.Position))
            return false;
        return LineOfSight.Clarity(sim.Map, unit.Position, VisionRules.EyeHeightAbsCm(sim.Map, unit),
            target.Position, VisionRules.TargetHeightAbsCm(sim.Map, target)) > 0;
    }

    /// <summary>The situation may have changed while the pin was pulled: target down, too close, out of range or a friend near it.</summary>
    private static bool SafeToRelease(Simulation sim, Unit unit, Unit target, GrenadeDef def)
    {
        if (target.IsOutOfAction)
            return false;
        long distanceSq = (target.Position - unit.Position).LengthSquared;
        long range = def.ThrowRangeCm * (unit.Stance == Stance.Prone ? CombatRules.ProneThrowPct : 100) / 100;
        if (distanceSq < (long)CombatRules.MinThrowCm * CombatRules.MinThrowCm || distanceSq > range * range)
            return false;
        return !FriendNear(sim, unit, target.Position);
    }

    private static bool FriendNear(Simulation sim, Unit unit, Vec2 point)
    {
        long safetySq = (long)CombatRules.GrenadeFriendSafetyCm * CombatRules.GrenadeFriendSafetyCm;
        foreach (var friend in sim.Units)
        {
            if (friend != unit && friend.Side == unit.Side && friend.IsAlive && (friend.Position - point).LengthSquared < safetySq)
                return true;
        }
        return false;
    }

    /// <summary>A cell next to the man offers cover a rifle bullet will not get through.</summary>
    public static bool HardCoverNear(GridMap map, Unit unit)
    {
        var c = unit.Position.ToCell();
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var n = new CellCoord(c.X + dx, c.Y + dy);
                if ((dx != 0 || dy != 0) && map.InBounds(n) && map[n].Cover >= CombatRules.HardCover
                    && map[n].ObstacleHeightCm >= CombatRules.ShieldObstacleMinCm)
                    return true;
            }
        }
        return false;
    }

    public static void StartThrow(Unit unit, Unit target)
    {
        Firing.Cancel(unit);
        unit.Action = CombatAction.Throwing;
        unit.ActionTicksLeft = CombatRules.ThrowTicks;
        unit.ThrowTarget = target.Id;
    }

    public static void UpdateThrowing(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.Action != CombatAction.Throwing || unit.IsOutOfAction)
            return;
        if (--unit.ActionTicksLeft > 0)
            return;
        unit.Action = CombatAction.None;
        var target = unit.ThrowTarget is { } id ? sim.FindUnit(id) : null;
        unit.ThrowTarget = null;
        if (target is null || unit.GrenadeType is not { } def || unit.Grenades <= 0)
            return;
        if (!SafeToRelease(sim, unit, target, def))
        {
            unit.LastThrowTick = tick; // the throw is called off; the grenade is kept for later
            return;
        }

        long distance = IntMath.Isqrt((target.Position - unit.Position).LengthSquared);
        int scatter = (int)(distance * def.ScatterPct / 100);
        var landing = target.Position;
        if (scatter > 0)
            landing += new Vec2(sim.Rng.NextInt(-scatter, scatter + 1), sim.Rng.NextInt(-scatter, scatter + 1));
        landing = new Vec2(Math.Clamp(landing.X, 0, sim.Map.WidthCm - 1), Math.Clamp(landing.Y, 0, sim.Map.HeightCm - 1));

        unit.Grenades--;
        unit.LastThrowTick = tick;
        unit.LastShotTick = tick; // throwing gives the thrower away like a shot
        var grenade = sim.AddGrenade(unit, landing, tick);
        events.Add(new GrenadeThrown(tick, unit.Id, unit.Position, landing, grenade.ExplodeTick));
    }

    public static void UpdateGrenades(Simulation sim, long tick, List<SimEvent> events)
    {
        bool any = false;
        foreach (var grenade in sim.Grenades)
        {
            if (!grenade.Exploded && tick >= grenade.ExplodeTick)
            {
                Explode(sim, grenade, tick, events);
                any = true;
            }
        }
        if (any)
            sim.RemoveExplodedGrenades();
    }

    private static void Explode(Simulation sim, Grenade grenade, long tick, List<SimEvent> events)
    {
        grenade.Exploded = true;
        events.Add(new GrenadeExploded(tick, grenade.Id, grenade.Landing));
        var def = grenade.Def;
        long blastSq = (long)def.BlastRadiusCm * def.BlastRadiusCm;
        foreach (var unit in sim.Units)
        {
            if (!unit.IsAlive)
                continue;
            long distanceSq = (unit.Position - grenade.Landing).LengthSquared;
            if (distanceSq > blastSq)
                continue;
            long distance = IntMath.Isqrt(distanceSq);
            bool shielded = Shielded(sim, grenade.Landing, unit);
            if (!shielded && distance < def.LethalRadiusCm)
            {
                long chance = (long)def.LethalityPct * (def.LethalRadiusCm - distance) / def.LethalRadiusCm
                              * CombatRules.FragmentStancePct(unit.Stance) / 100;
                if (sim.Rng.NextInt(100) < chance)
                    Damage.ApplyHit(sim, unit, def.LethalityPct, tick, events, grenade.From);
            }
            int suppression = (int)(def.Suppression * (def.BlastRadiusCm - distance) / def.BlastRadiusCm);
            MoraleSystem.AddSuppression(sim, unit, shielded ? suppression / 2 : suppression, tick, events, grenade.From);
        }
    }

    /// <summary>
    /// Something solid between the blast and the man: a rise in the ground, or a solid obstacle that catches the
    /// fragment. Down in a foxhole the fragments of a grenade bursting outside fly over him; standing in it, his head
    /// and shoulders are still out (half the time they are spared).
    /// </summary>
    private static bool Shielded(Simulation sim, Vec2 from, Unit unit)
    {
        var map = sim.Map;
        var to = unit.Position;
        var a = from.ToCell();
        var b = to.ToCell();
        if (a != b && CoverFinder.InPit(map, to))
            return unit.Stance != Stance.Standing || sim.Rng.NextInt(2) == 0;
        int groundLimit = Math.Max(map[a].GroundHeightCm, map[b].GroundHeightCm) + CombatRules.ShieldHillMarginCm;
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        int err = dx - dy;
        int x = a.X, y = a.Y;
        while (x != b.X || y != b.Y)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            if (x == b.X && y == b.Y)
                break;
            var cell = map[new CellCoord(x, y)];
            if (cell.GroundHeightCm > groundLimit)
                return true;
            if (cell.ObstacleHeightCm >= CombatRules.ShieldObstacleMinCm && cell.Cover > 0 && sim.Rng.NextInt(255) < cell.Cover)
                return true;
        }
        return false;
    }
}
