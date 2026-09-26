using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.AI;

/// <summary>Each soldier's own decisions (spec §6): drop under fire, pick targets, retreat when broken.</summary>
internal static class SoldierBrain
{
    private static readonly (int Dx, int Dy)[] RetreatFallbacks =
        [(200, 0), (-200, 0), (0, 200), (0, -200), (200, 200), (-200, 200), (200, -200), (-200, -200)];

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction)
            return;
        if (unit.MoraleState == MoraleState.Broken)
        {
            ForgetCover(unit); // a broken man only runs; the reaction is not saved for later
            Retreat(sim, unit);
            return;
        }
        if (unit.Action is CombatAction.Melee or CombatAction.Throwing)
        {
            unit.CoverReactionPending = false;
            return;
        }
        if (unit.TakingCover && unit.MoveTarget is null)
        {
            unit.TakingCover = false;
            TakeFiringStance(sim, unit, unit.CoverThreat);
            unit.CoverThreat = null;
        }
        if (unit.CoverReactionPending)
        {
            unit.CoverReactionPending = false;
            // Men in an attack follow its plan, which already puts them in cover.
            if (unit.AttackGroupId is null && ReactToFire(sim, unit))
                return;
        }
        if (unit.LootTarget is not null && LootSystem.Approach(sim, unit))
            return;
        if (unit.AssaultTarget is not null)
        {
            Assault(sim, unit, tick);
            return;
        }
        if (unit.AutoPace && unit.MoveTarget is not null)
            AdjustPace(sim, unit);

        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (idle && unit.Suppression >= MoraleSystem.GoProneAt(unit) && unit.Stance != Stance.Prone)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        if (idle && unit.Stance == Stance.Standing && unit.Suppression < CombatRules.CalmSuppression
            && !unit.StanceOrdered && !unit.HoldsCoverStance && unit.Action is not (CombatAction.Aiming or CombatAction.Firing)
            && EnemyInSightWithin(sim, unit, CombatRules.AutoCrouchRangeCm))
        {
            Movement.BeginStanceChange(unit, Stance.Crouching);
            return;
        }
        // Down on the ground after a burst, once the fire dies away a man who shoots better kneeling comes up to kneel
        // (where he can see from there); a veteran shoots as well lying, and a man told to lie down stays down.
        if (idle && unit.Stance == Stance.Prone && unit.Suppression < CombatRules.CalmSuppression && !unit.StanceOrdered
            && unit.MoraleState == MoraleState.Steady && unit.AttackGroupId is null
            && CombatRules.ProneSpreadPct(unit.Experience) > CombatRules.StanceSpreadPct(Stance.Crouching)
            && NearestSeenEnemy(sim, unit, CombatRules.AutoCrouchRangeCm) is { } seen)
        {
            TakeFiringStance(sim, unit, seen.Position);
            if (unit.TargetStance is not null)
                return;
        }
        if (idle && unit.Action == CombatAction.None && ChooseLootTarget(sim, unit, tick) is { } body
            && Pathfinder.FindPath(sim.Map, unit.Position, body.Position) is { } lootPath)
        {
            unit.LootTarget = body.Id;
            unit.AutoPace = true;
            Movement.StartPath(unit, body.Position, ChoosePace(sim, unit), lootPath);
            return;
        }
        // On a move at their own pace men fire as they go; a run order is a sprint without firing.
        bool firesOnTheMove = unit.AutoPace && unit.MoveTarget is not null && unit.TargetStance is null;
        if ((!idle && !firesOnTheMove) || unit.Action != CombatAction.None || unit.FirePolicy == FirePolicy.HoldFire)
            return;
        if (ChooseGrenadeTarget(sim, unit, tick) is { } grenadeTarget)
        {
            GrenadeSystem.StartThrow(unit, grenadeTarget);
            return;
        }
        if (unit.OutOfAmmo)
            return;
        var target = ChooseTarget(sim, unit, tick);
        if (target is not null)
            Firing.StartAiming(unit, target);
    }

    /// <summary>A man short of ammo, with the fighting quiet around him, goes for the nearest body holding ammo he can use.</summary>
    public static Unit? ChooseLootTarget(Simulation sim, Unit unit, long tick)
    {
        if (unit.Weapon is null || unit.LootTarget is not null || unit.AssaultTarget is not null || unit.AttackGroupId is not null
            || unit.MoraleState != MoraleState.Steady || (unit.Magazines > CombatRules.LowOnMagazines && !unit.OutOfAmmo)
            || MoraleSystem.UnderFire(unit, tick) || EnemyInSightWithin(sim, unit, CombatRules.AutoCrouchRangeCm))
            return null;
        long bestSq = (long)CombatRules.AutoLootRangeCm * CombatRules.AutoLootRangeCm;
        Unit? best = null;
        foreach (var body in sim.Units)
        {
            long distanceSq = (body.Position - unit.Position).LengthSquared;
            if (distanceSq > bestSq || (best is not null && distanceSq == bestSq) || !LootSystem.HasUsefulLoot(unit, body)
                || sim.Units.Any(friend => friend != unit && friend.Side == unit.Side && !friend.IsOutOfAction && friend.LootTarget == body.Id))
                continue;
            best = body;
            bestSq = distanceSq;
        }
        return best;
    }

    public static Unit? ChooseTarget(Simulation sim, Unit unit, long tick)
    {
        var knowledge = sim.Knowledge(unit.Side);
        Unit? best = null;
        long bestDistance = long.MaxValue;
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side == unit.Side || enemy.IsOutOfAction || knowledge.LevelOf(enemy.Id) != ContactLevel.Visible)
                continue;
            if (unit.FirePolicy == FirePolicy.ReturnFire && tick - enemy.LastShotTick > CombatRules.ReturnFireMemoryTicks)
                continue;
            if (!Firing.CanSee(sim, unit, enemy) || Firing.FriendInLine(sim, unit, enemy))
                continue;
            if (enemy.Id == unit.OrderedTarget)
                return enemy;
            long d = (enemy.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = enemy;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>The pace a soldier on an Auto move picks: run under fire, sneak with the enemy in sight nearby, else walk.</summary>
    /// <summary>
    /// The enemy has just opened fire on him (spec 2026-09-25-take-cover-design §3–§4): the tough hold on; the rest take cover
    /// behind something facing the threat, or drop prone where there is none. Player orders other than an own-pace move win.
    /// </summary>
    private static bool ReactToFire(Simulation sim, Unit unit)
    {
        var threat = unit.CoverThreat;
        unit.CoverThreat = null;
        bool ownPaceMove = unit.MoveTarget is null || unit.AutoPace;
        if (unit.IsTough || unit.MoraleState != MoraleState.Steady || unit.AssaultTarget is not null
            || !ownPaceMove || unit.StanceOrdered)
            return false;
        if (unit.MoveTarget is null && unit.Stance == Stance.Prone && unit.TargetStance is null)
            return true; // already down: that is cover enough, he stays put
        LootSystem.Abandon(unit);
        if (unit.MoveTarget is not null)
        {
            Movement.ClearPath(unit);
            unit.AutoPace = false;
        }
        if (CoverFinder.CoveredAt(sim.Map, unit.Position.ToCell(), threat) > 0)
        {
            TakeFiringStance(sim, unit, threat);
            return true;
        }
        if (CoverFinder.Find(sim, unit, threat) is { } cover && Pathfinder.FindPath(sim.Map, unit.Position, cover) is { } path)
        {
            Firing.Cancel(unit);
            Movement.StartPath(unit, cover, MoveMode.Run, path);
            unit.TakingCover = true;
            unit.CoverThreat = threat; // kept to pick his stance when he gets there
            return true;
        }
        if (unit.Stance != Stance.Prone)
            Movement.BeginStanceChange(unit, Stance.Prone);
        return true;
    }

    /// <summary>
    /// In cover, the stance he shoots best from that still sees toward the threat — kneeling, or lying for an old hand —
    /// and under heavy fire the lowest that sees; if none sees over the cover, his preferred one. With the threat unknown,
    /// kneeling (prone under heavy fire or for a veteran).
    /// </summary>
    internal static void TakeFiringStance(Simulation sim, Unit unit, Vec2? threat)
    {
        // He fires from where he shoots best — kneeling, or for an old hand lying with his weapon rested — and under
        // heavy fire hugs the ground whatever it costs his aim.
        bool heavyFire = unit.Suppression >= MoraleSystem.GoProneAt(unit);
        bool proneIsBest = CombatRules.ProneSpreadPct(unit.Experience) <= CombatRules.StanceSpreadPct(Stance.Crouching);
        var stance = heavyFire || proneIsBest ? Stance.Prone : Stance.Crouching;
        if (threat is { } t)
        {
            var map = sim.Map;
            int ground = map.CellAt(unit.Position).GroundHeightCm;
            int targetHeight = map.CellAt(t).GroundHeightCm + CombatRules.CoverSightTargetCm;
            var order = heavyFire || proneIsBest ? new[] { Stance.Prone, Stance.Crouching, Stance.Standing } : new[] { Stance.Crouching, Stance.Prone, Stance.Standing };
            foreach (var candidate in order)
            {
                if (LineOfSight.Clarity(map, unit.Position, ground + StanceRules.EyeHeightCm(candidate), t, targetHeight) > 0)
                {
                    stance = candidate;
                    break;
                }
            }
        }
        if (stance != unit.Stance)
            Movement.BeginStanceChange(unit, stance);
        unit.HoldsCoverStance = true;
    }

    /// <summary>A new player order replaces any running for cover.</summary>
    internal static void ForgetCover(Unit unit)
    {
        unit.HoldsCoverStance = false;
        unit.TakingCover = false;
        unit.CoverReactionPending = false;
        unit.CoverThreat = null;
    }

    public static MoveMode ChoosePace(Simulation sim, Unit unit)
    {
        if (MoraleSystem.UnderFire(unit, sim.Tick))
            return MoveMode.Run;
        return EnemyInSightWithin(sim, unit, CombatRules.SneakRangeCm) ? MoveMode.Sneak : MoveMode.Walk;
    }

    private static void AdjustPace(Simulation sim, Unit unit)
    {
        var mode = ChoosePace(sim, unit);
        if (mode == unit.MoveMode)
            return;
        unit.MoveMode = mode;
        Movement.BeginStanceChange(unit, StanceRules.RequiredFor(mode));
    }

    private static Unit? NearestSeenEnemy(Simulation sim, Unit unit, int rangeCm)
    {
        long rangeSq = (long)rangeCm * rangeCm;
        var knowledge = sim.Knowledge(unit.Side);
        Unit? best = null;
        long bestSq = long.MaxValue;
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side == unit.Side || enemy.IsOutOfAction || knowledge.LevelOf(enemy.Id) != ContactLevel.Visible)
                continue;
            long d = (enemy.Position - unit.Position).LengthSquared;
            if (d <= rangeSq && d < bestSq)
            {
                best = enemy;
                bestSq = d;
            }
        }
        return best;
    }

    private static bool EnemyInSightWithin(Simulation sim, Unit unit, int rangeCm)
    {
        long rangeSq = (long)rangeCm * rangeCm;
        var knowledge = sim.Knowledge(unit.Side);
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side != unit.Side && !enemy.IsOutOfAction && knowledge.LevelOf(enemy.Id) == ContactLevel.Visible
                && (enemy.Position - unit.Position).LengthSquared <= rangeSq)
                return true;
        }
        return false;
    }

    private static Unit? ChooseGrenadeTarget(Simulation sim, Unit unit, long tick)
    {
        if (unit.Grenades <= 0)
            return null;
        var knowledge = sim.Knowledge(unit.Side);
        Unit? best = null;
        long bestDistance = long.MaxValue;
        foreach (var enemy in sim.Units)
        {
            if (enemy.Side == unit.Side || enemy.IsOutOfAction || knowledge.LevelOf(enemy.Id) != ContactLevel.Visible)
                continue;
            if (unit.FirePolicy == FirePolicy.ReturnFire && tick - enemy.LastShotTick > CombatRules.ReturnFireMemoryTicks)
                continue;
            if (!GrenadeSystem.CanThrowAt(sim, unit, enemy, tick))
                continue;
            long d = (enemy.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = enemy;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Charge the target: re-plan when he moves, throw a grenade on the way when it makes sense, close in for melee.</summary>
    private static void Assault(Simulation sim, Unit unit, long tick)
    {
        var target = sim.FindUnit(unit.AssaultTarget!.Value);
        if (target is null || target.IsOutOfAction)
        {
            unit.AssaultTarget = null;
            Movement.ClearPath(unit);
            return;
        }
        if (FriendlyGrenadeAhead(sim, unit, target.Position))
        {
            Movement.ClearPath(unit); // wait for our own grenade to go off
            return;
        }
        if (unit.MoraleState == MoraleState.Pinned || unit.TargetStance is not null || unit.Action != CombatAction.None)
            return;
        if (sim.Knowledge(unit.Side).LevelOf(target.Id) == ContactLevel.Visible && GrenadeSystem.CanThrowAt(sim, unit, target, tick))
        {
            GrenadeSystem.StartThrow(unit, target);
            return;
        }
        if ((target.Position - unit.Position).LengthSquared <= (long)CombatRules.MeleeRangeCm * CombatRules.MeleeRangeCm)
            return;
        if (unit.MoveTarget is not null
            && (unit.AssaultGoal - target.Position).LengthSquared <= (long)CombatRules.AssaultRepathCm * CombatRules.AssaultRepathCm)
            return;
        var path = Pathfinder.FindPath(sim.Map, unit.Position, target.Position);
        if (path is null)
        {
            unit.AssaultTarget = null;
            Movement.ClearPath(unit);
            return;
        }
        Movement.StartPath(unit, target.Position, MoveMode.Run, path);
        unit.AssaultGoal = target.Position;
    }

    /// <summary>A live grenade of our own lies close by on the way to the target.</summary>
    private static bool FriendlyGrenadeAhead(Simulation sim, Unit unit, Vec2 goal)
    {
        foreach (var grenade in sim.Grenades)
        {
            if (grenade.Exploded || grenade.Side != unit.Side)
                continue;
            var toGrenade = grenade.Landing - unit.Position;
            long keep = grenade.Def.LethalRadiusCm + CombatRules.AssaultGrenadeClearanceCm;
            if (toGrenade.LengthSquared < keep * keep && toGrenade.Dot(goal - unit.Position) > 0)
                return true;
        }
        return false;
    }

    private static void Retreat(Simulation sim, Unit unit)
    {
        // Panic overrides whatever stance change was under way; only an ongoing retreat run is left alone.
        if (unit.MoveTarget is not null)
            return;
        if (unit.Retreated)
        {
            if (unit.TargetStance is null && unit.Stance != Stance.Prone)
                Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        unit.Retreated = true;

        if (NearestThreat(sim, unit) is not { } threat)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        var away = unit.Position - threat;
        long length = Math.Max(1, IntMath.Isqrt(away.LengthSquared));
        var spot = Clamp(sim.Map, new Vec2(
            unit.Position.X + (int)(away.X * CombatRules.RetreatDistanceCm / length),
            unit.Position.Y + (int)(away.Y * CombatRules.RetreatDistanceCm / length)));

        var path = Pathfinder.FindPath(sim.Map, unit.Position, spot);
        foreach (var (dx, dy) in RetreatFallbacks)
        {
            if (path is not null)
                break;
            path = Pathfinder.FindPath(sim.Map, unit.Position, Clamp(sim.Map, spot + new Vec2(dx, dy)));
        }
        if (path is null)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        Movement.StartPath(unit, path[^1], MoveMode.Run, path);
    }

    private static Vec2? NearestThreat(Simulation sim, Unit unit)
    {
        Vec2? best = null;
        long bestDistance = long.MaxValue;
        foreach (var contact in sim.Knowledge(unit.Side).Contacts)
        {
            if (contact.Level == ContactLevel.Unknown || sim.FindUnit(contact.Target) is not { IsAlive: true })
                continue;
            long d = (contact.Position - unit.Position).LengthSquared;
            if (d < bestDistance)
            {
                best = contact.Position;
                bestDistance = d;
            }
        }
        return best;
    }

    private static Vec2 Clamp(GridMap map, Vec2 p) =>
        new(Math.Clamp(p.X, 50, map.WidthCm - 50), Math.Clamp(p.Y, 50, map.HeightCm - 50));
}
