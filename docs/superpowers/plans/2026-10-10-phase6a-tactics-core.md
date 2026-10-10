# Phase 6a: tactics core — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, then run `dotnet test NoMansForest.slnx`. Work on branch `phase-6a-tactics`, merge to main with `--no-ff` ("Merge phase 6a: …") and push.

**Goal:** A double click on a seen enemy makes the men plan an attack on his position (fire base, flank, assault on a suppressed position, or long fire), carry it out, and halt on their own when it goes wrong. A permanent probe measures whether this beats the old attack.

**Architecture:** `Nmf.Sim/Tactics` holds pure data (`TacticDef`, `TacticPlan`), the `ThreatMap`, the `TacticPlanner` (a read-only function of map, side knowledge and own men), and the `PlannedAttack` runner, a state machine run on the brain tick like `AttackPlanner`. The plan travels inside `PlannedAttackOrder`, so replays stay deterministic. `Nmf.Content/Tactics` loads `content/core/tactics/*.yaml`. The client suggests the plan on a double click and shows toasts. `Nmf.Cli probe-attack` runs the Iskuosasto attack headless over seeds.

**Tech Stack:** C# (.NET, net8.0 libraries; the CLI is net10.0), xUnit, YamlDotNet, Godot 4.7.2 (toasts only).

**Spec:** `docs/superpowers/specs/2026-10-10-tactics-design.md`

## Global Constraints

- The sim stays deterministic. Integers only, fixed iteration order (unit id, then cell order), no `Simulation.Rng` draws in the planner, and new state goes into `StateHash`.
- The planner sees only what the side knows (`SideKnowledge` contacts), never true enemy positions, except a `Visible` contact's current position, which the side does know.
- The objective is the **position**: every known enemy within 30 m of `Aim` (`PositionRadiusCm = 3000`), re-gathered during the run around the position's centre.
- Phase timings: Deploy ends when the fire team is set or after 60 s. Suppress → Assault needs ≥ 5 s of fire and the whole upright strike team at the form-up. "No fire" halt after 20 s without a fire-team round in Suppress. "Target moved" halt when the position centre drifts > 20 m from `Aim`.
- Halts: casualties ≥ `max(1, strength × casualties_pct × (100 + avgNerve − 50) / 10000)`; the attack leader down; ≥ half the strike team pinned continuously `pinned_s`; the position not suppressed by `give_up_s`. After a halt the fire team keeps area fire at the position (a standing area target); the strike team returns to normal behaviour.
- The old `AttackOrder`/`AttackPlanner` stay (the enemy uses them until 6c). `GameSession` falls back to them when no tactics are loaded.
- Texts are in English and Finnish (`Alerts`).

## Review Focus

1. **The plan is made during a pause and applied a tick later, or the men or the position move meanwhile:** the run must use current positions and halt with "target moved", not chase a stale plan. Test: `PlannedAttackTests.PositionMovingAway_HaltsWithTargetMoved`.
2. **A man in the plan is pinned, dead or broken when the order lands:** he is left out, and the order is not rejected for the others. Test: `PlannedAttackTests.PinnedManInThePlan_IsLeftOut_OthersGo`.
3. **The player gives one of the men another order mid-attack:** he leaves, and the attack goes on with the rest or ends when nobody is left. Test: `PlannedAttackTests.AnotherOrder_TakesAManOut`.
4. **A position the fire base cannot see at all:** "no fire" halt instead of a strike team waiting forever at the form-up. Test: `PlannedAttackTests.NoFireAtAll_HaltsWithNoFire`.
5. **Planner cost on the real 1 km map:** a plan within the time limit from the real contact positions, not a node-budget null. Test: `TacticPlannerKarhumakiTests.IskuosastoContact_PlansFireAndMovement_InTime`.

---

### Task 1: The probe and the baseline

**Files:**
- Create: `src/Nmf.Cli/AttackProbe.cs`
- Modify: `src/Nmf.Cli/Program.cs`, `docs/superpowers/specs/2026-10-10-tactics-design.md` (baseline numbers)

**Interfaces:**
- Produces: `AttackProbe.Run(string contentRoot, string missionId, string tactic, int seeds, int minutes, TextWriter output) : ProbeSummary`. `tactic` is `legacy` now; Task 8 adds tactic ids and `suggest` through a `Func<Simulation, IReadOnlyList<Unit>, Unit, bool>? attack` hook, so the probe does not depend on the planner yet.

- [ ] **Step 1: Implement the probe** (a tool; its check is running it, see Step 2).
  - Load the mission (`MissionLoader.Load`), map (`TmxMapLoader`), weapons and grenades as `GameRoot` does. Then `MissionScenario.Create(map, spec, weapons, grenades, seed)` and `new MissionTracker(spec, map)`.
  - For each seed `1..seeds`, loop `scenario.Tick(); sim.Step(); tracker.Update(sim)` for `minutes × 60 × 20` ticks after first contact, at most 20 min in all.
  - **Approach.** The Finns walk the mission plan's first `attack` arrow (`spec.Plan`), each man with a `MoveOrder` to the next arrow point (Auto pace). From 160 m before the last point they sneak (`MoveMode.Sneak`). Waypoints advance when the group centre is within 10 m. Before the plan's end the Finns are the probe's; once contact happens they are the attack's.
  - **Contact** is the first tick a red unit is `Visible` to Blue.
  - **Attack.** The target is the red leader (`IsLeader`) if visible, else the nearest visible red. Every fit Finn (not out of action, not broken) gets the attack. Legacy means one `AttackOrder` per man.
  - **Re-attack.** After that, every 15 s, if no fit Finn is in an attack (`AttackGroupId`/`PlannedAttackId` null for all) and a red is visible, attack again.
  - **Record per seed:** Finns out of action, reds out of action, red leader down, attacks given, mission result (`tracker`), and halts by reason (Task 8 adds those).
  - **Print** one line per seed and a mean line, e.g. `seed 3: blue out 6/7, red out 2/9, leader down no, attacks 3`.
  - **Command line:** `nmf probe-attack --mission iskuosasto --tactic legacy --seeds 8 --minutes 8 [--content <dir>]`. The content folder is found with `ContentLocator`-style walking up from the current directory to `content/core`; the CLI does not reference `Nmf.Client`, so copy the 10-line walk.

- [ ] **Step 2: Run the baseline.** `dotnet run --project src/Nmf.Cli -c Release -- probe-attack --mission iskuosasto --tactic legacy --seeds 8 --minutes 8`. Expected: it finishes in a few minutes and prints 8 seed lines plus a mean line.

- [ ] **Step 3: Record** the mean and per-seed lines under a new section "## Mittaukset" at the end of the spec, as "Lähtötaso (legacy, 10.10.2026)".

- [ ] **Step 4: Commit** — "Attack probe as a CLI command (nmf probe-attack); legacy baseline in the spec".

---

### Task 2: Tactics as data

**Files:**
- Create: `src/Nmf.Sim/Tactics/TacticDef.cs`, `src/Nmf.Content/Tactics/TacticLoader.cs`, `content/core/tactics/fire_and_movement.yaml`, `content/core/tactics/long_fire.yaml`
- Modify: `src/Nmf.Content.Tests/CoreContentTests.cs`
- Test: `src/Nmf.Content.Tests/TacticLoaderTests.cs`

**Interfaces:**
- Produces:

```csharp
namespace Nmf.Sim.Tactics;
public sealed record TacticManeuver(int FlankDeg, int FormUpMinCm, int FormUpMaxCm);
public sealed record TacticAssault(int Suppression, int WaitTicks, int GiveUpTicks);
public sealed record TacticDef(string Id, string NameEn, string NameFi, int FireBaseMinCm, int FireBaseMaxCm,
    TacticManeuver? Maneuver, TacticAssault? Assault, int AbortCasualtiesPct, int AbortPinnedTicks)
{
    public string Name(string language) => language == "fi" ? NameFi : NameEn;
    public TacticDef Validated();   // throws ArgumentException with the field name
}
```

  `TacticLoader.LoadDirectory(string dir) : IReadOnlyList<TacticDef>` returns tactics in file-name order; the first is the default. To make fire and movement first, its file is named `10_fire_and_movement.yaml` and long fire's `20_long_fire.yaml`.

- [ ] **Step 1: Write the failing tests:**
  - `ValidFile_Loads` (fire and movement as in the spec: metres → cm, seconds → ticks)
  - `LongFire_HasNoManeuverNorAssault`
  - `[Theory] BadField_IsAClearError` covering:
    - `min_m: 0`
    - `max_m < min_m`
    - `max_m: 301`
    - `flank_deg: 151`
    - `form_up.max_m > fire_base.max_m`
    - `casualties_pct: 0`
    - `assault without maneuver`
  - `DuplicateId_IsAnError`
  - `CoreContentTests.CoreTactics_Load` (two tactics, the first `fire_and_movement`).

- [ ] **Step 2: Run** `dotnet test src/Nmf.Content.Tests --filter "TacticLoaderTests|CoreTactics"`. Expected: build fails.

- [ ] **Step 3: Implement** the records, `Validated()` (the spec's rules) and the loader in the `WeaponLoader` style, then write the two YAML files exactly as in the spec.

- [ ] **Step 4: Run the tests.** Expected: PASS. **Step 5: Commit** — "Tactics as data: fire and movement, long fire".

---

### Task 3: Weighted path search

**Files:**
- Modify: `src/Nmf.Sim/World/Pathfinder.cs`
- Test: `src/Nmf.Sim.Tests/World/PathfinderTests.cs` (existing file; find it with `grep -rl "class PathfinderTests" src/Nmf.Sim.Tests`)

**Interfaces:**
- Produces: `Pathfinder.FindPath(GridMap map, Vec2 start, Vec2 target, Func<int, int, int> extraCost, int maxExpandedNodes = MaxExpandedNodes)`. The extra cost for entering cell (x, y), ≥ 0, is added to the step cost. The existing overload stays unchanged and allocation-free.

- [ ] **Step 1: Failing test** `ExtraCost_SendsThePathAroundACostlyStrip`. On a 60 × 40 open map, a strip x = 30, y = 0..30 costs 5000 per cell. The path from (10, 5) to (50, 5) must pass below y = 30, while the plain search goes straight through. Plus `ZeroExtraCost_GivesThePlainPath`.
- [ ] **Step 2: Run.** Expected: build fails.
- [ ] **Step 3: Implement.** Factor the A* body into a private method taking a nullable `Func<int,int,int>`; the step becomes `step + (extra?.Invoke(nx, ny) ?? 0)`. The heuristic stays the same: it is still a lower bound, because extra costs are ≥ 0.
- [ ] **Step 4: Run** `dotnet test src/Nmf.Sim.Tests`. Expected: PASS. **Step 5: Commit** — "Path search with an extra cost per cell".

---

### Task 4: Threat map

**Files:**
- Create: `src/Nmf.Sim/Tactics/ThreatMap.cs`
- Test: `src/Nmf.Sim.Tests/Tactics/ThreatMapTests.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class ThreatMap
{
    public const int BlockCm = 400, SightCm = 15_000, EyeCm = 100, TargetCm = 100;
    public static ThreatMap Build(Simulation sim, Side side, Vec2 centre, int radiusCm);
    public int ExposureAt(Vec2 p);                       // sources that see the block; 0 outside the window
    public int ExposureAt(Vec2 p, Vec2 excludeNear, int excludeRadiusCm);  // not counting sources near a point (the position itself)
}
```

  The sources are the side's contacts at levels `Visible`/`LastKnown`/`Suspected`. Their place is the unit's position for `Visible` and the contact's position otherwise. Out-of-action units are skipped (`sim.FindUnit(...).IsOutOfAction`); the side sees bodies anyway. The window radius is clamped to 25 000 cm. Each source is stored per block as a bit in a `ulong` mask (at most 64 sources; contacts beyond 64 are ignored, nearest the centre first), so `ExposureAt(p, excludeNear, r)` can drop the sources within r of a point.

- [ ] **Step 1: Failing tests** on a 200 × 200 open map with a red at (100, 20) m, seen by blue:
  - `OpenGround_IsExposed` (block at (100, 80) m)
  - `BehindARidge_IsNotExposed` (a 3 m high ridge row at y = 50 m)
  - `UnknownEnemy_DoesNotCount`
  - `SuspectedEnemy_CountsFromTheContactPlace_NotHisRealPlace` (move the unit away after the contact is set; the exposure follows the contact)
  - `BeyondSight_IsNotExposed` (160 m)
  - `ExcludingThePosition_DropsItsSources`.
- [ ] **Step 2: Run.** Expected: build fails.
- [ ] **Step 3: Implement.**
  - The block grid covers the window. Block centre heights are `ground + TargetCm`; the source eye is `ground + EyeCm`.
  - For each source and each block within `SightCm`, `LineOfSight.Clarity(...) > 0` sets the bit.
  - Iterate sources in contact (unit id) order and blocks row-major.
- [ ] **Step 4: Run.** Expected: PASS. **Step 5: Commit** — "Threat map: where the enemy the side knows of can see".

---

### Task 5: Planner

**Files:**
- Create: `src/Nmf.Sim/Tactics/TacticPlan.cs`, `src/Nmf.Sim/Tactics/TacticPlanner.cs`
- Test: `src/Nmf.Sim.Tests/Tactics/TacticPlannerTests.cs`, `src/Nmf.Content.Tests/TacticPlannerKarhumakiTests.cs` (in `Timing` collection; it needs the real map and mission)

**Interfaces:**
- Consumes: `TacticDef` (Task 2), `Pathfinder.FindPath(..., extraCost)` (Task 3), `ThreatMap` (Task 4), `CoverFinder.CoveredAt`, `CoverFinder.InPit`, `CoverFinder.RimAboveCm`, `LineOfSight.Clarity`, `Pathfinder.Reachable`.
- Produces:

```csharp
public sealed record FirePosition(UnitId Man, Vec2 At);
public sealed record TacticPlan(string TacticId, UnitId Target, Vec2 Aim, IReadOnlyList<UnitId> Position,
    IReadOnlyList<FirePosition> FireTeam, IReadOnlyList<UnitId> StrikeTeam, IReadOnlyList<FirePosition> FormUp,
    IReadOnlyList<Vec2> Route, bool FireBaseBlind, int Score, PlanReason Reason);
public sealed record PlanReason(int FireBaseM, int Side /* -1 left, 1 right, 0 none */, bool Blind);
public static class TacticPlanner
{
    public const int PositionRadiusCm = 3000;
    public static TacticPlan? Propose(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, TacticDef tactic);
    public static TacticPlan? Suggest(Simulation sim, Side side, IReadOnlyList<UnitId> men, UnitId target, IReadOnlyList<TacticDef> tactics);
    public static IReadOnlyList<UnitId> PositionOf(Simulation sim, Side side, Vec2 centre); // known enemies within 30 m, id order
}
```

  `FormUp` holds the strike team's form-up spots, one per man. `Route` is the strike team's shared way to the form-up, with points at most 10 m apart.

- [ ] **Step 1: Failing tests** (`TacticPlannerTests`, small maps built in the test with `GridMap` and cell writes; a helper `Field` spawns four or seven Finns and reds, sets knowledge by stepping the sim on open ground with `HoldFire` on everyone):
  - `Teams_TwoSquads_LmgSquadIsTheFireTeam`
  - `Teams_OneSquad_LmgAndBestShotsFire_SmgsStrike`
  - `FireAndMovement_NeedsFourMen`
  - `FireBase_IsWithinTheBand_OnOurSide_AndSeesThePosition`
  - `FireBase_PrefersCover` (two equal spots, one behind a low wall facing the target)
  - `FireBase_NoSightInBand_ComesCloser` (a wall of forest leaves sight only at 30 m)
  - `FireBase_NoSightAnywhere_IsBlind`
  - `FormUp_IsOffTheFireLine_ByTheFlankAngle`
  - `Route_GoesRoundTheOpenGroundTheEnemySees` (a forest belt alongside an open field)
  - `Route_DoesNotCrossTheFireLine`
  - `Position_GathersKnownEnemiesWithin30m`
  - `Suggest_PrefersLongFire_WhenTheEnemyOutnumbersUs`
  - `Suggest_PrefersFireAndMovement_OtherwiseByOrder`
  - `SameInput_SamePlan` (determinism)
  - `UnknownTarget_NoPlan`.

  `TacticPlannerKarhumakiTests.IskuosastoContact_PlansFireAndMovement_InTime`:
  - load karhumaki and the Iskuosasto mission
  - place the Finns at the plan arrow's third point and the post as spawned
  - make the side see Belov by setting the contact: an internal helper `SideKnowledge.GetOrAdd`, visible to `Nmf.Content.Tests` through `InternalsVisibleTo`, already there
  - `Propose(fire_and_movement)` is non-null and takes < 500 ms × `NMF_TIMING_FACTOR`.
- [ ] **Step 2: Run.** Expected: build fails.
- [ ] **Step 3: Implement** (the spec's "Suunnittelija" section, with these exact rules):
  - **Aim:** `Visible` → `target.Position`; `LastKnown`/`Suspected` → contact position; else null plan.
  - **Teams** as in the spec. "Best shots" = highest `Marksmanship`, ties by unit id. The fire-team size for one squad = `(n + 1) / 2` including the LMG.
  - **Threat map:** `ThreatMap.Build(sim, side, aim, max(FireBaseMaxCm, dist(centroid, aim)) + 4000)`.
  - **Fire base:**
    - Candidates every 200 cm on rings from `FireBaseMaxCm` down to `FireBaseMinCm` in 200 cm steps, angles within ±70° of centroid→aim seen from aim (integer: compare dot products with `cos70 ≈ 342/1000`).
    - Each candidate must be passable, `Reachable` from the fire team's first man, and in the map.
    - Sight: `Clarity(map, c, ground(c) + 100, aim, aimHeight) >= 60`, with aimHeight = `ground(aim) + 60`, or `ground(aim) + rim + 30` when `InPit(aim)`, and distance ≤ the fire team's shortest weapon range.
    - Score: `clarity + CoveredAt(map, cell, aim) + 2 × distM − 60 × exposure(c, excluding the position) − travelM / 2`, where travel is the straight-line distance from the fire team centroid.
    - Ties go to the lower ring index, then the lower angle index.
    - If none qualify, extend the rings down to 2500 cm in 500 cm steps. If still none, `FireBaseBlind`: each fire-team man's spot is `CoverFinder.Find(sim, man, aim) ?? man.Position`.
    - Otherwise spread the men: the LMG first, then by id. Each takes the best-scoring qualifying candidate within 1200 cm of the anchor and ≥ 300 cm from spots taken (the anchor itself goes to the first).
  - **Form-up** (fire and movement only):
    - Candidates every 200 cm on rings `FormUpMinCm..FormUpMaxCm`, with |angle between (aim→fire base) and (aim→candidate)| ≥ `FlankDeg`, compared with integer cosines from a 0..180° table of `cos × 1000`.
    - Pre-score: `CoveredAt − 60 × exposure(excluding position) − straightM / 2` from the strike centroid.
    - Take the top 5. Run `FindPath(map, strikeCentroid, candidate, ExtraCost, 40_000)` with `ExtraCost(x, y) = 300 × exposureAt(cell centre, excluding position) + (2000 if within 800 cm of the segment fireBase→aim, strictly between them)`.
    - Final score = `pre − pathExtraCost / 100`, best wins. If no path is found, there is no plan.
    - The form-up spots: the strike men in id order get the winning cell and its neighbours along the ring ±300 cm, passable ones.
  - **Route:** the found path thinned to points ≤ 1000 cm apart, ending at the form-up.
  - **Score:** fire-base score (+ form-up final score for fire and movement). `Suggest`: fire and movement −200 when known enemies (contacts at levels ≥ Suspected, not out of action) ≥ fit own men, −300 when blind. The highest wins, ties go to the tactics list order.
  - **Reason:** fire-base distance in metres; side −1 or 1 from the cross product sign of (aim→fireBase) × (aim→formUp), from the strike team's point of view (left/right as seen from the fire base looking at the aim); blind flag.
- [ ] **Step 4: Run** `dotnet test src/Nmf.Sim.Tests --filter TacticPlanner` and the Karhumäki test. Expected: PASS.
- [ ] **Step 5: Commit** — "Tactic planner: the position, fire base, flank form-up and a route round what the enemy sees".

---

### Task 6: Running the plan

**Files:**
- Create: `src/Nmf.Sim/Tactics/PlannedAttack.cs`, `src/Nmf.Sim/Tactics/PlannedAttackRunner.cs`
- Modify:
  - `src/Nmf.Sim/Orders/Orders.cs` (`PlannedAttackOrder`)
  - `src/Nmf.Sim/Events/SimEvents.cs` (`PlannedAttackPhaseChanged`, `PlannedAttackHalted`, `PlannedAttackEnded`)
  - `src/Nmf.Sim/Units/Unit.cs` (`PlannedAttackId`, internal)
  - `src/Nmf.Sim/Simulation.cs` (apply the order; run the attacks on the brain tick after `AttackPlanner`; every other order calls a new `LeavePlannedAttack(unit)` where it now calls `LeaveAttack`)
  - `src/Nmf.Sim/AI/SoldierBrain.cs` (members skip `ReactToFire`, the idle crouch/kneel adjustments and target choice the same way `AttackGroupId` members do: treat `PlannedAttackId != null` like `AttackGroupId != null`)
  - `src/Nmf.Sim/Core/StateHash.cs`
- Test: `src/Nmf.Sim.Tests/Tactics/PlannedAttackTests.cs`

**Interfaces:**
- Consumes: `TacticPlan`, `TacticDef` (the order carries the def too, so the sim needs no tactic registry), `TacticPlanner.PositionOf`, `Simulation.StartAssault`, `SoldierBrain.TakeFiringStance`, `Movement.StartPath`, `SoldierBrain.ChoosePace`.
- Produces:

```csharp
public sealed record PlannedAttackOrder(UnitId Unit, TacticPlan Plan, TacticDef Tactic) : Order(Unit);
public enum PlannedPhase : byte { Deploy, Suppress, Assault }
public enum HaltReason : byte { Casualties, LeaderDown, StrikePinned, NotSuppressed, NoFire, TargetMoved }
public sealed record PlannedAttackPhaseChanged(long Tick, Side Side, int Id, PlannedPhase Phase) : SimEvent(Tick);
public sealed record PlannedAttackHalted(long Tick, Side Side, int Id, HaltReason Reason, int Casualties) : SimEvent(Tick);
public sealed record PlannedAttackEnded(long Tick, Side Side, int Id, bool Success) : SimEvent(Tick);
public sealed class PlannedAttack   // public read-only view: Id, Side, TacticId, Phase, Members, FireTeam, StrikeTeam, Position, Ended
// Simulation: public IReadOnlyList<PlannedAttack> PlannedAttacks
```

- [ ] **Step 1: Failing tests** (`PlannedAttackTests`; open test maps; plans made with `TacticPlanner.Propose` on the test field, or hand-built `TacticPlan`s where geometry must be exact):
  - `Order_StartsBothTeamsAtOnce` (fire team moving to its spots, strike team moving along the route, phase Deploy)
  - `FireTeamSet_GoesToSuppress_AndFires`
  - `CoveringMan_WhoCannotSeeTheTarget_AreaFiresAtHim_EvenIfTheSideSeesHim`
  - `FireIsSharedAcrossThePosition_LmgOnTheLmg`
  - `Assault_WaitsUntilEverySeenManOfThePositionIsSuppressed` (two reds in adjacent pits)
  - `Assault_AfterWait_GoesWhenHalfSuppressed`
  - `PositionTaken_EndsWithSuccess_NoNewTarget` (a red 40 m beyond stays untouched)
  - `Casualties_Halt_FireTeamKeepsAreaFire`
  - `LeaderDown_Halts`
  - `StrikeTeamPinned_Halts`
  - `NotSuppressedByGiveUp_Halts`
  - `NoFireAtAll_HaltsWithNoFire`
  - `PositionMovingAway_HaltsWithTargetMoved`
  - `ForestContactAt26m_FireTeamFallsBackAndActuallyFires` (the strike team must not block the line)
  - `PinnedManInThePlan_IsLeftOut_OthersGo`
  - `AnotherOrder_TakesAManOut`
  - `LongFire_NeverAssaults`
  - `Replay_ReproducesThePlannedAttack` (`Replay.Run` → same `StateHash`).
- [ ] **Step 2: Run.** Expected: build fails.
- [ ] **Step 3: Implement** the spec's "Toteutus" section:
  - **Apply:** reject when the target is out of action, or when no man of the plan can take part. Otherwise create a `PlannedAttack(id, side, plan, tactic, tick)`. Every eligible man: the same clean-up as `AttackOrder` (`LeaveAttack`, `LeavePlannedAttack`, area target null, loot abandon, forget cover, assault target null, stance ordered false, clear path), then `PlannedAttackId = id`, and the fire team gets `OrderedTarget = plan.Target`. Leader = the member with `IsLeader` and the highest `LeaderQualityPct`, else the first. Strength = members count. Orders for the same plan in the same tick join one attack. The plan's first unit carries it (one order per plan).
  - **Each brain tick (5 ticks):**
    1. Drop members who are out of action, broken, or have `PlannedAttackId != id`, counting the out-of-action ones as casualties when their wound ≥ Serious. Not here but in the casualty check: wounded men who still fight count when their wound ≥ Serious.
    2. Re-gather the position = `PositionOf(sim, side, centre)` plus the remaining original position men still known. The centre = mean of their known places. If the position is empty (all down or no longer known) → end, Success = any position man is out of action.
    3. Halts in this order: leader down, casualties, target moved (centre drift > 2000 cm from `Aim`), strike pinned (≥ half the upright strike men pinned for `AbortPinnedTicks` continuously), no fire (Suppress, 20 s with no `ShotFired` by the fire team: track `LastFireTeamShotTick` from members' `LastShotTick`), not suppressed (Assault phase older than `GiveUpTicks` without going in).
    4. **Phase Deploy:** a fire man not yet sent gets `Movement.StartPath(man, spot, ChoosePace, path)` with `AutoPace = true`; on arrival `TakeFiringStance(man, aim)`. A strike man not yet sent follows `Route` + his form-up spot (path = route points after the nearest one + spot; `FindPath` from his position to the first route point he has not passed, joined). Deploy → Suppress when every upright fire man has arrived and his stance change is done, or after 1200 ticks.
    5. **Fire distribution (Suppress and Assault):** the position men sorted (LMG first, then distance to the fire base centre, then id), dealt round-robin to fire men (LMG man first). Each fire man: if he can see his man (`Firing.CanSee`) → `OrderedTarget` = him, `AreaTarget` = null; else `AreaTarget` = that man's known place (when he has spare magazines and is not on HoldFire).
    6. **Suppress → Assault** when fire has gone on ≥ 100 ticks (first fire-team shot since Suppress began) and every upright strike man is at his form-up spot (≤ 150 cm). Long fire never leaves Suppress.
    7. **Assault:** go when every seen position man is pinned, has suppression ≥ `Assault.Suppression × ExperienceTimePct(avg strike exp) / 100`, or is out of action; or when ≥ `WaitTicks` have passed in Assault and every seen one has ≥ half of that. Going in: each upright strike man `StartAssault(man, nearest position man, his known place)`, `PlannedAttackId` stays (so he counts until the end); the phase flag `AssaultLaunched` is set; the fire team keeps firing (the existing friend checks hold its fire near our men).
    8. Phase changes emit `PlannedAttackPhaseChanged`.
  - **Halt:** emit `PlannedAttackHalted`. Strike men: `PlannedAttackId = null`, `AreaTarget = null` (normal brain). Fire men: `PlannedAttackId = null`, `AreaTarget` = the position centre if in range and they have magazines (a standing area order, as `AreaFireOrder` leaves it). Mark ended.
  - **End:** emit `PlannedAttackEnded`; everyone `PlannedAttackId = null`, `AreaTarget = null`, `TakeFiringStance` toward the centre for the idle ones.
  - **StateHash:** for each attack in order: id, phase, ticks (`PhaseTick`, `PinnedSince`, `LastFireTick`), member ids, casualties, position ids, ended; per unit `PlannedAttackId ?? -1`.
- [ ] **Step 4: Run** `dotnet test NoMansForest.slnx`. Expected: PASS, including the old attack and determinism tests.
- [ ] **Step 5: Commit** — "Planned attacks run: deploy, suppress the whole position, assault only a suppressed one, halt on losses, no fire or a moving target".

---

### Task 7: The double click, toasts and loading

**Files:**
- Modify:
  - `src/Nmf.Client/GameSession.cs` (`Tactics` property; `OrderAttack` plans)
  - `src/Nmf.Client/Mission/Alerts.cs` (tactic texts)
  - `src/Nmf.Game/Hud.cs` (`OnEvents` toasts for the three events, own side only)
  - `src/Nmf.Game/GameRoot.cs` (load `content/core/tactics`, set `session.Tactics`; after an `AttackOrdered` click, toast the plan summary)
- Test: `src/Nmf.Client.Tests/GameSessionTests.cs`, `src/Nmf.Client.Tests/AlertsTests.cs` (new if none exists)

**Interfaces:**
- Produces:
  - `GameSession.Tactics : IReadOnlyList<TacticDef>` (empty by default).
  - `GameSession.LastPlan : TacticPlan?`
  - `GameSession.OrderAttack(UnitId target)` behaves as follows:
    - no tactics → the legacy `AttackOrder` per man (old tests stay green)
    - a plan → one `PlannedAttackOrder`, and `LastPlan` is set
    - no plan → `OrderFireAt` per man, and `LastPlan` = null with `LastPlanFailure` = "no plan".
  - `Alerts.PlanSummary(TacticPlan, TacticDef, lang)`, `Alerts.Halted(PlannedAttackHalted, lang)`, `Alerts.Phase(PlannedAttackPhaseChanged, lang)` (only Assault: "Isku lähtee" / "Assault!"), `Alerts.Ended(PlannedAttackEnded, lang)` ("Asema vallattu" / "Position taken").
- [ ] **Step 1: Failing tests:**
  - `DoubleClickOnSeenEnemy_WithTactics_SendsOnePlannedAttack`
  - `WithoutTactics_SendsTheOldAttack`
  - `NoPlanPossible_FiresInstead_AndSaysWhy`
  - Alerts:
    - `PlanSummary_SaysTacticDistanceAndSide` ("Tuli ja liike: tuliasema 72 m, koukkaus vasemmalta")
    - `Halted_SaysWhyAndHowMany` ("Hyökkäys pysähtyi: 2 haavoittunutta" for casualties; one text per reason, fi and en)
    - `LongFire_Summary_HasNoFlank`.
- [ ] **Step 2: Run.** Expected: build fails.
- [ ] **Step 3: Implement.** In `GameRoot`, after loading weapons: `session.Tactics = TacticLoader.LoadDirectory(Path.Combine(contentRoot, "core", "tactics"))` inside the same try. After a click with `ClickResult.AttackOrdered`: show `Alerts.PlanSummary` when `LastPlan` is set, else the failure text.
- [ ] **Step 4: Run** `dotnet test NoMansForest.slnx` and `dotnet build src/Nmf.Game/Nmf.Game.sln`. Expected: PASS.
- [ ] **Step 5: Run the game** on `--mission=testikentta`, double-click a Soviet in a foxhole, and watch for the toast, the fire team moving back and the strike team moving round. Use a `--screenshot-at` shot to look at it: `tools/run_game.sh -- --mission=testikentta --screenshot-at=20 --screenshot=<scratch>/6a.png` cannot double-click, so add `--demo=tactic` that does the double click programmatically on the first visible red at 2 s (in `GameRoot`, beside `--demo=attack`).
- [ ] **Step 6: Commit** — "Double click plans the attack; toasts for the plan, the assault, halts and a taken position; --demo=tactic".

---

### Task 8: Probe the tactics and record

**Files:**
- Modify: `src/Nmf.Cli/AttackProbe.cs` (tactic ids, `suggest`, halts by reason), `docs/superpowers/specs/2026-10-10-tactics-design.md` (results), `README.md` (controls: double click = planned attack)

- [ ] **Step 1:** Wire the probe's attack hook. A tactic id means `TacticPlanner.Propose` with that def; `suggest` means `Suggest` with all. Then a `PlannedAttackOrder`. Count halts by reason from `PlannedAttackHalted` events.
- [ ] **Step 2: Run** `probe-attack --tactic fire_and_movement`, `--tactic long_fire` and `--tactic suggest`, 8 seeds, 8 min.
- [ ] **Step 3: Compare with the baseline** against the spec's 6a target. Record all three in "## Mittaukset" with a short reading: what the men did, the main halt reasons, whether the target was met. If the target is missed, look at one losing seed with `--demo=tactic`-like reasoning (halt reasons, phase times). Record the cause and leave tuning to the user's call; no blind number changes.
- [ ] **Step 4:** README controls: "double click an enemy: the men plan and make an attack on his position (fire base + flank); Shift+double click: straight assault", in EN and FI.
- [ ] **Step 5: Commit** — "Probe results for the tactics; README controls".

---

### Task 9: Finish

- [ ] Full `dotnet test NoMansForest.slnx` and the Python suites.
- [ ] Final review of the branch (superpowers:requesting-code-review, one reviewer on the most capable model) with the Review Focus list. Fix Critical/Important with failing tests first; minors go to the ledger and memory.
- [ ] Merge `--no-ff` to main, push, update the project memory.
