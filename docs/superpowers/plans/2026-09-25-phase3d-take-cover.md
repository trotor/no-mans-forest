# Phase 3d: take cover under fire — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans). TDD: write the named tests, watch them fail, implement, run `dotnet test NoMansForest.slnx`.

**Goal:** When the enemy opens fire, soldiers without much nerve run to the nearest cover facing the threat, or drop prone. Tough ones (Nerve ≥ 75) hold on.

**Architecture:**
- `MoraleSystem.AddSuppression` gains an optional threat position and flags a pending reaction on the first fire after 10 s of quiet.
- `SoldierBrain.ReactToFire` handles the reaction.
- `World/CoverFinder.Find` picks the cover cell.

**Tech Stack:** .NET 10 / C#, xUnit, Godot 4.7 .NET.

**Spec:** `docs/superpowers/specs/2026-09-25-take-cover-design.md`

## Global Constraints
- Deterministic sim: integer math, id order, every new field in `StateHash`.
- Constants:
  - `ToughNerve = 75`
  - `FireQuietTicks = 200`
  - `CoverSearchCm = 800`
  - `CoverDistancePenaltyPerM = 20`
  - `CoverObstacleMinCm = 50`
  - `CoverOccupiedCm = 100`

## Review Focus
1. **The player re-orders a man under continuous fire.** He must obey; no new reaction until 10 s of quiet.
2. **A man already behind cover facing the threat.** He must not run off to a "better" cell elsewhere.
3. **Several men choose cover at the same time.** They must not all pick the same cell (occupied check, in id order).
4. **A grenade blast gives the threat position.** The man must not run toward the grenade.
5. **Red defenders (idle).** They react too, and their fire must keep working afterwards (the brain still targets from cover).

---

### Task 1: CoverFinder
**Files:**
- Create: `src/Nmf.Sim/World/CoverFinder.cs`
- Test: `src/Nmf.Sim.Tests/World/CoverFinderTests.cs`

**Interfaces:**
- Produces:
  - `CoverFinder.Find(Simulation sim, Unit unit, Vec2? threat) → Vec2?` (a cell centre)
  - `CoverFinder.CoveredAt(GridMap map, CellCoord cell, Vec2? threat) → int` (the best facing cover, 0 if none)

**Tests:**
- `RockBetweenHimAndTheThreat_CellBehindItChosen`
- `CellOnTheThreatSideOfTheRock_NotChosen`
- `OpenField_Null`
- `CoverFartherThanEightMetres_Null`
- `CellTakenByAFriend_Skipped`
- `NoThreat_AnyCoveredCell`
- `AlreadyCovered_CoveredAtPositive`

### Task 2: Nerve and the reaction
**Files:**
- Modify: `Unit.cs` (`Nerve`, `CoverReactionPending`, `CoverThreat`, `TakingCover`)
- Modify: `MoraleSystem.AddSuppression(..., Vec2? threat = null)`
- Modify: `Firing` and `Damage.ApplyHit` (pass the shooter's position), `GrenadeSystem` (pass the landing point)
- Modify: `SoldierBrain` (`ReactToFire`; clear `TakingCover` on arrival and crouch)
- Modify: `Simulation.Apply` (orders clear `TakingCover` and the pending reaction)
- Modify: `StateHash`, `CombatRules`

**Tests** (`TakeCoverTests`):
- `ShotAt_RunsToCoverBehindTheRock`
- `ShotAtInTheOpen_GoesProne`
- `ToughMan_StaysPut`
- `AlreadyBehindCover_CrouchesInPlace`
- `SecondShotSoonAfter_NoNewReaction_AndOrdersAreObeyed`
- `AutoMover_StopsForCover_ToughOneKeepsGoing`
- `AssaultAndRunOrder_DoNotReact`
- `StanceOrdered_DoesNotReact`
- `GrenadeThreat_DoesNotRunTowardTheBlast`
- `TwoMenSameMoment_DifferentCells`

### Task 3: Scenario and client
**Files:**
- Modify: `SkirmishScenario` (nerve per index)
- Modify: `UnitStatus` ("Taking cover")
- Modify: Hud help, README

**Tests:**
- `SkirmishScenarioTests.Nerve_PerSideAndIndex`
- `UnitStatusTests.TakingCover`
- `SkirmishFightTests.FightWithCoverReactions_IsDeterministic` (the existing 3-minute fight covers it; assert that at least one `TakingCover` happens)
