# Phase 8c: mortar support — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-8c-mortar`, merge `--no-ff` to main and push.

**Goal:** A mission can give the player an 81 mm mortar by radio: call (it can fail), a 60–120 s delay, ranging rounds corrected when someone sees them land, fire for effect, limited rounds. The shells can hit our own men.

**Spec:** `docs/superpowers/specs/2026-10-10-mortar-design.md`

## Global Constraints
- Deterministic: everything happens inside `Simulation.Step` with `sim.Rng`; `FireMissionOrder` is an order, and the support state is in `StateHash`.
- The damage function is the hand grenade's, `GrenadeSystem.Burst`, refactored and unchanged for grenades.
- No mission without `support` changes in any way.

## Review Focus
1. **The radioman falls mid-mission:** shells already in the air still land, and no new mission can be called (`RadioDown_NoNewMission_ShellsInFlightLand`).
2. **A target off the map, or out of the mortar's reach:** the order is rejected (`OffMap_Rejected`).
3. **Ammunition runs out during ranging:** the mission ends and says so (`OutOfRounds_Ends`).
4. **A new order while firing for effect:** rejected. While calling or waiting: it replaces the old one (`NewOrder_ReplacesOnlyBeforeFiring`).
5. **Ranging error never below what the dispersion gives:** no shell is placed by the true target position directly (no hidden aiming), only by target + error.

---

### Task 1: Sim
- `GrenadeSystem.Burst(sim, at, def, from, tick, events)`, extracted from `Explode`.
- `MortarSupport` (Nmf.Sim.Combat):
  - state, `Order(FireMissionOrder)`, and `Update` called each step after the grenades
  - shells in flight: impact tick and place
- `FireMissionOrder`, plus the events `FireMissionCalled`, `FireMissionFailed`, `FireMissionPhaseChanged`, `ShellIncoming`, `ShellExploded` and `FireMissionEnded`.
- `Simulation.MortarSupports`, and `StateHash`.
- Tests: `MortarTests`, as the spec lists.

### Task 2: Mission
- `MissionSpec.Support`: `MortarSupportSpec(He, DelayMinS, DelayMaxS, Radio)`. The loader validates it, and `MissionScenario` creates the support.
- iskuosasto content.
- Tests: `MissionLoaderTests.Support_Read` and `Support_RadioMustBeAPlayerMan`.

### Task 3: Client and Godot
- `GameSession.Mortar` (the player side's support), `OrderFireMission(point, rounds)`, and `MortarText.Status`.
- The `ContextMenu` row, the Hud status, toasts and a map marker. AudioView: `shell.incoming`, plus `grenade.explode` for the impact.
- Tests:
  - `ContextMenuTests.MortarRow`
  - `MortarTextTests`
  - `AlertsTests`

### Task 4: Finish
- Screenshot, README, final review, merge, push, memory.
