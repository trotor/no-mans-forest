# Phase 4c: missions, orders paper, map view, soldier attributes — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans). TDD: named tests first, watch them fail, implement, then run `dotnet test NoMansForest.slnx` and `python3 -m unittest discover -s tools/mapgen/tests -t .`.

**Goal:** Play "Iskuosasto" as a mission. It has orders on paper (always available), an openable paper map with markers, tracked objectives with a result, and hero Finns defined with per-man attributes.

**Spec:** `docs/superpowers/specs/2026-09-26-missions-design.md`

## Global Constraints
- Deterministic sim. New unit state (BaseMorale, Marksmanship, Name) goes into StateHash; objective tracking is a pure function of sim state.
- `Nmf.Sim` has no dependencies. YAML loading lives in `Nmf.Content`, and mission data records live in `Nmf.Sim/Mission`.
- Texts are localized `{ en, fi }`, with English required. The UI stays English.

## Review Focus
1. **The papers' carrier falls after `pick_up` is done:** the objective reverts, and the papers can be taken again from his body.
2. **Mission result is final:** a later change of state does not flip it.
3. **Roster larger than the map's spawn points, or a bad zone name:** a clear load error.
4. **The paper map and markers on the 1 km map:** rendered once, markers cheap, fog/knowledge respected (no unseen enemies on the map).
5. **The orders paper opens at start with the game paused, and closing it resumes play.**

---

### Task 1: Soldier attributes (sim)
- `Unit.BaseMorale`, `Unit.Marksmanship` (0–100, default 50) and `Unit.Name` (string?).
- `MoraleSystem` recovers toward `unit.BaseMorale`; a promoted successor gets max(own base, LeaderMorale).
- Ballistics spread × (150 − marksmanship)/100.
- `StateHash` covers the new fields.
- Tests:
  - `BallisticsTests.Marksmanship_ScalesTheSpread`
  - `MoraleTests.Recovery_StopsAtTheMansOwnBase`
  - `MoraleTests.Successor_KeepsAHigherOwnBase`

### Task 2: Mission records and tracker (sim)
- `Nmf.Sim/Mission`:
  - `Localized(en, fi)`
  - `SoldierSpec(Name, Weapon, Grenade, Leader, Nerve, Morale, Marksmanship, Leadership, Items)`
  - `ObjectiveSpec(Id, Type, Item, Zone, Carrying, Requires, Text)`
  - `MissionSpec(Id, Title, Date, Map, BriefingEn, BriefingFi, Player, Enemy, Objectives, ItemNames)`
- `MissionScenario.Create(map, spec, weapons, grenades, seed) → Scenario`
- `MissionTracker(spec, map).Update(sim) → events`; `ObjectiveChanged`, `MissionEnded`
- Tests (`MissionScenarioTests`, `MissionTrackerTests`):
  - roster applied in point order
  - items given
  - too few points throws
  - pick_up done / reverts when the carrier falls
  - reach_zone needs requires and carrying
  - success
  - failure when all down
  - result final
  - deterministic

### Task 3: Mission loader (content) and Iskuosasto
- `Nmf.Content/Missions/MissionLoader.Load(dir) → MissionSpec`, which reads the briefings.
- `content/core/missions/iskuosasto/` holds `mission.yaml` and the en/fi briefings.
- Tests (`MissionLoaderTests`):
  - reads all fields
  - errors: unknown type, missing en text, requires cycle, attribute out of range
  - `Iskuosasto_LoadsAndMatchesItsMap`: zones exist, enough points, weapons known

### Task 4: Map zones (generator)
- The generator writes `start_zone` (40×30 m around the Finns) and `outpost` (70×50 m, centred 10 m off the real position). The map is regenerated.
- Tests: Python `test_zones`; C# `KarhumakiMapTests.HasStartAndOutpostZones`.

### Task 5: Client
- `PaperMap.Render(map, metresPerPixel) → (w, h, rgba)`
- `MarkdownLite.ToBbcode`
- `MissionPaper.Objectives(tracker, lang)`
- `GameSession` owns the mission and tracker, and emits mission events.
- `UnitNames` uses `Unit.Name`.
- Tests: `PaperMapTests`, `MarkdownLiteTests`, `MissionPaperTests`, `GameSessionMissionTests`.

### Task 6: Godot
- `OrdersView` (B) and `MapView` (M, click centres the camera).
- HUD buttons, the objective count, toasts and the end panel.
- `--mission=` and `--lang=`; the orders open paused at start.
- Verify: build, smoke run, screenshots. README update.
