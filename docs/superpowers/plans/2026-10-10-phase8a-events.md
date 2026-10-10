# Phase 8a: events, triggers, find and rescue objectives — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-8a-events`, merge `--no-ff` to main and push.

**Goal:** A mission can script "when … then …" events, place search targets and zones in its own file, hide and reveal objectives, and ask the player to find a place and bring a stranded man home.

**Spec:** `docs/superpowers/specs/2026-10-10-events-design.md`

## Global Constraints
- Deterministic: the event script acts only through orders, the stranded flag and the tracker. The order log replays it.
- Existing missions load and play unchanged. Every new field is optional.
- The guide never shows an unfound `find` place.
- Every load error names the mission, the event or objective, and the bad name.

## Review Focus
1. **An event whose actions are still waiting when the mission ends or the session reloads:** no exception, nothing more happens. Test: `MissionEventsTests.MissionOver_PendingActionsDropped`.
2. **`send` to a squad whose men are all down:** nothing happens and nothing crashes (`Send_AllDown_Nothing`).
3. **The stranded man before he is found:** he is not commandable, not on the cards, and gives nobody away. He holds his fire (`Stranded_HoldsFire_UntilFound`).
4. **The rescue man killed:** the mission fails at once, even if other objectives are done (`Rescue_ManDead_Fails`).
5. **A hidden objective never revealed** does not block success (`HiddenObjective_NotRevealed_DoesNotBlock`).

---

### Task 1: Spec model and loader
- Sim (`MissionSpec.cs`):
  - `MissionSpec` gets `Places` (name → Vec2 cm), `MissionZones` (`MapZone`s) and `Events` (`EventSpec`).
  - `ObjectiveType.Find` and `ObjectiveType.Rescue`. `ObjectiveSpec` gets `Place`, `RadiusCm`, `Unit` (a name), `Area` and `Hidden`.
  - `SoldierSpec.Stranded`.
  - `EventSpec(Id, Condition When, IReadOnlyList<EventAction> Do)`.
  - `EventCondition(AfterTicks?, Objective?, Event?, ContactBy?, ZoneName?, ZoneSide?, LossesSide?, LossesAtLeast?)`.
  - `EventAction(Kind, …)`, where Kind is Wait, Message, Send, Release, Reveal, End.
- Loader:
  - parses all of these
  - validates places, zones, squads, objectives, events, units and ids, and rejects unknown keys in `when` and `do`
- Tests: `MissionLoaderTests`, one per new feature plus the errors.

### Task 2: Zones and tracker
- `MissionSpec.ZoneNamed(GridMap, name)`: mission zones first, then the map's.
- Tracker:
  - `Find` latches.
  - `Rescue` fails the mission when the man is dead.
  - Hidden objectives are skipped until `Reveal(id)`, which raises `ObjectiveRevealed`.
  - The result counts only visible objectives.
- Tests (`MissionTrackerTests`):
  - `Find_WithinRadius_Latches`
  - `Rescue_AliveInZone_Done`
  - `Rescue_ManDead_Fails`
  - `HiddenObjective_NotRevealed_DoesNotBlock`
  - `Reveal_ThenItCounts`

### Task 3: Event runner and stranded men
- `Unit.Stranded` (internal set, hashed). `MissionScenario` sets it from the spec and gives him HoldFire.
- `MissionEvents` (Nmf.Sim.Mission):
  - ticked by `Scenario.Tick`; reads the tracker for `objective` conditions (the tracker is passed in, or `Scenario` owns it)
  - evaluates each condition every 10 ticks; actions run in order with waits
  - `MissionMessage` and `MissionEnded` go out as events the scenario returns, alongside the step's events
  - release adds the men to the `EnemyCommander` (`Adopt`)
- Stranded: found when a non-stranded fit player man is within 5 m. Then the flag is cleared and `StrandedFound` is raised.
- Tests (`MissionEventsTests`):
  - one per condition
  - `Wait_DelaysTheRest`
  - `Send_OrdersTheSquad`
  - `Send_AllDown_Nothing`
  - `Release_TheCommanderUsesThem`
  - `Reveal_ShowsTheObjective`
  - `End_EndsTheMission`
  - `FiresOnce`
  - `MissionOver_PendingActionsDropped`
  - `Stranded_JoinsWhenReached`
  - `Stranded_HoldsFire_UntilFound`

### Task 4: Client
- `GameSession`:
  - takes the scenario's events into its event stream
  - `CommandedIds` and `Selection` skip stranded men
  - `OwnUnits` for the cards skip them
- Hud:
  - `MissionMessage` toast (8 s)
  - orders paper (B) message log
  - `StrandedFound` toast
  - objectives list shows only visible objectives
- `ObjectiveGuide`: `Find` points to the area (or nothing), `Rescue` points to the man once found, then to the zone.
- Tests:
  - `GameSessionTests.Stranded_NotCommanded`
  - `ObjectiveGuideTests.Find_ShowsAreaNotPlace`
  - `AlertsTests` for message and found

### Task 5: Content and finish
- Debug mission `tapahtumakoe` (karhumaki): places, zones, a stranded pilot, a reserve, events and briefings (en and fi).
- Content test that it loads and its names resolve.
- Screenshots, README (mission format), final review, merge, push, memory.
