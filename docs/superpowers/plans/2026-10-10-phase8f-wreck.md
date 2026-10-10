# Phase 8f: the wreck mission — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-8f-wreck`, merge `--no-ff` to main and push.

**Goal:** A playable winter mission on its own real map: find a downed pilot behind the Soviet line, pull him back on an ahkio, and race a search party; tuned with a headless rescue probe.

**Spec:** `docs/superpowers/specs/2026-10-10-wreck-mission-design.md`

## Global Constraints
- **Content only, plus a CLI probe.** The simulation does not change.
- **Unchanged gates:**
  - The `probe-attack` output for iskuosasto stays byte-identical (`gate.sh`).
  - `mapgate.py` regenerates karhumaki byte-identical.
- **The povenets map:**
  - It is generated from the cached data in `tools/mapgen/data/povenets` and committed.
  - Running `generate povenets` twice gives the same file.

## Review Focus
1. **Every mission place, zone and man** is on passable ground (`CoreContentTests` covers it).
2. **The guide arrow does not point at the wreck** before it is found (8a behaviour, find with an area): check this in the screenshot.
3. **The probe:**
   - It ends every run: success, failure or timeout.
   - It never orders the stranded pilot.
4. **Balance** within the spec's targets, or the gap recorded with numbers.

---

### Task 1: The povenets map
- Add the area to `areas.py` (done), fetch (done) and generate.
- Commit the data and the tmx.
- Python test: `RealAreaTests`-style determinism for povenets (build twice, equal).

### Task 2: The hylky mission
- `content/core/missions/hylky/`: `mission.yaml` and the briefings in fi and en.
- `MissionLoaderTests`: Hylky_Loads (winter, dusk, phone, search, rescue).
- The suite passes, including `CoreContentTests` over all missions.

### Task 3: probe-rescue and balance
- `Nmf.Cli` `RescueProbe.Run(content, mission, seeds, minutes)` and the `probe-rescue` command.
- A test that one seed runs to an end with a result line.
- Tune the mission numbers to the targets: enemy counts, search delay, snow, the post's distance. Record the measurements in the spec.

### Task 4: Finish
- A screenshot, README (missions list), final review, merge, push, memory.
