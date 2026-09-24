# Phase 3: Fire, Suppression, Morale & Autonomous Firefights – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Soldiers fight on their own. The test skirmish becomes a playable firefight in which the player steers movement, stance, fire policy and targets. In it, soldiers:
- spot and shoot visible enemies with data-driven weapons
- get suppressed and pinned, and go prone
- are wounded, bleed and fall
- break and are rallied by their leader

**Architecture:** `Nmf.Sim` gains a `Combat` namespace:
- `WeaponDef` for weapon data
- `Ballistics` for one bullet
- `Damage` for wounds and bleeding
- `MoraleSystem` for suppression, morale, rally and leader succession
- `Firing` for the aim → burst → recover/reload action

An `AI/SoldierBrain` picks targets, drops prone under fire and makes broken soldiers retreat. `Simulation.Step` runs them in a fixed order. `Nmf.Content` loads weapon YAML files with YamlDotNet. `Nmf.Client` adds combat effects, status text and fire orders. The generated art gains a `dead` pose and blood, and the Godot glue draws tracers, flashes, corpses, state icons and HUD bars.

**Tech Stack:** .NET 10 SDK (libraries `net8.0`), xUnit, YamlDotNet 16 (only in `Nmf.Content`), Python 3 + Pillow + numpy (art), Godot 4.7.2 .NET.

**Spec:** `docs/superpowers/specs/2026-09-24-combat-design.md` (and main spec §6, §8)

## Global Constraints

- **Determinism:**
  - all randomness comes from `Simulation.Rng`
  - units are processed in id order
  - no floating point in simulation state
  - `StateHash` covers all new combat state
- **Step order** (spec §7):
  1. orders
  2. movement
  3. firing
  4. bleeding
  5. suppression and morale
  6. every 5 ticks: vision/hearing
  7. every 5 ticks: soldier brains
- **Numbers from the spec:**
  - pinned at suppression ≥ 400, unpinned below 250; go prone at ≥ 250; morale check when suppression crosses 800
  - decay 3/tick, +2 prone, +2 near the leader; near-miss radius 250 cm; a hit adds +250 suppression
  - morale 700 (leader 800), recovery +5 per second below 100 suppression
  - morale losses: −100 for a wound, −60 for a casualty within 20 m, −150 when the leader goes down
  - leader bonus 150 × quality; rally bonus 200 × quality, −300 without a leader; command radius 3000 cm; acting leader quality 50 %
  - serious wounds turn to incapacitated after 1800 ticks
  - firing visibility 4× for 40 ticks; return-fire memory 200 ticks; retreat distance 2000 cm
  - hit radius 25 cm (prone 30 cm); aim point at 60 % of stance height; stance spread 100/80/60 %; suppression spread ×(100 + s/5) %
  - pinned soldiers aim 1.5× slower; wound speed 100/85/50/0 %
- **`Nmf.Sim` has no package dependencies.** YamlDotNet is used only in `Nmf.Content`.
- **Rejection reasons** (exact strings): `"unit is out of action"`, `"unit is broken"`, `"unit is pinned"`, `"invalid target"`.
- **Player knowledge:**
  - Tracers and muzzle flashes are drawn only for shooters shown to the player; hidden shooters show only the bullet impact.
  - Enemy corpses show only when last seen.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Review Focus

1. **A soldier hit mid-action** (while aiming, bursting, reloading, moving or changing stance): he ends in a consistent state. He is prone with no path, no target and no action, he never fires again and never moves → `Damage` tests in Task 2 and `Firing` tests in Task 4.
2. **The last visible target dies during a burst, or walks out of sight:** the burst stops, and there are no shots at corpses or through hills → `Firing` tests in Task 4.
3. **The leader dies while every other soldier is already down:** there is no successor and no crash. Morale checks on a squad with no leader still work → `MoraleSystem` tests in Task 3.
4. **A broken soldier boxed in or at the map edge:** the retreat target is clamped to the map, and an unreachable spot falls back to going prone. There is no crash and no endless re-planning → `SoldierBrain` tests in Task 5.
5. **A long firefight on the real skirmish map:** 2 minutes with no exception, identical hashes for identical seeds, and at least one shot fired → Content integration test in Task 6.

---

## File Structure

```
src/Nmf.Sim/Combat/WeaponDef.cs        # weapon record + validation
src/Nmf.Sim/Combat/CombatTypes.cs      # WoundLevel, MoraleState, FirePolicy, CombatAction
src/Nmf.Sim/Combat/CombatRules.cs      # every tunable number from the spec
src/Nmf.Sim/Combat/Ballistics.cs       # one bullet: error, terrain/cover, first man hit, near misses
src/Nmf.Sim/Combat/Damage.cs           # hit severity, escalation, out-of-action, bleeding
src/Nmf.Sim/Combat/MoraleSystem.cs     # suppression, pinned, morale checks, rally, casualties, leader
src/Nmf.Sim/Combat/Firing.cs           # aim -> burst -> recover / reload action per soldier
src/Nmf.Sim/AI/SoldierBrain.cs         # target choice, drop under fire, broken retreat
src/Nmf.Sim/Units/Unit.cs              # + combat state
src/Nmf.Sim/Units/Movement.cs          # out of action, wound speed
src/Nmf.Sim/Orders/Orders.cs           # + FireAtOrder, SetFirePolicyOrder
src/Nmf.Sim/Events/SimEvents.cs        # + ShotFired, UnitWounded, MoraleChanged, LeaderChanged
src/Nmf.Sim/Simulation.cs              # spawn with weapon, guards, new orders, step pipeline
src/Nmf.Sim/Vision/VisionRules.cs, VisionSystem.cs   # dead/out-of-action, gunfire visibility + hearing
src/Nmf.Sim/Core/StateHash.cs          # + combat state
src/Nmf.Sim/Scenarios/SkirmishScenario.cs            # loadouts
src/Nmf.Sim.Tests/Combat/*.cs, AI/SoldierBrainTests.cs, Scenarios/SkirmishCombatTests.cs
src/Nmf.Content/Weapons/WeaponLoader.cs, ContentLoadException.cs
src/Nmf.Content.Tests/WeaponLoaderTests.cs, SkirmishFightTests.cs
content/core/weapons/*.yaml            # 6 weapons
src/Nmf.Client/Effects/CombatEffects.cs
src/Nmf.Client/UnitStatus.cs, GameSession.cs, Art/UnitAnimator.cs
src/Nmf.Client.Tests/Effects/CombatEffectsTests.cs (+ updates)
tools/art/palette.py, soldiers.py, objects.py, assemble_sheet.py, tests; content/core/art (regenerated)
docs/art/character-sprite-brief.md, docs/superpowers/specs/2026-09-24-pixel-art-design.md (§5 dead row)
src/Nmf.Game/ArtLibrary.cs, UnitView.cs, Hud.cs, GameRoot.cs
README.md
```

---

### Task 1: Combat types, weapon data and unit combat state

**Files:**
- Create: `src/Nmf.Sim/Combat/WeaponDef.cs`, `src/Nmf.Sim/Combat/CombatTypes.cs`, `src/Nmf.Sim/Combat/CombatRules.cs`
- Modify: `src/Nmf.Sim/Units/Unit.cs`, `src/Nmf.Sim/Simulation.cs` (the `SpawnUnit` overload), `src/Nmf.Sim/Core/StateHash.cs`
- Test: `src/Nmf.Sim.Tests/Combat/TestWeapons.cs`, `src/Nmf.Sim.Tests/Combat/WeaponDefTests.cs`

**Interfaces:**
- Produces:
  - `enum WeaponClass { Rifle, Smg, Lmg }`
  - `sealed record WeaponDef(string Id, string Name, WeaponClass Class, int MagazineSize, int AimTicks, int RoundsPerBurst, int RoundIntervalTicks, int RecoverTicks, int ReloadTicks, int SpreadMrad, int RangeCm, int LethalityPct, int SuppressionPerRound, int NoiseRadiusCm)` with `WeaponDef Validated()`, which throws `ArgumentException("weapon '<id>': <problem>")`.
  - `enum WoundLevel { None, Light, Serious, Incapacitated, Dead }`, `enum MoraleState { Steady, Pinned, Broken }`, `enum FirePolicy { FireAtWill, ReturnFire, HoldFire }` and `enum CombatAction { None, Aiming, Firing, Recovering, Reloading }`.
  - `static class CombatRules` holding every constant listed under Global Constraints, plus `StanceSpreadPct(Stance)`, `WoundSpeedPct(WoundLevel)` and `WoundMoralePenalty(WoundLevel)`.
  - New `Unit` members:
    - `WeaponDef? Weapon` and `int Ammo`
    - `bool IsLeader` and `int LeaderQualityPct`
    - `WoundLevel Wound` and `long WoundTick`
    - `int Suppression`, `int Morale` and `MoraleState MoraleState`
    - `FirePolicy FirePolicy`, `CombatAction Action`, `int ActionTicksLeft` and `int RoundsLeftInBurst`
    - `UnitId? Target`, `UnitId? OrderedTarget` and `long LastShotTick` (starts at `Unit.NeverShot = -1_000_000`)
    - `bool IsAlive` and `bool IsOutOfAction`
    - internal `bool Retreated`
  - `Simulation.SpawnUnit(Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader = false)`. The old 3-argument overload spawns an unarmed soldier.
  - `StateHash` covers all new fields.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Combat/TestWeapons.cs`:
```csharp
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
```

`src/Nmf.Sim.Tests/Combat/WeaponDefTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class WeaponDefTests
{
    [Theory]
    [InlineData(0, 1, 100, "magazine")]
    [InlineData(5, 0, 100, "burst")]
    [InlineData(5, 1, 101, "lethality")]
    public void Validated_RejectsBadValues(int magazine, int burst, int lethality, string field)
    {
        var bad = new WeaponDef("x", "X", WeaponClass.Rifle, magazine, 1, burst, 0, 1, 1, 5, 10_000, lethality, 10, 100);
        var ex = Assert.Throws<ArgumentException>(() => bad.Validated());
        Assert.Contains(field, ex.Message);
        Assert.Contains("'x'", ex.Message);
    }

    [Fact]
    public void SpawnUnit_WithWeapon_StartsLoadedWithBaseMorale()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var rifleman = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7, TestWeapons.Rifle(magazine: 5));
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(150, 50), 7, TestWeapons.Smg(), isLeader: true);
        var unarmed = sim.SpawnUnit(Side.Red, new Vec2(250, 50), 7);

        Assert.Equal(5, rifleman.Ammo);
        Assert.Equal(CombatRules.BaseMorale, rifleman.Morale);
        Assert.Equal(CombatRules.LeaderMorale, leader.Morale);
        Assert.True(leader.IsLeader);
        Assert.Equal(100, leader.LeaderQualityPct);
        Assert.Null(unarmed.Weapon);
        Assert.True(unarmed.IsAlive);
        Assert.False(unarmed.IsOutOfAction);
        Assert.Equal(FirePolicy.FireAtWill, unarmed.FirePolicy);
        Assert.Equal(Unit.NeverShot, unarmed.LastShotTick);
    }

    [Fact]
    public void StateHash_ChangesWithCombatState()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7, TestWeapons.Rifle());
        var before = StateHash.Compute(sim);
        u.Suppression = 10;
        Assert.NotEqual(before, StateHash.Compute(sim));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: build FAILS with `The type or namespace name 'Combat' does not exist`.

- [ ] **Step 3: Implement the combat types**

`src/Nmf.Sim/Combat/WeaponDef.cs`:
```csharp
namespace Nmf.Sim.Combat;

public enum WeaponClass : byte
{
    Rifle,
    Smg,
    Lmg,
}

/// <summary>Static weapon data (content/core/weapons/*.yaml). Times are ticks, distances centimetres.</summary>
public sealed record WeaponDef(
    string Id,
    string Name,
    WeaponClass Class,
    int MagazineSize,
    int AimTicks,
    int RoundsPerBurst,
    int RoundIntervalTicks,
    int RecoverTicks,
    int ReloadTicks,
    int SpreadMrad,
    int RangeCm,
    int LethalityPct,
    int SuppressionPerRound,
    int NoiseRadiusCm)
{
    /// <summary>Returns this weapon, or throws <see cref="ArgumentException"/> naming the first invalid field.</summary>
    public WeaponDef Validated()
    {
        Require(!string.IsNullOrWhiteSpace(Id), "id must not be empty");
        Require(MagazineSize >= 1, "magazine must be at least 1");
        Require(RoundsPerBurst >= 1, "burst must be at least 1");
        Require(AimTicks >= 0 && RoundIntervalTicks >= 0 && RecoverTicks >= 0 && ReloadTicks >= 0, "tick values must not be negative");
        Require(SpreadMrad >= 0, "spread_mrad must not be negative");
        Require(RangeCm >= 100, "range_m must be at least 1");
        Require(LethalityPct is >= 0 and <= 100, "lethality_pct must be 0..100");
        Require(SuppressionPerRound >= 0, "suppression must not be negative");
        Require(NoiseRadiusCm >= 0, "noise_m must not be negative");
        return this;
    }

    private void Require(bool ok, string problem)
    {
        if (!ok)
            throw new ArgumentException($"weapon '{Id}': {problem}");
    }
}
```

`src/Nmf.Sim/Combat/CombatTypes.cs`:
```csharp
namespace Nmf.Sim.Combat;

public enum WoundLevel : byte
{
    None,
    Light,
    Serious,
    Incapacitated,
    Dead,
}

public enum MoraleState : byte
{
    Steady,
    Pinned,
    Broken,
}

public enum FirePolicy : byte
{
    FireAtWill,
    ReturnFire,
    HoldFire,
}

public enum CombatAction : byte
{
    None,
    Aiming,
    Firing,
    Recovering,
    Reloading,
}
```

`src/Nmf.Sim/Combat/CombatRules.cs`:
```csharp
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Every tunable number of the combat model (spec 2026-09-24-combat-design §3–§6).</summary>
public static class CombatRules
{
    public const int MaxSuppression = 1000;
    public const int PinnedAt = 400;
    public const int UnpinBelow = 250;
    public const int GoProneAt = 250;
    public const int MoraleCheckSuppression = 800;
    public const int CalmSuppression = 100;
    public const int SuppressionDecayPerTick = 3;
    public const int ProneDecayBonus = 2;
    public const int LeaderDecayBonus = 2;
    public const int NearMissRadiusCm = 250;
    public const int HitSuppression = 250;

    public const int MaxMorale = 1000;
    public const int BaseMorale = 700;
    public const int LeaderMorale = 800;
    public const int MoraleIntervalTicks = 20;
    public const int MoraleRecoveryPerInterval = 5;
    public const int WoundMoraleLoss = 100;
    public const int CasualtyMoraleLoss = 60;
    public const int LeaderLossMoraleLoss = 150;
    public const int CasualtyWitnessRadiusCm = 2000;
    public const int LeaderMoraleBonus = 150;
    public const int LeaderRallyBonus = 200;
    public const int NoLeaderRallyPenalty = 300;
    public const int RallyMoraleGain = 100;
    public const int CommandRadiusCm = 3000;
    public const int ActingLeaderQualityPct = 50;

    public const int SeriousBleedTicks = 1800;
    public const int FiringVisibilityTicks = 40;
    public const int FiringVisibilityPct = 400;
    public const int ReturnFireMemoryTicks = 200;
    public const int RetreatDistanceCm = 2000;
    public const int UnitHitRadiusCm = 25;
    public const int ProneHitRadiusCm = 30;
    public const int AimPointPct = 60;
    public const int PinnedAimPct = 150;

    public static int StanceSpreadPct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 80,
        _ => 60,
    };

    public static int WoundSpeedPct(WoundLevel wound) => wound switch
    {
        WoundLevel.None => 100,
        WoundLevel.Light => 85,
        WoundLevel.Serious => 50,
        _ => 0,
    };

    public static int WoundMoralePenalty(WoundLevel wound) => wound switch
    {
        WoundLevel.Light => 100,
        WoundLevel.Serious => 250,
        _ => 0,
    };
}
```

- [ ] **Step 4: Extend the unit, spawning and state hash**

In `src/Nmf.Sim/Units/Unit.cs`:
- Add `using Nmf.Sim.Combat;`.
- Change the constructor to `internal Unit(UnitId id, Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader)`, and at its end add:
```csharp
        Weapon = weapon;
        Ammo = weapon?.MagazineSize ?? 0;
        IsLeader = isLeader;
        LeaderQualityPct = isLeader ? 100 : 0;
        Morale = isLeader ? CombatRules.LeaderMorale : CombatRules.BaseMorale;
```
- Add these members after `MovedSinceVisionUpdate`:
```csharp
    public const long NeverShot = -1_000_000;

    public WeaponDef? Weapon { get; }
    public int Ammo { get; internal set; }
    public bool IsLeader { get; internal set; }

    /// <summary>100 for the original leader, 50 for a man who took over.</summary>
    public int LeaderQualityPct { get; internal set; }

    public WoundLevel Wound { get; internal set; }
    public long WoundTick { get; internal set; }
    public int Suppression { get; internal set; }
    public int Morale { get; internal set; }
    public MoraleState MoraleState { get; internal set; }
    public FirePolicy FirePolicy { get; internal set; }
    public CombatAction Action { get; internal set; }
    public int ActionTicksLeft { get; internal set; }
    public int RoundsLeftInBurst { get; internal set; }
    public UnitId? Target { get; internal set; }
    public UnitId? OrderedTarget { get; internal set; }
    public long LastShotTick { get; internal set; } = NeverShot;

    public bool IsAlive => Wound != WoundLevel.Dead;
    public bool IsOutOfAction => Wound >= WoundLevel.Incapacitated;

    /// <summary>A broken soldier has already started (or finished) his retreat.</summary>
    internal bool Retreated { get; set; }
```

In `src/Nmf.Sim/Simulation.cs`:
- Add `using Nmf.Sim.Combat;`.
- Replace the whole `SpawnUnit` method with:
```csharp
    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick) => SpawnUnit(side, position, speedCmPerTick, null);

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick, WeaponDef? weapon, bool isLeader = false)
    {
        if (!Map.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position), $"Spawn position {position} is outside the map.");
        if (speedCmPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedCmPerTick), speedCmPerTick, "Speed must be positive.");

        var unit = new Unit(new UnitId(_nextUnitId++), side, position, speedCmPerTick, weapon, isLeader);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }
```

In `src/Nmf.Sim/Core/StateHash.cs`, inside the unit loop after the path waypoints, add:
```csharp
            h.Add(unit.Weapon?.Id.GetHashCode(StringComparison.Ordinal) ?? 0);
            h.Add(unit.Ammo);
            h.Add(unit.IsLeader ? 1 : 0);
            h.Add(unit.LeaderQualityPct);
            h.Add((int)unit.Wound);
            h.Add((ulong)unit.WoundTick);
            h.Add(unit.Suppression);
            h.Add(unit.Morale);
            h.Add((int)unit.MoraleState);
            h.Add((int)unit.FirePolicy);
            h.Add((int)unit.Action);
            h.Add(unit.ActionTicksLeft);
            h.Add(unit.RoundsLeftInBurst);
            h.Add(unit.Target?.Value ?? 0);
            h.Add(unit.OrderedTarget?.Value ?? 0);
            h.Add((ulong)unit.LastShotTick);
            h.Add(unit.Retreated ? 1 : 0);
            h.Add(unit.MovedSinceVisionUpdate ? 1 : 0);
```
Note: `string.GetHashCode` is randomised per process in .NET, so replace the weapon line with a stable ordinal FNV over the characters:
```csharp
            foreach (char c in unit.Weapon?.Id ?? "")
                h.Add(c);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS (all existing tests plus the new ones).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(sim): combat types, weapon data and unit combat state

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Ballistics and damage

**Files:**
- Create: `src/Nmf.Sim/Combat/Ballistics.cs`, `src/Nmf.Sim/Combat/Damage.cs`
- Modify: `src/Nmf.Sim/Events/SimEvents.cs` (add `UnitWounded`, `MoraleChanged`, `LeaderChanged`, `ShotFired`), `src/Nmf.Sim/Units/Movement.cs`
- Create (stub for this task, completed in Task 3): `src/Nmf.Sim/Combat/MoraleSystem.cs`, and in Task 4: `src/Nmf.Sim/Combat/Firing.cs`
- Test: `src/Nmf.Sim.Tests/Combat/BallisticsTests.cs`, `src/Nmf.Sim.Tests/Combat/DamageTests.cs`

**Interfaces:**
- Consumes: `LineOfSight`, `StanceRules`, `CombatRules`, `Unit` combat state (Task 1)
- Produces:
  - `internal readonly record struct NearMiss(Unit Unit, int DistanceCm)` and `internal sealed record ShotResult(Vec2 End, Unit? Hit, IReadOnlyList<NearMiss> NearMisses)`.
  - `internal static class Ballistics` with `ShotResult Trace(Simulation sim, Unit shooter, Unit target)`. It draws exactly 2 RNG values (lateral and vertical error) when the spread is above 0, plus one per cover roll.
  - `internal static class Damage` with `ApplyHit(Simulation, Unit, WeaponDef, long tick, List<SimEvent>)`, `SetWound(Simulation, Unit, WoundLevel, long tick, List<SimEvent>)` and `Update(Simulation, Unit, long tick, List<SimEvent>)` (bleeding).
  - Events:
    - `ShotFired(long Tick, UnitId Shooter, Vec2 From, Vec2 To, UnitId? Hit)`
    - `UnitWounded(long Tick, UnitId Unit, WoundLevel Level)`
    - `MoraleChanged(long Tick, UnitId Unit, MoraleState State)`
    - `LeaderChanged(long Tick, Side Side, UnitId Leader)`
  - `Movement.Update` makes out-of-action units stay put, and the path budget is scaled by `CombatRules.WoundSpeedPct`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Combat/BallisticsTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class BallisticsTests
{
    private static readonly Vec2 ShooterPos = new(50, 1050);  // cell (0,10)
    private static readonly Vec2 TargetPos = new(2050, 1050); // cell (20,10), 20 m east

    private static (Simulation Sim, Unit Shooter, Unit Target) Setup(GridMap? map = null, int spread = 0, ulong seed = 3)
    {
        var sim = new Simulation(map ?? new GridMap(60, 20, ["none"]), seed);
        var shooter = sim.SpawnUnit(Side.Blue, ShooterPos, 7, TestWeapons.Rifle(spread: spread));
        var target = sim.SpawnUnit(Side.Red, TargetPos, 7);
        return (sim, shooter, target);
    }

    [Fact]
    public void ZeroSpread_HitsStandingTargetInOpen()
    {
        var (sim, shooter, target) = Setup();
        var shot = Ballistics.Trace(sim, shooter, target);
        Assert.Same(target, shot.Hit);
        Assert.Equal(TargetPos, shot.End);
    }

    [Fact]
    public void HillBetween_StopsTheBullet()
    {
        var map = new GridMap(60, 20, ["none"]);
        for (int y = 0; y < 20; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, shooter, target) = Setup(map);
        var shot = Ballistics.Trace(sim, shooter, target);
        Assert.Null(shot.Hit);
        Assert.Equal(1050, shot.End.X);
    }

    [Fact]
    public void RockWithFullCover_ProtectsProneTargetBehindIt()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(19, 10)] = new CellData(0, 120, 255, 255, 0, CellData.Impassable);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        Assert.Null(Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void BushWithoutCover_DoesNotStopTheBullet()
    {
        var map = new GridMap(60, 20, ["none"]);
        map[new CellCoord(19, 10)] = new CellData(0, 80, 153, 0, 0);
        var (sim, shooter, target) = Setup(map);
        target.Stance = Stance.Prone;
        Assert.Same(target, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void FriendInTheLine_TakesTheBullet()
    {
        var (sim, shooter, target) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        Assert.Same(friend, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void DeadMenInTheLine_AreIgnored()
    {
        var (sim, shooter, target) = Setup();
        var corpse = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        corpse.Wound = WoundLevel.Dead;
        Assert.Same(target, Ballistics.Trace(sim, shooter, target).Hit);
    }

    [Fact]
    public void NearMisses_ListOnlyEnemiesBesideTheFlight()
    {
        var (sim, shooter, target) = Setup();
        var enemyBeside = sim.SpawnUnit(Side.Red, new Vec2(1050, 1200), 7);
        sim.SpawnUnit(Side.Blue, new Vec2(1050, 900), 7);
        var shot = Ballistics.Trace(sim, shooter, target);
        var miss = Assert.Single(shot.NearMisses);
        Assert.Same(enemyBeside, miss.Unit);
        Assert.Equal(150, miss.DistanceCm);
    }

    [Fact]
    public void LargerSpread_MissesMoreOften()
    {
        int Hits(int spread)
        {
            var (sim, shooter, target) = Setup(spread: spread);
            int hits = 0;
            for (int i = 0; i < 300; i++)
                if (Ballistics.Trace(sim, shooter, target).Hit == target) hits++;
            return hits;
        }
        int tight = Hits(4), wide = Hits(80);
        Assert.Equal(300, tight);
        Assert.InRange(wide, 1, 150);
    }

    [Fact]
    public void SameSeed_GivesSameShots()
    {
        var (a, sa, ta) = Setup(spread: 40, seed: 11);
        var (b, sb, tb) = Setup(spread: 40, seed: 11);
        for (int i = 0; i < 50; i++)
            Assert.Equal(Ballistics.Trace(a, sa, ta).End, Ballistics.Trace(b, sb, tb).End);
    }
}
```

`src/Nmf.Sim.Tests/Combat/DamageTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class DamageTests
{
    private static (Simulation Sim, Unit Unit) Setup()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 5);
        return (sim, sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle()));
    }

    [Fact]
    public void ZeroLethality_GivesLightWoundsThatEscalateWithEachHit()
    {
        var (sim, unit) = Setup();
        var weapon = TestWeapons.Rifle(lethality: 0);
        var events = new List<SimEvent>();
        var levels = new List<WoundLevel>();
        for (int i = 0; i < 4; i++)
        {
            Damage.ApplyHit(sim, unit, weapon, sim.Tick, events);
            levels.Add(unit.Wound);
        }
        Assert.Equal(new[] { WoundLevel.Light, WoundLevel.Serious, WoundLevel.Incapacitated, WoundLevel.Dead }, levels);
        Assert.Contains<SimEvent>(new UnitWounded(0, unit.Id, WoundLevel.Dead), events);
    }

    [Fact]
    public void FullLethality_NeverGivesLightWounds()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 9);
        var weapon = TestWeapons.Rifle(lethality: 100);
        for (int i = 0; i < 60; i++)
        {
            var u = sim.SpawnUnit(Side.Blue, new Vec2(50 + i * 50, 50), 7);
            Damage.ApplyHit(sim, u, weapon, 0, []);
            Assert.NotEqual(WoundLevel.Light, u.Wound);
        }
    }

    [Fact]
    public void GoingDown_StopsEverythingAndDropsProne()
    {
        var (sim, unit) = Setup();
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(5050, 1050)));
        sim.Step();
        unit.Action = CombatAction.Aiming;
        unit.Target = new UnitId(99);
        Damage.SetWound(sim, unit, WoundLevel.Incapacitated, sim.Tick, []);

        Assert.Null(unit.MoveTarget);
        Assert.Empty(unit.Path);
        Assert.Equal(Stance.Prone, unit.Stance);
        Assert.Null(unit.TargetStance);
        Assert.Equal(CombatAction.None, unit.Action);
        Assert.Null(unit.Target);
        var at = unit.Position;
        for (int i = 0; i < 40; i++) sim.Step();
        Assert.Equal(at, unit.Position);
    }

    [Fact]
    public void SeriousWound_BleedsUntilDown()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Serious, 0, []);
        var events = new List<SimEvent>();
        for (int i = 0; i <= CombatRules.SeriousBleedTicks; i++) events.AddRange(sim.Step());
        Assert.Equal(WoundLevel.Incapacitated, unit.Wound);
        Assert.Contains(events, e => e is UnitWounded { Level: WoundLevel.Incapacitated });
    }

    [Fact]
    public void Wound_CostsMorale()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Light, 0, []);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.WoundMoraleLoss, unit.Morale);
    }

    [Fact]
    public void WoundedSoldier_MovesSlower()
    {
        var (sim, unit) = Setup();
        Damage.SetWound(sim, unit, WoundLevel.Serious, 0, []);
        unit.MoraleState = MoraleState.Steady;
        sim.Submit(Side.Blue, new MoveOrder(unit.Id, new Vec2(1090, 1050)));
        sim.Step();
        Assert.Equal(new Vec2(1053, 1050), unit.Position); // 7 cm/tick at 50 %
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: build FAILS with `The name 'Ballistics' does not exist`.

- [ ] **Step 3: Add events and minimal stubs for the systems Damage calls**

Append to `src/Nmf.Sim/Events/SimEvents.cs` (and add `using Nmf.Sim.Combat;`):
```csharp

public sealed record ShotFired(long Tick, UnitId Shooter, Vec2 From, Vec2 To, UnitId? Hit) : SimEvent(Tick);

public sealed record UnitWounded(long Tick, UnitId Unit, WoundLevel Level) : SimEvent(Tick);

public sealed record MoraleChanged(long Tick, UnitId Unit, MoraleState State) : SimEvent(Tick);

public sealed record LeaderChanged(long Tick, Side Side, UnitId Leader) : SimEvent(Tick);
```

Create `src/Nmf.Sim/Combat/MoraleSystem.cs` as a stub (Task 3 replaces it):
```csharp
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

internal static class MoraleSystem
{
    public static void AddSuppression(Simulation sim, Unit unit, int amount, long tick, List<SimEvent> events) =>
        unit.Suppression = Math.Min(CombatRules.MaxSuppression, unit.Suppression + Math.Max(0, amount));

    public static void Check(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
    }

    public static void OnCasualty(Simulation sim, Unit casualty, long tick, List<SimEvent> events)
    {
    }

    public static void Tick(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
    }
}
```

Create `src/Nmf.Sim/Combat/Firing.cs` as a stub (Task 4 replaces it):
```csharp
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

internal static class Firing
{
    public static void Cancel(Unit unit)
    {
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
        {
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
            unit.RoundsLeftInBurst = 0;
        }
        unit.Target = null;
    }
}
```

- [ ] **Step 4: Implement ballistics and damage**

`src/Nmf.Sim/Combat/Ballistics.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Combat;

internal readonly record struct NearMiss(Unit Unit, int DistanceCm);

internal sealed record ShotResult(Vec2 End, Unit? Hit, IReadOnlyList<NearMiss> NearMisses);

/// <summary>One bullet: aim error, then terrain and cover along the flight, then the first man in its path.</summary>
internal static class Ballistics
{
    public static ShotResult Trace(Simulation sim, Unit shooter, Unit target)
    {
        var map = sim.Map;
        var weapon = shooter.Weapon ?? throw new InvalidOperationException("An unarmed unit cannot fire.");
        var from = shooter.Position;
        var toTarget = target.Position - from;
        long distance = Math.Max(1, IntMath.Isqrt(toTarget.LengthSquared));

        int spread = weapon.SpreadMrad * CombatRules.StanceSpreadPct(shooter.Stance) / 100 * (100 + shooter.Suppression / 5) / 100;
        int lateralMrad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);
        int verticalMrad = spread == 0 ? 0 : sim.Rng.NextInt(-spread, spread + 1);

        long lateral = distance * lateralMrad / 1000;
        var aim = new Vec2(
            target.Position.X + (int)(-toTarget.Y * lateral / distance),
            target.Position.Y + (int)(toTarget.X * lateral / distance));
        long fromHeight = map.CellAt(from).GroundHeightCm + StanceRules.EyeHeightCm(shooter.Stance);
        long aimHeight = map.CellAt(target.Position).GroundHeightCm
                         + StanceRules.HeightCm(target.Stance) * CombatRules.AimPointPct / 100
                         + distance * verticalMrad / 1000;

        var dir = aim - from;
        long dirLength = Math.Max(1, IntMath.Isqrt(dir.LengthSquared));
        long HeightAt(long s) => fromHeight + (aimHeight - fromHeight) * s / dirLength;

        long stopAt = StopDistance(sim, from, dir, dirLength, weapon.RangeCm, HeightAt);

        Unit? hit = null;
        long hitAt = long.MaxValue;
        foreach (var unit in sim.Units)
        {
            if (unit == shooter || !unit.IsAlive)
                continue;
            var (along, side) = Project(unit.Position - from, dir, dirLength);
            if (along <= 0 || along > stopAt || along >= hitAt)
                continue;
            int radius = unit.Stance == Stance.Prone ? CombatRules.ProneHitRadiusCm : CombatRules.UnitHitRadiusCm;
            long ground = map.CellAt(unit.Position).GroundHeightCm;
            long height = HeightAt(along);
            if (side <= radius && height >= ground && height <= ground + StanceRules.HeightCm(unit.Stance))
            {
                hit = unit;
                hitAt = along;
            }
        }

        long flight = hit is null ? stopAt : hitAt;
        var misses = new List<NearMiss>();
        foreach (var unit in sim.Units)
        {
            if (unit == hit || unit.Side == shooter.Side || unit.IsOutOfAction)
                continue;
            var (along, side) = Project(unit.Position - from, dir, dirLength);
            if (along > 0 && along <= flight && side <= CombatRules.NearMissRadiusCm)
                misses.Add(new NearMiss(unit, (int)side));
        }

        var end = hit?.Position ?? new Vec2(from.X + (int)(dir.X * flight / dirLength), from.Y + (int)(dir.Y * flight / dirLength));
        return new ShotResult(end, hit, misses);
    }

    /// <summary>Distance along the flight line and sideways distance from it, in centimetres.</summary>
    private static (long Along, long Side) Project(Vec2 rel, Vec2 dir, long dirLength) =>
        (((long)rel.X * dir.X + (long)rel.Y * dir.Y) / dirLength,
         Math.Abs((long)rel.X * dir.Y - (long)rel.Y * dir.X) / dirLength);

    /// <summary>Where along the line the bullet stops: in the ground, in cover, at the map edge or at maximum range.</summary>
    private static long StopDistance(Simulation sim, Vec2 from, Vec2 dir, long dirLength, long range, Func<long, long> heightAt)
    {
        var map = sim.Map;
        var end = new Vec2(from.X + (int)(dir.X * range / dirLength), from.Y + (int)(dir.Y * range / dirLength));
        var a = from.ToCell();
        var b = end.ToCell();
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        int err = dx - dy;
        int x = a.X, y = a.Y;
        long last = 0;
        while (x != b.X || y != b.Y)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
            var coord = new CellCoord(x, y);
            if (!map.InBounds(coord))
                return last;
            var (s, _) = Project(coord.CenterCm - from, dir, dirLength);
            if (s <= 0)
                continue;
            if (s > range)
                return range;
            last = s;
            var cell = map[coord];
            long h = heightAt(s);
            if (h <= cell.GroundHeightCm)
                return s;
            if (h < cell.GroundHeightCm + cell.ObstacleHeightCm && cell.Cover > 0 && sim.Rng.NextInt(255) < cell.Cover)
                return s;
        }
        return range;
    }
}
```

`src/Nmf.Sim/Combat/Damage.cs`:
```csharp
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Wound severity, escalation, going down and bleeding.</summary>
internal static class Damage
{
    public static void ApplyHit(Simulation sim, Unit unit, WeaponDef weapon, long tick, List<SimEvent> events)
    {
        int roll = sim.Rng.NextInt(100);
        int l = weapon.LethalityPct;
        var level = roll < l / 3 ? WoundLevel.Dead
            : roll < l * 2 / 3 ? WoundLevel.Incapacitated
            : roll < l ? WoundLevel.Serious
            : WoundLevel.Light;
        if (unit.Wound != WoundLevel.None && level <= unit.Wound)
            level = (WoundLevel)Math.Min((int)WoundLevel.Dead, (int)unit.Wound + 1);
        SetWound(sim, unit, level, tick, events);
        MoraleSystem.AddSuppression(sim, unit, CombatRules.HitSuppression, tick, events);
    }

    public static void SetWound(Simulation sim, Unit unit, WoundLevel level, long tick, List<SimEvent> events)
    {
        bool wasInAction = !unit.IsOutOfAction;
        unit.Wound = level;
        unit.WoundTick = tick;
        events.Add(new UnitWounded(tick, unit.Id, level));

        if (unit.IsOutOfAction)
        {
            Movement.ClearPath(unit);
            unit.TargetStance = null;
            unit.StanceTicksLeft = 0;
            unit.Stance = Stance.Prone;
            Firing.Cancel(unit);
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
            unit.Suppression = 0;
            if (wasInAction)
                MoraleSystem.OnCasualty(sim, unit, tick, events);
        }
        else
        {
            unit.Morale = Math.Max(0, unit.Morale - CombatRules.WoundMoraleLoss);
            MoraleSystem.Check(sim, unit, tick, events);
        }
    }

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.Wound == WoundLevel.Serious && tick - unit.WoundTick >= CombatRules.SeriousBleedTicks)
            SetWound(sim, unit, WoundLevel.Incapacitated, tick, events);
    }
}
```

In `src/Nmf.Sim/Units/Movement.cs`:
- Add `using Nmf.Sim.Combat;`.
- At the start of `Update`, after `unit.IsMoving = false;`, add:
```csharp
        if (unit.IsOutOfAction)
            return;
```
- In `FollowPath`, replace the budget line with:
```csharp
        int budget = Math.Max(1, StanceRules.SpeedCmPerTick(unit, unit.MoveMode) * 100 / map.CellAt(from).MoveCostPct
                                 * CombatRules.WoundSpeedPct(unit.Wound) / 100);
```

In `src/Nmf.Sim/Simulation.cs` `Step()`, after the movement loop, add:
```csharp
        foreach (var unit in _units)
        {
            Damage.Update(this, unit, Tick, events);
            MoraleSystem.Tick(this, unit, Tick, events);
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(sim): ballistics with cover and near misses, wounds and bleeding

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Suppression, morale, rally and leadership

**Files:**
- Replace: `src/Nmf.Sim/Combat/MoraleSystem.cs`
- Test: `src/Nmf.Sim.Tests/Combat/MoraleTests.cs`

**Interfaces:**
- Consumes: `Damage.SetWound` (Task 2), `Movement.ClearPath` and `BeginStanceChange`
- Produces: `MoraleSystem` with these methods:
  - `AddSuppression(sim, unit, amount, tick, events)`: pins and triggers a morale check when suppression crosses 800
  - `Tick(sim, unit, tick, events)`: decay, recovery, rally, pin/unpin
  - `Check(sim, unit, tick, events)`
  - `OnCasualty(sim, casualty, tick, events)`: witness morale losses, leader succession and `LeaderChanged`
  - `Unit? LeaderInRange(Simulation, Unit)`

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Combat/MoraleTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class MoraleTests
{
    private static Simulation NewSim() => new(new GridMap(60, 20, ["none"]), 7);

    [Fact]
    public void Suppression_DecaysFasterWhenProneAndNearLeader()
    {
        var sim = NewSim();
        var standing = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        var prone = sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        prone.Stance = Stance.Prone;
        standing.Suppression = 100;
        prone.Suppression = 100;
        sim.Step();
        Assert.Equal(97, standing.Suppression);
        Assert.Equal(95, prone.Suppression);

        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        sim.Step();
        Assert.Equal(92, standing.Suppression);
    }

    [Fact]
    public void HeavySuppression_PinsAndDropsProne_ThenUnpinsWhenItFades()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = CombatRules.MaxMorale;
        var events = new List<SimEvent>();
        MoraleSystem.AddSuppression(sim, u, 450, sim.Tick, events);
        Assert.Equal(MoraleState.Pinned, u.MoraleState);
        Assert.Equal(Stance.Prone, u.TargetStance);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Pinned), events);

        for (int i = 0; i < 60 && u.MoraleState == MoraleState.Pinned; i++) sim.Step();
        Assert.Equal(MoraleState.Steady, u.MoraleState);
        Assert.True(u.Suppression < CombatRules.UnpinBelow);
    }

    [Fact]
    public void MoraleCheck_WithNoMorale_Breaks()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 0;
        var events = new List<SimEvent>();
        MoraleSystem.Check(sim, u, 0, events);
        Assert.Equal(MoraleState.Broken, u.MoraleState);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Broken), events);
    }

    [Fact]
    public void MoraleCheck_WithFullMoraleAndNoPressure_NeverBreaks()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = CombatRules.MaxMorale;
        for (int i = 0; i < 200; i++) MoraleSystem.Check(sim, u, 0, []);
        Assert.Equal(MoraleState.Steady, u.MoraleState);
    }

    [Fact]
    public void CrossingTheCheckThreshold_TriggersAMoraleCheck()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 0;
        MoraleSystem.AddSuppression(sim, u, 850, 0, []);
        Assert.Equal(MoraleState.Broken, u.MoraleState);
    }

    [Fact]
    public void LeaderDeath_PassesCommandAndCostsEveryoneMorale()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var second = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        var far = sim.SpawnUnit(Side.Blue, new Vec2(5050, 1050), 7);
        var events = new List<SimEvent>();

        Damage.SetWound(sim, leader, WoundLevel.Dead, 0, events);

        Assert.False(leader.IsLeader);
        Assert.True(second.IsLeader);
        Assert.Equal(CombatRules.ActingLeaderQualityPct, second.LeaderQualityPct);
        Assert.Contains<SimEvent>(new LeaderChanged(0, Side.Blue, second.Id), events);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.CasualtyMoraleLoss - CombatRules.LeaderLossMoraleLoss, second.Morale);
        Assert.Equal(CombatRules.BaseMorale - CombatRules.LeaderLossMoraleLoss, far.Morale);
    }

    [Fact]
    public void LeaderDeath_WithEveryoneElseDown_LeavesNoLeaderWithoutCrashing()
    {
        var sim = NewSim();
        var leader = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, null, isLeader: true);
        var other = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        Damage.SetWound(sim, other, WoundLevel.Incapacitated, 0, []);
        Damage.SetWound(sim, leader, WoundLevel.Dead, 0, []);
        Assert.DoesNotContain(sim.Units, u => u.IsLeader);
        sim.Step();
    }

    [Fact]
    public void BrokenSoldier_RalliesQuicklyNearHisLeader()
    {
        var sim = NewSim();
        sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7, null, isLeader: true);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.MoraleState = MoraleState.Broken;
        u.Morale = 900;
        var events = new List<SimEvent>();
        events.AddRange(sim.Step());
        Assert.Equal(MoraleState.Steady, u.MoraleState);
        Assert.Equal(CombatRules.MaxMorale, u.Morale);
        Assert.Contains<SimEvent>(new MoraleChanged(0, u.Id, MoraleState.Steady), events);
    }

    [Fact]
    public void BrokenSoldier_WithLowMoraleAndNoLeader_StaysBroken()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.MoraleState = MoraleState.Broken;
        u.Morale = 200;
        for (int i = 0; i < 400; i++) sim.Step();
        Assert.Equal(MoraleState.Broken, u.MoraleState);
    }

    [Fact]
    public void Morale_RecoversSlowlyWhenCalm()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Morale = 500;
        for (int i = 0; i < 40; i++) sim.Step(); // intervals at ticks 0 and 20
        Assert.Equal(510, u.Morale);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~MoraleTests`
Expected: FAIL. With the stub, suppression does not decay (`Expected: 97, Actual: 100`) and no state changes happen.

- [ ] **Step 3: Implement**

Replace `src/Nmf.Sim/Combat/MoraleSystem.cs`:
```csharp
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Combat;

/// <summary>Suppression, the Steady / Pinned / Broken states, morale checks, rally and leader succession.</summary>
internal static class MoraleSystem
{
    public static void AddSuppression(Simulation sim, Unit unit, int amount, long tick, List<SimEvent> events)
    {
        if (amount <= 0 || unit.IsOutOfAction)
            return;
        int before = unit.Suppression;
        unit.Suppression = Math.Min(CombatRules.MaxSuppression, unit.Suppression + amount);
        if (before < CombatRules.MoraleCheckSuppression && unit.Suppression >= CombatRules.MoraleCheckSuppression)
            Check(sim, unit, tick, events);
        UpdatePinned(unit, tick, events);
    }

    public static void Tick(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction)
            return;
        if (unit.Suppression > 0)
        {
            int decay = CombatRules.SuppressionDecayPerTick
                        + (unit.Stance == Stance.Prone ? CombatRules.ProneDecayBonus : 0)
                        + (LeaderInRange(sim, unit) is not null ? CombatRules.LeaderDecayBonus : 0);
            unit.Suppression = Math.Max(0, unit.Suppression - decay);
        }
        if (tick % CombatRules.MoraleIntervalTicks == 0)
        {
            if (unit.Suppression < CombatRules.CalmSuppression)
                unit.Morale = Math.Min(BaseMorale(unit), unit.Morale + CombatRules.MoraleRecoveryPerInterval);
            if (unit.MoraleState == MoraleState.Broken)
                TryRally(sim, unit, tick, events);
        }
        UpdatePinned(unit, tick, events);
    }

    public static void Check(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction || unit.MoraleState == MoraleState.Broken)
            return;
        int effective = unit.Morale + LeaderBonus(sim, unit, CombatRules.LeaderMoraleBonus)
                        - unit.Suppression / 4 - CombatRules.WoundMoralePenalty(unit.Wound);
        if (sim.Rng.NextInt(1000) >= effective)
            Break(unit, tick, events);
    }

    public static void OnCasualty(Simulation sim, Unit casualty, long tick, List<SimEvent> events)
    {
        long witnessSq = (long)CombatRules.CasualtyWitnessRadiusCm * CombatRules.CasualtyWitnessRadiusCm;
        foreach (var other in sim.Units)
        {
            if (other == casualty || other.Side != casualty.Side || other.IsOutOfAction)
                continue;
            if ((other.Position - casualty.Position).LengthSquared > witnessSq)
                continue;
            other.Morale = Math.Max(0, other.Morale - CombatRules.CasualtyMoraleLoss);
            Check(sim, other, tick, events);
        }

        if (!casualty.IsLeader)
            return;
        casualty.IsLeader = false;
        var successor = sim.Units.FirstOrDefault(u => u.Side == casualty.Side && !u.IsOutOfAction);
        if (successor is not null)
        {
            successor.IsLeader = true;
            successor.LeaderQualityPct = CombatRules.ActingLeaderQualityPct;
            events.Add(new LeaderChanged(tick, casualty.Side, successor.Id));
        }
        foreach (var other in sim.Units)
        {
            if (other.Side != casualty.Side || other.IsOutOfAction)
                continue;
            other.Morale = Math.Max(0, other.Morale - CombatRules.LeaderLossMoraleLoss);
            Check(sim, other, tick, events);
        }
    }

    /// <summary>The unit's leader if he is in command radius and not broken (a leader is not his own leader).</summary>
    public static Unit? LeaderInRange(Simulation sim, Unit unit)
    {
        long radiusSq = (long)CombatRules.CommandRadiusCm * CombatRules.CommandRadiusCm;
        foreach (var other in sim.Units)
        {
            if (other != unit && other.IsLeader && other.Side == unit.Side && !other.IsOutOfAction
                && other.MoraleState != MoraleState.Broken && (other.Position - unit.Position).LengthSquared <= radiusSq)
                return other;
        }
        return null;
    }

    private static int LeaderBonus(Simulation sim, Unit unit, int bonus) =>
        LeaderInRange(sim, unit) is { } leader ? bonus * leader.LeaderQualityPct / 100 : 0;

    private static int BaseMorale(Unit unit) => unit.IsLeader ? CombatRules.LeaderMorale : CombatRules.BaseMorale;

    private static void TryRally(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        var leader = LeaderInRange(sim, unit);
        int chance = unit.Morale - unit.Suppression
                     + (leader is not null ? CombatRules.LeaderRallyBonus * leader.LeaderQualityPct / 100 : -CombatRules.NoLeaderRallyPenalty);
        if (sim.Rng.NextInt(1000) >= chance)
            return;
        unit.Morale = Math.Min(CombatRules.MaxMorale, unit.Morale + CombatRules.RallyMoraleGain);
        unit.MoraleState = unit.Suppression >= CombatRules.UnpinBelow ? MoraleState.Pinned : MoraleState.Steady;
        unit.Retreated = false;
        events.Add(new MoraleChanged(tick, unit.Id, unit.MoraleState));
    }

    private static void Break(Unit unit, long tick, List<SimEvent> events)
    {
        unit.MoraleState = MoraleState.Broken;
        unit.Retreated = false;
        Firing.Cancel(unit);
        Movement.ClearPath(unit);
        events.Add(new MoraleChanged(tick, unit.Id, MoraleState.Broken));
    }

    private static void UpdatePinned(Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.MoraleState == MoraleState.Broken)
            return;
        bool pinned = unit.MoraleState == MoraleState.Pinned
            ? unit.Suppression >= CombatRules.UnpinBelow
            : unit.Suppression >= CombatRules.PinnedAt;
        var next = pinned ? MoraleState.Pinned : MoraleState.Steady;
        if (next == unit.MoraleState)
            return;
        unit.MoraleState = next;
        events.Add(new MoraleChanged(tick, unit.Id, next));
        if (next == MoraleState.Pinned)
        {
            Movement.ClearPath(unit);
            Movement.BeginStanceChange(unit, Stance.Prone);
        }
    }
}
```

Note on `Morale_RecoversSlowlyWhenCalm`: the intervals fall on ticks 0 and 20, so 500 + 5 + 5 = 510.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(sim): suppression, pinned and broken states, morale checks, rally and leader succession

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Firing action, fire orders and combat-aware vision

**Files:**
- Replace: `src/Nmf.Sim/Combat/Firing.cs`
- Modify: `src/Nmf.Sim/Orders/Orders.cs`, `src/Nmf.Sim/Simulation.cs` (the `Apply` guards and new orders, the step pipeline), `src/Nmf.Sim/Vision/VisionRules.cs`, `src/Nmf.Sim/Vision/VisionSystem.cs`
- Create (stub for now, replaced in Task 5): `src/Nmf.Sim/AI/SoldierBrain.cs`
- Test: `src/Nmf.Sim.Tests/Combat/FiringTests.cs`, `src/Nmf.Sim.Tests/Combat/CombatOrderTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3
- Produces:
  - `Firing` with `StartAiming(Unit, Unit)`, `Cancel(Unit)`, `Update(Simulation, Unit, long, List<SimEvent>)` and `bool CanSee(Simulation, Unit shooter, Unit target)` (within range and line-of-sight clarity > 0).
  - Orders: `FireAtOrder(UnitId Unit, UnitId Target)` and `SetFirePolicyOrder(UnitId Unit, FirePolicy Policy)`.
  - `Apply` guards (spec §6): out of action, then broken, then pinned (for move orders and non-prone stance orders).
  - `VisionRules.VisibilityPct(Unit unit, long tick)` replaces `MovementVisibilityPct`.
  - Vision: dead targets are not updated; out-of-action observers and listeners are skipped; gunfire is heard within the weapon's noise radius.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Combat/FiringTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class FiringTests
{
    private static GridMap Open() => new(60, 20, ["none"]);

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var all = new List<SimEvent>();
        for (int i = 0; i < n; i++) all.AddRange(sim.Step());
        return all;
    }

    [Fact]
    public void ArmedSoldier_SpotsAndShootsAVisibleEnemy()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        var events = StepN(sim, 60);
        Assert.Contains(events, e => e is ShotFired s && s.Shooter == blue.Id && s.Hit == red.Id);
        Assert.NotEqual(WoundLevel.None, red.Wound);
        Assert.True(blue.LastShotTick > 0);
    }

    [Fact]
    public void HoldFire_NeverShoots()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        sim.Submit(Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.HoldFire));
        Assert.DoesNotContain(StepN(sim, 120), e => e is ShotFired);
    }

    [Fact]
    public void ReturnFire_WaitsUntilTheEnemyShoots()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 60));
        var red = sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 60, aim: 40));
        sim.Submit(Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.ReturnFire));
        var shots = StepN(sim, 300).OfType<ShotFired>().ToList();
        var firstRed = shots.FindIndex(s => s.Shooter == red.Id);
        var firstBlue = shots.FindIndex(s => s.Shooter == blue.Id);
        Assert.True(firstRed >= 0);
        Assert.True(firstBlue > firstRed);
    }

    [Fact]
    public void EmptyMagazine_Reloads()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0, spread: 200, magazine: 2, reload: 30));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        bool sawReload = false;
        for (int i = 0; i < 200 && !sawReload; i++)
        {
            sim.Step();
            sawReload = blue.Action == CombatAction.Reloading;
        }
        Assert.True(sawReload);
        Assert.Equal(0, blue.Ammo);
        for (int i = 0; i < 31; i++) sim.Step();
        Assert.Equal(2, blue.Ammo);
    }

    [Fact]
    public void MoveOrder_CancelsAiming()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(aim: 100));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        for (int i = 0; i < 60 && blue.Action != CombatAction.Aiming; i++) sim.Step();
        Assert.Equal(CombatAction.Aiming, blue.Action);
        sim.Submit(Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 1850)));
        sim.Step();
        Assert.Equal(CombatAction.None, blue.Action);
        Assert.Null(blue.Target);
    }

    [Fact]
    public void FireAtOrder_PrefersTheOrderedTarget()
    {
        var sim = new Simulation(Open(), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        sim.SpawnUnit(Side.Red, new Vec2(1050, 1450), 7);
        var far = sim.SpawnUnit(Side.Red, new Vec2(3050, 1050), 7);
        sim.Submit(Side.Blue, new FireAtOrder(blue.Id, far.Id));
        var first = StepN(sim, 120).OfType<ShotFired>().First(s => s.Shooter == blue.Id);
        Assert.Equal(far.Id, first.Hit);
    }

    [Fact]
    public void TargetGoingDown_StopsTheBurst()
    {
        var sim = new Simulation(Open(), 1);
        var smg = new WeaponDef("burst", "Burst", WeaponClass.Smg, 71, 2, 10, 2, 4, 20, 0, 12_000, 100, 50, 10_000).Validated();
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, smg);
        var red = sim.SpawnUnit(Side.Red, new Vec2(1050, 1050), 7);
        var events = StepN(sim, 80);
        int downAt = events.FindIndex(e => e is UnitWounded { Level: >= WoundLevel.Incapacitated } w && w.Unit == red.Id);
        Assert.True(downAt >= 0);
        Assert.DoesNotContain(events.Skip(downAt + 1), e => e is ShotFired s && s.Shooter == blue.Id && s.Hit == red.Id);
    }

    [Fact]
    public void EnemyBehindHill_IsNotShot()
    {
        var map = Open();
        for (int y = 0; y < 20; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var sim = new Simulation(map, 1);
        sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle());
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        Assert.DoesNotContain(StepN(sim, 200), e => e is ShotFired);
    }

    [Fact]
    public void Gunfire_IsHeardByHiddenEnemies()
    {
        var map = Open();
        for (int y = 0; y < 20; y++) map[new CellCoord(30, y)] = new CellData(0, 1500, 255, 0, 0);
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(50, 1050), 7, TestWeapons.Rifle(lethality: 0));
        sim.SpawnUnit(Side.Red, new Vec2(2050, 1050), 7);
        var listener = sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7);
        StepN(sim, 80);
        Assert.Equal(ContactLevel.Suspected, sim.Knowledge(Side.Red).LevelOf(blue.Id) is ContactLevel.Visible ? ContactLevel.Suspected : sim.Knowledge(Side.Red).LevelOf(blue.Id));
        Assert.NotEqual(ContactLevel.Unknown, sim.Knowledge(Side.Red).LevelOf(blue.Id));
        Assert.True(listener.IsAlive);
    }
}
```
(The first `Gunfire_IsHeardByHiddenEnemies` assertion is contorted. Replace it with the single, clearer assertion that follows it: the Red side knows about the blue shooter, at level Visible or Suspected.)

`src/Nmf.Sim.Tests/Combat/CombatOrderTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Combat;

public class CombatOrderTests
{
    private static (Simulation Sim, Unit Blue, Unit Red) Setup()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 1);
        return (sim, sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7, TestWeapons.Rifle()), sim.SpawnUnit(Side.Red, new Vec2(5050, 1050), 7));
    }

    private static string? RejectionOf(Simulation sim, Side side, Order order)
    {
        sim.Submit(side, order);
        return sim.Step().OfType<OrderRejected>().FirstOrDefault(r => r.Order == order)?.Reason;
    }

    [Fact]
    public void OutOfAction_RejectsEverything()
    {
        var (sim, blue, _) = Setup();
        Damage.SetWound(sim, blue, WoundLevel.Incapacitated, 0, []);
        Assert.Equal("unit is out of action", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
        Assert.Equal("unit is out of action", RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Standing)));
    }

    [Fact]
    public void Broken_RejectsOrders()
    {
        var (sim, blue, _) = Setup();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        Assert.Equal("unit is broken", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
    }

    [Fact]
    public void Pinned_RejectsMovingAndStandingUpButAllowsProneAndFireOrders()
    {
        var (sim, blue, red) = Setup();
        blue.Suppression = 600;
        blue.MoraleState = MoraleState.Pinned;
        Assert.Equal("unit is pinned", RejectionOf(sim, Side.Blue, new MoveOrder(blue.Id, new Vec2(50, 50))));
        Assert.Equal("unit is pinned", RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Standing)));
        Assert.Null(RejectionOf(sim, Side.Blue, new SetStanceOrder(blue.Id, Stance.Prone)));
        Assert.Null(RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, red.Id)));
        Assert.Equal(red.Id, blue.OrderedTarget);
    }

    [Fact]
    public void FireAt_OwnSideOrUnknown_IsRejected()
    {
        var (sim, blue, _) = Setup();
        var friend = sim.SpawnUnit(Side.Blue, new Vec2(1250, 1050), 7);
        Assert.Equal("invalid target", RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, friend.Id)));
        Assert.Equal("invalid target", RejectionOf(sim, Side.Blue, new FireAtOrder(blue.Id, new UnitId(99))));
    }

    [Fact]
    public void FirePolicyOrder_SetsPolicy()
    {
        var (sim, blue, _) = Setup();
        Assert.Null(RejectionOf(sim, Side.Blue, new SetFirePolicyOrder(blue.Id, FirePolicy.ReturnFire)));
        Assert.Equal(FirePolicy.ReturnFire, blue.FirePolicy);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: build FAILS with `The type or namespace name 'SetFirePolicyOrder' could not be found`.

- [ ] **Step 3: Orders, firing, brain stub**

Append to `src/Nmf.Sim/Orders/Orders.cs` (and add `using Nmf.Sim.Combat;`):
```csharp

public sealed record FireAtOrder(UnitId Unit, UnitId Target) : Order(Unit);

public sealed record SetFirePolicyOrder(UnitId Unit, FirePolicy Policy) : Order(Unit);
```

Replace `src/Nmf.Sim/Combat/Firing.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.Combat;

/// <summary>The aim → burst → recover / reload action of one soldier, one tick at a time.</summary>
internal static class Firing
{
    public static void StartAiming(Unit unit, Unit target)
    {
        unit.Target = target.Id;
        unit.Action = CombatAction.Aiming;
        int pct = unit.MoraleState == MoraleState.Pinned ? CombatRules.PinnedAimPct : 100;
        unit.ActionTicksLeft = Math.Max(1, unit.Weapon!.AimTicks * pct / 100);
    }

    public static void Cancel(Unit unit)
    {
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
        {
            unit.Action = CombatAction.None;
            unit.ActionTicksLeft = 0;
            unit.RoundsLeftInBurst = 0;
        }
        unit.Target = null;
    }

    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        var weapon = unit.Weapon;
        if (weapon is null || unit.IsOutOfAction)
            return;
        switch (unit.Action)
        {
            case CombatAction.Aiming:
            {
                if (!CanEngage(sim, unit, out var target))
                {
                    Cancel(unit);
                    return;
                }
                if (--unit.ActionTicksLeft > 0)
                    return;
                unit.Action = CombatAction.Firing;
                unit.RoundsLeftInBurst = Math.Min(weapon.RoundsPerBurst, unit.Ammo);
                FireRound(sim, unit, target, weapon, tick, events);
                return;
            }
            case CombatAction.Firing:
            {
                if (!CanEngage(sim, unit, out var target))
                {
                    Cancel(unit);
                    return;
                }
                if (--unit.ActionTicksLeft > 0)
                    return;
                FireRound(sim, unit, target, weapon, tick, events);
                return;
            }
            case CombatAction.Recovering:
                if (--unit.ActionTicksLeft <= 0)
                    unit.Action = CombatAction.None;
                return;
            case CombatAction.Reloading:
                if (--unit.ActionTicksLeft <= 0)
                {
                    unit.Ammo = weapon.MagazineSize;
                    unit.Action = CombatAction.None;
                }
                return;
        }
    }

    /// <summary>Within weapon range and in line of sight from the shooter's eyes.</summary>
    public static bool CanSee(Simulation sim, Unit shooter, Unit target)
    {
        long range = shooter.Weapon?.RangeCm ?? 0;
        if ((target.Position - shooter.Position).LengthSquared > range * range)
            return false;
        return LineOfSight.Clarity(sim.Map, shooter.Position, VisionRules.EyeHeightAbsCm(sim.Map, shooter),
            target.Position, VisionRules.TargetHeightAbsCm(sim.Map, target)) > 0;
    }

    private static bool CanEngage(Simulation sim, Unit unit, [NotNullWhen(true)] out Unit? target)
    {
        target = unit.Target is { } id ? sim.FindUnit(id) : null;
        if (target is null || target.IsOutOfAction || unit.MoveTarget is not null || unit.TargetStance is not null
            || unit.MoraleState == MoraleState.Broken || unit.FirePolicy == FirePolicy.HoldFire || !CanSee(sim, unit, target))
        {
            target = null;
            return false;
        }
        return true;
    }

    private static void FireRound(Simulation sim, Unit unit, Unit target, WeaponDef weapon, long tick, List<SimEvent> events)
    {
        var shot = Ballistics.Trace(sim, unit, target);
        unit.Ammo--;
        unit.RoundsLeftInBurst--;
        unit.LastShotTick = tick;
        events.Add(new ShotFired(tick, unit.Id, unit.Position, shot.End, shot.Hit?.Id));

        if (shot.Hit is { IsAlive: true } hit)
            Damage.ApplyHit(sim, hit, weapon, tick, events);
        foreach (var miss in shot.NearMisses)
        {
            int amount = weapon.SuppressionPerRound * (CombatRules.NearMissRadiusCm - miss.DistanceCm) / CombatRules.NearMissRadiusCm;
            MoraleSystem.AddSuppression(sim, miss.Unit, amount, tick, events);
        }

        if (unit.Ammo == 0)
        {
            unit.Action = CombatAction.Reloading;
            unit.ActionTicksLeft = weapon.ReloadTicks;
            unit.Target = null;
        }
        else if (unit.RoundsLeftInBurst > 0)
        {
            unit.ActionTicksLeft = Math.Max(1, weapon.RoundIntervalTicks);
        }
        else
        {
            unit.Action = CombatAction.Recovering;
            unit.ActionTicksLeft = weapon.RecoverTicks;
            unit.Target = null;
        }
    }
}
```

Create `src/Nmf.Sim/AI/SoldierBrain.cs` (a working minimal version: targets only; Task 5 adds the rest):
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Sim.AI;

internal static class SoldierBrain
{
    public static void Update(Simulation sim, Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.IsOutOfAction || unit.MoraleState == MoraleState.Broken)
            return;
        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (!idle || unit.Weapon is null || unit.Action != CombatAction.None || unit.FirePolicy == FirePolicy.HoldFire)
            return;
        var target = ChooseTarget(sim, unit, tick);
        if (target is not null)
            Firing.StartAiming(unit, target);
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
            if (!Firing.CanSee(sim, unit, enemy))
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
}
```

- [ ] **Step 4: Simulation guards, new orders and pipeline**

In `src/Nmf.Sim/Simulation.cs`:
- Add `using Nmf.Sim.AI;`.
- In `Apply`, after the `unit.Side != logged.Issuer` check, insert:
```csharp
        if (unit.IsOutOfAction)
        {
            events.Add(new OrderRejected(Tick, order, "unit is out of action"));
            return;
        }
        if (unit.MoraleState == MoraleState.Broken)
        {
            events.Add(new OrderRejected(Tick, order, "unit is broken"));
            return;
        }
        bool pinned = unit.MoraleState == MoraleState.Pinned;
```
- Add these cases at the top of the `switch`:
```csharp
            case MoveOrder when pinned:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
            case SetStanceOrder stanceWhilePinned when pinned && stanceWhilePinned.Stance != Stance.Prone:
                events.Add(new OrderRejected(Tick, order, "unit is pinned"));
                break;
```
- Add these before `default:`:
```csharp
            case FireAtOrder fire:
                var fireTarget = FindUnit(fire.Target);
                if (fireTarget is null || fireTarget.Side == unit.Side)
                {
                    events.Add(new OrderRejected(Tick, order, "invalid target"));
                    break;
                }
                unit.OrderedTarget = fireTarget.Id;
                if (unit.Target != fireTarget.Id)
                    Firing.Cancel(unit);
                break;
            case SetFirePolicyOrder policy:
                unit.FirePolicy = policy.Policy;
                if (policy.Policy == FirePolicy.HoldFire)
                    Firing.Cancel(unit);
                break;
```
- Make `Step()` run: movement loop; then `foreach (var unit in _units) Firing.Update(this, unit, Tick, events);`; then the bleeding and morale loop (from Task 2); then:
```csharp
        if (Tick % VisionRules.IntervalTicks == 0)
        {
            VisionSystem.Update(this, Tick, events);
            foreach (var unit in _units)
                SoldierBrain.Update(this, unit, Tick, events);
        }
```
  This replaces the old vision block.

- [ ] **Step 5: Combat-aware vision**

In `src/Nmf.Sim/Vision/VisionRules.cs`:
- Add `using Nmf.Sim.Combat;`.
- Replace `MovementVisibilityPct` with:
```csharp
    /// <summary>Firing makes a soldier much easier to notice for a moment; moving targets are easier to notice too.</summary>
    public static int VisibilityPct(Unit unit, long tick)
    {
        if (tick - unit.LastShotTick <= CombatRules.FiringVisibilityTicks)
            return CombatRules.FiringVisibilityPct;
        return !unit.IsMoving ? 100 : unit.MoveMode switch
        {
            MoveMode.Walk => 200,
            MoveMode.Run => 300,
            _ => 120,
        };
    }
```

In `src/Nmf.Sim/Vision/VisionSystem.cs`:
- In `Update`, change `if (target.Side == side)` to `if (target.Side == side || !target.IsAlive)`.
- In `UpdateSight`, change the observer filter to `if (observer.Side != side || observer.IsOutOfAction) continue;` and use `VisionRules.VisibilityPct(target, tick)` in the gain.
- Replace the start of `UpdateHearing` up to (and including) the `radius` line with:
```csharp
        bool fired = target.Weapon is not null && target.LastShotTick > tick - VisionRules.IntervalTicks;
        if (contact.Level == ContactLevel.Visible || (!target.MovedSinceVisionUpdate && !fired))
            return;
        // A recent sighting is more precise than a noise; keep the last-seen marker until it goes stale.
        if (contact.Level == ContactLevel.LastKnown && tick - contact.LastUpdateTick <= VisionRules.SuspectedTimeoutTicks)
            return;

        long radius = target.MovedSinceVisionUpdate
            ? (long)VisionRules.NoiseRadiusCm(target.MoveMode) * sim.Map.CellAt(target.Position).MoveCostPct / 100
            : 0;
        if (fired)
            radius = Math.Max(radius, target.Weapon!.NoiseRadiusCm);
```
- In the listener loop, add `!listener.IsOutOfAction &&` to the condition.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS, including all phase 1–2 tests (unarmed units never fire, so their exact event lists are unchanged).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(sim): firing actions, fire orders, order guards and gunfire in vision

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Soldier brain – dropping under fire and broken retreat; skirmish loadouts

**Files:**
- Modify: `src/Nmf.Sim/AI/SoldierBrain.cs`, `src/Nmf.Sim/Scenarios/SkirmishScenario.cs`
- Test: `src/Nmf.Sim.Tests/AI/SoldierBrainTests.cs`, `src/Nmf.Sim.Tests/Scenarios/SkirmishCombatTests.cs`

**Interfaces:**
- Consumes: Tasks 1–4, `Pathfinder`
- Produces:
  - The brain:
    - goes prone when idle, not prone and suppression ≥ 250
    - broken soldiers run about 20 m away from the nearest threat (Visible, LastKnown or Suspected contact), with the spot clamped into the map
    - an unreachable spot falls back to 8 nearby spots and then to going prone
    - once retreated, the soldier stays prone
  - `SkirmishScenario.Create(GridMap map, ulong seed, IReadOnlyDictionary<string, WeaponDef> weapons)`: per side, the first spawn is the leader with an SMG, the second is the LMG gunner and the rest are riflemen. The ids are the constants `FinnishLeaderWeapon = "suomi_kp31"`, `FinnishSupportWeapon = "lahti_saloranta"`, `FinnishRifle = "mosin_m39"`, `SovietLeaderWeapon = "ppsh41"`, `SovietSupportWeapon = "dp27"` and `SovietRifle = "mosin_9130"`. A missing id throws `ArgumentException("weapon '<id>' is not defined")`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/AI/SoldierBrainTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class SoldierBrainTests
{
    [Fact]
    public void SuppressedIdleSoldier_GoesProne()
    {
        var sim = new Simulation(new GridMap(60, 20, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1050, 1050), 7);
        u.Suppression = 320;
        sim.Step();
        Assert.Equal(Stance.Prone, u.TargetStance);
        Assert.Equal(MoraleState.Steady, u.MoraleState);
    }

    [Fact]
    public void BrokenSoldier_RunsAwayFromTheThreatThenStaysDown()
    {
        var sim = new Simulation(new GridMap(80, 20, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(4050, 1050), 7);
        sim.SpawnUnit(Side.Red, new Vec2(6050, 1050), 7);
        for (int i = 0; i < 25; i++) sim.Step(); // red becomes visible to blue
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 5; i++) sim.Step();
        Assert.NotNull(blue.MoveTarget);
        Assert.True(blue.MoveTarget!.Value.X < 4050 - 1500);
        Assert.Equal(MoveMode.Run, blue.MoveMode);

        for (int i = 0; i < 400; i++) sim.Step();
        Assert.Null(blue.MoveTarget);
        Assert.True(blue.Stance == Stance.Prone || blue.TargetStance == Stance.Prone);
    }

    [Fact]
    public void BrokenSoldierAtMapEdge_ClampsTheRetreatAndDoesNotCrash()
    {
        var sim = new Simulation(new GridMap(40, 10, ["none"]), 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(60, 550), 7);
        sim.SpawnUnit(Side.Red, new Vec2(1060, 550), 7);
        for (int i = 0; i < 25; i++) sim.Step();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 200; i++) sim.Step();
        Assert.True(sim.Map.Contains(blue.Position));
    }

    [Fact]
    public void BoxedInBrokenSoldier_GoesProneInstead()
    {
        var map = new GridMap(40, 10, ["none"]);
        for (int x = 18; x <= 22; x++) { map[new CellCoord(x, 3)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(x, 7)].ExtraMoveCost = CellData.Impassable; }
        for (int y = 3; y <= 7; y++) { map[new CellCoord(18, y)].ExtraMoveCost = CellData.Impassable; map[new CellCoord(22, y)].ExtraMoveCost = CellData.Impassable; }
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, new Vec2(2050, 550), 7);
        sim.SpawnUnit(Side.Red, new Vec2(3550, 550), 7);
        for (int i = 0; i < 25; i++) sim.Step();
        blue.MoraleState = MoraleState.Broken;
        blue.Morale = 0;
        for (int i = 0; i < 60; i++) sim.Step();
        Assert.Null(blue.MoveTarget);
        Assert.True(blue.Stance == Stance.Prone || blue.TargetStance == Stance.Prone);
    }
}
```
(In `BoxedInBrokenSoldier…` the red unit is outside the box. Red is visible because rocks are 0 cm tall obstacles in this map, since only `ExtraMoveCost` is set.)

`src/Nmf.Sim.Tests/Scenarios/SkirmishCombatTests.cs`:
```csharp
using Nmf.Sim.Combat;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Tests.Combat;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Scenarios;

public class SkirmishCombatTests
{
    private static GridMap Field()
    {
        var points = new List<MapPoint>();
        for (int i = 0; i < 4; i++)
        {
            points.Add(new MapPoint($"b{i}", "blue", new Vec2(3000 + i * 300, 5500)));
            points.Add(new MapPoint($"r{i}", "red", new Vec2(3000 + i * 300, 1500)));
        }
        return new GridMap(80, 70, ["none"], new MapFeatures([], points, []));
    }

    [Fact]
    public void Create_WithWeapons_GivesLeaderSupportAndRiflemen()
    {
        var sim = SkirmishScenario.Create(Field(), 1, TestWeapons.SkirmishSet()).Sim;
        var blue = sim.Units.Where(u => u.Side == Side.Blue).ToList();
        Assert.True(blue[0].IsLeader);
        Assert.Equal("suomi_kp31", blue[0].Weapon!.Id);
        Assert.Equal("lahti_saloranta", blue[1].Weapon!.Id);
        Assert.Equal("mosin_m39", blue[2].Weapon!.Id);
        Assert.Equal("ppsh41", sim.Units.First(u => u.Side == Side.Red).Weapon!.Id);
    }

    [Fact]
    public void Create_WithMissingWeapon_Throws()
    {
        var weapons = TestWeapons.SkirmishSet().Where(kv => kv.Key != "dp27").ToDictionary(kv => kv.Key, kv => kv.Value);
        var ex = Assert.Throws<ArgumentException>(() => SkirmishScenario.Create(Field(), 1, weapons));
        Assert.Contains("dp27", ex.Message);
    }

    [Fact]
    public void Firefight_ProducesCasualtiesAndIsDeterministic()
    {
        (ulong Hash, int Shots, int Wounds) Run()
        {
            var scenario = SkirmishScenario.Create(Field(), 42, TestWeapons.SkirmishSet());
            int shots = 0, wounds = 0;
            for (int i = 0; i < 1200; i++)
            {
                scenario.Tick();
                foreach (var e in scenario.Sim.Step())
                {
                    if (e is ShotFired) shots++;
                    if (e is UnitWounded) wounds++;
                }
            }
            return (StateHash.Compute(scenario.Sim), shots, wounds);
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a, b);
        Assert.True(a.Shots > 10);
        Assert.True(a.Wounds > 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter "FullyQualifiedName~SoldierBrainTests|FullyQualifiedName~SkirmishCombatTests"`
Expected: build FAILS with `No overload for method 'Create' takes 3 arguments`.

- [ ] **Step 3: Implement the brain**

Replace `src/Nmf.Sim/AI/SoldierBrain.cs`:
```csharp
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
            Retreat(sim, unit);
            return;
        }
        bool idle = unit.MoveTarget is null && unit.TargetStance is null;
        if (idle && unit.Suppression >= CombatRules.GoProneAt && unit.Stance != Stance.Prone)
        {
            Movement.BeginStanceChange(unit, Stance.Prone);
            return;
        }
        if (!idle || unit.Weapon is null || unit.Action != CombatAction.None || unit.FirePolicy == FirePolicy.HoldFire)
            return;
        var target = ChooseTarget(sim, unit, tick);
        if (target is not null)
            Firing.StartAiming(unit, target);
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
            if (!Firing.CanSee(sim, unit, enemy))
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

    private static void Retreat(Simulation sim, Unit unit)
    {
        if (unit.MoveTarget is not null || unit.TargetStance is not null)
            return;
        if (unit.Retreated)
        {
            if (unit.Stance != Stance.Prone)
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
```

- [ ] **Step 4: Implement loadouts**

In `src/Nmf.Sim/Scenarios/SkirmishScenario.cs`:
- Add `using Nmf.Sim.Combat;`.
- Add the weapon id constants.
- Change the existing `Create(GridMap map, ulong seed)` to `=> Create(map, seed, null);`.
- Add the main body as `Create(GridMap map, ulong seed, IReadOnlyDictionary<string, WeaponDef>? weapons)`, spawning with:
```csharp
        int blueIndex = 0, redIndex = 0;
        foreach (var point in map.Features.Points)
        {
            if (point.Type == BluePointType)
                Spawn(sim, Side.Blue, point.Position, blueIndex++, weapons, FinnishLeaderWeapon, FinnishSupportWeapon, FinnishRifle);
            else if (point.Type == RedPointType)
                Spawn(sim, Side.Red, point.Position, redIndex++, weapons, SovietLeaderWeapon, SovietSupportWeapon, SovietRifle);
        }
```
- Add the helper:
```csharp
    private static void Spawn(Simulation sim, Side side, Core.Vec2 position, int index, IReadOnlyDictionary<string, WeaponDef>? weapons,
        string leaderWeapon, string supportWeapon, string rifle)
    {
        if (weapons is null)
        {
            sim.SpawnUnit(side, position, SoldierWalkSpeedCmPerTick);
            return;
        }
        string id = index == 0 ? leaderWeapon : index == 1 ? supportWeapon : rifle;
        if (!weapons.TryGetValue(id, out var weapon))
            throw new ArgumentException($"weapon '{id}' is not defined");
        sim.SpawnUnit(side, position, SoldierWalkSpeedCmPerTick, weapon, isLeader: index == 0);
    }
```
  Existing unarmed callers behave as before.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(sim): soldier brain drops under fire and retreats when broken; skirmish loadouts

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Weapon YAML content

**Files:**
- Modify: `src/Nmf.Content/Nmf.Content.csproj` (add YamlDotNet)
- Create: `src/Nmf.Content/ContentLoadException.cs`, `src/Nmf.Content/Weapons/WeaponLoader.cs`
- Create: `content/core/weapons/mosin_m39.yaml`, `mosin_9130.yaml`, `suomi_kp31.yaml`, `ppsh41.yaml`, `lahti_saloranta.yaml`, `dp27.yaml`
- Test: `src/Nmf.Content.Tests/WeaponLoaderTests.cs`, `src/Nmf.Content.Tests/SkirmishFightTests.cs`

**Interfaces:**
- Produces:
  - `sealed class ContentLoadException(string path, string message, Exception? inner = null)` with `Path`.
  - `static class WeaponLoader` with `WeaponDef Load(string path)` and `IReadOnlyDictionary<string, WeaponDef> LoadDirectory(string directory)`. The YAML keys are snake_case as in spec §2, and `range_m`/`noise_m` are converted to cm. Unknown keys, missing keys, bad types and duplicate ids all raise `ContentLoadException` naming the file.

- [ ] **Step 1: Add the package and write the failing tests**

Run: `dotnet add src/Nmf.Content package YamlDotNet`

`src/Nmf.Content.Tests/WeaponLoaderTests.cs`:
```csharp
using Nmf.Content;
using Nmf.Content.Weapons;
using Nmf.Sim.Combat;

namespace Nmf.Content.Tests;

public class WeaponLoaderTests
{
    private const string Rifle = """
        id: test_rifle
        name: "Test Rifle"
        class: rifle
        magazine: 5
        aim_ticks: 30
        burst: 1
        round_interval_ticks: 0
        recover_ticks: 24
        reload_ticks: 80
        spread_mrad: 6
        range_m: 300
        lethality_pct: 70
        suppression: 80
        noise_m: 300
        """;

    private static string Write(string dir, string name, string yaml)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, yaml);
        return path;
    }

    [Fact]
    public void Load_ReadsAllFieldsAndConvertsMetres()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            var w = WeaponLoader.Load(Write(dir, "rifle.yaml", Rifle));
            Assert.Equal(new WeaponDef("test_rifle", "Test Rifle", WeaponClass.Rifle, 5, 30, 1, 0, 24, 80, 6, 30_000, 70, 80, 30_000), w);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("magazine: 5\n", "", "magazine")]
    [InlineData("class: rifle", "class: cannon", "class")]
    [InlineData("magazine: 5", "magazine: lots", "rifle.yaml")]
    [InlineData("noise_m: 300", "noise_m: 300\ncolour: red", "colour")]
    [InlineData("lethality_pct: 70", "lethality_pct: 170", "lethality")]
    public void Load_BadFiles_ThrowWithFileAndProblem(string find, string replace, string expected)
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            var path = Write(dir, "rifle.yaml", Rifle.Replace(find, replace));
            var ex = Assert.Throws<ContentLoadException>(() => WeaponLoader.Load(path));
            Assert.Contains("rifle.yaml", ex.Message);
            Assert.Contains(expected, ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LoadDirectory_RejectsDuplicateIds()
    {
        var dir = Directory.CreateTempSubdirectory("nmf-weapons-").FullName;
        try
        {
            Write(dir, "a.yaml", Rifle);
            Write(dir, "b.yaml", Rifle);
            var ex = Assert.Throws<ContentLoadException>(() => WeaponLoader.LoadDirectory(dir));
            Assert.Contains("duplicate", ex.Message);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CoreWeapons_LoadAndCoverTheSkirmishLoadouts()
    {
        var root = Nmf.Content.Tests.CoreContentTestsAccess.RepoRoot();
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));
        foreach (var id in new[] { "mosin_m39", "mosin_9130", "suomi_kp31", "ppsh41", "lahti_saloranta", "dp27" })
            Assert.True(weapons.ContainsKey(id), id);
    }
}
```
Expose the repo-root helper for these tests: in `CoreContentTests.cs`, make `RepoRoot()` `internal static` and add `internal static class CoreContentTestsAccess { public static string RepoRoot() => CoreContentTests.RepoRoot(); }`. Alternatively, call `CoreContentTests.RepoRoot()` directly once it is internal and drop the access class.

`src/Nmf.Content.Tests/SkirmishFightTests.cs`:
```csharp
using Nmf.Content.Tiled;
using Nmf.Content.Weapons;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Scenarios;

namespace Nmf.Content.Tests;

/// <summary>The real skirmish map and weapons fight for two minutes without errors, deterministically.</summary>
public class SkirmishFightTests
{
    [Fact]
    public void TwoMinuteFight_IsDeterministicAndHasShots()
    {
        var root = CoreContentTests.RepoRoot();
        var map = TmxMapLoader.Load(Path.Combine(root, "content", "core", "maps", "skirmish.tmx"));
        var weapons = WeaponLoader.LoadDirectory(Path.Combine(root, "content", "core", "weapons"));

        (ulong Hash, int Shots) Run()
        {
            var scenario = SkirmishScenario.Create(map, 1942, weapons);
            int shots = 0;
            for (int i = 0; i < 2400; i++)
            {
                scenario.Tick();
                shots += scenario.Sim.Step().Count(e => e is ShotFired);
            }
            return (StateHash.Compute(scenario.Sim), shots);
        }

        var a = Run();
        Assert.Equal(a, Run());
        Assert.True(a.Shots > 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Content.Tests`
Expected: build FAILS with `The type or namespace name 'Weapons' does not exist`.

- [ ] **Step 3: Implement the loader and weapon files**

`src/Nmf.Content/ContentLoadException.cs`:
```csharp
namespace Nmf.Content;

/// <summary>A content file (other than a map) could not be loaded. The message is meant for modders.</summary>
public sealed class ContentLoadException(string path, string message, Exception? inner = null)
    : Exception($"{path}: {message}", inner)
{
    public string Path { get; } = path;
}
```

`src/Nmf.Content/Weapons/WeaponLoader.cs`:
```csharp
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
```

The weapon files use the spec §2 layout:
- `mosin_m39.yaml`: name "Kivääri M/39", rifle, magazine 5, aim 30, burst 1, interval 0, recover 24, reload 80, spread 6, range 300, lethality 70, suppression 80, noise 300
- `mosin_9130.yaml`: name "Vintovka 91/30", the same values
- `suomi_kp31.yaml`: name "Konepistooli M/31 Suomi", smg, 71, 8, 5, 1, 10, 100, 35, 120, 45, 50, 200
- `ppsh41.yaml`: name "PPŠ-41", smg, 71, 8, 5, 1, 10, 100, 38, 120, 45, 50, 200
- `lahti_saloranta.yaml`: name "Pikakivääri M/26", lmg, 20, 16, 5, 2, 14, 120, 12, 400, 65, 100, 400
- `dp27.yaml`: name "DP-27", lmg, 47, 16, 5, 2, 14, 120, 13, 400, 65, 100, 400

For example, `content/core/weapons/mosin_m39.yaml`:
```yaml
id: mosin_m39
name: "Kivääri M/39"
class: rifle
magazine: 5
aim_ticks: 30
burst: 1
round_interval_ticks: 0
recover_ticks: 24
reload_ticks: 80
spread_mrad: 6
range_m: 300
lethality_pct: 70
suppression: 80
noise_m: 300
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(content): weapon YAML files and loader; real-map firefight test

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Client – effects, status, fire orders; art – dead pose and blood

**Files:**
- Create: `src/Nmf.Client/Effects/CombatEffects.cs`
- Modify: `src/Nmf.Client/UnitStatus.cs`, `src/Nmf.Client/GameSession.cs`, `src/Nmf.Client/Art/UnitAnimator.cs`
- Modify: `tools/art/palette.py`, `tools/art/soldiers.py`, `tools/art/objects.py`, `tools/art/assemble_sheet.py`, `tools/art/tests/test_art.py`; regenerate `content/core/art`
- Modify: `docs/superpowers/specs/2026-09-24-pixel-art-design.md` §5, `docs/art/character-sprite-brief.md`
- Test: `src/Nmf.Client.Tests/Effects/CombatEffectsTests.cs`, plus updates in `UnitStatusTests.cs`, `GameSessionTests.cs` and `Art/UnitAnimatorTests.cs`

**Interfaces:**
- Produces:
  - `enum EffectKind { Tracer, MuzzleFlash, Impact }`, `sealed class Effect` with `Kind`, `From`, `To`, `Lifetime`, `Age` and `Progress`, and `sealed class CombatEffects` with:
    - constants `TracerSeconds = 0.12`, `FlashSeconds = 0.06` and `ImpactSeconds = 0.35`
    - `IReadOnlyList<Effect> Active`
    - `void Add(IEnumerable<SimEvent> events, Func<UnitId, bool> shooterShown)`: a shown shooter adds a tracer, a flash and, on a miss, an impact; a hidden shooter adds only the impact
    - `void Update(double seconds)`
  - `UnitStatus.Describe` priority: Dead > Down > Broken > Pinned > Reloading > Firing (Aiming or Firing) > the phase 2 states. Also `UnitStatus.Condition(Unit)` (Unhurt, Light wound, Serious wound, Down, Dead) and `UnitStatus.PolicyName(FirePolicy)` (Fire at will, Return fire, Hold fire).
  - `GameSession` gains:
    - `IReadOnlyList<SimEvent> TakeEvents()`: returns the events since the last call and clears them
    - `OrderFireAt(UnitId target)` and `CycleFirePolicy()`
    - `Unit? EnemyAt(Vec2 point, int radiusCm)`: nearest alive enemy shown to the player within the radius
  - `UnitAnimator.Current` uses "dead" (or "prone" when the sheet has no dead animation) for out-of-action units.
  - Art: a `dead` animation at row 48 (the sheet becomes 384 × 3584) and a `blood` object (64 px, 2 variants). The palette gains dark reds (110, 24, 20) and (140, 32, 26). In `assemble_sheet`, dead falls back to prone and then idle.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Client.Tests/Effects/CombatEffectsTests.cs`:
```csharp
using Nmf.Client.Effects;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Tests.Effects;

public class CombatEffectsTests
{
    private static readonly ShotFired Miss = new(0, new UnitId(1), new Vec2(0, 0), new Vec2(1000, 0), null);
    private static readonly ShotFired Hit = new(0, new UnitId(1), new Vec2(0, 0), new Vec2(1000, 0), new UnitId(2));

    [Fact]
    public void ShownShooter_AddsTracerFlashAndImpactOnMiss()
    {
        var fx = new CombatEffects();
        fx.Add([Miss, Hit], _ => true);
        Assert.Equal(2, fx.Active.Count(e => e.Kind == EffectKind.Tracer));
        Assert.Equal(2, fx.Active.Count(e => e.Kind == EffectKind.MuzzleFlash));
        Assert.Single(fx.Active, e => e.Kind == EffectKind.Impact);
    }

    [Fact]
    public void HiddenShooter_ShowsOnlyTheImpact()
    {
        var fx = new CombatEffects();
        fx.Add([Miss], _ => false);
        var only = Assert.Single(fx.Active);
        Assert.Equal(EffectKind.Impact, only.Kind);
        Assert.Equal(new Vec2(1000, 0), only.From);
    }

    [Fact]
    public void Update_AgesAndRemovesEffects()
    {
        var fx = new CombatEffects();
        fx.Add([Miss], _ => true);
        fx.Update(0.1);
        Assert.DoesNotContain(fx.Active, e => e.Kind == EffectKind.MuzzleFlash);
        Assert.Contains(fx.Active, e => e.Kind == EffectKind.Tracer);
        Assert.InRange(fx.Active.First(e => e.Kind == EffectKind.Tracer).Progress, 0.8, 0.9);
        fx.Update(1.0);
        Assert.Empty(fx.Active);
    }
}
```

Append to the class in `src/Nmf.Client.Tests/UnitStatusTests.cs` (and add `using Nmf.Sim.Combat;`):
```csharp
    [Fact]
    public void Describe_CombatStatesTakePriority()
    {
        var sim = new Simulation(new GridMap(60, 60, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(1000, 1000), 7);
        u.Action = CombatAction.Aiming;
        Assert.Equal("Firing", UnitStatus.Describe(u));
        u.Action = CombatAction.Reloading;
        Assert.Equal("Reloading", UnitStatus.Describe(u));
        u.MoraleState = MoraleState.Pinned;
        Assert.Equal("Pinned", UnitStatus.Describe(u));
        u.MoraleState = MoraleState.Broken;
        Assert.Equal("Broken", UnitStatus.Describe(u));
        u.Wound = WoundLevel.Incapacitated;
        Assert.Equal("Down", UnitStatus.Describe(u));
        u.Wound = WoundLevel.Dead;
        Assert.Equal("Dead", UnitStatus.Describe(u));
    }

    [Fact]
    public void Condition_AndPolicyNames()
    {
        var sim = new Simulation(new GridMap(10, 10, ["none"]), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(100, 100), 7);
        Assert.Equal("Unhurt", UnitStatus.Condition(u));
        u.Wound = WoundLevel.Serious;
        Assert.Equal("Serious wound", UnitStatus.Condition(u));
        Assert.Equal("Return fire", UnitStatus.PolicyName(FirePolicy.ReturnFire));
    }
```
(Tests set internal `Unit` members, so `Nmf.Sim.csproj` must also list `<InternalsVisibleTo Include="Nmf.Client.Tests" />`.)

Append to the class in `src/Nmf.Client.Tests/GameSessionTests.cs` (and add `using Nmf.Sim.Combat;`, `using Nmf.Sim.Events;` and `using Nmf.Sim.Orders;`):
```csharp
    [Fact]
    public void TakeEvents_ReturnsEventsOnceSinceLastCall()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        var first = session.TakeEvents();
        Assert.Contains(first, e => e is UnitMoved);
        Assert.Empty(session.TakeEvents());
    }

    [Fact]
    public void CycleFirePolicy_AdvancesFromTheFirstSelectedUnit()
    {
        var session = NewSession();
        SelectAll(session);
        session.CycleFirePolicy();
        session.StepOnce();
        Assert.All(session.OwnUnits, u => Assert.Equal(FirePolicy.ReturnFire, u.FirePolicy));
    }

    [Fact]
    public void EnemyAt_FindsOnlyEnemiesShownToThePlayer()
    {
        var session = NewSession();
        var enemy = session.Sim.Units[2];
        Assert.Null(session.EnemyAt(enemy.Position, 100));
        enemy.Position = new Vec2(400, 150); // right next to the blue men: spotted after a few vision updates
        for (int i = 0; i < 30; i++) session.StepOnce();
        Assert.Same(enemy, session.EnemyAt(enemy.Position, 100));
        Assert.Null(session.EnemyAt(new Vec2(3000, 3000), 100));
    }

    [Fact]
    public void OrderFireAt_SubmitsForSelection()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderFireAt(session.Sim.Units[2].Id);
        session.StepOnce();
        Assert.Equal(2, session.Sim.OrderLog.Count(o => o.Order is FireAtOrder));
    }
```
(`Unit.Position` has an internal setter, so this also needs the `InternalsVisibleTo` for `Nmf.Client.Tests`.)

Append to the class in `src/Nmf.Client.Tests/Art/UnitAnimatorTests.cs` (and add `using Nmf.Sim.Combat;`):
```csharp
    [Fact]
    public void OutOfAction_UsesDeadAnimation()
    {
        var (_, unit) = NewUnit();
        var a = new UnitAnimator();
        a.Update(unit, (1000, 1000), false);
        unit.Wound = WoundLevel.Dead;
        Assert.Equal("dead", a.Current(unit, Sheet).Animation);
    }
```

Python: in `tools/art/tests/test_art.py`:
- In `test_meta_matches_spec_contract`, add `"dead": (48, 1, 0)` to the expected dict.
- Change every `(3072, 384, 4)` to `(3584, 384, 4)`.
- Add to `ObjectTests` a check that `objects.strip("blood")` is 128 × 64 and has content.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Client.Tests; python3 -m unittest discover -s tools/art/tests -t .`
Expected: the C# build FAILS (`Effects` namespace missing) and Python reports FAIL/ERROR (no dead row, no blood).

- [ ] **Step 3: Implement client pieces**

`src/Nmf.Client/Effects/CombatEffects.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Client.Effects;

public enum EffectKind
{
    Tracer,
    MuzzleFlash,
    Impact,
}

public sealed class Effect(EffectKind kind, Vec2 from, Vec2 to, double lifetime)
{
    public EffectKind Kind { get; } = kind;
    public Vec2 From { get; } = from;
    public Vec2 To { get; } = to;
    public double Lifetime { get; } = lifetime;
    public double Age { get; internal set; }
    public double Progress => Math.Clamp(Age / Lifetime, 0, 1);
}

/// <summary>Short-lived tracers, muzzle flashes and bullet impacts made from simulation events (real-time, presentation only).</summary>
public sealed class CombatEffects
{
    public const double TracerSeconds = 0.12;
    public const double FlashSeconds = 0.06;
    public const double ImpactSeconds = 0.35;

    private readonly List<Effect> _active = [];

    public IReadOnlyList<Effect> Active => _active;

    /// <summary>Shots by shooters the player cannot see only show where the bullet landed.</summary>
    public void Add(IEnumerable<SimEvent> events, Func<UnitId, bool> shooterShown)
    {
        foreach (var e in events)
        {
            if (e is not ShotFired shot)
                continue;
            if (shooterShown(shot.Shooter))
            {
                _active.Add(new Effect(EffectKind.Tracer, shot.From, shot.To, TracerSeconds));
                _active.Add(new Effect(EffectKind.MuzzleFlash, shot.From, shot.To, FlashSeconds));
            }
            if (shot.Hit is null)
                _active.Add(new Effect(EffectKind.Impact, shot.To, shot.To, ImpactSeconds));
        }
    }

    public void Update(double seconds)
    {
        foreach (var effect in _active)
            effect.Age += seconds;
        _active.RemoveAll(e => e.Age >= e.Lifetime);
    }
}
```

`src/Nmf.Client/UnitStatus.cs`: add `using Nmf.Sim.Combat;`. At the start of `Describe` add:
```csharp
        if (unit.Wound == WoundLevel.Dead)
            return "Dead";
        if (unit.IsOutOfAction)
            return "Down";
        if (unit.MoraleState == MoraleState.Broken)
            return "Broken";
        if (unit.MoraleState == MoraleState.Pinned)
            return "Pinned";
        if (unit.Action == CombatAction.Reloading)
            return "Reloading";
        if (unit.Action is CombatAction.Aiming or CombatAction.Firing)
            return "Firing";
```
Add these methods:
```csharp
    public static string Condition(Unit unit) => unit.Wound switch
    {
        WoundLevel.None => "Unhurt",
        WoundLevel.Light => "Light wound",
        WoundLevel.Serious => "Serious wound",
        WoundLevel.Incapacitated => "Down",
        _ => "Dead",
    };

    public static string PolicyName(FirePolicy policy) => policy switch
    {
        FirePolicy.FireAtWill => "Fire at will",
        FirePolicy.ReturnFire => "Return fire",
        _ => "Hold fire",
    };
```

`src/Nmf.Client/GameSession.cs`: add `using Nmf.Sim.Combat;` and `using Nmf.Sim.Events;`, and the field `private readonly List<SimEvent> _events = [];`. In `StepOnce`, replace `Sim.Step();` with:
```csharp
        _events.AddRange(Sim.Step());
        if (_events.Count > 20_000)
            _events.RemoveRange(0, _events.Count - 20_000);
```
Add these members:
```csharp
    public IReadOnlyList<SimEvent> TakeEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public void OrderFireAt(UnitId target)
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new FireAtOrder(id, target));
    }

    public void CycleFirePolicy()
    {
        var ids = Selection.Ids;
        if (ids.Count == 0 || Sim.FindUnit(ids[0]) is not { } first)
            return;
        var next = (FirePolicy)(((int)first.FirePolicy + 1) % 3);
        foreach (var id in ids)
            Sim.Submit(PlayerSide, new SetFirePolicyOrder(id, next));
    }

    public Unit? EnemyAt(Vec2 point, int radiusCm)
    {
        long radiusSq = (long)radiusCm * radiusCm;
        return Sim.Units
            .Where(u => u.Side != PlayerSide && u.IsAlive && IsShownToPlayer(u, revealAll: false)
                        && (u.Position - point).LengthSquared <= radiusSq)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
    }
```

`src/Nmf.Client/Art/UnitAnimator.cs`: in `Current`, before the `string animation = ...` line, add:
```csharp
        if (unit.IsOutOfAction)
        {
            string down = sheet.Animations.ContainsKey("dead") ? "dead" : "prone";
            return new AnimationFrame(down, state.Direction, 0);
        }
```

Add `<InternalsVisibleTo Include="Nmf.Client.Tests" />` next to the existing entry in `src/Nmf.Sim/Nmf.Sim.csproj`.

- [ ] **Step 4: Implement art additions and regenerate**

In `tools/art/palette.py`, add `(110, 24, 20), (140, 32, 26),` to the accents.

In `tools/art/soldiers.py`:
- Append `("dead", 1, 0)` to `ANIMATIONS`.
- Add the pose:
```python
def _dead(pen, colours):
    u = colours["uniform"]
    cloth = darker(u[1], 14)
    # on the back, legs apart, one arm flung out, helmet knocked off, rifle dropped
    for side in (-1, 1):
        pen.ellipse(side * 0.14, -0.45, 0.08, 0.32, darker(u[0], 10))
        pen.ellipse(side * 0.18, -0.8, 0.07, 0.07, BOOT)
    pen.ellipse(0.0, 0.1, 0.23, 0.3, cloth)
    pen.line(0.2, 0.25, 0.55, 0.45, 0.09, cloth)
    pen.line(-0.2, 0.25, -0.42, 0.02, 0.09, cloth)
    pen.ellipse(0.0, 0.48, 0.1, 0.11, (150, 108, 80))
    pen.ellipse(-0.33, 0.62, 0.13, 0.13, darker(colours["helmet"], 10))
    _rifle(pen, (0.36, -0.28), (0.74, 0.34))
```
- In `frame()`, add the branch `elif animation == "dead": _dead(pen, colours)` before the final `else` (crawl).

In `tools/art/objects.py`, add:
```python
BLOOD = [(110, 24, 20), (140, 32, 26)]


def blood(variant):
    rng = np.random.default_rng(500 + variant)
    blobs = [(rng.uniform(-0.35, 0.35), rng.uniform(-0.3, 0.3), rng.uniform(0.12, 0.28)) for _ in range(7)]

    def draw(pen):
        for x, y, r in blobs:
            pen.ellipse(x, y, r, r * 0.8, BLOOD[0])
        for x, y, r in blobs[:3]:
            pen.ellipse(x, y, r * 0.5, r * 0.4, BLOOD[1])

    return render_sprite(64, draw, outline=(56, 18, 14))
```
Register it as `"blood": (64, 2, blood)` in `OBJECTS`. Add `(56, 18, 14)` to the palette as well.

In `tools/art/assemble_sheet.py`, add `"dead": ["prone", "idle"]` to `FALLBACKS`.

In spec §5 of `docs/superpowers/specs/2026-09-24-pixel-art-design.md`, update the sheet to 56 rows (384 × 3584) with the optional `"dead": {"row": 48, "frames": 1, "strideCm": 0}`, and the object `blood` 64 px × 2. In `docs/art/character-sprite-brief.md`:
- add the table row `| dead | 1 | Lying dead on the back or side, limbs sprawled, helmet knocked off beside the head, rifle dropped. No blood (the game draws it). |`
- update the frame totals to 20 per direction and 100 files for the 5 minimum directions

Run: `python3 -m unittest discover -s tools/art/tests -t . && python3 -m tools.art.generate`
Expected: `OK`, then `wrote art to content/core/art`.

- [ ] **Step 5: Run all tests**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(client, art): combat effects, status and fire orders; dead pose and blood

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Godot – firefight on screen

**Files:**
- Modify: `src/Nmf.Game/ArtLibrary.cs` (blood), `src/Nmf.Game/UnitView.cs`, `src/Nmf.Game/Hud.cs`, `src/Nmf.Game/GameRoot.cs`, `README.md`

**Interfaces:**
- Consumes: Tasks 5–7
- Produces:
  - `ArtLibrary.Blood` (Texture2D, size checked against the catalog).
  - `UnitView.Effects` (CombatEffects) draws:
    - blood under dead soldiers
    - the aim line of selected own soldiers to a shown target
    - state icons over own soldiers: "!" when pinned, "!!" when broken, "R" when reloading
    - tracers, muzzle flashes and impacts
  - HUD cards with a condition line, a policy line, and a morale bar and a suppression bar (ProgressBar 0–1000), greyed when dead. The top bar shows own and enemy losses (seen). The help text has the new keys.
  - `GameRoot`:
    - loads weapons (`ContentLoadException` → a clear `[NMF]` error)
    - right-click on a shown enemy gives `OrderFireAt`, and P runs `CycleFirePolicy`
    - effects are fed from `TakeEvents()` each frame
  - A `--demo` run shows a firefight within the screenshot frames: the demo walks the Finns north at ×4.

- [ ] **Step 1: Implement ArtLibrary blood and UnitView drawing**

`ArtLibrary`:
- add `public Texture2D Blood { get; private set; } = null!;`
- in `Load`, after the objects loop:
```csharp
        if (!library.Catalog.Objects.TryGetValue("blood", out var bloodInfo))
            throw new FormatException("objects.json has no entry for 'blood'");
        library.Blood = Texture(Path.Combine(art, "objects", "blood.png"), bloodInfo.Size * bloodInfo.Count, bloodInfo.Size);
```

`UnitView` additions:
- `public CombatEffects Effects { get; set; } = null!;`
- In `_Draw`, before the shadow loop, for each shown unit with `unit.Wound == WoundLevel.Dead`, draw the blood region `(unit.Id.Value % 2) * 64` scaled to 1.2 × cell under the body.
- After the sprite loop, for each own shown unit that is alive and not out of action, draw the icon text at `pos + (-6, -cell/2)`: pinned "!" in orange, broken "!!" in red, reloading "R" in light grey (font size 22, outline). If the unit is selected and `Target` is a shown unit, also draw `DrawDashedLine(pos, targetPos, red with A = 0.6, 1.5, 6)`.
- Then the effects:
  - For `Tracer`, draw from `From + dir * 60 cm` to `To` with colour `(1, 0.92, 0.55, 1 − progress)`, width 2.
  - For `MuzzleFlash`, draw `DrawCircle(muzzle, 5 × (1 − progress) + 2, (1, 0.95, 0.65, 1 − progress))`.
  - For `Impact`, draw `DrawCircle(To, 3 + 6 × progress, (0.55, 0.45, 0.3, 0.7 × (1 − progress)))`.
  - Here dir = normalized(To − From), and the muzzle is `From + dir × 60 cm` in pixels.

- [ ] **Step 2: HUD cards and top bar**

In `Hud.BuildCard`:
- After the status label, add a condition label (font 13), a policy label (font 13), and two `ProgressBar { MinValue = 0, MaxValue = 1000, ShowPercentage = false, CustomMinimumSize = (96, 6) }`.
- Style the morale bar's `fill` with a green StyleBoxFlat (0.35, 0.7, 0.3) and the suppression bar's `fill` with orange (0.95, 0.55, 0.15). Both get the background stylebox (0.2, 0.2, 0.18).
- Store the labels and bars in the card tuple.

In `Refresh`:
- set status, condition and policy text and the bar values
- set `panel.Modulate` to grey (0.55, 0.55, 0.55) when `unit.Wound == Dead`, white otherwise
- in the top bar, add `Losses: {dead} KIA · {wounded} wounded   Enemy down (seen): {n}`. Here `n` counts enemies whose contact level is Visible and who are out of action.

Add to `HelpText`:
```
"Right click on enemy   fire at that enemy\n" +
"P                      fire policy: fire at will / return fire / hold fire\n" +
```

- [ ] **Step 3: GameRoot wiring**

- Load the weapons after the art: `var weapons = WeaponLoader.LoadDirectory(Path.Combine(contentRoot, "core", "weapons"));` inside the existing try, and also catch `ContentLoadException`. Create the scenario with `SkirmishScenario.Create(map, seed: 1942, weapons)`.
- Create `var effects = new CombatEffects();` and pass it to `UnitView`.
- In `_Process`, after `_session.Update(delta)`:
```csharp
        _units.Effects.Add(_session.TakeEvents(), id => _session.Sim.FindUnit(id) is { } shooter && _session.IsShownToPlayer(shooter, _units.RevealAll));
        _units.Effects.Update(delta);
```
- Right click: before `OrderMove`, use `if (_session.EnemyAt(target, 150) is { } enemy) _session.OrderFireAt(enemy.Id); else ...OrderMove...`.
- `HandleKey`: `case Key.P: session.CycleFirePolicy(); break;`.
- `README.md`: add the table rows `| Right click on enemy | fire at that enemy |` and `| P | fire policy (fire at will / return fire / hold fire) |`, and a sentence saying that soldiers fire on their own, get pinned, break and are rallied by their leader.

- [ ] **Step 4: Build, smoke, screenshots**

Run: `dotnet build NoMansForest.slnx 2>&1 | tail -2` → `0 Error(s)`. Headless smoke → `[NMF] ready`, no ERROR.
Then run `godot-mono --path src/Nmf.Game -- --demo --screenshot=<scratch>/shot-combat.png`. Increase `ScreenshotFrame` via a `--screenshot-frame=N` user arg if the fight starts later than frame 90; ×4 demo speed at 60 fps covers about 6 s of game time by frame 90.
Expected: tracers or muzzle flashes, soldiers prone or firing, card statuses such as "Firing" or "Pinned" and the bars.

- [ ] **Step 5: Full test run and commit**

Run: `dotnet test NoMansForest.slnx && python3 -m unittest discover -s tools/art/tests -t .` → PASS / OK.

```bash
git add -A
git commit -m "feat(game): firefight on screen: tracers, flashes, corpses, state icons, combat HUD

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Spec coverage

| Combat spec item | Task |
|---|---|
| §2 weapons as data, 3 per side, loadouts | 1, 5, 6 |
| §3 shot: aim point, lateral/vertical error, stance/suppression spread, terrain and cover, first man hit, friendly fire, near misses, firing visibility and noise | 2, 4 |
| §4 wounds, escalation, out of action, bleeding, wound speed | 2 |
| §5 suppression, pinned/broken, morale checks, recovery, rally, leader bonus, succession | 3 |
| §6 fire policies, target choice, drop under fire, retreat, action timing, cancellation, orders and rejections | 4, 5 |
| §7 step order, determinism, vision of the dead | 2, 4 |
| §8 effects, dead pose, blood, icons, cards, bars, inputs, top bar | 7, 8 |
| §9 tests | 1–8 |
