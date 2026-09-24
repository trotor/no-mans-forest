# Phase 2: Movement, Vision & First Playable Godot View – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Soldiers find paths across terrain, change stance, see and hear each other through a deterministic vision model with fog of war, and a first Godot 4 view lets a person play a small test skirmish on this Mac.

**Architecture:**
- `Nmf.Sim` gains four things:
  - A* pathfinding
  - stances and move modes, where actions take ticks
  - line of sight, spotting progress and hearing, stored per side in `SideKnowledge`
  - a viewshed for drawing fog
- A tiny patrol behaviour and a scenario builder turn map points into units.
- A new engine-free library `Nmf.Client` holds all testable presentation logic: selection, formations, palette, the game session loop and interpolation.
- The Godot project `Nmf.Game` is thin glue that draws the map, the fog and the units, and turns input into session calls.

**Tech Stack:** .NET SDK 10 (libraries `net8.0`, tests `net10.0`), xUnit, Godot 4.7.2 .NET (`Godot.NET.Sdk/4.7.2`, GL Compatibility renderer), Python 3 (stdlib only) for map generation, Tiled TMX.

**Spec:** `docs/superpowers/specs/2026-09-24-no-mans-forest-design.md`. This plan implements §13 phase 2 and uses §5.1 (actions take ticks), §6.1 (the player sees only what own units see), §7 (vision, sounds, fog) and §4 (determinism).

## Global Constraints

- **20 ticks per second.** Integer centimetres for all simulation positions; 1 m cells.
- **`Nmf.Sim` has no dependency on Godot, `Nmf.Content`, `Nmf.Client` or NuGet packages.**
- **Dependency direction:** `Nmf.Game` → `Nmf.Client` → `Nmf.Sim`; `Nmf.Game` → `Nmf.Content` → `Nmf.Sim`. `Nmf.Client` has no Godot dependency.
- **Determinism:**
  - no floating point in simulation state
  - all randomness from `Simulation.Rng`
  - iteration in unit-id order
  - `SideKnowledge` enumerates contacts in unit-id order
  - `StateHash` covers the new state (stance, path, knowledge)
- **Orders in, events out:** UI changes the simulation only through `Simulation.Submit`.
- **The player sees only what own units see** (spec §6.1, §7.6). Enemy units are drawn only when the player side's contact level is `Visible`. A debug reveal toggle is allowed (spec §10.5 developer mode).
- **Vision is updated every 5 ticks** (spec §7.7).
- **Contact levels** follow spec §7.4: Unknown, Suspected (heard), Visible, LastKnown. The "Identified" level waits until unit types exist (phase 3).
- **Stance heights** (spec §7.2): standing 170 cm, crouching 100 cm, prone 30 cm.
- **Parsing** uses `CultureInfo.InvariantCulture`. Nullable is on and warnings are errors, except in the Godot project, where the Godot source generators decide.
- **Commits** end with `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Review Focus

1. **Clicking an unreachable spot** (a rock, an enclosed area, outside the map): the order is rejected with `OrderRejected`, the unit stays put, and nothing crashes → tests in Tasks 2, 3 and 10.
2. **A unit standing on an impassable cell**, e.g. spawned on a rock: it can still walk off → test in Task 2.
3. **Many selected units sent to one point:** they spread into a formation. Offsets that land outside the map or on rocks fall back to the clicked point → tests in Task 10.
4. **Spotting at or beyond maximum range, and gains that truncate to zero:** progress never goes negative and never overflows, and a target beyond range stays Unknown → tests in Task 5.
5. **Pause and speed changes during interpolation:** pausing stops ticks and positions stay stable. Interpolation stays between the previous and current tick → tests in Task 10.

---

## File Structure

```
src/Nmf.Sim/
  Nmf.Sim.csproj                 # + InternalsVisibleTo Nmf.Sim.Tests
  World/CellData.cs              # + ExtraMoveCost, IsPassable, MoveCostPct
  World/Pathfinder.cs            # A* on the cell grid -> waypoints
  Units/Unit.cs                  # + Stance, MoveMode, path, stance change state
  Units/StanceRules.cs           # heights, eye heights, stance timings, speeds
  Units/Movement.cs              # stance changes + path following per tick
  Orders/Orders.cs               # + MoveOrder.Mode, SetStanceOrder
  Events/SimEvents.cs            # + StanceChanged, ContactChanged
  Vision/LineOfSight.cs          # clarity 0..255 between two points
  Vision/SideKnowledge.cs        # ContactLevel, Contact, SideKnowledge
  Vision/VisionRules.cs          # tunable constants and factors
  Vision/VisionSystem.cs         # spotting + hearing update every 5 ticks
  Vision/Viewshed.cs             # cells visible to a set of observers (fog)
  AI/PatrolBehavior.cs           # ping-pong patrol along a path
  Scenarios/Scenario.cs          # Simulation + scripted behaviours
  Scenarios/SkirmishScenario.cs  # map points/paths -> units and patrols
  Simulation.cs                  # + knowledge, vision step, new orders
  Core/StateHash.cs              # + new unit fields and knowledge
src/Nmf.Content/Tiled/TmxMapLoader.cs   # + move_cost / impassable properties
src/Nmf.Client/                  # NEW net8.0 library, no Godot
  Nmf.Client.csproj
  Selection.cs  Formation.cs  TerrainPalette.cs  ContentLocator.cs  GameSession.cs
src/Nmf.Client.Tests/            # NEW xUnit project
src/Nmf.Game/                    # NEW Godot 4.7 .NET project
  project.godot  Main.tscn  Nmf.Game.csproj
  GameRoot.cs  Coords.cs  MapView.cs  FogView.cs  UnitView.cs  CameraController.cs  Hud.cs
content/core/tilesets/terrain.tsx, obstacles.tsx   # + move costs, thinner forest concealment
content/core/maps/skirmish.tmx   # generated 128 x 96 test map
tools/make_skirmish_map.py       # deterministic generator for skirmish.tmx
tools/run_game.sh                # build + import + launch
README.md                        # + how to play the test skirmish
```

---

### Task 1: Movement cost in cells and Tiled properties

**Files:**
- Modify: `src/Nmf.Sim/World/CellData.cs`
- Modify: `src/Nmf.Content/Tiled/TmxMapLoader.cs` (the `BuildTerrain` and `ApplyObstacles` methods, and a new `MoveCostProperty` method)
- Modify: `content/core/tilesets/terrain.tsx`, `content/core/tilesets/obstacles.tsx`
- Modify: `README.md` (Tiled property list)
- Test: `src/Nmf.Sim.Tests/World/GridMapTests.cs` (append), `src/Nmf.Content.Tests/TmxMapLoaderTests.cs` (append)

**Interfaces:**
- Consumes: phase 1 `CellData`, `TmxMapLoader` internals (`Resolve`, `Error`)
- Produces:
  - `CellData` gets a 6th positional member `byte ExtraMoveCost = 0`. It stores the movement time multiplier minus 1, in percent, and 255 means impassable.
  - `const byte CellData.Impassable = 255`
  - `bool IsPassable`
  - `int MoveCostPct` (= 100 + ExtraMoveCost)
- Tiled tile properties:
  - `move_cost`: float 1..3.54, stored as round((v − 1) × 100)
  - `impassable`: `true` / `false`, where `true` stores 255
  - The obstacles layer merges `ExtraMoveCost` with max.

- [ ] **Step 1: Write the failing tests**

Append to the class in `src/Nmf.Sim.Tests/World/GridMapTests.cs`:
```csharp
    [Fact]
    public void CellData_DefaultIsPassableAtNormalCost()
    {
        var cell = default(CellData);
        Assert.True(cell.IsPassable);
        Assert.Equal(100, cell.MoveCostPct);
    }

    [Fact]
    public void CellData_ImpassableValue_BlocksMovement()
    {
        var cell = new CellData(0, 0, 0, 0, 0, CellData.Impassable);
        Assert.False(cell.IsPassable);
    }
```

Append to the class in `src/Nmf.Content.Tests/TmxMapLoaderTests.cs`:
```csharp
    [Fact]
    public void Load_MoveCostAndImpassable_AreStoredAsExtraMoveCost()
    {
        var extra = """
            <tileset firstgid="20" name="move" tilecount="3">
             <tile id="0"><properties>
              <property name="terrain" value="mud"/>
              <property name="move_cost" type="float" value="2"/>
             </properties></tile>
             <tile id="1"><properties><property name="impassable" type="bool" value="true"/></properties></tile>
             <tile id="2"><properties><property name="move_cost" type="float" value="3.54"/></properties></tile>
            </tileset>
            """ + TmxText.Layer("obstacles", 3, 1, "0,21,22");
        var map = LoadText(TmxText.Map(3, 1, "20,20,20", extra));

        Assert.Equal(100, map[new CellCoord(0, 0)].ExtraMoveCost);
        Assert.False(map[new CellCoord(1, 0)].IsPassable);
        Assert.Equal(254, map[new CellCoord(2, 0)].ExtraMoveCost);
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("4")]
    [InlineData("fast")]
    public void Load_MoveCostOutOfRange_Throws(string value)
    {
        var extra = $"""
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="move_cost" type="float" value="{value}"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(1, 1, "20", extra));
        Assert.Contains("move_cost", ex.Message);
    }

    [Fact]
    public void Load_ImpassableNotBoolean_Throws()
    {
        var extra = """
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="impassable" value="maybe"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(1, 1, "20", extra));
        Assert.Contains("impassable", ex.Message);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test NoMansForest.slnx`
Expected: build FAILS with `'CellData' does not contain a definition for 'IsPassable'`.

- [ ] **Step 3: Implement CellData**

Replace `src/Nmf.Sim/World/CellData.cs`:
```csharp
namespace Nmf.Sim.World;

/// <summary>Static terrain values of one 1 m cell.</summary>
/// <param name="GroundHeightCm">Ground elevation.</param>
/// <param name="ObstacleHeightCm">Height of what stands on the cell (trees, rocks, walls).</param>
/// <param name="ConcealmentPerM">How much one metre of this cell blocks sight, 0..255 = 0..1.</param>
/// <param name="Cover">How well the cell stops bullets, 0..255 = 0..1.</param>
/// <param name="TerrainId">Index into <see cref="GridMap.TerrainNames"/>.</param>
/// <param name="ExtraMoveCost">Extra movement time in percent (0 = normal, 100 = twice as slow); <see cref="Impassable"/> blocks movement.</param>
public record struct CellData(
    short GroundHeightCm,
    short ObstacleHeightCm,
    byte ConcealmentPerM,
    byte Cover,
    ushort TerrainId,
    byte ExtraMoveCost = 0)
{
    public const byte Impassable = 255;

    public readonly bool IsPassable => ExtraMoveCost != Impassable;

    /// <summary>Movement time multiplier in percent (100 = normal ground).</summary>
    public readonly int MoveCostPct => 100 + ExtraMoveCost;
}
```

- [ ] **Step 4: Implement the loader properties**

In `src/Nmf.Content/Tiled/TmxMapLoader.cs`, in `BuildTerrain`, change the `new CellData(...)` call so that it ends with
```csharp
                    TerrainId: id,
                    ExtraMoveCost: MoveCostProperty(props, x, y));
```
(replacing the old final line `TerrainId: id);`).

In `ApplyObstacles`, after the line that merges `Cover`, add:
```csharp
                cells[i].ExtraMoveCost = Math.Max(cells[i].ExtraMoveCost, MoveCostProperty(props, x, y));
```

Add this method next to `FractionProperty`:
```csharp
        private byte MoveCostProperty(IReadOnlyDictionary<string, string> props, int x, int y)
        {
            if (props.TryGetValue("impassable", out var flag))
            {
                if (flag == "true")
                    return CellData.Impassable;
                if (flag != "false")
                    throw Error($"property 'impassable' of the tile at ({x},{y}) must be true or false, was '{flag}'");
            }
            if (!props.TryGetValue("move_cost", out var text))
                return 0;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !(value >= 1 && value <= 3.54))
                throw Error($"property 'move_cost' of the tile at ({x},{y}) must be a number between 1 and 3.54, was '{text}'");
            return (byte)Math.Round((value - 1) * 100, MidpointRounding.AwayFromZero);
        }
```

- [ ] **Step 5: Update core tilesets and README**

In `content/core/tilesets/terrain.tsx`:
- In tile 1 (forest), change `concealment_per_m` from `0.12` to `0.03` (about 30 m of sight through forest) and add `<property name="move_cost" type="float" value="1.3"/>`.
- In tile 2 (swamp), add `<property name="move_cost" type="float" value="2.2"/>`.

In `content/core/tilesets/obstacles.tsx`:
- In tile 0 (rock), add `<property name="impassable" type="bool" value="true"/>`.
- In tile 1 (bush), add `<property name="move_cost" type="float" value="1.5"/>`.

In `README.md`, replace the bullet that starts with `` - `terrain` (required, every cell filled)`` and the two bullets after it with:
```markdown
  - `terrain` (required, every cell filled): tile property `terrain` (name), optional `concealment_per_m` (0–1), `cover` (0–1), `obstacle_height_cm`, `move_cost` (1–3.54, time multiplier), `impassable` (true/false).
  - `height` (optional): tile property `height_cm`.
  - `obstacles` (optional): `obstacle_height_cm`, `concealment_per_m`, `cover`, `move_cost`, `impassable`, combined with the terrain using the larger value.
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS (all phase 1 tests plus the new ones; the core-content tests still load both core maps).

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: movement cost and impassable cells from Tiled properties

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: A* pathfinding

**Files:**
- Create: `src/Nmf.Sim/World/Pathfinder.cs`
- Test: `src/Nmf.Sim.Tests/World/PathfinderTests.cs`

**Interfaces:**
- Consumes: `GridMap`, `CellData.IsPassable`, `CellData.MoveCostPct` (Task 1), `Vec2`, `CellCoord`, `Rng`
- Produces:
  - `static class Pathfinder` with `const int MaxExpandedNodes = 250_000` and `List<Vec2>? FindPath(GridMap map, Vec2 start, Vec2 target)`.
  - The waypoints exclude the start and end exactly at `target`. Intermediate waypoints are the centres of the cells where the path turns.
  - The search is 8-directional and never cuts corners past impassable cells. Step cost is 100 straight or 141 diagonal, times `MoveCostPct / 100` of the entered cell.
  - Returns `null` when the target is outside the map, lies in an impassable cell, or is unreachable.
  - The start cell may be impassable.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/World/PathfinderTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

public class PathfinderTests
{
    private static GridMap OpenMap(int w, int h) => new(w, h, ["none"]);

    private static void Block(GridMap map, int x, int y) => map[new CellCoord(x, y)].ExtraMoveCost = CellData.Impassable;

    /// <summary>Samples every path segment each 10 cm and fails if any sample lies in an impassable cell.</summary>
    private static void AssertWalkable(GridMap map, Vec2 start, IReadOnlyList<Vec2> path)
    {
        var from = start;
        foreach (var to in path)
        {
            var d = to - from;
            int steps = Math.Max(1, d.Length / 10);
            for (int i = 0; i <= steps; i++)
            {
                var p = new Vec2(from.X + (int)((long)d.X * i / steps), from.Y + (int)((long)d.Y * i / steps));
                Assert.True(map.CellAt(p).IsPassable || p.ToCell() == start.ToCell(), $"path crosses impassable cell at {p}");
            }
            from = to;
        }
    }

    [Fact]
    public void SameCell_ReturnsTargetOnly()
    {
        var path = Pathfinder.FindPath(OpenMap(5, 5), new Vec2(10, 10), new Vec2(90, 60));
        Assert.Equal(new[] { new Vec2(90, 60) }, path);
    }

    [Fact]
    public void StraightLine_CollapsesToTarget()
    {
        var path = Pathfinder.FindPath(OpenMap(20, 5), new Vec2(50, 250), new Vec2(1850, 250));
        Assert.Equal(new[] { new Vec2(1850, 250) }, path);
    }

    [Fact]
    public void WallWithGap_RoutesThroughGap()
    {
        var map = OpenMap(12, 12);
        for (int y = 0; y <= 10; y++) Block(map, 5, y);
        var start = new Vec2(150, 150);
        var target = new Vec2(950, 150);

        var path = Pathfinder.FindPath(map, start, target);

        Assert.NotNull(path);
        Assert.Equal(target, path[^1]);
        Assert.Contains(path, p => p.ToCell().Y == 11);
        AssertWalkable(map, start, path);
    }

    [Fact]
    public void EnclosedTarget_IsUnreachable()
    {
        var map = OpenMap(10, 10);
        for (int x = 3; x <= 7; x++) { Block(map, x, 3); Block(map, x, 7); }
        for (int y = 3; y <= 7; y++) { Block(map, 3, y); Block(map, 7, y); }
        Assert.Null(Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 550)));
    }

    [Fact]
    public void ImpassableTargetCell_IsUnreachable()
    {
        var map = OpenMap(10, 10);
        Block(map, 5, 5);
        Assert.Null(Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 550)));
    }

    [Fact]
    public void TargetOutsideMap_IsUnreachable()
    {
        Assert.Null(Pathfinder.FindPath(OpenMap(10, 10), new Vec2(50, 50), new Vec2(5000, 50)));
    }

    [Fact]
    public void StartOnImpassableCell_CanStillLeave()
    {
        var map = OpenMap(10, 10);
        Block(map, 0, 0);
        var path = Pathfinder.FindPath(map, new Vec2(50, 50), new Vec2(550, 50));
        Assert.NotNull(path);
        Assert.Equal(new Vec2(550, 50), path[^1]);
    }

    [Fact]
    public void ExpensiveTerrain_IsAvoided()
    {
        var map = OpenMap(20, 11);
        for (int x = 5; x <= 14; x++)
            for (int y = 2; y <= 8; y++)
                map[new CellCoord(x, y)].ExtraMoveCost = 200;
        var start = new Vec2(150, 550);
        var path = Pathfinder.FindPath(map, start, new Vec2(1850, 550));

        Assert.NotNull(path);
        var from = start;
        foreach (var to in path)
        {
            var d = to - from;
            int steps = Math.Max(1, d.Length / 10);
            for (int i = 0; i <= steps; i++)
            {
                var p = new Vec2(from.X + (int)((long)d.X * i / steps), from.Y + (int)((long)d.Y * i / steps));
                Assert.True(map.CellAt(p).ExtraMoveCost == 0, $"path enters swamp at {p}");
            }
            from = to;
        }
    }

    [Fact]
    public void RandomRockFields_PathsNeverCutThroughRocks()
    {
        var rng = new Rng(5);
        var map = OpenMap(20, 20);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 20; x++)
                if (rng.Chance(150)) Block(map, x, y);

        int found = 0;
        for (int i = 0; i < 60; i++)
        {
            var start = new Vec2(rng.NextInt(2000), rng.NextInt(2000));
            var target = new Vec2(rng.NextInt(2000), rng.NextInt(2000));
            if (!map.CellAt(start).IsPassable) continue;
            var path = Pathfinder.FindPath(map, start, target);
            if (path is null) continue;
            found++;
            Assert.Equal(target, path[^1]);
            AssertWalkable(map, start, path);
        }
        Assert.True(found > 20, $"only {found} paths found; the fixture is too dense");
    }

    [Fact]
    public void SameInput_GivesSamePath()
    {
        var map = OpenMap(30, 30);
        for (int y = 0; y < 25; y++) Block(map, 15, y);
        var a = Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(2850, 150));
        var b = Pathfinder.FindPath(map, new Vec2(150, 150), new Vec2(2850, 150));
        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~PathfinderTests`
Expected: build FAILS with `The name 'Pathfinder' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/World/Pathfinder.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Deterministic 8-directional A* over map cells.</summary>
public static class Pathfinder
{
    public const int MaxExpandedNodes = 250_000;

    private const int StraightCost = 100;
    private const int DiagonalCost = 141;

    private static readonly (int Dx, int Dy)[] Directions =
        [(1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)];

    /// <summary>
    /// Waypoints from <paramref name="start"/> (excluded) to <paramref name="target"/> (last element),
    /// or null if the target is outside the map, impassable or unreachable.
    /// </summary>
    public static List<Vec2>? FindPath(GridMap map, Vec2 start, Vec2 target)
    {
        if (!map.Contains(start) || !map.Contains(target))
            return null;
        var startCell = start.ToCell();
        var goalCell = target.ToCell();
        if (!map[goalCell].IsPassable)
            return null;
        if (startCell == goalCell)
            return [target];

        int width = map.Width;
        int count = width * map.Height;
        var cost = new int[count];
        Array.Fill(cost, int.MaxValue);
        var parent = new int[count];
        Array.Fill(parent, -1);
        var closed = new bool[count];
        var open = new PriorityQueue<int, (int F, int H, int Seq)>();

        int sequence = 0;
        int startIndex = startCell.Y * width + startCell.X;
        int goalIndex = goalCell.Y * width + goalCell.X;
        cost[startIndex] = 0;
        int startH = Heuristic(startCell, goalCell);
        open.Enqueue(startIndex, (startH, startH, sequence++));

        int expanded = 0;
        while (open.TryDequeue(out int current, out _))
        {
            if (closed[current])
                continue;
            if (current == goalIndex)
                return BuildWaypoints(parent, current, width, target);
            closed[current] = true;
            if (++expanded > MaxExpandedNodes)
                return null;

            int cx = current % width, cy = current / width;
            foreach (var (dx, dy) in Directions)
            {
                var next = new CellCoord(cx + dx, cy + dy);
                if (!map.InBounds(next) || !map[next].IsPassable)
                    continue;
                bool diagonal = dx != 0 && dy != 0;
                // No corner cutting: both orthogonal neighbours of a diagonal step must be passable.
                if (diagonal && (!map[new CellCoord(cx + dx, cy)].IsPassable || !map[new CellCoord(cx, cy + dy)].IsPassable))
                    continue;

                int nextIndex = next.Y * width + next.X;
                if (closed[nextIndex])
                    continue;
                int step = (diagonal ? DiagonalCost : StraightCost) * map[next].MoveCostPct / 100;
                int tentative = cost[current] + step;
                if (tentative >= cost[nextIndex])
                    continue;

                cost[nextIndex] = tentative;
                parent[nextIndex] = current;
                int h = Heuristic(next, goalCell);
                open.Enqueue(nextIndex, (tentative + h, h, sequence++));
            }
        }
        return null;
    }

    /// <summary>Octile distance at the cheapest cost; admissible because every step costs at least its base cost.</summary>
    private static int Heuristic(CellCoord a, CellCoord b)
    {
        int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
        int diagonal = Math.Min(dx, dy);
        return diagonal * DiagonalCost + (Math.Max(dx, dy) - diagonal) * StraightCost;
    }

    private static List<Vec2> BuildWaypoints(int[] parent, int goalIndex, int width, Vec2 target)
    {
        var cells = new List<CellCoord>();
        for (int i = goalIndex; i != -1; i = parent[i])
            cells.Add(new CellCoord(i % width, i / width));
        cells.Reverse(); // cells[0] is the start cell

        var waypoints = new List<Vec2>();
        for (int i = 1; i < cells.Count - 1; i++)
        {
            var prev = cells[i - 1];
            var cur = cells[i];
            var next = cells[i + 1];
            bool straight = cur.X - prev.X == next.X - cur.X && cur.Y - prev.Y == next.Y - cur.Y;
            if (!straight)
                waypoints.Add(cur.CenterCm);
        }
        waypoints.Add(target);
        return waypoints;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~PathfinderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/World/Pathfinder.cs src/Nmf.Sim.Tests/World/PathfinderTests.cs
git commit -m "feat(sim): add deterministic A* pathfinding over map cells

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Stances, move modes and path-following movement

**Files:**
- Modify: `src/Nmf.Sim/Nmf.Sim.csproj` (InternalsVisibleTo)
- Replace: `src/Nmf.Sim/Units/Unit.cs`, `src/Nmf.Sim/Units/Movement.cs`, `src/Nmf.Sim/Orders/Orders.cs`, `src/Nmf.Sim/Simulation.cs`, `src/Nmf.Sim/Core/StateHash.cs`
- Create: `src/Nmf.Sim/Units/StanceRules.cs`
- Modify: `src/Nmf.Sim/Events/SimEvents.cs` (add `StanceChanged`)
- Test: `src/Nmf.Sim.Tests/Units/MovementTests.cs`, `src/Nmf.Sim.Tests/Replays/ReplayTests.cs` (append one test)

**Interfaces:**
- Consumes: `Pathfinder.FindPath` (Task 2), `CellData.MoveCostPct` (Task 1)
- Produces:
  - Enums: `Stance : byte { Standing, Crouching, Prone }` and `MoveMode : byte { Walk, Run, Crawl }`.
  - New `Unit` members: `Stance Stance`, `Stance? TargetStance`, `int StanceTicksLeft`, `MoveMode MoveMode`, `bool IsMoving` (moved during the last step), `IReadOnlyList<Vec2> Path`, `int PathIndex`. Internal members: `List<Vec2> PathPoints` and `bool MovedSinceVisionUpdate`.
  - `static class StanceRules` with `HeightCm(Stance)` (170/100/30), `EyeHeightCm(Stance)` (160/90/25), `StepTicks(Stance from, Stance to)` (10 between Standing and Crouching, 15 between Crouching and Prone), `NextToward(Stance current, Stance target)`, `RequiredFor(MoveMode)` (Walk and Run need Standing, Crawl needs Prone) and `SpeedCmPerTick(Unit, MoveMode)` (Walk = walk speed, Run = ×2, Crawl = max(1, speed/5)).
  - Orders: `MoveOrder(UnitId Unit, Vec2 Target, MoveMode Mode = MoveMode.Walk)` and `SetStanceOrder(UnitId Unit, Stance Stance)`.
  - Event: `StanceChanged(long Tick, UnitId Unit, Stance Stance)`.
  - A move order pathfinds. If the target is unreachable it is rejected with reason `"target not reachable"`. If the stance does not match the move mode, the unit changes stance first and moves after that.
  - Per-tick speed is `SpeedCmPerTick(unit, mode) * 100 / MoveCostPct` of the current cell, minimum 1.
  - `SetStanceOrder` stops movement.
  - The tick in which a stance change completes does not move the unit.

- [ ] **Step 1: Allow tests to reach internals**

Add to `src/Nmf.Sim/Nmf.Sim.csproj` before `</Project>`:
```xml
  <ItemGroup>
    <InternalsVisibleTo Include="Nmf.Sim.Tests" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

`src/Nmf.Sim.Tests/Units/MovementTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Units;

public class MovementTests
{
    private static GridMap OpenMap(int w = 30, int h = 30) => new(w, h, ["none"]);

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < n; i++) events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void CostlyCell_SlowsMovement()
    {
        var map = OpenMap();
        map[new CellCoord(0, 0)].ExtraMoveCost = 100;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(10, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50)));
        sim.Step();
        Assert.Equal(new Vec2(15, 50), u.Position);
        Assert.True(u.IsMoving);
    }

    [Fact]
    public void RunMode_DoublesSpeed()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(10, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50), MoveMode.Run));
        sim.Step();
        Assert.Equal(new Vec2(30, 50), u.Position);
    }

    [Fact]
    public void SetStance_StandingToProne_TakesTwentyFiveTicksViaCrouch()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Prone));

        StepN(sim, 10);
        Assert.Equal(Stance.Crouching, u.Stance);
        StepN(sim, 14);
        Assert.Equal(Stance.Crouching, u.Stance);
        var last = sim.Step();
        Assert.Equal(Stance.Prone, u.Stance);
        Assert.Null(u.TargetStance);
        Assert.Contains<SimEvent>(new StanceChanged(24, u.Id, Stance.Prone), last);
    }

    [Fact]
    public void CrawlOrder_GoesProneFirstThenCrawlsSlowly()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(2050, 50), MoveMode.Crawl));

        StepN(sim, 25);
        Assert.Equal(Stance.Prone, u.Stance);
        Assert.Equal(new Vec2(50, 50), u.Position);

        sim.Step();
        Assert.Equal(new Vec2(52, 50), u.Position);
    }

    [Fact]
    public void WalkOrderWhileProne_StandsUpBeforeMoving()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new SetStanceOrder(u.Id, Stance.Prone));
        StepN(sim, 25);

        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(90, 50)));
        StepN(sim, 25);
        Assert.Equal(Stance.Standing, u.Stance);
        Assert.Equal(new Vec2(50, 50), u.Position);

        sim.Step();
        Assert.Equal(new Vec2(60, 50), u.Position);
    }

    [Fact]
    public void MoveOrder_ToImpassableCell_IsRejected()
    {
        var map = OpenMap();
        map[new CellCoord(5, 0)].ExtraMoveCost = CellData.Impassable;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var order = new MoveOrder(u.Id, new Vec2(550, 50));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "target not reachable") }, events);
        Assert.Null(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_AroundWall_NeverEntersImpassableCells()
    {
        var map = OpenMap(20, 20);
        for (int y = 0; y <= 18; y++) map[new CellCoord(10, y)].ExtraMoveCost = CellData.Impassable;
        var sim = new Simulation(map, 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(150, 150), 7);
        var target = new Vec2(1850, 150);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, target));

        for (int i = 0; i < 2000 && (i == 0 || u.MoveTarget is not null); i++)
        {
            sim.Step();
            Assert.True(map.CellAt(u.Position).IsPassable, $"unit entered impassable cell at {u.Position}");
        }
        Assert.Equal(target, u.Position);
    }

    [Fact]
    public void StopOrder_ClearsPath()
    {
        var sim = new Simulation(OpenMap(), 1);
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(2550, 2550)));
        sim.Step();
        sim.Submit(Side.Blue, new StopOrder(u.Id));
        sim.Step();
        Assert.Null(u.MoveTarget);
        Assert.Empty(u.Path);
        Assert.False(u.IsMoving);
    }
}
```

Append to the class in `src/Nmf.Sim.Tests/Replays/ReplayTests.cs`:
```csharp
    [Fact]
    public void StateHash_ChangesWhenStanceChanges()
    {
        var sim = NewSim();
        sim.Step();
        var before = StateHash.Compute(sim);
        var other = NewSim();
        other.Submit(Side.Blue, new SetStanceOrder(other.Units[0].Id, Stance.Prone));
        other.Step();
        Assert.NotEqual(before, StateHash.Compute(other));
    }
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: build FAILS with `The type or namespace name 'SetStanceOrder' could not be found`.

- [ ] **Step 4: Implement units, rules, orders and events**

Replace `src/Nmf.Sim/Units/Unit.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.Units;

public enum Side : byte
{
    Blue = 0,
    Red = 1,
}

public enum Stance : byte
{
    Standing = 0,
    Crouching = 1,
    Prone = 2,
}

public enum MoveMode : byte
{
    Walk = 0,
    Run = 1,
    Crawl = 2,
}

public readonly record struct UnitId(int Value)
{
    public override string ToString() => $"U{Value}";
}

public sealed class Unit
{
    internal Unit(UnitId id, Side side, Vec2 position, int speedCmPerTick)
    {
        Id = id;
        Side = side;
        Position = position;
        SpeedCmPerTick = speedCmPerTick;
    }

    public UnitId Id { get; }
    public Side Side { get; }

    /// <summary>Walking speed on open ground.</summary>
    public int SpeedCmPerTick { get; }

    public Vec2 Position { get; internal set; }

    /// <summary>Final destination of the current move order, or null when not moving.</summary>
    public Vec2? MoveTarget { get; internal set; }

    public MoveMode MoveMode { get; internal set; }
    public Stance Stance { get; internal set; }

    /// <summary>Stance the unit is changing to, or null.</summary>
    public Stance? TargetStance { get; internal set; }

    public int StanceTicksLeft { get; internal set; }

    /// <summary>True if the unit changed position during the last step.</summary>
    public bool IsMoving { get; internal set; }

    /// <summary>Waypoints of the current path; <see cref="PathIndex"/> is the next one.</summary>
    public IReadOnlyList<Vec2> Path => PathPoints;

    public int PathIndex { get; internal set; }

    internal List<Vec2> PathPoints { get; } = [];

    /// <summary>Set by movement, cleared by the vision update; used for hearing.</summary>
    internal bool MovedSinceVisionUpdate { get; set; }
}
```

`src/Nmf.Sim/Units/StanceRules.cs`:
```csharp
namespace Nmf.Sim.Units;

public static class StanceRules
{
    public static int HeightCm(Stance stance) => stance switch
    {
        Stance.Standing => 170,
        Stance.Crouching => 100,
        _ => 30,
    };

    public static int EyeHeightCm(Stance stance) => stance switch
    {
        Stance.Standing => 160,
        Stance.Crouching => 90,
        _ => 25,
    };

    /// <summary>Ticks for one step between adjacent stances.</summary>
    public static int StepTicks(Stance from, Stance to) =>
        (from, to) is (Stance.Standing, Stance.Crouching) or (Stance.Crouching, Stance.Standing) ? 10 : 15;

    public static Stance NextToward(Stance current, Stance target) =>
        current < target ? current + 1 : current - 1;

    public static Stance RequiredFor(MoveMode mode) => mode == MoveMode.Crawl ? Stance.Prone : Stance.Standing;

    /// <summary>Speed on open ground for the given move mode.</summary>
    public static int SpeedCmPerTick(Unit unit, MoveMode mode) => mode switch
    {
        MoveMode.Walk => unit.SpeedCmPerTick,
        MoveMode.Run => unit.SpeedCmPerTick * 2,
        _ => Math.Max(1, unit.SpeedCmPerTick / 5),
    };
}
```

Replace `src/Nmf.Sim/Orders/Orders.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Orders;

public abstract record Order(UnitId Unit);

public sealed record MoveOrder(UnitId Unit, Vec2 Target, MoveMode Mode = MoveMode.Walk) : Order(Unit);

public sealed record StopOrder(UnitId Unit) : Order(Unit);

public sealed record SetStanceOrder(UnitId Unit, Stance Stance) : Order(Unit);

/// <summary>An order as submitted: the tick it was submitted on and by which side.</summary>
public sealed record LoggedOrder(long Tick, Side Issuer, Order Order);
```

Append to `src/Nmf.Sim/Events/SimEvents.cs`:
```csharp

public sealed record StanceChanged(long Tick, UnitId Unit, Stance Stance) : SimEvent(Tick);
```

- [ ] **Step 5: Implement movement**

Replace `src/Nmf.Sim/Units/Movement.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.World;

namespace Nmf.Sim.Units;

/// <summary>Per-tick stance changes and path following.</summary>
internal static class Movement
{
    public static void StartPath(Unit unit, Vec2 target, MoveMode mode, List<Vec2> path)
    {
        unit.MoveTarget = target;
        unit.MoveMode = mode;
        unit.PathPoints.Clear();
        unit.PathPoints.AddRange(path);
        unit.PathIndex = 0;
        BeginStanceChange(unit, StanceRules.RequiredFor(mode));
    }

    public static void ClearPath(Unit unit)
    {
        unit.MoveTarget = null;
        unit.PathPoints.Clear();
        unit.PathIndex = 0;
    }

    public static void BeginStanceChange(Unit unit, Stance target)
    {
        if (unit.TargetStance == target)
            return;
        if (unit.Stance == target)
        {
            unit.TargetStance = null;
            unit.StanceTicksLeft = 0;
            return;
        }
        unit.TargetStance = target;
        unit.StanceTicksLeft = StanceRules.StepTicks(unit.Stance, StanceRules.NextToward(unit.Stance, target));
    }

    public static void Update(Unit unit, GridMap map, long tick, List<SimEvent> events)
    {
        unit.IsMoving = false;

        if (unit.TargetStance is { } targetStance)
        {
            if (--unit.StanceTicksLeft > 0)
                return;
            unit.Stance = StanceRules.NextToward(unit.Stance, targetStance);
            if (unit.Stance != targetStance)
            {
                unit.StanceTicksLeft = StanceRules.StepTicks(unit.Stance, StanceRules.NextToward(unit.Stance, targetStance));
                return;
            }
            unit.TargetStance = null;
            events.Add(new StanceChanged(tick, unit.Id, unit.Stance));
            return;
        }

        if (unit.MoveTarget is not null)
            FollowPath(unit, map, tick, events);
    }

    private static void FollowPath(Unit unit, GridMap map, long tick, List<SimEvent> events)
    {
        var from = unit.Position;
        int budget = Math.Max(1, StanceRules.SpeedCmPerTick(unit, unit.MoveMode) * 100 / map.CellAt(from).MoveCostPct);
        var pos = from;
        var path = unit.PathPoints;

        while (budget > 0 && unit.PathIndex < path.Count)
        {
            var waypoint = path[unit.PathIndex];
            var delta = waypoint - pos;
            long distance = IntMath.Isqrt(delta.LengthSquared);
            if (distance <= budget)
            {
                pos = waypoint;
                budget -= (int)distance;
                unit.PathIndex++;
                continue;
            }

            int mx = (int)(delta.X * (long)budget / distance);
            int my = (int)(delta.Y * (long)budget / distance);
            if (mx == 0 && my == 0)
            {
                // Truncation can round a slow diagonal step down to nothing; always make progress.
                if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
                    mx = Math.Sign(delta.X);
                else
                    my = Math.Sign(delta.Y);
            }
            pos = new Vec2(pos.X + mx, pos.Y + my);
            budget = 0;
        }

        unit.Position = pos;
        if (pos != from)
        {
            unit.IsMoving = true;
            unit.MovedSinceVisionUpdate = true;
            events.Add(new UnitMoved(tick, unit.Id, from, pos));
        }
        if (unit.PathIndex >= path.Count)
        {
            ClearPath(unit);
            events.Add(new UnitArrived(tick, unit.Id, pos));
        }
    }
}
```

- [ ] **Step 6: Wire orders into the simulation and extend the state hash**

Replace `src/Nmf.Sim/Simulation.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim;

/// <summary>
/// The whole deterministic world. Orders are the only input and events the only output;
/// nothing outside this class mutates simulation state.
/// </summary>
public sealed class Simulation
{
    private readonly List<Unit> _units = [];
    private readonly Dictionary<UnitId, Unit> _unitsById = [];
    private readonly List<LoggedOrder> _pending = [];
    private readonly List<LoggedOrder> _orderLog = [];
    private int _nextUnitId = 1;

    public Simulation(GridMap map, ulong seed)
    {
        Map = map;
        Seed = seed;
        Rng = new Rng(seed);
    }

    public GridMap Map { get; }
    public ulong Seed { get; }
    public Rng Rng { get; }
    public long Tick { get; private set; }
    public IReadOnlyList<Unit> Units => _units;
    public IReadOnlyList<LoggedOrder> OrderLog => _orderLog;

    public Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick)
    {
        if (!Map.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position), $"Spawn position {position} is outside the map.");
        if (speedCmPerTick <= 0)
            throw new ArgumentOutOfRangeException(nameof(speedCmPerTick), speedCmPerTick, "Speed must be positive.");

        var unit = new Unit(new UnitId(_nextUnitId++), side, position, speedCmPerTick);
        _units.Add(unit);
        _unitsById.Add(unit.Id, unit);
        return unit;
    }

    public Unit? FindUnit(UnitId id) => _unitsById.GetValueOrDefault(id);

    /// <summary>Queues an order; it is applied at the start of the next <see cref="Step"/>.</summary>
    public void Submit(Side issuer, Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        _pending.Add(new LoggedOrder(Tick, issuer, order));
    }

    public IReadOnlyList<SimEvent> Step()
    {
        var events = new List<SimEvent>();

        foreach (var logged in _pending)
        {
            _orderLog.Add(logged);
            Apply(logged, events);
        }
        _pending.Clear();

        foreach (var unit in _units)
            Movement.Update(unit, Map, Tick, events);

        Tick++;
        return events;
    }

    private void Apply(LoggedOrder logged, List<SimEvent> events)
    {
        var order = logged.Order;
        var unit = FindUnit(order.Unit);
        if (unit is null)
        {
            events.Add(new OrderRejected(Tick, order, "unknown unit"));
            return;
        }
        if (unit.Side != logged.Issuer)
        {
            events.Add(new OrderRejected(Tick, order, "unit belongs to another side"));
            return;
        }

        switch (order)
        {
            case MoveOrder move when !Map.Contains(move.Target):
                events.Add(new OrderRejected(Tick, order, "target outside map"));
                break;
            case MoveOrder move:
                var path = Pathfinder.FindPath(Map, unit.Position, move.Target);
                if (path is null)
                {
                    events.Add(new OrderRejected(Tick, order, "target not reachable"));
                    break;
                }
                Movement.StartPath(unit, move.Target, move.Mode, path);
                break;
            case StopOrder:
                Movement.ClearPath(unit);
                break;
            case SetStanceOrder stance:
                Movement.ClearPath(unit);
                Movement.BeginStanceChange(unit, stance.Stance);
                break;
            default:
                events.Add(new OrderRejected(Tick, order, $"unsupported order {order.GetType().Name}"));
                break;
        }
    }
}
```

Replace `src/Nmf.Sim/Core/StateHash.cs`:
```csharp
namespace Nmf.Sim.Core;

/// <summary>Order-sensitive 64-bit fingerprint of the simulation state, used for determinism checks.</summary>
public static class StateHash
{
    public static ulong Compute(Simulation sim)
    {
        var h = new Fnv1a64();
        h.Add((ulong)sim.Tick);
        var rng = sim.Rng.State;
        h.Add(rng.State);
        h.Add(rng.Increment);
        foreach (var unit in sim.Units)
        {
            h.Add(unit.Id.Value);
            h.Add((int)unit.Side);
            h.Add(unit.SpeedCmPerTick);
            h.Add(unit.Position.X);
            h.Add(unit.Position.Y);
            h.Add((int)unit.Stance);
            h.Add(unit.TargetStance is { } targetStance ? (int)targetStance + 1 : 0);
            h.Add(unit.StanceTicksLeft);
            h.Add((int)unit.MoveMode);
            h.Add(unit.IsMoving ? 1 : 0);
            if (unit.MoveTarget is { } target)
            {
                h.Add(1);
                h.Add(target.X);
                h.Add(target.Y);
            }
            else
            {
                h.Add(0);
            }
            h.Add(unit.PathIndex);
            h.Add(unit.Path.Count);
            foreach (var waypoint in unit.Path)
            {
                h.Add(waypoint.X);
                h.Add(waypoint.Y);
            }
        }
        return h.Value;
    }

    private sealed class Fnv1a64
    {
        private const ulong Prime = 1099511628211UL;
        public ulong Value { get; private set; } = 14695981039346656037UL;

        public void Add(int value) => Add((ulong)(uint)value);

        public void Add(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                Value ^= (byte)(value >> (i * 8));
                Value = unchecked(Value * Prime);
            }
        }
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS, including all phase 1 simulation and replay tests.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(sim): stances, move modes and path-following movement

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Line of sight

**Files:**
- Create: `src/Nmf.Sim/Vision/LineOfSight.cs`
- Test: `src/Nmf.Sim.Tests/Vision/LineOfSightTests.cs`

**Interfaces:**
- Consumes: `GridMap`, `CellData`, `Vec2`
- Produces: `static class LineOfSight` with `const int Clear = 255` and `int Clarity(GridMap map, Vec2 from, int fromHeightCm, Vec2 to, int toHeightCm)`. Heights are absolute, meaning ground plus body. The result is 0 (blocked) to 255 (clear). How it works:
  - The line is traced with Bresenham from the observer's cell to the target's cell.
  - At each cell the ray height is interpolated along the dominant axis.
  - An intermediate cell blocks the ray when the ray height is ≤ its ground.
  - Where the ray is below ground + obstacle height, the cell adds its `ConcealmentPerM` (diagonal steps weigh 141 %). The target cell counts too.
  - Once the concealment sum reaches 255, the ray is blocked.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Vision/LineOfSightTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class LineOfSightTests
{
    private static readonly Vec2 Observer = new(50, 550);  // cell (0,5)
    private static readonly Vec2 Target = new(2050, 550);  // cell (20,5)

    private static GridMap OpenMap(int w = 30, int h = 10) => new(w, h, ["none"]);

    [Fact]
    public void OpenGround_IsClear()
    {
        Assert.Equal(255, LineOfSight.Clarity(OpenMap(), Observer, 160, Target, 160));
    }

    [Fact]
    public void SameCell_IsClear()
    {
        Assert.Equal(255, LineOfSight.Clarity(OpenMap(), new Vec2(10, 10), 160, new Vec2(90, 90), 20));
    }

    [Fact]
    public void ForestStrip_AccumulatesConcealment()
    {
        var map = OpenMap();
        for (int x = 5; x <= 14; x++)
            map[new CellCoord(x, 5)] = new CellData(0, 1500, 20, 0, 0);
        Assert.Equal(55, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void OpaqueWall_Blocks()
    {
        var map = OpenMap();
        map[new CellCoord(10, 5)] = new CellData(0, 300, 255, 230, 0);
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void LowBush_BlocksProneButNotStanding()
    {
        var map = OpenMap();
        map[new CellCoord(5, 5)] = new CellData(0, 80, 255, 0, 0);
        Assert.Equal(255, LineOfSight.Clarity(map, Observer, 160, Target, 160));
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 25, Target, 20));
    }

    [Fact]
    public void HillBetween_Blocks()
    {
        var map = OpenMap();
        map[new CellCoord(10, 5)].GroundHeightCm = 300;
        Assert.Equal(0, LineOfSight.Clarity(map, Observer, 160, Target, 160));
    }

    [Fact]
    public void TargetInsideBush_IsPartlyHidden()
    {
        var map = OpenMap();
        map[new CellCoord(20, 5)] = new CellData(0, 80, 153, 0, 0);
        Assert.Equal(102, LineOfSight.Clarity(map, Observer, 160, Target, 20));
    }

    [Fact]
    public void DiagonalSteps_CountMoreConcealmentThanStraightOnes()
    {
        var map = OpenMap(30, 30);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                map[new CellCoord(x, y)] = new CellData(0, 1500, 10, 0, 0);

        Assert.Equal(115, LineOfSight.Clarity(map, new Vec2(50, 50), 160, new Vec2(1050, 1050), 160));
        Assert.Equal(155, LineOfSight.Clarity(map, new Vec2(50, 50), 160, new Vec2(1050, 50), 160));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~LineOfSightTests`
Expected: build FAILS with `The type or namespace name 'Vision' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Vision/LineOfSight.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

public static class LineOfSight
{
    public const int Clear = 255;

    /// <summary>
    /// How clearly a point at <paramref name="toHeightCm"/> can be seen from <paramref name="fromHeightCm"/>:
    /// 0 = blocked, 255 = clear. Heights are absolute (ground + body).
    /// </summary>
    public static int Clarity(GridMap map, Vec2 from, int fromHeightCm, Vec2 to, int toHeightCm)
    {
        var a = from.ToCell();
        var b = to.ToCell();
        int dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
        int steps = Math.Max(dx, dy);
        if (steps == 0)
            return Clear;

        int sx = Math.Sign(b.X - a.X), sy = Math.Sign(b.Y - a.Y);
        int err = dx - dy;
        int x = a.X, y = a.Y;
        int accumulated = 0;

        while (true)
        {
            int e2 = 2 * err;
            bool movedX = false, movedY = false;
            if (e2 > -dy) { err -= dy; x += sx; movedX = true; }
            if (e2 < dx) { err += dx; y += sy; movedY = true; }

            var cell = map[new CellCoord(x, y)];
            bool isTarget = x == b.X && y == b.Y;
            int progress = dx >= dy ? Math.Abs(x - a.X) : Math.Abs(y - a.Y);
            int rayHeight = fromHeightCm + (toHeightCm - fromHeightCm) * progress / steps;

            if (!isTarget && rayHeight <= cell.GroundHeightCm)
                return 0;
            if (rayHeight < cell.GroundHeightCm + cell.ObstacleHeightCm)
            {
                accumulated += cell.ConcealmentPerM * (movedX && movedY ? 141 : 100) / 100;
                if (accumulated >= Clear)
                    return 0;
            }
            if (isTarget)
                return Clear - accumulated;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~LineOfSightTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Vision/LineOfSight.cs src/Nmf.Sim.Tests/Vision/LineOfSightTests.cs
git commit -m "feat(sim): add line of sight with terrain, obstacles and concealment

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Side knowledge, spotting and hearing

**Files:**
- Create: `src/Nmf.Sim/Vision/SideKnowledge.cs`, `src/Nmf.Sim/Vision/VisionRules.cs`, `src/Nmf.Sim/Vision/VisionSystem.cs`
- Modify: `src/Nmf.Sim/Events/SimEvents.cs` (add `ContactChanged` and a using)
- Modify: `src/Nmf.Sim/Simulation.cs` (knowledge + vision step)
- Modify: `src/Nmf.Sim/Core/StateHash.cs` (hash knowledge)
- Test: `src/Nmf.Sim.Tests/Vision/VisionSystemTests.cs`

**Interfaces:**
- Consumes: `LineOfSight.Clarity` (Task 4), `StanceRules`, `Unit.IsMoving`, `Unit.MovedSinceVisionUpdate` (Task 3)
- Produces:
  - `enum ContactLevel : byte { Unknown, Suspected, LastKnown, Visible }`.
  - `Contact` with `UnitId Target`, `ContactLevel Level`, `Vec2 Position`, `long LastUpdateTick` and `int Progress` (0..1000).
  - `SideKnowledge` with `IEnumerable<Contact> Contacts` (in unit-id order), `Contact? Get(UnitId)` and `ContactLevel LevelOf(UnitId)`.
  - `Simulation.Knowledge(Side) → SideKnowledge`.
  - Event: `ContactChanged(long Tick, Side Observer, UnitId Target, ContactLevel Level, Vec2 Position)`.
  - `VisionRules` constants: `IntervalTicks = 5`, `MaxSightRangeCm = 40_000`, `SpottedThreshold = 1000`, `BaseGainPerUpdate = 250`, `DecayPerUpdate = 50`, `ReacquireProgress = 500`, `SuspectedTimeoutTicks = 200`, `SuspectedGridCm = 1000`.
  - `VisionRules` methods: `NoiseRadiusCm(MoveMode)` (walk 3000, run 6000, crawl 800), `MovementVisibilityPct(Unit)` (still 100, walk 200, run 300, crawl 120), `StanceVisibilityPct(Stance)` (100/60/30), `EyeHeightAbsCm(GridMap, Unit)` and `TargetHeightAbsCm(GridMap, Unit)` (ground + height − 10).
  - Vision runs inside `Step()` after movement, whenever `Tick % 5 == 0`. Per update:
    - Spotting: each enemy's gain is the best over all observers of `250 × clarity/255 × (range − distance)/range × movement% × stance%`.
    - A Visible contact stays Visible while any observer has clarity > 0 within range. Otherwise it becomes LastKnown and progress resets to 500.
    - Hearing: an enemy that moved since the last update and is within noise radius × cell MoveCostPct/100 of any listener becomes Suspected. Its position is set to the centre of its 10 m grid square.
    - A Suspected contact that is not refreshed for more than 200 ticks becomes Unknown.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Vision/VisionSystemTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class VisionSystemTests
{
    private static readonly Vec2 BluePos = new(50, 550);

    private static (Simulation Sim, Unit Blue, Unit Red) Pair(GridMap map, Vec2 redPos)
    {
        var sim = new Simulation(map, 1);
        var blue = sim.SpawnUnit(Side.Blue, BluePos, 7);
        var red = sim.SpawnUnit(Side.Red, redPos, 7);
        return (sim, blue, red);
    }

    private static List<SimEvent> StepN(Simulation sim, int n)
    {
        var events = new List<SimEvent>();
        for (int i = 0; i < n; i++) events.AddRange(sim.Step());
        return events;
    }

    [Fact]
    public void StandingEnemyInOpen_IsSpottedAfterFiveUpdates()
    {
        var (sim, _, red) = Pair(new GridMap(60, 10, ["none"]), new Vec2(2050, 550));

        StepN(sim, 16); // updates at ticks 0, 5, 10, 15: 4 x 237 = 948
        var knowledge = sim.Knowledge(Side.Blue);
        Assert.Equal(ContactLevel.Unknown, knowledge.LevelOf(red.Id));
        Assert.Equal(948, knowledge.Get(red.Id)!.Progress);

        var events = StepN(sim, 5); // update at tick 20
        Assert.Equal(ContactLevel.Visible, knowledge.LevelOf(red.Id));
        Assert.Contains<SimEvent>(new ContactChanged(20, Side.Blue, red.Id, ContactLevel.Visible, red.Position), events);
    }

    [Fact]
    public void EnemyBehindHill_IsNeverSpotted()
    {
        var map = new GridMap(60, 10, ["none"]);
        for (int y = 0; y < 10; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));

        StepN(sim, 200);

        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        Assert.Equal(0, sim.Knowledge(Side.Blue).Get(red.Id)!.Progress);
    }

    [Fact]
    public void ProneEnemyInBush_TakesMuchLongerThanStandingInOpen()
    {
        var bushMap = new GridMap(120, 10, ["none"]);
        bushMap[new CellCoord(100, 5)] = new CellData(0, 80, 153, 0, 0);
        var (hidden, _, proneRed) = Pair(bushMap, new Vec2(10050, 550));
        proneRed.Stance = Stance.Prone;

        var (open, _, standingRed) = Pair(new GridMap(120, 10, ["none"]), new Vec2(10050, 550));

        StepN(open, 30);
        Assert.Equal(ContactLevel.Visible, open.Knowledge(Side.Blue).LevelOf(standingRed.Id));

        StepN(hidden, 200);
        Assert.Equal(ContactLevel.Unknown, hidden.Knowledge(Side.Blue).LevelOf(proneRed.Id));
        Assert.InRange(hidden.Knowledge(Side.Blue).Get(proneRed.Id)!.Progress, 1, 999);
    }

    [Fact]
    public void LosingSight_TurnsContactIntoLastKnownAtLastSeenPosition()
    {
        var map = new GridMap(60, 10, ["none"]);
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        StepN(sim, 21);
        Assert.Equal(ContactLevel.Visible, sim.Knowledge(Side.Blue).LevelOf(red.Id));

        for (int y = 0; y < 10; y++)
            map[new CellCoord(10, y)] = map[new CellCoord(10, y)] with { ObstacleHeightCm = 300, ConcealmentPerM = 255 };
        var events = StepN(sim, 5);

        var contact = sim.Knowledge(Side.Blue).Get(red.Id)!;
        Assert.Equal(ContactLevel.LastKnown, contact.Level);
        Assert.Equal(new Vec2(2050, 550), contact.Position);
        Assert.Equal(VisionRules.ReacquireProgress, contact.Progress);
        Assert.Contains(events, e => e is ContactChanged { Observer: Side.Blue, Level: ContactLevel.LastKnown });
    }

    [Fact]
    public void RunningEnemyBehindHill_IsHeardAsRoughPositionThenForgotten()
    {
        var map = new GridMap(60, 10, ["none"]);
        for (int y = 0; y < 10; y++) map[new CellCoord(10, y)].GroundHeightCm = 300;
        var (sim, _, red) = Pair(map, new Vec2(2050, 550));
        sim.Submit(Side.Red, new MoveOrder(red.Id, new Vec2(2850, 550), MoveMode.Run));

        StepN(sim, 11);
        var contact = sim.Knowledge(Side.Blue).Get(red.Id)!;
        Assert.Equal(ContactLevel.Suspected, contact.Level);
        Assert.Equal(new Vec2(2500, 500), contact.Position);

        sim.Submit(Side.Red, new StopOrder(red.Id));
        StepN(sim, 220);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
    }

    [Fact]
    public void EnemyBeyondMaxRange_StaysUnknownWithZeroProgress()
    {
        var (sim, _, red) = Pair(new GridMap(420, 10, ["none"]), new Vec2(41050, 550));
        StepN(sim, 100);
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(red.Id));
        Assert.Equal(0, sim.Knowledge(Side.Blue).Get(red.Id)!.Progress);
    }

    [Fact]
    public void Contacts_AreEnumeratedInUnitIdOrder()
    {
        var sim = new Simulation(new GridMap(60, 10, ["none"]), 1);
        sim.SpawnUnit(Side.Blue, BluePos, 7);
        var ids = new[] { new Vec2(4050, 550), new Vec2(2050, 550), new Vec2(3050, 550) }
            .Select(p => sim.SpawnUnit(Side.Red, p, 7).Id).ToList();
        sim.Step();
        Assert.Equal(ids, sim.Knowledge(Side.Blue).Contacts.Select(c => c.Target));
    }

    [Fact]
    public void OwnSide_IsNeverAContact()
    {
        var sim = new Simulation(new GridMap(60, 10, ["none"]), 1);
        var a = sim.SpawnUnit(Side.Blue, BluePos, 7);
        var b = sim.SpawnUnit(Side.Blue, new Vec2(250, 550), 7);
        StepN(sim, 30);
        Assert.Empty(sim.Knowledge(Side.Blue).Contacts);
        Assert.Null(sim.Knowledge(Side.Blue).Get(b.Id));
        Assert.Equal(ContactLevel.Unknown, sim.Knowledge(Side.Blue).LevelOf(a.Id));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~VisionSystemTests`
Expected: build FAILS with `'Simulation' does not contain a definition for 'Knowledge'`.

- [ ] **Step 3: Implement knowledge types and rules**

`src/Nmf.Sim/Vision/SideKnowledge.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Vision;

public enum ContactLevel : byte
{
    Unknown = 0,
    Suspected = 1,
    LastKnown = 2,
    Visible = 3,
}

/// <summary>What one side believes about one enemy unit.</summary>
public sealed class Contact
{
    internal Contact(UnitId target) => Target = target;

    public UnitId Target { get; }
    public ContactLevel Level { get; internal set; }

    /// <summary>Visible: current position. LastKnown: where last seen. Suspected: centre of the 10 m square it was heard in.</summary>
    public Vec2 Position { get; internal set; }

    public long LastUpdateTick { get; internal set; }

    /// <summary>Spotting progress toward <see cref="VisionRules.SpottedThreshold"/>.</summary>
    public int Progress { get; internal set; }
}

public sealed class SideKnowledge
{
    private readonly SortedDictionary<int, Contact> _contacts = [];

    internal SideKnowledge()
    {
    }

    /// <summary>All contacts in unit-id order.</summary>
    public IEnumerable<Contact> Contacts => _contacts.Values;

    public Contact? Get(UnitId id) => _contacts.TryGetValue(id.Value, out var contact) ? contact : null;

    public ContactLevel LevelOf(UnitId id) => Get(id)?.Level ?? ContactLevel.Unknown;

    internal Contact GetOrAdd(UnitId id)
    {
        if (!_contacts.TryGetValue(id.Value, out var contact))
        {
            contact = new Contact(id);
            _contacts.Add(id.Value, contact);
        }
        return contact;
    }
}
```

`src/Nmf.Sim/Vision/VisionRules.cs`:
```csharp
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

/// <summary>Tunable numbers of the spotting and hearing model.</summary>
public static class VisionRules
{
    public const int IntervalTicks = 5;
    public const int MaxSightRangeCm = 40_000;
    public const int SpottedThreshold = 1000;
    public const int BaseGainPerUpdate = 250;
    public const int DecayPerUpdate = 50;
    public const int ReacquireProgress = 500;
    public const int SuspectedTimeoutTicks = 200;
    public const int SuspectedGridCm = 1000;

    public static int NoiseRadiusCm(MoveMode mode) => mode switch
    {
        MoveMode.Walk => 3000,
        MoveMode.Run => 6000,
        _ => 800,
    };

    /// <summary>Moving targets are easier to notice.</summary>
    public static int MovementVisibilityPct(Unit unit) => !unit.IsMoving ? 100 : unit.MoveMode switch
    {
        MoveMode.Walk => 200,
        MoveMode.Run => 300,
        _ => 120,
    };

    public static int StanceVisibilityPct(Stance stance) => stance switch
    {
        Stance.Standing => 100,
        Stance.Crouching => 60,
        _ => 30,
    };

    public static int EyeHeightAbsCm(GridMap map, Unit unit) =>
        map.CellAt(unit.Position).GroundHeightCm + StanceRules.EyeHeightCm(unit.Stance);

    /// <summary>Just below the top of the head.</summary>
    public static int TargetHeightAbsCm(GridMap map, Unit unit) =>
        map.CellAt(unit.Position).GroundHeightCm + StanceRules.HeightCm(unit.Stance) - 10;
}
```

`src/Nmf.Sim/Vision/VisionSystem.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;

namespace Nmf.Sim.Vision;

/// <summary>Updates both sides' knowledge; runs every <see cref="VisionRules.IntervalTicks"/> ticks.</summary>
internal static class VisionSystem
{
    private static readonly Side[] Sides = [Side.Blue, Side.Red];

    public static void Update(Simulation sim, long tick, List<SimEvent> events)
    {
        foreach (var side in Sides)
        {
            var knowledge = sim.Knowledge(side);
            foreach (var target in sim.Units)
            {
                if (target.Side == side)
                    continue;
                var contact = knowledge.GetOrAdd(target.Id);
                UpdateSight(sim, side, contact, target, tick, events);
                UpdateHearing(sim, side, contact, target, tick, events);
                if (contact.Level == ContactLevel.Suspected && tick - contact.LastUpdateTick > VisionRules.SuspectedTimeoutTicks)
                {
                    contact.Level = ContactLevel.Unknown;
                    events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Unknown, contact.Position));
                }
            }
        }
        foreach (var unit in sim.Units)
            unit.MovedSinceVisionUpdate = false;
    }

    private static void UpdateSight(Simulation sim, Side side, Contact contact, Unit target, long tick, List<SimEvent> events)
    {
        var map = sim.Map;
        int targetHeight = VisionRules.TargetHeightAbsCm(map, target);
        long bestGain = 0;
        bool seen = false;

        foreach (var observer in sim.Units)
        {
            if (observer.Side != side)
                continue;
            long distance = IntMath.Isqrt((target.Position - observer.Position).LengthSquared);
            if (distance > VisionRules.MaxSightRangeCm)
                continue;
            int clarity = LineOfSight.Clarity(map, observer.Position, VisionRules.EyeHeightAbsCm(map, observer), target.Position, targetHeight);
            if (clarity == 0)
                continue;
            seen = true;
            long gain = (long)VisionRules.BaseGainPerUpdate * clarity * (VisionRules.MaxSightRangeCm - distance)
                        * VisionRules.MovementVisibilityPct(target) * VisionRules.StanceVisibilityPct(target.Stance)
                        / (255L * VisionRules.MaxSightRangeCm * 100 * 100);
            bestGain = Math.Max(bestGain, gain);
        }

        if (contact.Level == ContactLevel.Visible)
        {
            if (seen)
            {
                contact.Position = target.Position;
                contact.LastUpdateTick = tick;
            }
            else
            {
                contact.Level = ContactLevel.LastKnown;
                contact.Progress = VisionRules.ReacquireProgress;
                events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.LastKnown, contact.Position));
            }
            return;
        }

        if (bestGain > 0)
        {
            contact.Progress = (int)Math.Min(VisionRules.SpottedThreshold, contact.Progress + bestGain);
            if (contact.Progress >= VisionRules.SpottedThreshold)
            {
                contact.Level = ContactLevel.Visible;
                contact.Position = target.Position;
                contact.LastUpdateTick = tick;
                events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Visible, contact.Position));
            }
        }
        else
        {
            contact.Progress = Math.Max(0, contact.Progress - VisionRules.DecayPerUpdate);
        }
    }

    private static void UpdateHearing(Simulation sim, Side side, Contact contact, Unit target, long tick, List<SimEvent> events)
    {
        if (contact.Level == ContactLevel.Visible || !target.MovedSinceVisionUpdate)
            return;

        long radius = (long)VisionRules.NoiseRadiusCm(target.MoveMode) * sim.Map.CellAt(target.Position).MoveCostPct / 100;
        bool heard = false;
        foreach (var listener in sim.Units)
        {
            if (listener.Side == side && (target.Position - listener.Position).LengthSquared <= radius * radius)
            {
                heard = true;
                break;
            }
        }
        if (!heard)
            return;

        var rough = RoughPosition(target.Position);
        bool changed = contact.Level != ContactLevel.Suspected || contact.Position != rough;
        contact.Level = ContactLevel.Suspected;
        contact.Position = rough;
        contact.LastUpdateTick = tick;
        if (changed)
            events.Add(new ContactChanged(tick, side, target.Id, ContactLevel.Suspected, rough));
    }

    private static Vec2 RoughPosition(Vec2 p)
    {
        const int grid = VisionRules.SuspectedGridCm;
        return new Vec2(IntMath.FloorDiv(p.X, grid) * grid + grid / 2, IntMath.FloorDiv(p.Y, grid) * grid + grid / 2);
    }
}
```

- [ ] **Step 4: Add the event, wire vision into the simulation, hash knowledge**

In `src/Nmf.Sim/Events/SimEvents.cs`, add `using Nmf.Sim.Vision;` to the usings and append:
```csharp

public sealed record ContactChanged(long Tick, Side Observer, UnitId Target, ContactLevel Level, Vec2 Position) : SimEvent(Tick);
```

In `src/Nmf.Sim/Simulation.cs`:
- Add `using Nmf.Sim.Vision;`.
- Add the field `private readonly SideKnowledge[] _knowledge = [new(), new()];` below `_orderLog`.
- Add the member `public SideKnowledge Knowledge(Side side) => _knowledge[(int)side];` below `OrderLog`.
- In `Step()`, between the movement loop and `Tick++;`, insert:
```csharp
        if (Tick % VisionRules.IntervalTicks == 0)
            VisionSystem.Update(this, Tick, events);
```

In `src/Nmf.Sim/Core/StateHash.cs`, add `using Nmf.Sim.Units;` at the top, and insert this before `return h.Value;` in `Compute`:
```csharp
        foreach (var side in new[] { Side.Blue, Side.Red })
        {
            foreach (var contact in sim.Knowledge(side).Contacts)
            {
                h.Add((int)side);
                h.Add(contact.Target.Value);
                h.Add((int)contact.Level);
                h.Add(contact.Position.X);
                h.Add(contact.Position.Y);
                h.Add(contact.Progress);
                h.Add((ulong)contact.LastUpdateTick);
            }
        }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: PASS, including phase 1 simulation tests with exact event lists (no vision events there, because those tests have at most one side with observers or no enemies in range of an update tick) and the replay tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(sim): side knowledge with spotting progress, hearing and contact events

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Viewshed for fog of war

**Files:**
- Create: `src/Nmf.Sim/Vision/Viewshed.cs`
- Test: `src/Nmf.Sim.Tests/Vision/ViewshedTests.cs`

**Interfaces:**
- Consumes: `GridMap`, `CellData`, `IntMath`
- Produces: `static class Viewshed` with `const int StandingTargetHeightCm = 170` and `void Compute(GridMap map, IEnumerable<(Vec2 Position, int EyeHeightAbsCm)> observers, int rangeCm, bool[] visible)`.
  - `visible` is indexed `y * map.Width + x` and cleared first. A wrong buffer size throws `ArgumentException`.
  - Result: a cell is marked when a standing man there could be seen by at least one observer. The observer's own cell is always marked.
  - How it works: rays are cast from each observer's cell to every cell on the square perimeter at the range radius, and each ray stops at the circular range. Ground blocks through a maximum-slope test. Cells whose obstacle height is ≥ 100 cm add concealment. A ray stops once concealment reaches 255, but the blocking cell itself is still marked.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Vision/ViewshedTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Vision;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Vision;

public class ViewshedTests
{
    private static readonly Vec2 Center = new(1050, 1050); // cell (10,10)

    private static bool[] Compute(GridMap map, int rangeCm = 500)
    {
        var visible = new bool[map.Width * map.Height];
        Viewshed.Compute(map, [(Center, 160)], rangeCm, visible);
        return visible;
    }

    private static bool At(bool[] visible, GridMap map, int x, int y) => visible[y * map.Width + x];

    [Fact]
    public void OpenGround_IsVisibleWithinRangeOnly()
    {
        var map = new GridMap(21, 21, ["none"]);
        var visible = Compute(map);
        Assert.True(At(visible, map, 10, 10));
        Assert.True(At(visible, map, 14, 10));
        Assert.True(At(visible, map, 15, 10));
        Assert.False(At(visible, map, 16, 10));
        Assert.False(At(visible, map, 14, 14)); // about 5.7 m away
    }

    [Fact]
    public void OpaqueWall_HidesCellsBehindIt()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)] = new CellData(0, 300, 255, 230, 0);
        var visible = Compute(map);
        Assert.True(At(visible, map, 11, 10));
        Assert.True(At(visible, map, 12, 10));
        Assert.False(At(visible, map, 13, 10));
    }

    [Fact]
    public void Hill_HidesLowGroundBehindIt()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)].GroundHeightCm = 300;
        var visible = Compute(map);
        Assert.True(At(visible, map, 12, 10));
        Assert.False(At(visible, map, 14, 10));
    }

    [Fact]
    public void LowBushes_DoNotBlockView()
    {
        var map = new GridMap(21, 21, ["none"]);
        for (int y = 0; y < 21; y++) map[new CellCoord(12, y)] = new CellData(0, 80, 255, 0, 0);
        Assert.True(At(Compute(map), map, 14, 10));
    }

    [Fact]
    public void WrongBufferSize_Throws()
    {
        var map = new GridMap(5, 5, ["none"]);
        Assert.Throws<ArgumentException>(() => Viewshed.Compute(map, [], 500, new bool[3]));
    }

    [Fact]
    public void ObserverNearEdge_DoesNotThrow()
    {
        var map = new GridMap(5, 5, ["none"]);
        var visible = new bool[25];
        Viewshed.Compute(map, [(new Vec2(10, 10), 160)], 2000, visible);
        Assert.True(visible[0]);
        Assert.True(visible[24]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~ViewshedTests`
Expected: build FAILS with `The name 'Viewshed' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Vision/Viewshed.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Vision;

/// <summary>Cells in which a standing man could be seen by at least one observer; used to draw the fog of war.</summary>
public static class Viewshed
{
    public const int StandingTargetHeightCm = 170;
    private const int BlockingObstacleHeightCm = 100;

    public static void Compute(GridMap map, IEnumerable<(Vec2 Position, int EyeHeightAbsCm)> observers, int rangeCm, bool[] visible)
    {
        if (visible.Length != map.Width * map.Height)
            throw new ArgumentException("Buffer size must match the map.", nameof(visible));
        Array.Clear(visible);
        int radius = rangeCm / SimConstants.CentimetersPerCell;

        foreach (var (position, eye) in observers)
        {
            var origin = position.ToCell();
            if (!map.InBounds(origin))
                continue;
            visible[origin.Y * map.Width + origin.X] = true;
            for (int i = -radius; i <= radius; i++)
            {
                CastRay(map, origin, eye, new CellCoord(origin.X + i, origin.Y - radius), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X + i, origin.Y + radius), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X - radius, origin.Y + i), radius, visible);
                CastRay(map, origin, eye, new CellCoord(origin.X + radius, origin.Y + i), radius, visible);
            }
        }
    }

    private static void CastRay(GridMap map, CellCoord from, int eye, CellCoord to, int radius, bool[] visible)
    {
        int dx = Math.Abs(to.X - from.X), dy = Math.Abs(to.Y - from.Y);
        int sx = Math.Sign(to.X - from.X), sy = Math.Sign(to.Y - from.Y);
        int err = dx - dy;
        int x = from.X, y = from.Y;
        long radiusSquared = (long)radius * radius;

        // Steepest ground slope seen so far along the ray, as slopeNum / slopeDen (cm / cm).
        bool hasSlope = false;
        long slopeNum = 0, slopeDen = 1;
        int concealment = 0;

        while (x != to.X || y != to.Y)
        {
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }

            var cellCoord = new CellCoord(x, y);
            if (!map.InBounds(cellCoord))
                return;
            long cx = x - from.X, cy = y - from.Y;
            long distanceSquared = cx * cx + cy * cy;
            if (distanceSquared > radiusSquared)
                return;

            long distance = IntMath.Isqrt(distanceSquared * 10_000); // centimetres
            var cell = map[cellCoord];
            long targetNum = cell.GroundHeightCm + StandingTargetHeightCm - eye;
            if (!hasSlope || targetNum * slopeDen >= slopeNum * distance)
                visible[y * map.Width + x] = true;

            long groundNum = cell.GroundHeightCm - eye;
            if (!hasSlope || groundNum * slopeDen > slopeNum * distance)
            {
                hasSlope = true;
                slopeNum = groundNum;
                slopeDen = distance;
            }

            if (cell.ObstacleHeightCm >= BlockingObstacleHeightCm)
            {
                concealment += cell.ConcealmentPerM;
                if (concealment >= LineOfSight.Clear)
                    return;
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~ViewshedTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Vision/Viewshed.cs src/Nmf.Sim.Tests/Vision/ViewshedTests.cs
git commit -m "feat(sim): add viewshed for fog of war

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Patrol behaviour and skirmish scenario

**Files:**
- Create: `src/Nmf.Sim/AI/PatrolBehavior.cs`, `src/Nmf.Sim/Scenarios/Scenario.cs`, `src/Nmf.Sim/Scenarios/SkirmishScenario.cs`
- Test: `src/Nmf.Sim.Tests/AI/PatrolBehaviorTests.cs`, `src/Nmf.Sim.Tests/Scenarios/SkirmishScenarioTests.cs`

**Interfaces:**
- Consumes: `Simulation.Submit`, `MoveOrder` (Task 3), `MapFeatures` (phase 1)
- Produces:
  - `sealed class PatrolBehavior(UnitId unit, IReadOnlyList<Vec2> points, MoveMode mode = MoveMode.Walk)` with `UnitId Unit` and `void Tick(Simulation sim)`.
    - It walks ping-pong: 0, 1, …, n−1, n−2, …, 0, 1, …
    - It submits the next leg only when the unit is idle (no move target and no stance change).
    - It throws for fewer than 2 points.
  - `sealed class Scenario` with `Simulation Sim`, `IReadOnlyList<PatrolBehavior> Patrols` and `void Tick()` (call once before every `Sim.Step()`).
  - `static class SkirmishScenario` with `const int SoldierWalkSpeedCmPerTick = 7` (1.4 m/s) and `Scenario Create(GridMap map, ulong seed)`.
    - Map points of type `"blue"` / `"red"` spawn soldiers of that side, in map order.
    - Each path of type `"patrol"` gets the nearest red soldier that is not yet patrolling, measured to its first point (ties go to the lowest id).
    - Other types are ignored.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/AI/PatrolBehaviorTests.cs`:
```csharp
using Nmf.Sim.AI;
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.AI;

public class PatrolBehaviorTests
{
    [Fact]
    public void Patrol_WalksPingPongAlongPoints()
    {
        var sim = new Simulation(new GridMap(30, 30, ["none"]), 1);
        var unit = sim.SpawnUnit(Side.Red, new Vec2(150, 150), 20);
        Vec2[] points = [new(150, 150), new(950, 150), new(950, 950)];
        var patrol = new PatrolBehavior(unit.Id, points);

        var arrivals = new List<Vec2>();
        for (int i = 0; i < 2000 && arrivals.Count < 5; i++)
        {
            patrol.Tick(sim);
            foreach (var e in sim.Step())
                if (e is UnitArrived arrived) arrivals.Add(arrived.Position);
        }

        Assert.Equal(new[] { points[0], points[1], points[2], points[1], points[0] }, arrivals);
    }

    [Fact]
    public void Patrol_NeedsTwoPoints()
    {
        Assert.Throws<ArgumentException>(() => new PatrolBehavior(new UnitId(1), [new Vec2(0, 0)]));
    }

    [Fact]
    public void Patrol_ForMissingUnit_DoesNothing()
    {
        var sim = new Simulation(new GridMap(5, 5, ["none"]), 1);
        new PatrolBehavior(new UnitId(9), [new Vec2(50, 50), new Vec2(150, 50)]).Tick(sim);
        sim.Step();
        Assert.Empty(sim.OrderLog);
    }
}
```

`src/Nmf.Sim.Tests/Scenarios/SkirmishScenarioTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Scenarios;

public class SkirmishScenarioTests
{
    private static GridMap MapWith(IReadOnlyList<MapPoint> points, IReadOnlyList<MapPath> paths) =>
        new(40, 40, ["none"], new MapFeatures([], points, paths));

    [Fact]
    public void Create_SpawnsUnitsFromTypedPoints()
    {
        var map = MapWith(
            [
                new MapPoint("b1", "blue", new Vec2(150, 150)),
                new MapPoint("r1", "red", new Vec2(3500, 3500)),
                new MapPoint("b2", "blue", new Vec2(350, 150)),
                new MapPoint("x", "spawn", new Vec2(50, 50)),
            ],
            []);

        var scenario = SkirmishScenario.Create(map, 3);

        Assert.Equal(new[] { Side.Blue, Side.Red, Side.Blue }, scenario.Sim.Units.Select(u => u.Side));
        Assert.Equal(new Vec2(350, 150), scenario.Sim.Units[2].Position);
        Assert.All(scenario.Sim.Units, u => Assert.Equal(SkirmishScenario.SoldierWalkSpeedCmPerTick, u.SpeedCmPerTick));
        Assert.Equal(3UL, scenario.Sim.Seed);
    }

    [Fact]
    public void Create_AssignsPatrolToNearestRedUnit()
    {
        var map = MapWith(
            [
                new MapPoint("r1", "red", new Vec2(3500, 3500)),
                new MapPoint("r2", "red", new Vec2(1000, 1000)),
                new MapPoint("b1", "blue", new Vec2(1100, 1000)),
            ],
            [new MapPath("p", "patrol", [new Vec2(1200, 1200), new Vec2(2000, 1200)])]);

        var scenario = SkirmishScenario.Create(map, 1);

        var patrol = Assert.Single(scenario.Patrols);
        Assert.Equal(new UnitId(2), patrol.Unit);
    }

    [Fact]
    public void Tick_DrivesPatrols()
    {
        var map = MapWith(
            [new MapPoint("r1", "red", new Vec2(1000, 1000))],
            [new MapPath("p", "patrol", [new Vec2(1000, 1000), new Vec2(2000, 1000)])]);
        var scenario = SkirmishScenario.Create(map, 1);

        for (int i = 0; i < 20; i++)
        {
            scenario.Tick();
            scenario.Sim.Step();
        }

        Assert.True(scenario.Sim.Units[0].Position.X > 1000);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter "FullyQualifiedName~PatrolBehaviorTests|FullyQualifiedName~SkirmishScenarioTests"`
Expected: build FAILS with `The type or namespace name 'AI' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/AI/PatrolBehavior.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;

namespace Nmf.Sim.AI;

/// <summary>Walks a unit back and forth along a list of points. Issues ordinary orders, like a player would.</summary>
public sealed class PatrolBehavior
{
    private readonly IReadOnlyList<Vec2> _points;
    private readonly MoveMode _mode;
    private int _next;
    private int _direction = 1;

    public PatrolBehavior(UnitId unit, IReadOnlyList<Vec2> points, MoveMode mode = MoveMode.Walk)
    {
        if (points.Count < 2)
            throw new ArgumentException("A patrol needs at least two points.", nameof(points));
        Unit = unit;
        _points = points;
        _mode = mode;
    }

    public UnitId Unit { get; }

    /// <summary>Call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick(Simulation sim)
    {
        var unit = sim.FindUnit(Unit);
        if (unit is null || unit.MoveTarget is not null || unit.TargetStance is not null)
            return;

        sim.Submit(unit.Side, new MoveOrder(Unit, _points[_next], _mode));
        if (_next + _direction < 0 || _next + _direction >= _points.Count)
            _direction = -_direction;
        _next += _direction;
    }
}
```

`src/Nmf.Sim/Scenarios/Scenario.cs`:
```csharp
using Nmf.Sim.AI;

namespace Nmf.Sim.Scenarios;

/// <summary>A simulation plus the scripted behaviours that drive it.</summary>
public sealed class Scenario
{
    internal Scenario(Simulation sim, IReadOnlyList<PatrolBehavior> patrols)
    {
        Sim = sim;
        Patrols = patrols;
    }

    public Simulation Sim { get; }
    public IReadOnlyList<PatrolBehavior> Patrols { get; }

    /// <summary>Runs scripted behaviours; call once before every <see cref="Simulation.Step"/>.</summary>
    public void Tick()
    {
        foreach (var patrol in Patrols)
            patrol.Tick(Sim);
    }
}
```

`src/Nmf.Sim/Scenarios/SkirmishScenario.cs`:
```csharp
using Nmf.Sim.AI;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Scenarios;

/// <summary>Builds the phase 2 test skirmish from map points ("blue", "red") and paths ("patrol").</summary>
public static class SkirmishScenario
{
    public const int SoldierWalkSpeedCmPerTick = 7;
    public const string BluePointType = "blue";
    public const string RedPointType = "red";
    public const string PatrolPathType = "patrol";

    public static Scenario Create(GridMap map, ulong seed)
    {
        var sim = new Simulation(map, seed);
        foreach (var point in map.Features.Points)
        {
            if (point.Type == BluePointType)
                sim.SpawnUnit(Side.Blue, point.Position, SoldierWalkSpeedCmPerTick);
            else if (point.Type == RedPointType)
                sim.SpawnUnit(Side.Red, point.Position, SoldierWalkSpeedCmPerTick);
        }

        var patrols = new List<PatrolBehavior>();
        var assigned = new HashSet<UnitId>();
        foreach (var path in map.Features.Paths.Where(p => p.Type == PatrolPathType))
        {
            var start = path.Points[0];
            var unit = sim.Units
                .Where(u => u.Side == Side.Red && !assigned.Contains(u.Id))
                .OrderBy(u => (u.Position - start).LengthSquared)
                .ThenBy(u => u.Id.Value)
                .FirstOrDefault();
            if (unit is null)
                break;
            assigned.Add(unit.Id);
            patrols.Add(new PatrolBehavior(unit.Id, path.Points));
        }
        return new Scenario(sim, patrols);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/AI src/Nmf.Sim/Scenarios src/Nmf.Sim.Tests/AI src/Nmf.Sim.Tests/Scenarios
git commit -m "feat(sim): patrol behaviour and skirmish scenario builder

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Skirmish test map

**Files:**
- Create: `tools/make_skirmish_map.py`, `content/core/maps/skirmish.tmx` (generated)
- Test: `src/Nmf.Content.Tests/CoreContentTests.cs` (append)

**Interfaces:**
- Consumes: core tilesets (Task 1), `TmxMapLoader`, `Pathfinder` (Task 2), `SkirmishScenario` type names (Task 7)
- Produces: `content/core/maps/skirmish.tmx`, a 128 × 96 map containing:
  - forest, swamp, a winding road, two hills (0–3 m), rocks and bushes
  - 4 points of type `blue` (`blue_1..4`, in the south)
  - 5 points of type `red` (`red_1..5`, in the north)
  - 1 polyline of type `patrol` (`patrol_north`)

  Every blue spawn can reach every red spawn and every patrol point.

- [ ] **Step 1: Write the failing test**

Append to the class in `src/Nmf.Content.Tests/CoreContentTests.cs` (add `using Nmf.Sim.World;` at the top):
```csharp
    [Fact]
    public void SkirmishMap_HasSpawnsPatrolAndConnectedPaths()
    {
        var map = TmxMapLoader.Load(Path.Combine(RepoRoot(), "content", "core", "maps", "skirmish.tmx"));
        Assert.Equal(128, map.Width);
        Assert.Equal(96, map.Height);

        var blue = map.Features.Points.Where(p => p.Type == "blue").ToList();
        var red = map.Features.Points.Where(p => p.Type == "red").ToList();
        Assert.Equal(4, blue.Count);
        Assert.Equal(5, red.Count);
        var patrol = Assert.Single(map.Features.Paths, p => p.Type == "patrol");

        foreach (var from in blue)
        {
            Assert.True(map.CellAt(from.Position).IsPassable, $"{from.Name} stands on an impassable cell");
            foreach (var to in red.Select(r => r.Position).Concat(patrol.Points))
                Assert.NotNull(Pathfinder.FindPath(map, from.Position, to));
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test src/Nmf.Content.Tests --filter FullyQualifiedName~SkirmishMap`
Expected: FAIL with `MapLoadException` ... `skirmish.tmx: file not found`.

- [ ] **Step 3: Write the generator**

`tools/make_skirmish_map.py`:
```python
"""Generates content/core/maps/skirmish.tmx, the 128 x 96 phase 2 test map. Deterministic (fixed seed).

Run from the repository root: python3 tools/make_skirmish_map.py
"""
import math
import pathlib
import random

W, H, TILE = 128, 96, 16
GRASS, FOREST, SWAMP, ROAD = 1, 2, 3, 4  # terrain.tsx, firstgid 1
HEIGHT_FIRST_GID = 5                      # heights.tsx: gids 5..8 = 0..3 m
ROCK, BUSH = 9, 10                        # obstacles.tsx, firstgid 9

BLUE = [(58, 88), (62, 89), (66, 88), (70, 89)]
RED = [(52, 14), (60, 12), (68, 14), (76, 16), (45, 22)]
PATROL = [(45, 22), (85, 22), (85, 34)]
OUT = pathlib.Path("content/core/maps/skirmish.tmx")


def generate():
    rng = random.Random(1942)
    terrain = [[GRASS] * W for _ in range(H)]

    def blob(kind, cx, cy, r):
        for y in range(max(0, cy - r), min(H, cy + r + 1)):
            for x in range(max(0, cx - r), min(W, cx + r + 1)):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r * (0.7 + 0.3 * rng.random()):
                    terrain[y][x] = kind

    for _ in range(16):
        blob(FOREST, rng.randrange(W), rng.randrange(H), rng.randint(5, 13))
    for _ in range(4):
        blob(SWAMP, rng.randrange(W), rng.randrange(20, H - 20), rng.randint(4, 8))
    for y in range(H):
        cx = int(64 + 10 * math.sin(y / 14.0))
        for x in (cx, cx + 1):
            terrain[y][x] = ROAD

    def hill(x, y, cx, cy, r, peak):
        return peak * math.exp(-((x - cx) ** 2 + (y - cy) ** 2) / (2 * r * r))

    height = [
        [min(3, int(round(hill(x, y, 36, 44, 12, 3.4) + hill(x, y, 96, 58, 9, 2.6)))) for x in range(W)]
        for y in range(H)
    ]

    keep_clear = BLUE + RED + PATROL

    def near_clear(x, y):
        return any((x - cx) ** 2 + (y - cy) ** 2 <= 9 for cx, cy in keep_clear)

    obstacles = [[0] * W for _ in range(H)]

    def scatter(kind, count):
        placed = 0
        while placed < count:
            x, y = rng.randrange(W), rng.randrange(H)
            if terrain[y][x] == ROAD or obstacles[y][x] or near_clear(x, y):
                continue
            obstacles[y][x] = kind
            placed += 1

    scatter(ROCK, 60)
    scatter(BUSH, 220)
    return terrain, height, obstacles


def csv(rows):
    return ",\n".join(",".join(str(v) for v in row) for row in rows)


def objects():
    lines = []
    oid = 1
    for side, points in (("blue", BLUE), ("red", RED)):
        for i, (x, y) in enumerate(points, start=1):
            lines.append(
                f'  <object id="{oid}" name="{side}_{i}" type="{side}" x="{x * TILE + 8}" y="{y * TILE + 8}">\n'
                f"   <point/>\n  </object>"
            )
            oid += 1
    x0, y0 = PATROL[0]
    rel = " ".join(f"{(x - x0) * TILE},{(y - y0) * TILE}" for x, y in PATROL)
    lines.append(
        f'  <object id="{oid}" name="patrol_north" type="patrol" x="{x0 * TILE + 8}" y="{y0 * TILE + 8}">\n'
        f'   <polyline points="{rel}"/>\n  </object>'
    )
    return "\n".join(lines), oid + 1


def main():
    terrain, height, obstacles = generate()
    height_gids = [[HEIGHT_FIRST_GID + h for h in row] for row in height]
    object_xml, next_id = objects()
    tmx = f"""<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="{W}" height="{H}" tilewidth="{TILE}" tileheight="{TILE}" infinite="0" nextlayerid="5" nextobjectid="{next_id}">
 <tileset firstgid="1" source="../tilesets/terrain.tsx"/>
 <tileset firstgid="5" source="../tilesets/heights.tsx"/>
 <tileset firstgid="9" source="../tilesets/obstacles.tsx"/>
 <layer id="1" name="terrain" width="{W}" height="{H}">
  <data encoding="csv">
{csv(terrain)}
</data>
 </layer>
 <layer id="2" name="height" width="{W}" height="{H}">
  <data encoding="csv">
{csv(height_gids)}
</data>
 </layer>
 <layer id="3" name="obstacles" width="{W}" height="{H}">
  <data encoding="csv">
{csv(obstacles)}
</data>
 </layer>
 <objectgroup id="4" name="ai">
{object_xml}
 </objectgroup>
</map>
"""
    OUT.write_text(tmx)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
```

- [ ] **Step 4: Generate the map and run the tests**

Run: `python3 tools/make_skirmish_map.py && dotnet test src/Nmf.Content.Tests`
Expected: `wrote content/core/maps/skirmish.tmx`, then PASS, including `AllCoreMaps_Load`, which now also loads the skirmish map.
Also run: `dotnet run --project src/Nmf.Cli -- map-info content/core/maps/skirmish.tmx`
Expected: `Size: 128 x 96 cells`, four terrain types, `Points (9)`, `Paths (1): patrol_north`.

- [ ] **Step 5: Commit**

```bash
git add tools/make_skirmish_map.py content/core/maps/skirmish.tmx src/Nmf.Content.Tests/CoreContentTests.cs
git commit -m "feat(content): generated 128x96 skirmish test map

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Client library – selection, formation, palette, content locator

**Files:**
- Create: `src/Nmf.Client/Nmf.Client.csproj`, `src/Nmf.Client/Selection.cs`, `src/Nmf.Client/Formation.cs`, `src/Nmf.Client/TerrainPalette.cs`, `src/Nmf.Client/ContentLocator.cs`
- Create: `src/Nmf.Client.Tests/Nmf.Client.Tests.csproj`
- Modify: `NoMansForest.slnx`
- Test: `src/Nmf.Client.Tests/SelectionTests.cs`, `FormationTests.cs`, `TerrainPaletteTests.cs`, `ContentLocatorTests.cs`

**Interfaces:**
- Consumes: `Unit`, `Side`, `Vec2`, `GridMap`, `CellData`
- Produces:
  - `sealed class Selection` with `IReadOnlyList<UnitId> Ids` (in id order), `int Count`, `bool Contains(UnitId)`, `void Clear()` and two selection methods:
    - `bool SelectAt(IEnumerable<Unit> units, Side side, Vec2 point, int radiusCm, bool additive)`: nearest own unit within the radius, ties go to the lowest id. When not additive it replaces the selection, and a click that hits nothing clears it.
    - `int SelectInBox(IEnumerable<Unit> units, Side side, Vec2 a, Vec2 b, bool additive)`: own units inside the rectangle, corners in any order. Returns the count added.
  - `static class Formation` with `const int SpacingCm = 200` and `IReadOnlyList<Vec2> Offsets(int count)`. Offsets form square rings on a 2 m grid, the centre first, sorted by distance, then Y, then X.
  - `readonly record struct Rgb(byte R, byte G, byte B)` and `static class TerrainPalette` with two methods:
    - `Rgb ColorFor(string terrain)`: grass, forest, swamp and road have colours; anything else is magenta.
    - `Rgb CellColor(GridMap map, CellCoord cell)`: impassable cells are rock grey and low obstacles blend with bush green. Shading is 88–100 % by height, plus a deterministic ±4 % jitter.
  - `static class ContentLocator` with `string? FindContentRoot(string startDirectory)`. It returns `<ancestor>/content` for the nearest ancestor that has a `content/core` directory, or null.

- [ ] **Step 1: Create the projects**

Run from repo root:
```bash
dotnet new classlib -n Nmf.Client -o src/Nmf.Client
sed -i '' 's|<TargetFramework>net10.0</TargetFramework>|<TargetFramework>net8.0</TargetFramework>|' src/Nmf.Client/Nmf.Client.csproj
rm src/Nmf.Client/Class1.cs
dotnet new xunit -n Nmf.Client.Tests -o src/Nmf.Client.Tests -f net10.0
rm src/Nmf.Client.Tests/UnitTest1.cs
dotnet add src/Nmf.Client reference src/Nmf.Sim
dotnet add src/Nmf.Client.Tests reference src/Nmf.Client
dotnet sln NoMansForest.slnx add src/Nmf.Client src/Nmf.Client.Tests
```
Expected: all succeed; `src/Nmf.Client.Tests/Nmf.Client.Tests.csproj` contains `<Using Include="Xunit" />` (add it if missing).

- [ ] **Step 2: Write the failing tests**

`src/Nmf.Client.Tests/SelectionTests.cs`:
```csharp
using Nmf.Client;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class SelectionTests
{
    private static Simulation NewSim(out Unit a, out Unit b, out Unit enemy)
    {
        var sim = new Simulation(new GridMap(40, 40, ["none"]), 1);
        a = sim.SpawnUnit(Side.Blue, new Vec2(500, 500), 7);
        b = sim.SpawnUnit(Side.Blue, new Vec2(800, 500), 7);
        enemy = sim.SpawnUnit(Side.Red, new Vec2(560, 500), 7);
        return sim;
    }

    [Fact]
    public void SelectAt_PicksNearestOwnUnitWithinRadius()
    {
        var sim = NewSim(out var a, out _, out _);
        var selection = new Selection();
        Assert.True(selection.SelectAt(sim.Units, Side.Blue, new Vec2(580, 500), 150, additive: false));
        Assert.Equal(new[] { a.Id }, selection.Ids);
    }

    [Fact]
    public void SelectAt_EmptyGround_ClearsSelection()
    {
        var sim = NewSim(out _, out _, out _);
        var selection = new Selection();
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(500, 500), 150, false);
        Assert.False(selection.SelectAt(sim.Units, Side.Blue, new Vec2(3000, 3000), 150, false));
        Assert.Equal(0, selection.Count);
    }

    [Fact]
    public void SelectAt_Additive_KeepsExistingSelection()
    {
        var sim = NewSim(out var a, out var b, out _);
        var selection = new Selection();
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(500, 500), 150, false);
        selection.SelectAt(sim.Units, Side.Blue, new Vec2(800, 500), 150, true);
        Assert.Equal(new[] { a.Id, b.Id }, selection.Ids);
    }

    [Fact]
    public void SelectInBox_SelectsOnlyOwnUnits_CornersInAnyOrder()
    {
        var sim = NewSim(out var a, out var b, out var enemy);
        var selection = new Selection();
        int added = selection.SelectInBox(sim.Units, Side.Blue, new Vec2(900, 600), new Vec2(400, 400), false);
        Assert.Equal(2, added);
        Assert.Equal(new[] { a.Id, b.Id }, selection.Ids);
        Assert.False(selection.Contains(enemy.Id));
    }
}
```

`src/Nmf.Client.Tests/FormationTests.cs`:
```csharp
using Nmf.Client;
using Nmf.Sim.Core;

namespace Nmf.Client.Tests;

public class FormationTests
{
    [Fact]
    public void Offsets_StartAtCentreThenNearestRing()
    {
        Assert.Equal(
            new[] { new Vec2(0, 0), new Vec2(0, -200), new Vec2(-200, 0), new Vec2(200, 0), new Vec2(0, 200) },
            Formation.Offsets(5));
    }

    [Fact]
    public void Offsets_ZeroCount_IsEmpty()
    {
        Assert.Empty(Formation.Offsets(0));
    }

    [Fact]
    public void Offsets_AreDistinctForLargeGroups()
    {
        var offsets = Formation.Offsets(30);
        Assert.Equal(30, offsets.Count);
        Assert.Equal(30, offsets.Distinct().Count());
    }
}
```

`src/Nmf.Client.Tests/TerrainPaletteTests.cs`:
```csharp
using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class TerrainPaletteTests
{
    [Fact]
    public void ColorFor_UnknownTerrain_IsMagenta()
    {
        Assert.Equal(new Rgb(255, 0, 255), TerrainPalette.ColorFor("lava"));
        Assert.NotEqual(TerrainPalette.ColorFor("grass"), TerrainPalette.ColorFor("forest"));
    }

    [Fact]
    public void CellColor_HigherGround_IsLighter()
    {
        var low = new GridMap(3, 3, ["none", "grass"]);
        var high = new GridMap(3, 3, ["none", "grass"]);
        low[new CellCoord(1, 1)] = new CellData(0, 0, 0, 0, 1);
        high[new CellCoord(1, 1)] = new CellData(300, 0, 0, 0, 1);
        var a = TerrainPalette.CellColor(low, new CellCoord(1, 1));
        var b = TerrainPalette.CellColor(high, new CellCoord(1, 1));
        Assert.True(b.G > a.G);
    }

    [Fact]
    public void CellColor_IsDeterministic_AndRockIsGrey()
    {
        var map = new GridMap(3, 3, ["none", "grass"]);
        map[new CellCoord(2, 2)] = new CellData(0, 120, 255, 230, 1, CellData.Impassable);
        var rock = TerrainPalette.CellColor(map, new CellCoord(2, 2));
        Assert.Equal(rock, TerrainPalette.CellColor(map, new CellCoord(2, 2)));
        Assert.True(Math.Abs(rock.R - rock.G) < 8 && Math.Abs(rock.G - rock.B) < 8);
    }
}
```

`src/Nmf.Client.Tests/ContentLocatorTests.cs`:
```csharp
using Nmf.Client;

namespace Nmf.Client.Tests;

public class ContentLocatorTests
{
    [Fact]
    public void FindContentRoot_WalksUpToContentCore()
    {
        var root = Directory.CreateTempSubdirectory("nmf-locator-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "content", "core"));
            var nested = Directory.CreateDirectory(Path.Combine(root, "src", "Nmf.Game", "bin")).FullName;
            Assert.Equal(Path.Combine(root, "content"), ContentLocator.FindContentRoot(nested));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindContentRoot_NoContent_ReturnsNull()
    {
        var root = Directory.CreateTempSubdirectory("nmf-locator-").FullName;
        try
        {
            Assert.Null(ContentLocator.FindContentRoot(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Client.Tests`
Expected: build FAILS with `The type or namespace name 'Selection' could not be found`.

- [ ] **Step 4: Implement**

`src/Nmf.Client/Selection.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Client;

/// <summary>The player's selected units, kept in unit-id order.</summary>
public sealed class Selection
{
    private readonly SortedSet<int> _ids = [];

    public IReadOnlyList<UnitId> Ids => _ids.Select(id => new UnitId(id)).ToList();
    public int Count => _ids.Count;

    public bool Contains(UnitId id) => _ids.Contains(id.Value);

    public void Clear() => _ids.Clear();

    public bool SelectAt(IEnumerable<Unit> units, Side side, Vec2 point, int radiusCm, bool additive)
    {
        if (!additive)
            Clear();
        long radiusSquared = (long)radiusCm * radiusCm;
        var nearest = units
            .Where(u => u.Side == side && (u.Position - point).LengthSquared <= radiusSquared)
            .OrderBy(u => (u.Position - point).LengthSquared)
            .ThenBy(u => u.Id.Value)
            .FirstOrDefault();
        if (nearest is null)
            return false;
        _ids.Add(nearest.Id.Value);
        return true;
    }

    public int SelectInBox(IEnumerable<Unit> units, Side side, Vec2 a, Vec2 b, bool additive)
    {
        if (!additive)
            Clear();
        int minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
        int minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        int added = 0;
        foreach (var unit in units)
        {
            var p = unit.Position;
            if (unit.Side == side && p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY && _ids.Add(unit.Id.Value))
                added++;
        }
        return added;
    }
}
```

`src/Nmf.Client/Formation.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Client;

/// <summary>Spreads a group move over nearby spots so soldiers do not stack on one point.</summary>
public static class Formation
{
    public const int SpacingCm = 200;

    public static IReadOnlyList<Vec2> Offsets(int count)
    {
        var result = new List<Vec2>(count);
        for (int ring = 0; result.Count < count; ring++)
        {
            var cells = new List<Vec2>();
            for (int y = -ring; y <= ring; y++)
                for (int x = -ring; x <= ring; x++)
                    if (Math.Max(Math.Abs(x), Math.Abs(y)) == ring)
                        cells.Add(new Vec2(x * SpacingCm, y * SpacingCm));

            foreach (var offset in cells.OrderBy(o => o.LengthSquared).ThenBy(o => o.Y).ThenBy(o => o.X))
            {
                if (result.Count == count)
                    break;
                result.Add(offset);
            }
        }
        return result;
    }
}
```

`src/Nmf.Client/TerrainPalette.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Client;

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>Placeholder map colours until real tile art exists.</summary>
public static class TerrainPalette
{
    private static readonly Rgb Rock = new(128, 128, 126);
    private static readonly Rgb Bush = new(40, 90, 35);

    public static Rgb ColorFor(string terrain) => terrain switch
    {
        "grass" => new Rgb(104, 138, 66),
        "forest" => new Rgb(46, 82, 44),
        "swamp" => new Rgb(88, 104, 84),
        "road" => new Rgb(150, 128, 92),
        _ => new Rgb(255, 0, 255),
    };

    public static Rgb CellColor(GridMap map, CellCoord c)
    {
        var cell = map[c];
        var color = !cell.IsPassable ? Rock : ColorFor(map.TerrainNames[cell.TerrainId]);
        if (cell.IsPassable && cell.ObstacleHeightCm is > 0 and <= 150)
            color = Blend(color, Bush);
        int shade = 88 + Math.Clamp((int)cell.GroundHeightCm, 0, 300) * 12 / 300;
        int jitter = (int)((uint)((c.X * 73856093) ^ (c.Y * 19349663)) % 9) - 4;
        return Scale(color, shade + jitter);
    }

    private static Rgb Blend(Rgb a, Rgb b) =>
        new((byte)((a.R + b.R) / 2), (byte)((a.G + b.G) / 2), (byte)((a.B + b.B) / 2));

    private static Rgb Scale(Rgb c, int percent) =>
        new(Channel(c.R, percent), Channel(c.G, percent), Channel(c.B, percent));

    private static byte Channel(byte value, int percent) => (byte)Math.Clamp(value * percent / 100, 0, 255);
}
```

`src/Nmf.Client/ContentLocator.cs`:
```csharp
namespace Nmf.Client;

public static class ContentLocator
{
    /// <summary>Walks up from <paramref name="startDirectory"/> to the nearest folder containing content/core.</summary>
    public static string? FindContentRoot(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var content = Path.Combine(dir.FullName, "content");
            if (Directory.Exists(Path.Combine(content, "core")))
                return content;
        }
        return null;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Client.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(client): selection, formation offsets, terrain palette and content locator

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Client library – game session

**Files:**
- Create: `src/Nmf.Client/GameSession.cs`
- Test: `src/Nmf.Client.Tests/GameSessionTests.cs`

**Interfaces:**
- Consumes: `Scenario` (Task 7), `FixedStepClock` (phase 1), `Viewshed`, `VisionRules`, `SideKnowledge` (Tasks 5–6), `Selection`, `Formation` (Task 9)
- Produces: `sealed class GameSession(Scenario scenario, Side playerSide = Side.Blue)` with these members:
  - Constants and data: `const int FogRangeCm = 15_000`, `Scenario Scenario`, `Simulation Sim`, `Side PlayerSide`, `FixedStepClock Clock`, `Selection Selection`.
  - Fog: `bool[] VisibleCells` (y × width + x) and `int FogVersion`, which increments every time the fog is recomputed: at construction and on every vision tick.
  - Knowledge and units: `SideKnowledge Knowledge`, `IEnumerable<Unit> OwnUnits`, `TimeSpan GameTime`.
  - Time: `int Update(double realDeltaSeconds)` returns the number of steps run; `void StepOnce()` runs `Scenario.Tick()` and then `Sim.Step()`.
  - `(double X, double Y) InterpolatedPositionCm(Unit unit)` lerps between the previous and the current tick by `Clock.Alpha`.
  - Orders:
    - `void OrderMove(Vec2 target, MoveMode mode)` sends the selected units, in id order, to target + formation offset. An offset that lands outside the map or on an impassable cell falls back to the target.
    - `void OrderStance(Stance stance)` and `void OrderStop()` apply to the selection.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Client.Tests/GameSessionTests.cs`:
```csharp
using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Client.Tests;

public class GameSessionTests
{
    private static GameSession NewSession(GridMap? map = null)
    {
        map ??= new GridMap(200, 40, ["none"], new MapFeatures(
            [],
            [
                new MapPoint("b1", "blue", new Vec2(150, 150)),
                new MapPoint("b2", "blue", new Vec2(350, 150)),
                new MapPoint("r1", "red", new Vec2(19_500, 3_500)),
            ],
            []));
        return new GameSession(SkirmishScenario.Create(map, 1));
    }

    private static void SelectAll(GameSession s) =>
        s.Selection.SelectInBox(s.Sim.Units, s.PlayerSide, Vec2.Zero, new Vec2(s.Sim.Map.WidthCm, s.Sim.Map.HeightCm), false);

    [Fact]
    public void Update_OneStepOfTime_AdvancesOneTick()
    {
        var session = NewSession();
        Assert.Equal(1, session.Update(0.05));
        Assert.Equal(1, session.Sim.Tick);
    }

    [Fact]
    public void Update_WhilePaused_DoesNotAdvanceOrMove()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.Update(0.05);
        var position = session.Sim.Units[0].Position;

        session.Clock.Paused = true;
        Assert.Equal(0, session.Update(1.0));
        Assert.Equal(position, session.Sim.Units[0].Position);
        var (x, y) = session.InterpolatedPositionCm(session.Sim.Units[0]);
        Assert.InRange(x, 150, position.X);
        Assert.InRange(y, 150, position.Y);
    }

    [Fact]
    public void OrderMove_SpreadsSelectedUnitsInFormation()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(2000, 2000), session.Sim.Units[0].MoveTarget);
        Assert.Equal(new Vec2(2000, 1800), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_OffsetOnImpassableCell_FallsBackToClickedPoint()
    {
        var session = NewSession();
        session.Sim.Map[new Vec2(2000, 1800).ToCell()].ExtraMoveCost = CellData.Impassable;
        SelectAll(session);
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(2000, 2000), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_OffsetOutsideMap_FallsBackToClickedPoint()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderMove(new Vec2(1000, 100), MoveMode.Walk);
        session.StepOnce();
        Assert.Equal(new Vec2(1000, 100), session.Sim.Units[1].MoveTarget);
    }

    [Fact]
    public void OrderMove_NothingSelected_SubmitsNothing()
    {
        var session = NewSession();
        session.OrderMove(new Vec2(2000, 2000), MoveMode.Walk);
        session.StepOnce();
        Assert.Empty(session.Sim.OrderLog);
    }

    [Fact]
    public void OrderStance_AppliesToSelection()
    {
        var session = NewSession();
        SelectAll(session);
        session.OrderStance(Stance.Prone);
        for (int i = 0; i < 25; i++) session.StepOnce();
        Assert.All(session.OwnUnits, u => Assert.Equal(Stance.Prone, u.Stance));
    }

    [Fact]
    public void Fog_ShowsNearbyCellsAndHidesFarOnes()
    {
        var session = NewSession();
        int width = session.Sim.Map.Width;
        Assert.True(session.VisibleCells[1 * width + 1]);
        Assert.False(session.VisibleCells[1 * width + 199]);
        int version = session.FogVersion;
        for (int i = 0; i < 5; i++) session.StepOnce();
        Assert.True(session.FogVersion > version);
    }

    [Fact]
    public void InterpolatedPosition_IsBetweenPreviousAndCurrentTick()
    {
        var session = NewSession();
        session.Selection.SelectAt(session.Sim.Units, Side.Blue, new Vec2(150, 150), 50, false);
        session.OrderMove(new Vec2(1500, 150), MoveMode.Walk);
        session.Update(0.05);  // one step: 150 -> 157
        session.Update(0.025); // half a step later
        var (x, y) = session.InterpolatedPositionCm(session.Sim.Units[0]);
        Assert.Equal(153.5, x, 3);
        Assert.Equal(150, y, 3);
    }

    [Fact]
    public void GameTime_FollowsTicks()
    {
        var session = NewSession();
        for (int i = 0; i < 40; i++) session.StepOnce();
        Assert.Equal(TimeSpan.FromSeconds(2), session.GameTime);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Client.Tests --filter FullyQualifiedName~GameSessionTests`
Expected: build FAILS with `The type or namespace name 'GameSession' could not be found`.

- [ ] **Step 3: Implement**

`src/Nmf.Client/GameSession.cs`:
```csharp
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Time;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Client;

/// <summary>
/// Everything the player-facing game needs on top of the simulation: time, selection,
/// group orders, fog of war and smooth positions. Engine independent, so it is unit tested.
/// </summary>
public sealed class GameSession
{
    public const int FogRangeCm = 15_000;

    private readonly Dictionary<UnitId, Vec2> _previousPositions = [];

    public GameSession(Scenario scenario, Side playerSide = Side.Blue)
    {
        Scenario = scenario;
        PlayerSide = playerSide;
        VisibleCells = new bool[Sim.Map.Width * Sim.Map.Height];
        SnapshotPositions();
        RefreshFog();
    }

    public Scenario Scenario { get; }
    public Simulation Sim => Scenario.Sim;
    public Side PlayerSide { get; }
    public FixedStepClock Clock { get; } = new();
    public Selection Selection { get; } = new();

    /// <summary>Cells the player's units can currently see, indexed y * width + x.</summary>
    public bool[] VisibleCells { get; }

    /// <summary>Changes whenever <see cref="VisibleCells"/> is recomputed.</summary>
    public int FogVersion { get; private set; }

    public SideKnowledge Knowledge => Sim.Knowledge(PlayerSide);
    public IEnumerable<Unit> OwnUnits => Sim.Units.Where(u => u.Side == PlayerSide);
    public TimeSpan GameTime => TimeSpan.FromSeconds((double)Sim.Tick / SimConstants.TicksPerSecond);

    public int Update(double realDeltaSeconds)
    {
        int steps = Clock.Advance(realDeltaSeconds);
        for (int i = 0; i < steps; i++)
            StepOnce();
        return steps;
    }

    public void StepOnce()
    {
        SnapshotPositions();
        Scenario.Tick();
        Sim.Step();
        if (Sim.Tick % VisionRules.IntervalTicks == 0)
            RefreshFog();
    }

    public (double X, double Y) InterpolatedPositionCm(Unit unit)
    {
        var current = unit.Position;
        var previous = _previousPositions.TryGetValue(unit.Id, out var p) ? p : current;
        double a = Math.Clamp(Clock.Alpha, 0, 1);
        return (previous.X + (current.X - previous.X) * a, previous.Y + (current.Y - previous.Y) * a);
    }

    public void OrderMove(Vec2 target, MoveMode mode)
    {
        var ids = Selection.Ids;
        var offsets = Formation.Offsets(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            var spot = target + offsets[i];
            if (!Sim.Map.Contains(spot) || !Sim.Map.CellAt(spot).IsPassable)
                spot = target;
            Sim.Submit(PlayerSide, new MoveOrder(ids[i], spot, mode));
        }
    }

    public void OrderStance(Stance stance)
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new SetStanceOrder(id, stance));
    }

    public void OrderStop()
    {
        foreach (var id in Selection.Ids)
            Sim.Submit(PlayerSide, new StopOrder(id));
    }

    private void SnapshotPositions()
    {
        foreach (var unit in Sim.Units)
            _previousPositions[unit.Id] = unit.Position;
    }

    private void RefreshFog()
    {
        Viewshed.Compute(
            Sim.Map,
            OwnUnits.Select(u => (u.Position, VisionRules.EyeHeightAbsCm(Sim.Map, u))),
            FogRangeCm,
            VisibleCells);
        FogVersion++;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS for all test projects.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Client/GameSession.cs src/Nmf.Client.Tests/GameSessionTests.cs
git commit -m "feat(client): game session with time, group orders, fog and interpolation

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Godot project – playable test skirmish

**Files:**
- Create: `src/Nmf.Game/project.godot`, `src/Nmf.Game/Main.tscn`, `src/Nmf.Game/Nmf.Game.csproj`
- Create: `src/Nmf.Game/GameRoot.cs`, `Coords.cs`, `MapView.cs`, `FogView.cs`, `UnitView.cs`, `CameraController.cs`, `Hud.cs`
- Create: `tools/run_game.sh`
- Modify: `NoMansForest.slnx`, `README.md`

**Interfaces:**
- Consumes: `GameSession` and everything in `Nmf.Client` (Tasks 9–10), `TmxMapLoader` (phase 1), `SkirmishScenario` (Task 7), `ContactLevel` (Task 5)
- Produces: a runnable Godot project. It supports the command-line user args `--demo` (select all own units, order a walk to the map centre, time ×4) and `--screenshot=<path>` (save the viewport at frame 90, then quit). It prints `[NMF] ready map=<w>x<h> units=<n> content=<dir>` on start.

This task is glue code without unit tests. It is verified by a clean build, a headless smoke run and a screenshot run.

- [ ] **Step 1: Create the Godot project files**

`src/Nmf.Game/project.godot`:
```ini
; Engine configuration file.
config_version=5

[application]
config/name="No Man's Forest"
run/main_scene="res://Main.tscn"
config/features=PackedStringArray("4.7", "C#", "GL Compatibility")

[display]
window/size/viewport_width=1280
window/size/viewport_height=800

[dotnet]
project/assembly_name="Nmf.Game"

[rendering]
renderer/rendering_method="gl_compatibility"
renderer/rendering_method.mobile="gl_compatibility"
```

`src/Nmf.Game/Main.tscn`:
```ini
[gd_scene load_steps=2 format=3]

[ext_resource type="Script" path="res://GameRoot.cs" id="1"]

[node name="GameRoot" type="Node2D"]
script = ExtResource("1")
```

`src/Nmf.Game/Nmf.Game.csproj`:
```xml
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>Nmf.Game</RootNamespace>
    <ImplicitUsings>disable</ImplicitUsings>
    <!-- Godot's source generators own some warnings; keep the build going and fix our own warnings by hand. -->
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Nmf.Client\Nmf.Client.csproj" />
    <ProjectReference Include="..\Nmf.Content\Nmf.Content.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write the Godot scripts**

`src/Nmf.Game/Coords.cs`:
```csharp
using Godot;
using Nmf.Sim.Core;

namespace Nmf.Game;

/// <summary>Conversions between simulation centimetres and Godot world pixels (16 px per 1 m cell).</summary>
public static class Coords
{
    public const float PixelsPerCell = 16f;
    public const float PixelsPerCm = PixelsPerCell / 100f;

    public static Vector2 ToPixels(Vec2 cm) => new Vector2(cm.X, cm.Y) * PixelsPerCm;

    public static Vector2 ToPixels(double xCm, double yCm) => new Vector2((float)xCm, (float)yCm) * PixelsPerCm;

    public static Vec2 ToCm(Vector2 px) => new((int)(px.X / PixelsPerCm), (int)(px.Y / PixelsPerCm));
}
```

`src/Nmf.Game/MapView.cs`:
```csharp
using Godot;
using Nmf.Client;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Game;

/// <summary>Static terrain: one texel per 1 m cell, scaled up with nearest filtering.</summary>
public partial class MapView : Sprite2D
{
    public static MapView Create(GridMap map)
    {
        var image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgb8);
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var c = TerrainPalette.CellColor(map, new CellCoord(x, y));
                image.SetPixel(x, y, Color.Color8(c.R, c.G, c.B));
            }
        }
        return new MapView
        {
            Texture = ImageTexture.CreateFromImage(image),
            Centered = false,
            Scale = new Vector2(Coords.PixelsPerCell, Coords.PixelsPerCell),
            TextureFilter = TextureFilterEnum.Nearest,
        };
    }
}
```

`src/Nmf.Game/FogView.cs`:
```csharp
using Godot;
using Nmf.Client;

namespace Nmf.Game;

/// <summary>Dark overlay on cells the player's units cannot currently see.</summary>
public partial class FogView : Sprite2D
{
    private static readonly Color Hidden = new(0.02f, 0.03f, 0.05f, 0.55f);

    private Image _image = null!;
    private ImageTexture _texture = null!;
    private int _version = -1;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        var map = Session.Sim.Map;
        _image = Image.CreateEmpty(map.Width, map.Height, false, Image.Format.Rgba8);
        _texture = ImageTexture.CreateFromImage(_image);
        Texture = _texture;
        Centered = false;
        Scale = new Vector2(Coords.PixelsPerCell, Coords.PixelsPerCell);
        TextureFilter = TextureFilterEnum.Linear;
        Refresh();
    }

    public void Refresh()
    {
        if (_version == Session.FogVersion)
            return;
        _version = Session.FogVersion;
        var map = Session.Sim.Map;
        var visible = Session.VisibleCells;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                _image.SetPixel(x, y, visible[y * map.Width + x] ? Colors.Transparent : Hidden);
        _texture.Update(_image);
    }
}
```

`src/Nmf.Game/UnitView.cs`:
```csharp
using System.Collections.Generic;
using Godot;
using Nmf.Client;
using Nmf.Sim.Units;
using Nmf.Sim.Vision;

namespace Nmf.Game;

/// <summary>Draws soldiers, selection, paths and enemy contacts the player knows about.</summary>
public partial class UnitView : Node2D
{
    private static readonly Color BlueFill = new(0.27f, 0.45f, 0.85f);
    private static readonly Color RedFill = new(0.82f, 0.22f, 0.18f);
    private static readonly Color SelectedRing = new(1f, 0.92f, 0.3f);
    private static readonly Color PathColor = new(1f, 0.92f, 0.3f, 0.6f);
    private static readonly Color LastKnownColor = new(0.9f, 0.3f, 0.2f, 0.7f);
    private static readonly Color SuspectedColor = new(1f, 0.6f, 0.1f, 0.18f);

    public GameSession Session { get; set; } = null!;
    public bool RevealAll { get; set; }
    public Rect2? DragRect { get; set; }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        foreach (var contact in Session.Knowledge.Contacts)
        {
            var at = Coords.ToPixels(contact.Position);
            switch (contact.Level)
            {
                case ContactLevel.Suspected:
                    DrawCircle(at, 5f * Coords.PixelsPerCell, SuspectedColor);
                    DrawString(font, at + new Vector2(-4, 6), "?", HorizontalAlignment.Left, -1, 18, new Color(1f, 0.7f, 0.2f));
                    break;
                case ContactLevel.LastKnown:
                    DrawArc(at, 6f, 0f, Mathf.Tau, 20, LastKnownColor, 1.5f);
                    DrawString(font, at + new Vector2(8, -6), "?", HorizontalAlignment.Left, -1, 14, LastKnownColor);
                    break;
            }
        }

        foreach (var unit in Session.Sim.Units)
        {
            bool own = unit.Side == Session.PlayerSide;
            if (!own && !RevealAll && Session.Knowledge.LevelOf(unit.Id) != ContactLevel.Visible)
                continue;

            var (x, y) = Session.InterpolatedPositionCm(unit);
            var pos = Coords.ToPixels(x, y);
            float radius = unit.Stance switch { Stance.Standing => 6f, Stance.Crouching => 5f, _ => 4f };

            if (own && Session.Selection.Contains(unit.Id))
            {
                var points = new List<Vector2> { pos };
                for (int i = unit.PathIndex; i < unit.Path.Count; i++)
                    points.Add(Coords.ToPixels(unit.Path[i]));
                if (points.Count >= 2)
                    DrawPolyline(points.ToArray(), PathColor, 1.5f);
                DrawArc(pos, radius + 3f, 0f, Mathf.Tau, 24, SelectedRing, 2f);
            }

            DrawCircle(pos, radius, own ? BlueFill : RedFill);
            DrawArc(pos, radius, 0f, Mathf.Tau, 16, Colors.Black, 1f);
            if (unit.TargetStance is not null)
                DrawArc(pos, radius + 1.5f, 0f, Mathf.Pi, 12, Colors.White, 1f);
        }

        if (DragRect is { } rect)
            DrawRect(rect, SelectedRing, false, 1f);
    }
}
```

`src/Nmf.Game/CameraController.cs`:
```csharp
using Godot;

namespace Nmf.Game;

/// <summary>Pan with WASD/arrows, middle mouse drag or two-finger trackpad pan; zoom with wheel or pinch.</summary>
public partial class CameraController : Camera2D
{
    private const float PanSpeed = 900f;
    private const float MinZoom = 0.25f;
    private const float MaxZoom = 4f;

    public Vector2 WorldSize { get; set; }

    public override void _Process(double delta)
    {
        var direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) direction.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) direction.Y += 1;
        if (direction != Vector2.Zero)
            MoveBy(direction.Normalized() * PanSpeed * (float)delta / Zoom.X);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomBy(1.1f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomBy(1f / 1.1f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Middle) != 0:
                MoveBy(-motion.Relative / Zoom.X);
                break;
            case InputEventPanGesture pan:
                MoveBy(pan.Delta * 20f / Zoom.X);
                break;
            case InputEventMagnifyGesture magnify:
                ZoomBy(magnify.Factor);
                break;
        }
    }

    private void MoveBy(Vector2 offset) => Position = (Position + offset).Clamp(Vector2.Zero, WorldSize);

    private void ZoomBy(float factor)
    {
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
    }
}
```

`src/Nmf.Game/Hud.cs`:
```csharp
using System;
using System.Globalization;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Sim.Vision;

namespace Nmf.Game;

public partial class Hud : CanvasLayer
{
    private const string Help =
        "LMB select / drag box · Shift adds · RMB move · Shift+RMB run · Alt+RMB crawl · 1/2/3 stand/crouch/prone · H halt\n" +
        "Space pause · +/- speed · WASD/arrows/middle drag/two-finger pan · wheel/pinch zoom · Tab all · Esc none · F reveal (debug)";

    private Label _label = null!;

    public GameSession Session { get; set; } = null!;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(12, 8) };
        _label.AddThemeFontSizeOverride("font_size", 15);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 5);
        AddChild(_label);
        Refresh();
    }

    public void Refresh()
    {
        var contacts = Session.Knowledge.Contacts.ToList();
        int seen = contacts.Count(c => c.Level == ContactLevel.Visible);
        int heard = contacts.Count(c => c.Level == ContactLevel.Suspected);
        int lastKnown = contacts.Count(c => c.Level == ContactLevel.LastKnown);
        var t = Session.GameTime;
        string paused = Session.Clock.Paused ? "   [PAUSED]" : "";
        _label.Text = string.Create(CultureInfo.InvariantCulture,
            $"No Man's Forest – test skirmish   {(int)t.TotalMinutes:00}:{t.Seconds:00}   x{Session.Clock.TimeScale:0.##}{paused}\n" +
            $"Selected {Session.Selection.Count}   Contacts: {seen} seen, {heard} heard, {lastKnown} last known\n" +
            Help);
    }
}
```

`src/Nmf.Game/GameRoot.cs`:
```csharp
using System;
using System.IO;
using System.Linq;
using Godot;
using Nmf.Client;
using Nmf.Content.Tiled;
using Nmf.Sim.Core;
using Nmf.Sim.Scenarios;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Game;

/// <summary>Scene root: loads the skirmish, builds the views and turns input into session calls.</summary>
public partial class GameRoot : Node2D
{
    private const int ClickRadiusCm = 150;
    private const int ScreenshotFrame = 90;

    private GameSession? _session;
    private UnitView _units = null!;
    private FogView _fog = null!;
    private Hud _hud = null!;
    private CameraController _camera = null!;
    private Vector2? _dragStart;
    private string? _screenshotPath;
    private int _frame;

    public override void _Ready()
    {
        string? contentRoot = ContentLocator.FindContentRoot(ProjectSettings.GlobalizePath("res://"))
                              ?? ContentLocator.FindContentRoot(OS.GetExecutablePath().GetBaseDir());
        if (contentRoot is null)
        {
            GD.PushError("[NMF] content/ directory not found");
            GetTree().Quit(1);
            return;
        }

        GridMap map;
        try
        {
            map = TmxMapLoader.Load(Path.Combine(contentRoot, "core", "maps", "skirmish.tmx"));
        }
        catch (MapLoadException ex)
        {
            GD.PushError($"[NMF] {ex.Message}");
            GetTree().Quit(1);
            return;
        }

        var session = new GameSession(SkirmishScenario.Create(map, seed: 1942));
        _session = session;

        AddChild(MapView.Create(map));
        _fog = new FogView { Session = session };
        AddChild(_fog);
        _units = new UnitView { Session = session };
        AddChild(_units);
        _camera = new CameraController { WorldSize = new Vector2(map.Width, map.Height) * Coords.PixelsPerCell };
        AddChild(_camera);
        _camera.MakeCurrent();
        _hud = new Hud { Session = session };
        AddChild(_hud);

        var firstOwn = session.OwnUnits.FirstOrDefault();
        if (firstOwn is not null)
            _camera.Position = Coords.ToPixels(firstOwn.Position) + new Vector2(0, -200);

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--screenshot=", StringComparison.Ordinal))
                _screenshotPath = arg["--screenshot=".Length..];
            else if (arg == "--demo")
                StartDemo(session);
        }

        GD.Print($"[NMF] ready map={map.Width}x{map.Height} units={session.Sim.Units.Count} content={contentRoot}");
    }

    public override void _Process(double delta)
    {
        if (_session is null)
            return;
        _session.Update(delta);
        _units.DragRect = _dragStart is { } start ? new Rect2(start, GetGlobalMousePosition() - start).Abs() : null;
        _units.QueueRedraw();
        _fog.Refresh();
        _hud.Refresh();

        if (_screenshotPath is not null && ++_frame == ScreenshotFrame)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshotPath);
            GD.Print($"[NMF] screenshot saved to {_screenshotPath}");
            GetTree().Quit();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_session is null)
            return;
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                HandleLeftClick(_session, click);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } click:
                var target = Coords.ToCm(GetGlobalMousePosition());
                if (_session.Sim.Map.Contains(target))
                    _session.OrderMove(target, click.ShiftPressed ? MoveMode.Run : click.AltPressed ? MoveMode.Crawl : MoveMode.Walk);
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(_session, key.Keycode);
                break;
        }
    }

    private void HandleLeftClick(GameSession session, InputEventMouseButton click)
    {
        if (click.Pressed)
        {
            _dragStart = GetGlobalMousePosition();
            return;
        }
        if (_dragStart is not { } start)
            return;
        var end = GetGlobalMousePosition();
        _dragStart = null;
        if (start.DistanceTo(end) < 6f / _camera.Zoom.X)
            session.Selection.SelectAt(session.Sim.Units, session.PlayerSide, Coords.ToCm(end), ClickRadiusCm, click.ShiftPressed);
        else
            session.Selection.SelectInBox(session.Sim.Units, session.PlayerSide, Coords.ToCm(start), Coords.ToCm(end), click.ShiftPressed);
    }

    private void HandleKey(GameSession session, Key key)
    {
        switch (key)
        {
            case Key.Space:
                session.Clock.Paused = !session.Clock.Paused;
                break;
            case Key.Key1:
                session.OrderStance(Stance.Standing);
                break;
            case Key.Key2:
                session.OrderStance(Stance.Crouching);
                break;
            case Key.Key3:
                session.OrderStance(Stance.Prone);
                break;
            case Key.H:
                session.OrderStop();
                break;
            case Key.Equal or Key.KpAdd:
                session.Clock.TimeScale = Math.Min(4, session.Clock.TimeScale * 2);
                break;
            case Key.Minus or Key.KpSubtract:
                session.Clock.TimeScale = Math.Max(0.25, session.Clock.TimeScale / 2);
                break;
            case Key.Tab:
                SelectAll(session);
                break;
            case Key.Escape:
                session.Selection.Clear();
                break;
            case Key.F:
                _units.RevealAll = !_units.RevealAll;
                _fog.Visible = !_units.RevealAll;
                break;
        }
    }

    private static void SelectAll(GameSession session) =>
        session.Selection.SelectInBox(session.Sim.Units, session.PlayerSide, Vec2.Zero,
            new Vec2(session.Sim.Map.WidthCm, session.Sim.Map.HeightCm), additive: false);

    private static void StartDemo(GameSession session)
    {
        SelectAll(session);
        session.OrderMove(new Vec2(session.Sim.Map.WidthCm / 2, session.Sim.Map.HeightCm / 2), MoveMode.Walk);
        session.Clock.TimeScale = 4;
    }
}
```

- [ ] **Step 3: Add the run script, solution entry and README section**

`tools/run_game.sh`:
```bash
#!/usr/bin/env bash
# Builds and starts the No Man's Forest test skirmish.
# Usage: tools/run_game.sh [-- game args, e.g. -- --demo]
set -euo pipefail
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot-mono}"
dotnet build src/Nmf.Game/Nmf.Game.csproj -v q -nologo
if [ ! -d src/Nmf.Game/.godot ]; then
  "$GODOT" --headless --path src/Nmf.Game --import
fi
exec "$GODOT" --path src/Nmf.Game "$@"
```
Run: `chmod +x tools/run_game.sh && dotnet sln NoMansForest.slnx add src/Nmf.Game`

In `README.md`, insert after the `## Tools` section:
````markdown
## Play the test skirmish

Requires Godot 4.7 .NET (`brew install --cask godot-mono` on macOS; on Windows/Linux download the ".NET" build from godotengine.org and set `GODOT` to its executable).

```bash
tools/run_game.sh            # play
tools/run_game.sh -- --demo  # all soldiers march to the map centre at x4 speed
```

Four Finnish soldiers (blue) start in the south; five Soviet soldiers (red) hold the north, one of them on patrol. You only see enemies your men can see; heard movement shows as an orange "?" area.

| Input | Action |
|---|---|
| Left click / drag | select soldier / box select (Shift adds) |
| Right click | walk there (Shift: run, Alt/Option: crawl) |
| 1 / 2 / 3 | stand / crouch / go prone |
| H | halt |
| Space | pause (orders still work) |
| + / − | game speed ×0.25 … ×4 |
| WASD, arrows, middle drag, two-finger pan | move camera |
| Wheel, pinch | zoom |
| Tab / Esc | select all / clear selection |
| F | debug: reveal all units, hide fog |
````

- [ ] **Step 4: Build everything**

Run: `dotnet build NoMansForest.slnx 2>&1 | tail -3`
Expected: `0 Error(s)`. The first build of `Nmf.Game` downloads `Godot.NET.Sdk` from NuGet.

- [ ] **Step 5: Import and run a headless smoke test**

Run:
```bash
godot-mono --headless --path src/Nmf.Game --import > /tmp/nmf-import.log 2>&1; echo import=$?
godot-mono --headless --path src/Nmf.Game --quit-after 120 > /tmp/nmf-smoke.log 2>&1; echo run=$?
grep -E "\[NMF\]|ERROR|Exception" /tmp/nmf-smoke.log
```
Expected: `import=0`, `run=0`, and the line `[NMF] ready map=128x96 units=9 content=/Users/.../game/content`, with no `ERROR` or `Exception` lines. (Use a scratch directory instead of /tmp when running under an agent sandbox.)

- [ ] **Step 6: Windowed screenshot run**

Run: `godot-mono --path src/Nmf.Game -- --demo --screenshot=/tmp/nmf-shot.png; echo exit=$?`
Expected: a window opens for about two seconds and closes, printing `[NMF] screenshot saved to /tmp/nmf-shot.png`. Open the PNG and check that it shows:
- the green/brown terrain with the darker forest and grey rocks
- the dimmed fog far from the soldiers
- four blue circles with yellow selection rings and yellow path lines heading north
- the HUD text in the top-left corner

- [ ] **Step 7: Run the full test suite and commit**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS for all four test projects.

```bash
git add -A
git commit -m "feat(game): first playable Godot view of the test skirmish

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Spec coverage (phase 2)

| Spec item | Task |
|---|---|
| §13.2 pathfinding | 2, 3 |
| §5.1 actions take ticks (stance changes, move speeds), interruptible | 3 (orders replace the current action) |
| §7.1 cell fields incl. movement | 1 |
| §7.2 line of sight, stance heights, obstacles over the ray | 4 |
| §7.3 spotting takes time: distance, vegetation, stance, movement | 5 |
| §7.4 contact levels and last-seen markers | 5, 11 |
| §7.5 sounds: volume by action and terrain, rough position | 5 |
| §7.6 soldier/side knowledge; the player sees the union of own units | 5, 10, 11 |
| §7.7 staggered vision updates | 5 (every 5 ticks) |
| §6.1 player as commander who sees only what own units see | 10, 11 |
| §13.2 first Godot view where units move on the map | 11 |
| §10.5 developer mode reveal | 11 (F key) |

Deliberately left for later phases:
- the "Identified" contact level (needs unit types, phase 3)
- per-soldier vs. per-squad knowledge and radio delay (§7.6, phase 3 with squads)
- lighting, flares and smoke (§7.3, phases 3/6)
- the turn-based mode (phase 4)
- enemy AI beyond the patrol (phase 5)
