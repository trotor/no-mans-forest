# Phase 8b: carrying the wounded — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-8b-carrying`, merge `--no-ff` to main and push.

**Goal:** A man who cannot move can be carried by one man (55 % speed), or by two (75 %). The carriers do not fire, and they drop him when they need their hands, fall, are pinned or break.

**Spec:** `docs/superpowers/specs/2026-10-10-carrying-design.md`

## Global Constraints
- Deterministic and in the order log: `CarryOrder` and `DropOrder` are orders. The carried man's position follows the carrier inside `Simulation.Step`.
- A carried man is never shot at as a separate target, and he takes the carrier's hits only by chance of fire at that place, as anyone near would.
- Existing loot behaviour is unchanged for the dead and for enemies.

## Review Focus
1. **The carried man killed while carried** (fire at the carrier's spot): the carrier keeps going with a body? No — he drops him (`Carried_Dies_Dropped`).
2. **The carrier given a move into a place the carried man couldn't be** (water edge): the carried man is always at the carrier's cell (`Carried_FollowsExactly`).
3. **Both carriers ordered elsewhere at once:** no duplicate carry and no ghost link (`TwoCarriers_OneOrderedAway_TheOtherCarriesOn`).
4. **Saving/hash:** the carry links are in `StateHash` (`StateHash_IncludesCarry`).
5. **The stranded pilot incapacitated:** found → carried → the rescue objective is done in the zone (`Rescue_CarriedHome`).

---

### Task 1: Sim carrying
- `Unit`: `CarriedBy`, `Carrying`, `HelpingCarry` (UnitId?), `CarryTicksLeft`; all hashed.
- `CarryOrder(Unit, Wounded)` and `DropOrder(Unit)`, plus `CombatAction.Lifting`.
- `CarrySystem` (Nmf.Sim.Combat or Units):
  - `Approach`, the same pattern as loot
  - `Update` after movement: the carried man's position is the carrier's; links are dropped on carrier down, pinned or broken, or on the carried man's death
  - the helper takes over
- Speed: `StanceRules.SpeedCmPerTick` × carry pct. Firing and grenades are gated in `SoldierBrain` and `Firing`. Orders that need hands (`FireAt`, `AreaFire`, `Assault`, `Loot`, throw) drop first.
- Tests (`CarryTests`), as the spec lists.

### Task 2: Tracker and mission
- Rescue counts the carried man's position, which already equals the carrier's.
- Test `Rescue_CarriedHome`.
- `tapahtumakoe`: the pilot becomes `incapacitated` (he must be carried).

### Task 3: Client and Godot
- `GameSession`:
  - `OrderCarry(UnitId wounded)`: the nearest commanded man, then a second if one is already carrying
  - `OrderDrop()`
  - `ClickTargetKind.Wounded` resolves on an own incapacitated alive man, and a double click carries
- `ClickPreview`: *tupla: kanna*. `ContextMenu` rows *Kanna* and *Laske maahan*.
- `UnitStatus` card lines. UnitView draws the carried man beside the carrier.
- `ControlsHelp`.
- Tests:
  - `GameSessionTests.DoubleOnOwnWounded_Carries`
  - `ClickPreviewTests`
  - `ContextMenuTests`
  - `UnitStatusTests`

### Task 4: Finish
- Screenshots, README, final review, merge, push, memory.
