# Phase 8e: winter, light and the map pipeline — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-8e-winter`, merge `--no-ff` to main and push.

**Goal:**
- A mission can be set in winter: deep snow slows men on foot, skis are fast and an ahkio pulls the wounded.
- Tracks are left in the snow, and the enemy notices and follows them.
- A mission can be set at dusk or at night, with flares lighting the dark.
- The map generator takes any area.

**Spec:** `docs/superpowers/specs/2026-10-10-winter-design.md`

## Global Constraints
- **Deterministic:**
  - Winter, tracks, light and flare light are simulation state, changed only inside `Step` or at scenario creation, and all of it is in `StateHash`.
  - Tracks are hashed in cell order (a sorted container).
- **Gated:**
  - Without `Simulation.Winter`, movement uses the exact old integer expression, no tracks are kept and nothing is allocated.
  - Without light, i.e. at day (100), no factor is applied.
- **Unchanged gates:**
  - `probe-attack --mission iskuosasto --seeds 24 --minutes 8`, with `--tactic suggest` and without, gives byte-identical output to the main baselines (scratchpad `probe-base-*.txt`, taken at 78e6ee1 and still valid at 57bf2e0 because 8d did not change it). Re-run after each sim task.
  - The scratchpad `mapgate.py` regenerates karhumaki byte-identical (tmx, heights tsx and png). Re-run after the mapgen task.
  - All existing tests pass.
- **Planned attack timeouts** (deploy, infiltrate, give up) scale with the winter foot factor, so that attacks do not halt for slowness.

## Review Focus
1. **Summer unchanged:** every winter, track and light path is gated (probe, `Summer_Unchanged`).
2. **Track hashing and cost:**
   - Hashing does not depend on dictionary order.
   - Noticing runs line of sight on the freshest candidate only, so 20 calm men on 1000×1000 stay cheap.
3. **Track following never ping-pongs:** the next cell must be strictly fresher than the one followed (`Search_FollowsATrail_ToItsEnd`).
4. **Flare light at the target:**
   - A man standing in the light does not see better into the dark.
   - The light expires after 20 s.
5. **Skis while sneaking or crawling and the ahkio in summer:** neither gives any benefit (tests).

---

### Task 1: Winter movement (sim)
- **Simulation:**
  - `SetWinter(int snowCm)`, `Winter` (`WinterState`: SnowCm, FootPct = min(250, 100 + snowCm × 5 / 4)), hashed.
- **Unit:**
  - `OnSkis` and `HasAhkio`, both hashed.
- **`Movement.FollowPath`:**
  - Without winter, the old expression.
  - With winter, the cost is the cell's `MoveCostPct` times a factor:
    - Foot: FootPct, or 100 on a road.
    - Skis (walk, run or auto, not prone or sneaking): 70, or 85 in forest.
- **`StanceRules`:** carry speeds with an ahkio in winter, 80/90 and helped 90/95.
- **`PlannedAttackRunner`:** the deploy, infiltrate and assault give-up timeouts are scaled by FootPct in winter.
- **Tests (`WinterTests`):**
  - Snow_SlowsAManOnFoot
  - Road_Trodden
  - Skis_Faster_NotSneaking
  - Ahkio_FasterThanCarrying_OnlyInWinter
  - Summer_Unchanged
  - SameSeed
- **Gates:** probe.

### Task 2: Tracks (sim)
- **`TrackSystem`:**
  - After movement in `Step`, in winter only.
  - Every cell a moving unit enters gets (side, tick) in `Simulation.Tracks`, a `SortedDictionary<int, Track>`, hashed.
- **Alertness:**
  - A calm man of an alertness-on side sees enemy tracks within 20 m that are under 30 min old.
  - Candidates are filtered by side, age and range; then line of sight to the freshest one only.
  - Result: Suspicious, with the alarm at that cell.
- **`SearchBehavior`:**
  - If a party man sees enemy tracks within 25 m that are fresher than the last one followed, go there; otherwise use the grid.
- **Tests (`TrackTests`):**
  - Tracks_OnlyInWinter
  - CalmSentry_NoticesFreshTracks
  - OldTracks_Ignored
  - Search_FollowsATrail_ToItsEnd
  - SameSeed
- **Gates:** probe.

### Task 3: Light (sim)
- **Light state:**
  - `Simulation.SetLight(LightLevel)` (Day, Dusk, Night).
  - `LightPct` (day 100, dusk 55/70 with snow, night 25/40), hashed.
- **Flare light:**
  - The flare list `(At, UntilTick)` is added in `AlertnessSystem.Flare`, kept 20 s and hashed.
  - `LightAt(Vec2)` gives 100 within 200 m of a burning flare.
- **`VisionSystem` gain:** multiplied by `LightAt(target)` when that is not 100.
- **Tests (`LightTests`):**
  - Dusk_SlowerSpotting
  - SnowBrightens
  - Flare_LightsTargetsWithin200m_For20s
  - LitObserver_SeesNoBetter
  - Day_Unchanged
- **Gates:** probe.

### Task 4: Mission loading
- **YAML:**
  - `winter: { snow_cm }` (1..150)
  - `light: day|dusk|night`
  - soldier `skis: true`, `ahkio: true`
- **Applied:** `MissionScenario` applies them.
- **Tests:** `MissionLoaderTests` Winter_Light_Skis_Read and BadValues; `MissionEventsTests` Mission_Winter_InTheSim.

### Task 5: Client, Godot and content
- **Client:**
  - `TrackView.Visible(session)`: own tracks always; enemy tracks where the fog is now visible.
  - `UnitStatus` card line "Sukset".
  - `LightOverlay.Alpha(light)`.
  - Footstep surface "snow" in winter (`AudioDirector`) and a `footstep.snow` cue.
- **Godot:**
  - The ground shader `snow` uniform blend; a snow texture (tools/art).
  - Winter decorations (no ground plants, frosted crowns).
  - Tracks drawn.
  - Ahkio drawn under the carrier.
  - Screen dimming with flare light.
- **Content:** `talvikoe` testbed.
- **Smoke run:** a throwaway headless check on talvikoe; planned attacks must not halt for slowness.
- **Docs:** help and README.
- **Tests:** `WinterClientTests`.

### Task 6: Map pipeline
- **`areas.py`** options:
  - optional lake
  - `open_share`, `seed`, `dem_points`
  - `forces: false`, which skips forces, foxholes and zones
- **Generator:**
  - `generate.py` and `fetch.py` require the area name.
  - The defaults reproduce karhumaki.
- **Tests:** an area without a lake or forces builds (synthetic data).
- **Gates:** `mapgate.py` byte-identical.
