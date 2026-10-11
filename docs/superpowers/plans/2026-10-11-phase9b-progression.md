# Phase 9b: growth and specialisations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Campaign men grow with their points (levels, skill growth) and can specialise as marksman, scout, sapper or medic, each with an effect in the sim (the medic bandages the badly wounded).

**Architecture:** `Specialty` lives on the sim `Unit` (set from `SoldierSpec.Specialty`, loaded from `specialty:` in mission.yaml and in the campaign roster); its effects are small hooks in Ballistics, VisionRules, GrenadeSystem, LootSystem, PhoneSystem and a new `FirstAidSystem` (order, brain approach, automatic choice, countdown). The campaign layer (Nmf.Client.Campaign) computes levels, grows the stats in `CampaignRecord`, lets the player specialise a man, and carries the specialty into the mission through `Apply`.

**Tech Stack:** C# / .NET 10, xUnit, Godot 4 mono (UI only).

**Spec:** docs/superpowers/specs/2026-10-11-campaign-design.md (§9b)

## Global Constraints

- Deterministic integer sim: no floats, no RNG in new choices; `StateHash` covers the new unit fields.
- New enum members are appended at the end (`CombatAction.FirstAid` after `Lifting`); `Specialty.None = 0`.
- Base missions (iskuosasto, hylky) get no specialties: `gate.sh` must stay PROBE-IDENTICAL, `probe-rescue --mission hylky --seeds 24 --minutes 30` 13/24.
- All skills capped at 100.
- Texts fi + en.

## Review Focus

1. A bandaged man hit again must bleed again (SetWound clears `Bandaged`).
2. The patient moves, is carried, dies, is captured: first aid follows or aborts; never progresses out of range.
3. Any other order (and going out of action) cancels first aid in one place; first aid is not queueable.
4. The automatic medic leaves men alone who have something to do (attack, move, loot, carry, assault, pinned/broken, under fire within 5 s); picks nearest then lowest id.
5. Growth crossing two levels in one mission applies twice; caps hold at 100; old saves without `Specialty` load as None.

---

### Task 1: Specialty on the unit, its loading and the marksman/scout/sapper effects

**Files:**
- Modify: `src/Nmf.Sim/Units/Unit.cs` (enum `Specialty`, `Unit.Specialty`)
- Modify: `src/Nmf.Sim/Mission/MissionSpec.cs` (`SoldierSpec.Specialty = Specialty.None`, trailing)
- Modify: `src/Nmf.Sim/Mission/MissionScenario.cs` (assign; sapper +2 grenades when he has some)
- Modify: `src/Nmf.Content/Missions/MissionLoader.cs` (`specialty: marksman|scout|sapper|medic`, unknown value → error)
- Modify: `src/Nmf.Sim/Core/StateHash.cs`
- Modify: `src/Nmf.Sim/Combat/Ballistics.cs` (marksman rifle spread ×70 %)
- Modify: `src/Nmf.Sim/Vision/VisionRules.cs` (`ObserverPct` +20 scout; moving `VisibilityPct` ×70 % floor 100; `NoiseRadiusCm(Unit)`)
- Modify: `src/Nmf.Sim/Vision/VisionSystem.cs`, `src/Nmf.Sim/AI/AlertnessSystem.cs` (use `NoiseRadiusCm(Unit)`)
- Modify: `src/Nmf.Sim/Combat/CombatRules.cs` (`MaxGrenadesFor(Unit)`, `SpecialtySpreadPct`, `SapperScatterPct`), `LootSystem.cs`, `GrenadeSystem.cs` (scatter), `src/Nmf.Sim/AI/PhoneSystem.cs` (`CutTicksFor(Unit)` 5 s for sapper)
- Test: `src/Nmf.Sim.Tests/Combat/SpecialtyTests.cs`, `src/Nmf.Content.Tests/MissionLoaderTests.cs`

**Interfaces:**
- Produces: `public enum Specialty : byte { None, Marksman, Scout, Sapper, Medic }` in `Nmf.Sim.Units`; `Unit.Specialty { get; internal set; }`; `SoldierSpec(..., Specialty Specialty = Specialty.None)`; `VisionRules.NoiseRadiusCm(Unit)`; `CombatRules.MaxGrenadesFor(Unit)`; `PhoneSystem.CutTicksFor(Unit)`.

- [ ] Step 1: failing tests — marksman spread smaller only with a rifle (same seed, many shots: mean |lateral| or a pure `CombatRules.SpecialtySpreadPct(unit)` = 70/100 by class); scout `ObserverPct` 120 at exp 50, `VisibilityPct` walking 140, sneaking 105, crawling 100 (floor), firing unchanged; `NoiseRadiusCm(scout walking)` 2100; sapper spawned with grenades has 4, without grenade type 0; `MaxGrenadesFor(sapper)` = 6; sapper looting tops up to 6; cut line by a sapper done in 100 ticks; loader reads `specialty: medic`, rejects `specialty: cook`.
- [ ] Step 2: run, watch fail (compile errors count as RED for new members).
- [ ] Step 3: implement.
- [ ] Step 4: `dotnet test` all green; gate.sh PROBE-IDENTICAL.
- [ ] Step 5: commit.

### Task 2: First aid

**Files:**
- Create: `src/Nmf.Sim/Combat/FirstAidSystem.cs`
- Modify: `Unit.cs` (`Bandaged`, `FirstAidTarget`), `CombatTypes.cs` (`CombatAction.FirstAid` appended), `Orders.cs` (`FirstAidOrder(UnitId Unit, UnitId Patient)`), `SimEvents.cs` (`UnitBandaged(Tick, Medic, Patient)`), `Damage.cs` (no bleed when bandaged; SetWound clears Bandaged, abandons first aid when out of action), `Simulation.cs` (order handling + abandon at the top for other orders; Update in Step after loot), `SoldierBrain.cs` (approach + automatic choice before loot), `StateHash.cs`, `CombatRules.cs` (`FirstAidTicks = 15 s`, `FirstAidRangeCm = 150`, `AutoFirstAidRangeCm = 2000`, `FirstAidQuietTicks = 5 s`)
- Test: `src/Nmf.Sim.Tests/Combat/FirstAidTests.cs`

**Interfaces:**
- Produces: `FirstAidOrder`, `UnitBandaged`, `Unit.Bandaged`, `Unit.FirstAidTarget`, `CombatAction.FirstAid`, `FirstAidSystem.CanTreat(Unit medic, Unit patient)`.

- [ ] Step 1: failing tests — order by a non-medic rejected; patient not Serious rejected; medic walks to a serious man 10 m off, bandages after 15 s (event, `Bandaged`), and the man does not go down after `SeriousBleedTicks`; unbandaged control does; self-aid; a new hit clears Bandaged; patient walks away → medic follows, no progress while out of range; patient carried / goes down → aborted; a move order to the medic aborts; idle medic picks the nearest serious man within 20 m himself (two at equal distance: lower id); not while under fire in the last 5 s, not in a planned attack, not with a move target; StateHash of two identical runs equal.
- [ ] Step 2: RED.
- [ ] Step 3: implement.
- [ ] Step 4: all green; gate.sh PROBE-IDENTICAL; hylky 13/24.
- [ ] Step 5: commit.

### Task 3: Campaign growth and specialising

**Files:**
- Modify: `src/Nmf.Content/Campaigns/CampaignLoader.cs` (`CampaignMan.Specialty`, roster `specialty:`)
- Modify: `src/Nmf.Client/Campaign/CampaignState.cs` (`CampaignManState.Specialty`, copied in New)
- Create: `src/Nmf.Client/Campaign/CampaignGrowth.cs` (`Level(xp)`, thresholds, `Grow`, `CanSpecialise`, `Specialise`)
- Modify: `CampaignOutcome.cs` (`Man.Led`; `ManReport` gets `LevelBefore`, `LevelAfter`, growth deltas), `CampaignMission.cs` (Apply copies Specialty)
- Test: `src/Nmf.Client.Tests/Campaign/CampaignGrowthTests.cs`, loader test

**Interfaces:**
- Produces: `CampaignGrowth.Level(int xp) → int (1..4)`, `CampaignGrowth.Thresholds`, `CampaignGrowth.CanSpecialise(CampaignManState)`, `CampaignState.Specialise(string name, Specialty s) → bool`; `ManReport(Name, Xp, Status, Hits, Kills, Wound, int LevelBefore, int LevelAfter)`.

- [ ] Step 1: failing tests — Level(0)=1, (24)=1, (25)=2, (70)=3, (140)=4; record from 22 to 80 xp adds Experience +10, Nerve +10; Marksmanship +min(4, hits/2); leader survivor +5 leadership, fallen leader none; caps at 100; Specialise only level≥2, not fallen, once; Apply puts the man's specialty on the slot (None clears a slot's); old JSON without Specialty → None; roster `specialty: medic` loads.
- [ ] Step 2: RED. Step 3: implement. Step 4: green. Step 5: commit.

### Task 4: Texts and the player's view

**Files:**
- Create: `src/Nmf.Client/SpecialtyText.cs` (names fi/en, one-line effects)
- Modify: `CampaignText.cs` (Lineup shows level name + specialty; Report shows level-up and "voi erikoistua"; roster lines for the specialise choice), `ContextMenu`/`ClickPreview`/`GameSession` (menu row "Sido haava", `GameSession.FirstAid`), card status text for FirstAid and bandaged, card tooltip specialty line
- Modify (Godot): `CampaignView.cs` (choose a specialty for eligible men), `Hud.cs` cards, `GameRoot.cs` wiring, `README.md`
- Test: `src/Nmf.Client.Tests/Campaign/CampaignTextTests.cs`, `SpecialtyTextTests`, context menu test

- [ ] Step 1: failing tests for text and menu; Step 2: RED; Step 3: implement; Step 4: green + screenshots of campaign view (`--campaign`) with a level-2 man; Step 5: commit.
