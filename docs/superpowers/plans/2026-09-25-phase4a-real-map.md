# Phase 4a: 1 km real-terrain map — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans). TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx` (plus `python3 -m unittest discover -s tools/mapgen/tests -t .` for the generator).

**Goal:** A 1000 × 1000 m skirmish map generated from OSM and ASTER data (Karhumäki, the Lake Onega shore), and an engine that stays smooth on it.

**Architecture:**
- The Python `tools/mapgen` fetches data into a committed cache, then generates `content/core/maps/karhumaki.tmx` (base64+zlib) and `heights_fine.tsx`.
- The C# side gains a TMX base64/zlib reader, pooled A* buffers, a water terrain and byte-buffer Godot textures.
- Decorations are drawn in chunks.

**Tech Stack:** Python 3 + Pillow + numpy, .NET 10, Godot 4.7 .NET.

**Spec:** `docs/superpowers/specs/2026-09-25-real-map-design.md`

## Global Constraints
- Deterministic generator (fixed seed) that works offline from the cached data.
- The map carries a `source` property with its attribution: OSM ODbL and ASTER GDEM (NASA/METI).
- Cells are 1 m; ground height is stored in `short` cm (relative to the lake level).
- The old `skirmish.tmx` stays as the test reference map.

## Review Focus
1. **Water.** It must never be walkable. Paths, cover finding, spawn points and patrols must avoid it.
2. **A* buffer reuse across maps of different sizes, and nested calls.** No stale results.
3. **A long path across the 1 km map.** The node budget must still reach, or fail cleanly.
4. **Fog, ground and decoration drawing on the 1 km map.** No per-cell Godot calls per frame.
5. **Attribution.** It must be present in the README and in the map file.

---

### Task 1: TMX base64 + zlib/gzip
- `TmxMapLoader.ReadLayerData` accepts `encoding="base64"` with `compression` absent, `zlib` or `gzip`. The data is little-endian uint32 gids.
- Tests (`TmxMapLoaderTests`): `Base64Uncompressed_Loads`, `Base64Zlib_Loads`, `Base64Gzip_Loads`, `UnknownCompression_Throws`, `Base64WrongLength_Throws`.

### Task 2: Pooled A*
- `Pathfinder` keeps thread-static buffers (`cost`, `parent`, a generation stamp per cell) grown to the map size. It resets through the generation counter.
- Tests (`PathfinderTests`): `RepeatedCalls_SameResult`, `AlternatingMapSizes_NoStaleState`, `KilometreMap_LongPath_IsFast` (20 cross-map paths on 1000×1000 within 2 s).

### Task 3: Water terrain
- `terrain.tsx` gets tile 4 `water`: impassable, concealment 0, cover 0.
- The art generator gains a `water` texture. `TerrainArt` gets slot 4. The shader gets `tex_water`.
- Tests:
  - Python art tests: the texture exists and is seamless
  - `TerrainArtTests.Water_HasItsOwnSlot`
  - `TmxMapLoaderTests.WaterTile_IsImpassableAndSeeThrough`

### Task 4: Godot at scale
- Ground and fog use byte buffers (`Image.CreateFromData` / `SetData`). A height-scale uniform comes from the map's maximum height.
- `DecorationView` draws 32×32-cell chunks as Node2D `_Draw`. Canopy chunks redraw only while their fade changes.
- Verify: build, headless smoke, screenshot.

### Task 5: tools/mapgen
- `fetch.py`: Overpass (unclipped rings; Lake Onega shoreline as member ways) and OpenTopoData ASTER, written to `tools/mapgen/data/<name>/`.
- `generate.py` builds the rasters, applies the 1942 changes, and writes the TMX, the tilesets and a preview PNG.
- Tests (`tools/mapgen/tests`):
  - `test_projection_corners`
  - `test_terrain_priority`
  - `test_lake_fill_from_corner`
  - `test_height_zero_at_lake`
  - `test_heights_tileset_steps`
  - `test_deterministic`
  - `test_spawns_passable`
  - `test_tmx_roundtrip_structure`

### Task 6: The map in the game
- Generate `karhumaki.tmx`.
- `GameRoot` takes `--map=<name>` (default `karhumaki`).
- README gets a credits section.
- Tests (`KarhumakiMapTests`):
  - `Loads_1000x1000_WithSpawns`
  - `SpawnsPassable_AndFinnsCanReachTheSoviets`
  - `TwoMinuteFight_IsDeterministic_AndFast` (< 20 s for 2 runs)
