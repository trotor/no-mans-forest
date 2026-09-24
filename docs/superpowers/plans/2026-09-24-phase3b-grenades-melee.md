# Phase 3b: Grenades, Melee, Assault & Simple Control – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the game "click where to go". The whole squad is commanded by default, and the soldiers choose their own pace and stance. They also throw grenades, fight hand to hand, surrender when broken and charge on a double click.

**Architecture:**
- `Nmf.Sim` gets new data and systems:
  - `GrenadeDef` data, a `Grenade` entity list in `Simulation`, and `GrenadeSystem` (throw action, flight, explosion with shielding)
  - `MeleeSystem` (start, resolve, surrender to capture)
  - a new `Sneak` move mode, plus `Auto` as an order-only mode
- The soldier brain gains auto pace, auto crouch, grenade choice and assault chasing.
- `AssaultOrder` joins the existing orders.
- `Nmf.Content` loads grenade YAML.
- `Nmf.Client` commands the whole squad when nothing is selected, maps the new click meanings, and adds explosion effects and craters.
- The Godot glue draws grenades, explosions, craters, melee and prisoners.

**Tech Stack:** .NET 10 SDK (libraries `net8.0`), xUnit, YamlDotNet, Godot 4.7.2 .NET.

**Spec:** `docs/superpowers/specs/2026-09-24-grenades-melee-design.md` (plus `2026-09-24-combat-design.md`)

## Global Constraints

- **Numbers** (spec §2–§5):
  - Auto pace: run at suppression ≥ 150; sneak (crouched, 60 % speed) when a visible enemy is within 6000 cm; otherwise walk. An idle standing soldier crouches when a visible enemy is within 6000 cm and his suppression is below 100.
  - Grenade throw: `Throwing` lasts 20 ticks, flight 12 ticks, explosion at the throw tick + `fuse_ticks`. Range is 800 cm … `throw_range` (60 % when prone). No friend within 800 cm of the aim point. Cooldown 100 ticks. 2 grenades per soldier. The target must be prone or next to hard cover (a neighbour cell with cover ≥ 128 and obstacle ≥ 50 cm), or the thrower must be pinned.
  - Fragments: chance `lethality × (1 − d/lethal) × stance%` (100/80/50). Shielding comes from a hill higher than both ends + 50 cm, or an obstacle ≥ 50 cm with a `cover/255` roll. Suppression is `s × (1 − d/blast)`, halved when shielded.
  - Melee: 200 cm, 40 ticks. Score `100 + skill + morale/10 + 30·surprise − wound − suppression/20`, with skill smg 60 / rifle 50 / lmg 30 / unarmed 20 and wound light 10 / serious 30. The loser rolls Dead 50 / Incapacitated 30 / Serious 20, escalating if already wounded. The winner gains +50 morale. A broken soldier within 200 cm of an in-action enemy is captured.
  - Assault: run to the target and re-path when it moves more than 200 cm. Grenade throws happen on the way. The assault ends when the target is out of action.
- **Step order** (spec §6):
  1. orders
  2. movement
  3. firing
  4. grenades
  5. melee
  6. bleeding and morale
  7. every 5 ticks: vision
  8. every 5 ticks: brains
- **Determinism:** all randomness comes from `sim.Rng`, units are processed in id order and grenades in id order. `StateHash` covers the new state.
- **Captured soldiers** are out of action (`IsOutOfAction`) but alive.
- **Rejections:** `AssaultOrder` with an invalid target gives "invalid target"; while pinned it gives "unit is pinned".
- **Player knowledge:** a grenade is shown if its thrower is shown to the player or its landing cell is in the player's visible cells. Explosions and craters are always shown.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Review Focus

1. **A grenade lands next to the thrower's own men, or next to the thrower** (short throw, scatter, enemy advancing): friendly casualties must come only from real scatter. The pre-throw safety check must hold at throw time → Task 2 tests.
2. **Melee among three or more men** (two against one, a fight at a map edge, one fighter hit by a bullet mid-fight): every soldier is in at most one fight, a fight ends cleanly when one side goes down by other means, and nobody stays frozen in `Melee` → Task 3 tests.
3. **An assault target that dies, is captured or walks behind an impassable wall:** the assault stops, and there is no endless re-pathing → Task 4 tests.
4. **A click with nothing selected on a map where some own men are down or captured:** only in-action men get orders → Task 6 tests.
5. **Long fight on the real map with grenades:** no exception, deterministic, and grenades are actually thrown → Task 5 test.

---

## File Structure

```
src/Nmf.Sim/Combat/GrenadeDef.cs, Grenade.cs, GrenadeSystem.cs, MeleeSystem.cs
src/Nmf.Sim/Combat/CombatTypes.cs      # + CombatAction.Throwing, Melee
src/Nmf.Sim/Combat/CombatRules.cs      # + grenade, melee, pace constants
src/Nmf.Sim/Combat/Damage.cs           # ApplyHit(lethality) overload
src/Nmf.Sim/Units/Unit.cs              # + grenades, melee, captured, assault, auto pace
src/Nmf.Sim/Units/StanceRules.cs       # + Sneak
src/Nmf.Sim/Units/Movement.cs          # frozen while throwing / in melee
src/Nmf.Sim/Orders/Orders.cs           # + AssaultOrder; MoveMode.Auto handled in Apply
src/Nmf.Sim/Events/SimEvents.cs        # + grenade / melee / capture events
src/Nmf.Sim/AI/SoldierBrain.cs         # pace, crouch, grenades, assault
src/Nmf.Sim/Simulation.cs              # grenades list, Apply, Step
src/Nmf.Sim/Vision/VisionRules.cs      # Sneak noise/visibility
src/Nmf.Sim/Core/StateHash.cs
src/Nmf.Sim/Scenarios/SkirmishScenario.cs   # grenade loadouts
src/Nmf.Content/Weapons/GrenadeLoader.cs; content/core/grenades/{m32,rgd33}.yaml
src/Nmf.Client/GameSession.cs, ClickControl.cs, UnitStatus.cs, Effects/CombatEffects.cs, Art/UnitAnimator.cs
src/Nmf.Game/GameRoot.cs, UnitView.cs, Hud.cs; README.md
tests: Nmf.Sim.Tests/Combat/{GrenadeTests,MeleeTests}.cs, AI/{AssaultTests,AutoPaceTests}.cs, Content GrenadeLoaderTests.cs, Client updates
```

---

### Task 1: Types, rules, unit state, events, Sneak mode

**Files:** `GrenadeDef.cs`, `Grenade.cs` (new), `CombatTypes.cs`, `CombatRules.cs`, `Unit.cs`, `StanceRules.cs`, `Movement.cs`, `Orders.cs`, `SimEvents.cs`, `Simulation.cs` (SpawnUnit overload, grenade list), `VisionRules.cs`, `StateHash.cs`, `Damage.cs`
**Test:** `src/Nmf.Sim.Tests/Combat/GrenadeDefTests.cs`

**Interfaces (produces):**
- `sealed record GrenadeDef(string Id, string Name, int FuseTicks, int ThrowRangeCm, int ScatterPct, int BlastRadiusCm, int LethalRadiusCm, int Suppression, int LethalityPct)` with `Validated()`. It requires fuse ≥ 13, range ≥ 500, scatter 0..100, blast ≥ lethal ≥ 100, suppression ≥ 0 and lethality 0..100.
- `sealed class Grenade` with `int Id`, `UnitId Thrower`, `Side Side`, `GrenadeDef Def`, `Vec2 From`, `Vec2 Landing`, `long ThrowTick`, `long LandTick`, `long ExplodeTick` and `bool Exploded`.
- New `CombatAction` values `Throwing` and `Melee`, and new `MoveMode` values `Sneak = 3` and `Auto = 4` (Auto is only valid in orders).
- New `Unit` members: `GrenadeDef? GrenadeType`, `int Grenades`, `long LastThrowTick`, `UnitId? ThrowTarget`, `UnitId? MeleeOpponent`, `bool IsCaptured`, `UnitId? AssaultTarget` and `bool AutoPace`. Internal: `bool MeleeSurprise` and `Vec2 AssaultGoal`. `IsOutOfAction` now also covers `IsCaptured`.
- `Simulation.SpawnUnit(side, pos, speed, weapon, isLeader = false, grenade = null)`, which gives 2 grenades when `grenade` is set. Also `IReadOnlyList<Grenade> Grenades`.
- `AssaultOrder(UnitId Unit, UnitId Target)`.
- Events `GrenadeThrown(long Tick, UnitId Thrower, Vec2 From, Vec2 To, long ExplodeTick)`, `GrenadeExploded(long Tick, int Grenade, Vec2 At)`, `MeleeStarted(long Tick, UnitId A, UnitId B)`, `MeleeEnded(long Tick, UnitId Winner, UnitId Loser)` and `UnitCaptured(long Tick, UnitId Unit)`.
- `Damage.ApplyHit(Simulation, Unit, int lethalityPct, long tick, List<SimEvent>)`, with the `WeaponDef` overload delegating to it.
- `StanceRules`: Sneak requires Crouching and moves at 60 % speed. `VisionRules`: Sneak noise is 1500 cm and visibility 150 %.
- `Movement.Update` returns early while `Action` is `Throwing` or `Melee`.

- [ ] **Step 1: Failing tests** — `GrenadeDefTests.cs`: `Validated` rejects a fuse of 5, a lethal radius larger than the blast radius and a lethality of 101. `SpawnUnit` with a grenade gives `Grenades == 2`. A captured unit is `IsOutOfAction` and `IsAlive`. A Sneak move order goes crouched and covers 60 % of the walk distance per tick.
- [ ] **Step 2: Run** `dotnet test src/Nmf.Sim.Tests` → the build FAILS (types missing).
- [ ] **Step 3: Implement** the interfaces above. `StateHash` adds, per unit: `Grenades`, `LastThrowTick`, `ThrowTarget`, `MeleeOpponent`, `IsCaptured`, `AssaultTarget`, `AutoPace`, `MeleeSurprise` and `AssaultGoal`. Per grenade it adds `Id`, `Landing`, `ThrowTick` and `Exploded`.
- [ ] **Step 4: Run** `dotnet test NoMansForest.slnx` → PASS.
- [ ] **Step 5: Commit** "feat(sim): grenade data, melee/capture/assault state, sneak mode".

### Task 2: Grenades

**Files:** `src/Nmf.Sim/Combat/GrenadeSystem.cs`, `Simulation.cs` (Step: after firing, `GrenadeSystem.UpdateThrowing` per unit, then `GrenadeSystem.UpdateGrenades`)
**Test:** `src/Nmf.Sim.Tests/Combat/GrenadeTests.cs`

**Interfaces:** `GrenadeSystem` provides:
- `bool CanThrowAt(Simulation, Unit, Unit target, long tick)`
- `void StartThrow(Unit, Unit target)`
- `void UpdateThrowing(Simulation, Unit, long tick, List<SimEvent>)`
- `void UpdateGrenades(Simulation, long tick, List<SimEvent>)`
- `bool HardCoverNear(GridMap, Unit)`

**Test cases:**
- A throw with scatter 0 lands exactly on the target. The event fires at the end of the 20 throw ticks and `Grenades` goes 2 → 1.
- The explosion happens at `ExplodeTick`. With lethality 100, a standing man 1 m from it in the open is wounded, and a man 15 m away is only suppressed.
- A man behind a 3 m hill (between him and the grenade) gets no wound and half the suppression.
- A prone man is hit less often than a standing man over 60 trials.
- `CanThrowAt` is false when:
  - the target is at 5 m or at 40 m
  - a friend is within 8 m of the target
  - no grenades are left
  - the cooldown is active
  - the target is standing in the open and the thrower is not pinned
- `CanThrowAt` is true for a prone target at 15 m.
- A thrower hit while `Throwing` never releases the grenade.
- Two identical runs give identical hashes.

- [ ] Steps: failing tests → run (FAIL) → implement → run (PASS) → commit "feat(sim): hand grenades: throw, flight, shielded fragments and suppression".

### Task 3: Melee and capture

**Files:** `src/Nmf.Sim/Combat/MeleeSystem.cs`, `Simulation.cs` (Step: after grenades, `MeleeSystem.Update`)
**Test:** `src/Nmf.Sim.Tests/Combat/MeleeTests.cs`

**Test cases:**
- Two enemies placed 1.5 m apart both enter `Melee` (with a `MeleeStarted` event), neither moves or fires, and within 41 ticks one of them is wounded or down and a `MeleeEnded` event is emitted.
- Three men, two against one: each is in at most one fight, and the third enemy waits.
- A broken soldier next to an enemy is captured: `UnitCaptured` fires, he is `IsOutOfAction`, alive, crouching, and his side's morale drops.
- A fighter killed by a bullet mid-fight: the opponent leaves `Melee` on the next tick.
- Deterministic: two identical runs give identical hashes.

- [ ] Steps: failing tests → FAIL → implement → PASS → commit "feat(sim): hand-to-hand fighting and surrender".

### Task 4: Brain – auto pace, auto crouch, grenades, assault; AssaultOrder and MoveMode.Auto in Apply

**Files:** `src/Nmf.Sim/AI/SoldierBrain.cs`, `Simulation.cs` (Apply)
**Test:** `src/Nmf.Sim.Tests/AI/AutoPaceTests.cs`, `src/Nmf.Sim.Tests/AI/AssaultTests.cs`

**Test cases:**
- An `Auto` move with no enemies walks.
- An `Auto` move with a visible enemy within 60 m sneaks crouched.
- An `Auto` move under fire (suppression 200) runs.
- An idle standing man with a visible enemy within 60 m crouches.
- `AssaultOrder` runs toward the target, re-paths when the target moves, and ends in melee.
- An assault on a target behind a wall that cannot be reached ends, and `AssaultTarget` becomes null.
- The assault stops when the target dies.
- `AssaultOrder` on a friend is rejected with "invalid target", and while pinned with "unit is pinned".
- A soldier with grenades next to a prone enemy at 15 m throws a grenade by himself.
- Existing phase-3 tests keep passing. The auto crouch delays the first shot by 10 ticks, which fits within the existing tick budgets.

- [ ] Steps: failing tests → FAIL → implement → PASS (full suite) → commit "feat(sim): soldiers pick pace and stance, throw grenades and charge on assault orders".

### Task 5: Grenade content and loadouts

**Files:**
- `src/Nmf.Content/Weapons/GrenadeLoader.cs`
- `content/core/grenades/m32.yaml` with `fuse_ticks 80, throw_range_m 30, scatter_pct 15, blast_radius_m 10, lethal_radius_m 4, suppression 600, lethality_pct 60`
- `content/core/grenades/rgd33.yaml` with `fuse_ticks 80, throw_range_m 35, scatter_pct 15, blast_radius_m 12, lethal_radius_m 5, suppression 600, lethality_pct 65`
- `SkirmishScenario.Create(map, seed, weapons, grenades = null)` with ids `m32` and `rgd33`

**Test:** `src/Nmf.Content.Tests/GrenadeLoaderTests.cs`, and `SkirmishFightTests` extended with grenades.

**Test cases:**
- The loader reads every field and converts metres to centimetres. Missing fields, unknown keys and bad values fail with the file name.
- The core grenades load.
- A 3-minute real-map fight with grenades is deterministic, fires shots and throws at least one grenade.

- [ ] Steps: failing tests → FAIL → implement → PASS → commit "feat(content): grenade YAML and loadouts".

### Task 6: Client – squad always commanded, new clicks, statuses, explosions

**Files:** `GameSession.cs`, `ClickControl.cs`, `UnitStatus.cs`, `Effects/CombatEffects.cs`, `Art/UnitAnimator.cs`
**Test:** updates in `ClickControlTests.cs`, `UnitStatusTests.cs`, `CombatEffectsTests.cs` and `UnitAnimatorTests.cs`

**Interfaces:**
- `GameSession` gains `IReadOnlyList<UnitId> CommandedIds` (the selection, or else every own in-action soldier), `bool IsSquadCommanded` and `OrderAssault(UnitId target)`.
- `OrderMove`, `OrderFireAt`, `OrderStance`, `OrderStop` and `CycleFirePolicy` all use `CommandedIds`.
- `HandleLeftClick`:
  - own soldier: select him, or on a double click clear the selection so the whole squad is commanded again
  - shown enemy: fire at him, or assault him on a double click
  - ground: an Auto move, Run on a double click, Crawl with Alt
- New value `ClickResult.AssaultOrdered`.
- `UnitStatus.Describe`: Captured, Melee, Throwing, Assaulting, Sneaking.
- `CombatEffects` gains an `Explosion` effect (0.6 s) and a persistent `Craters` list (capped at 300), both fed by `GrenadeExploded`.
- `UnitAnimator`: a captured soldier uses `crouch`, and a moving crouched soldier uses `walk`.

**Test cases:**
- Clicking ground with nothing selected moves every in-action own soldier with the `Auto` mode, and downed or captured men get no order.
- Double-clicking an enemy gives an `AssaultOrder` for each commanded soldier.
- Double-clicking your own soldier returns command to the whole squad.
- The statuses, explosion and crater cases above.

- [ ] Steps: failing tests → FAIL → implement → PASS → commit "feat(client): whole squad commanded by default, assault clicks, grenade effects".

### Task 7: Godot – grenades, explosions, craters, melee, prisoners, HUD

**Files:** `GameRoot.cs` (load grenades, `ShowOutcome` for assault), `UnitView.cs`, `Hud.cs`, `README.md`

**Draw:**
- craters (dark scorch ellipses) before the units
- grenades in flight along an arc (with a height offset), and landed grenades blinking. The M/32 is drawn as a stick with a head. Only grenades that are shown (see Global Constraints) are drawn.
- explosions: a flash, a smoke ring and dust
- a "⚔" icon over units in `Melee`, and a white flag with grey tint over captured units

**HUD:**
- the top bar shows `Commanding: whole squad` or the soldier's name
- the cards show the grenade count
- the help text and README describe the new click meanings

**Verify:** build, headless smoke, and a combat screenshot (`--demo --screenshot-frame`).

- [ ] Steps: implement → build → smoke → screenshot → tests → commit "feat(game): grenades, explosions, melee and prisoners on screen; squad command HUD".

---

## Spec coverage

| Spec | Task |
|---|---|
| §2 squad command, click meanings, auto pace, sneak, auto crouch | 1, 4, 6, 7 |
| §3 grenade data, throw, flight, explosion, shielding, auto throw | 1, 2, 4, 5 |
| §4 melee, surprise, capture | 3 |
| §5 assault | 4, 6 |
| §6 step order, rejections, events, hash | 1–4 |
| §7 visuals | 6, 7 |
| §8 tests | 1–7 |
