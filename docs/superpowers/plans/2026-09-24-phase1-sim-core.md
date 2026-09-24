# Phase 1: Simulation Core & Tiled Loading – Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deterministic, engine-independent simulation core of No Man's Forest (fixed tick, RNG, orders in / events out, grid map, replays) plus a Tiled `.tmx` map loader and a tiny CLI, all covered by tests.

**Architecture:** `Nmf.Sim` is a pure C# library with no Godot dependency; all state changes flow through orders submitted to `Simulation` and are reported back as `SimEvent`s. Positions are integer centimetres so the simulation is bit-for-bit reproducible. `Nmf.Content` turns Tiled maps into `GridMap`s; `Nmf.Cli` (`nmf`) exposes content tools on the command line.

**Tech Stack:** .NET SDK 10 (libraries target `net8.0` for Godot 4 compatibility; tests and CLI target `net10.0`), C#, xUnit, System.Xml.Linq, Tiled map format (TMX/TSX).

**Spec:** `docs/superpowers/specs/2026-09-24-no-mans-forest-design.md` (this plan implements section 13, phase 1; it relies on sections 2, 3, 4, 7.1, 10.2 and 12).

## Global Constraints

- Simulation runs at **20 ticks per second** (`SimConstants.TicksPerSecond = 20`).
- Map cell is **1 m × 1 m**; all simulation positions are **integer centimetres** (`SimConstants.CentimetersPerCell = 100`).
- `Nmf.Sim` has **no dependency** on Godot, `Nmf.Content` or any NuGet package.
- Dependency direction: `Nmf.Game` → `Nmf.Content` → `Nmf.Sim`; `Nmf.Cli` uses the same libraries as the game.
- **Determinism:** all randomness comes from the simulation's own `Rng`; units are processed in id order; no floating point in simulation state.
- **Orders are the only input, events are the only output.** UI never mutates simulation state directly.
- Libraries target `net8.0`; test and CLI projects target `net10.0` (only the .NET 10 runtime is installed).
- Nullable enabled, warnings are errors, all parsing uses `CultureInfo.InvariantCulture`.
- Tiled maps: orthogonal, square tiles, **CSV** layer format; tile layers named `terrain` (required), `height`, `obstacles`; object layers hold zones (rectangles), points and paths (polylines).
- License: code MIT.
- Commits end with the line `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.

## Review Focus

1. **Finnish locale (fi-FI, decimal comma, U+2212 minus):** map loading and the map summary must produce identical results to the invariant culture → tests in Task 8 and Task 10.
2. **Tiled files saved with non-default settings** (base64/zlib layers, infinite maps, flipped tiles, layer groups): flipped tiles load as the base tile, the rest fail with an error that tells the modder what to change → tests in Task 8.
3. **Very slow units moving diagonally** (1 cm/tick): integer truncation must never leave a unit stuck short of its target → test in Task 5.
4. **Bad orders** (unknown unit, other side's unit, target outside the map): rejected with an `OrderRejected` event, simulation keeps running → tests in Task 5.
5. **Long frame hitch** (a 10 s frame after a debugger pause or window drag): the clock caps steps per frame and drops the backlog instead of spiralling → test in Task 7.

---

## File Structure

```
NoMansForest.slnx
Directory.Build.props            # shared compiler settings
.gitignore
README.md                        # build/test/CLI + Tiled rules for modders (Task 10)
src/
  Nmf.Sim/
    Nmf.Sim.csproj
    SimConstants.cs              # tick rate, cell size
    Simulation.cs                # world state, Submit(), Step()
    Core/Rng.cs                  # PCG32 deterministic RNG + RngState
    Core/IntMath.cs              # Isqrt, FloorDiv
    Core/Vec2.cs                 # Vec2 (cm) and CellCoord
    Core/StateHash.cs            # FNV-1a hash of simulation state
    World/CellData.cs            # per-cell terrain values
    World/GridMap.cs             # cell grid
    World/MapFeatures.cs         # zones, points, paths from the map
    Units/Unit.cs                # Unit, UnitId, Side
    Units/Movement.cs            # straight-line movement per tick
    Orders/Orders.cs             # Order, MoveOrder, StopOrder, LoggedOrder
    Events/SimEvents.cs          # SimEvent and concrete events
    Replays/Replay.cs            # Replay record + ReplayRunner
    Time/FixedStepClock.cs       # real time -> whole steps + interpolation alpha
  Nmf.Sim.Tests/                 # xUnit, one test file per Sim file above
  Nmf.Content/
    Nmf.Content.csproj
    MapSummary.cs                # human-readable map description
    Tiled/MapLoadException.cs
    Tiled/TilesetRef.cs          # parsed tileset (internal)
    Tiled/TsxParser.cs           # <tileset> element -> TilesetRef (internal)
    Tiled/TmxMapLoader.cs        # .tmx -> GridMap
  Nmf.Content.Tests/
    Fixtures/terrain.tsx, heights.tsx, valid.tmx
    TempMapDir.cs, TmxText.cs    # helpers for writing test maps
    TmxMapLoaderTests.cs, TmxObjectTests.cs, MapSummaryTests.cs, CoreContentTests.cs
  Nmf.Cli/
    Nmf.Cli.csproj               # AssemblyName nmf
    Program.cs                   # `nmf map-info <map.tmx>`
content/core/
  tilesets/terrain.tsx, heights.tsx, obstacles.tsx (+ placeholder .png)
  maps/sandbox.tmx
tools/make_placeholder_tiles.py  # writes placeholder tileset PNGs (stdlib only)
```

---

### Task 1: Solution scaffold

**Files:**
- Create: `Directory.Build.props`, `.gitignore`, `NoMansForest.slnx`
- Create: `src/Nmf.Sim/Nmf.Sim.csproj`, `src/Nmf.Sim/SimConstants.cs`
- Create: `src/Nmf.Content/Nmf.Content.csproj`
- Create: `src/Nmf.Cli/Nmf.Cli.csproj`, `src/Nmf.Cli/Program.cs`
- Create: `src/Nmf.Sim.Tests/Nmf.Sim.Tests.csproj`, `src/Nmf.Content.Tests/Nmf.Content.Tests.csproj`
- Test: `src/Nmf.Sim.Tests/SimConstantsTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces: `Nmf.Sim.SimConstants.TicksPerSecond` (`const int` = 20), `Nmf.Sim.SimConstants.CentimetersPerCell` (`const int` = 100); project layout used by every later task.

- [ ] **Step 1: Create shared build settings and .gitignore**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <Deterministic>true</Deterministic>
  </PropertyGroup>
</Project>
```

`.gitignore`:
```
bin/
obj/
.godot/
.vs/
.idea/
*.user
.DS_Store
TestResults/
```

- [ ] **Step 2: Create solution and projects**

Run from repo root:
```bash
dotnet new sln -n NoMansForest --format slnx
dotnet new classlib -n Nmf.Sim -o src/Nmf.Sim -f net8.0
dotnet new classlib -n Nmf.Content -o src/Nmf.Content -f net8.0
dotnet new console -n Nmf.Cli -o src/Nmf.Cli -f net10.0
dotnet new xunit -n Nmf.Sim.Tests -o src/Nmf.Sim.Tests -f net10.0
dotnet new xunit -n Nmf.Content.Tests -o src/Nmf.Content.Tests -f net10.0
rm src/Nmf.Sim/Class1.cs src/Nmf.Content/Class1.cs src/Nmf.Sim.Tests/UnitTest1.cs src/Nmf.Content.Tests/UnitTest1.cs
dotnet add src/Nmf.Content reference src/Nmf.Sim
dotnet add src/Nmf.Cli reference src/Nmf.Content
dotnet add src/Nmf.Sim.Tests reference src/Nmf.Sim
dotnet add src/Nmf.Content.Tests reference src/Nmf.Content
dotnet sln NoMansForest.slnx add src/Nmf.Sim src/Nmf.Content src/Nmf.Cli src/Nmf.Sim.Tests src/Nmf.Content.Tests
```
Expected: each command succeeds. If `net8.0` targeting packs are downloaded on first build, that is normal.

- [ ] **Step 3: Adjust project files**

In `src/Nmf.Cli/Nmf.Cli.csproj`, inside the first `<PropertyGroup>`, add:
```xml
    <AssemblyName>nmf</AssemblyName>
```

In both test `.csproj` files, make sure this item group exists (add it if the template did not):
```xml
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
```
Test files in this plan therefore never write `using Xunit;`.

Replace `src/Nmf.Cli/Program.cs` with a placeholder that compiles (Task 10 replaces it):
```csharp
Console.Error.WriteLine("usage: nmf map-info <map.tmx>");
return 2;
```

- [ ] **Step 4: Write the failing test**

`src/Nmf.Sim.Tests/SimConstantsTests.cs`:
```csharp
using Nmf.Sim;

namespace Nmf.Sim.Tests;

public class SimConstantsTests
{
    [Fact]
    public void TickRateAndCellSize_MatchSpec()
    {
        Assert.Equal(20, SimConstants.TicksPerSecond);
        Assert.Equal(100, SimConstants.CentimetersPerCell);
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test NoMansForest.slnx`
Expected: build FAILS with `The name 'SimConstants' does not exist`.

- [ ] **Step 6: Implement**

`src/Nmf.Sim/SimConstants.cs`:
```csharp
namespace Nmf.Sim;

public static class SimConstants
{
    /// <summary>Simulation steps per second of game time.</summary>
    public const int TicksPerSecond = 20;

    /// <summary>One map cell is 1 m; positions are stored in whole centimetres.</summary>
    public const int CentimetersPerCell = 100;
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS, 1 test (Nmf.Content.Tests reports no tests, which is fine).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution with Sim, Content, Cli and test projects

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Deterministic RNG (PCG32)

**Files:**
- Create: `src/Nmf.Sim/Core/Rng.cs`
- Test: `src/Nmf.Sim.Tests/Core/RngTests.cs`

**Interfaces:**
- Consumes: nothing
- Produces:
  - `readonly record struct RngState(ulong State, ulong Increment)`
  - `sealed class Rng` with `Rng(ulong seed, ulong stream = Rng.DefaultStream)`, `const ulong DefaultStream = 54`, `uint NextUInt()`, `int NextInt(int maxExclusive)`, `int NextInt(int minInclusive, int maxExclusive)`, `bool Chance(int permille)` (always consumes one `NextInt(1000)` draw), `RngState State { get; }`, `static Rng FromState(RngState state)`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Core/RngTests.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class RngTests
{
    [Fact]
    public void Seed42Stream54_MatchesPcg32ReferenceOutput()
    {
        // Reference values from the pcg-c demo (pcg32_srandom_r(42, 54)).
        var rng = new Rng(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];
        foreach (var value in expected)
            Assert.Equal(value, rng.NextUInt());
    }

    [Fact]
    public void NextInt_Seed42_MatchesGoldenValues()
    {
        var rng = new Rng(42);
        Assert.Equal(3, rng.NextInt(10));
        Assert.Equal(7, rng.NextInt(10));
        Assert.Equal(4, rng.NextInt(10));
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new Rng(1234);
        var b = new Rng(1234);
        for (int i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt(), b.NextUInt());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new Rng(1);
        var b = new Rng(2);
        var sa = Enumerable.Range(0, 8).Select(_ => a.NextUInt()).ToArray();
        var sb = Enumerable.Range(0, 8).Select(_ => b.NextUInt()).ToArray();
        Assert.NotEqual(sa, sb);
    }

    [Fact]
    public void NextInt_StaysWithinBound()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(rng.NextInt(7), 0, 6);
    }

    [Fact]
    public void NextInt_MinMax_SupportsNegativeRanges()
    {
        var rng = new Rng(7);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(rng.NextInt(-5, 5), -5, 4);
    }

    [Fact]
    public void NextInt_IsRoughlyUniform()
    {
        var rng = new Rng(99);
        var buckets = new int[4];
        for (int i = 0; i < 40_000; i++)
            buckets[rng.NextInt(4)]++;
        Assert.All(buckets, count => Assert.InRange(count, 9_000, 11_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NextInt_NonPositiveBound_Throws(int bound)
    {
        var rng = new Rng(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(bound));
    }

    [Fact]
    public void NextInt_EmptyRange_Throws()
    {
        var rng = new Rng(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void State_RoundTripsThroughFromState()
    {
        var rng = new Rng(5);
        for (int i = 0; i < 5; i++) rng.NextUInt();
        var saved = rng.State;
        var expected = new[] { rng.NextUInt(), rng.NextUInt(), rng.NextUInt() };

        var restored = Rng.FromState(saved);
        Assert.Equal(expected, new[] { restored.NextUInt(), restored.NextUInt(), restored.NextUInt() });
    }

    [Fact]
    public void FromState_EvenIncrement_Throws()
    {
        Assert.Throws<ArgumentException>(() => Rng.FromState(new RngState(1, 2)));
    }

    [Fact]
    public void Chance_ZeroNeverAndThousandAlways()
    {
        var rng = new Rng(3);
        for (int i = 0; i < 1000; i++)
        {
            Assert.False(rng.Chance(0));
            Assert.True(rng.Chance(1000));
        }
    }

    [Fact]
    public void Chance_AlwaysConsumesOneDraw()
    {
        var a = new Rng(11);
        var b = new Rng(11);
        a.Chance(0);
        b.NextInt(1000);
        Assert.Equal(a.State, b.State);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~RngTests`
Expected: build FAILS with `The type or namespace name 'Rng' could not be found`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Core/Rng.cs`:
```csharp
namespace Nmf.Sim.Core;

public readonly record struct RngState(ulong State, ulong Increment);

/// <summary>
/// PCG32 (XSH-RR, 64-bit state). Platform independent and fully deterministic;
/// the only source of randomness inside the simulation.
/// </summary>
public sealed class Rng
{
    public const ulong DefaultStream = 54;
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    public Rng(ulong seed, ulong stream = DefaultStream)
    {
        _state = 0;
        _increment = (stream << 1) | 1UL;
        NextUInt();
        _state = unchecked(_state + seed);
        NextUInt();
    }

    private Rng(RngState state)
    {
        _state = state.State;
        _increment = state.Increment;
    }

    public RngState State => new(_state, _increment);

    public static Rng FromState(RngState state)
    {
        if ((state.Increment & 1UL) == 0)
            throw new ArgumentException("RNG increment must be odd.", nameof(state));
        return new Rng(state);
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rotation = (int)(old >> 59);
        return (xorShifted >> rotation) | (xorShifted << (-rotation & 31));
    }

    /// <summary>Unbiased integer in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive, "Bound must be positive.");
        uint bound = (uint)maxExclusive;
        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return (int)(r % bound);
        }
    }

    /// <summary>Unbiased integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        long span = (long)maxExclusive - minInclusive;
        if (span <= 0 || span > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), $"Invalid range [{minInclusive}, {maxExclusive}).");
        return minInclusive + NextInt((int)span);
    }

    /// <summary>True with probability permille/1000. Always consumes exactly one NextInt(1000) draw.</summary>
    public bool Chance(int permille) => NextInt(1000) < permille;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~RngTests`
Expected: PASS, 14 tests (the theory counts twice).

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Core/Rng.cs src/Nmf.Sim.Tests/Core/RngTests.cs
git commit -m "feat(sim): add deterministic PCG32 RNG

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: Integer math, Vec2 and CellCoord

**Files:**
- Create: `src/Nmf.Sim/Core/IntMath.cs`, `src/Nmf.Sim/Core/Vec2.cs`
- Test: `src/Nmf.Sim.Tests/Core/IntMathTests.cs`, `src/Nmf.Sim.Tests/Core/Vec2Tests.cs`

**Interfaces:**
- Consumes: `SimConstants.CentimetersPerCell` (Task 1)
- Produces:
  - `static class IntMath` with `long Isqrt(long n)` (n in 0..2^62) and `int FloorDiv(int a, int b)` (b > 0)
  - `readonly record struct Vec2(int X, int Y)` (centimetres) with `Vec2.Zero`, operators `+` and `-`, `long LengthSquared`, `int Length`, `CellCoord ToCell()`
  - `readonly record struct CellCoord(int X, int Y)` with `Vec2 CenterCm`

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Core/IntMathTests.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class IntMathTests
{
    [Theory]
    [InlineData(0L, 0L)]
    [InlineData(1L, 1L)]
    [InlineData(2L, 1L)]
    [InlineData(3L, 1L)]
    [InlineData(4L, 2L)]
    [InlineData(15L, 3L)]
    [InlineData(16L, 4L)]
    [InlineData(17L, 4L)]
    [InlineData(250_000L, 500L)]
    [InlineData(4_000_000_000_000_000_000L, 2_000_000_000L)]
    [InlineData(3_999_999_999_999_999_999L, 1_999_999_999L)]
    public void Isqrt_ReturnsFloorOfSquareRoot(long n, long expected)
    {
        Assert.Equal(expected, IntMath.Isqrt(n));
    }

    [Fact]
    public void Isqrt_Negative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.Isqrt(-1));
    }

    [Theory]
    [InlineData(0, 100, 0)]
    [InlineData(99, 100, 0)]
    [InlineData(100, 100, 1)]
    [InlineData(-1, 100, -1)]
    [InlineData(-100, 100, -1)]
    [InlineData(-101, 100, -2)]
    public void FloorDiv_RoundsTowardNegativeInfinity(int a, int b, int expected)
    {
        Assert.Equal(expected, IntMath.FloorDiv(a, b));
    }

    [Fact]
    public void FloorDiv_NonPositiveDivisor_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => IntMath.FloorDiv(5, 0));
    }
}
```

`src/Nmf.Sim.Tests/Core/Vec2Tests.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.Tests.Core;

public class Vec2Tests
{
    [Fact]
    public void Operators_AddAndSubtract()
    {
        Assert.Equal(new Vec2(4, 6), new Vec2(1, 2) + new Vec2(3, 4));
        Assert.Equal(new Vec2(-2, -2), new Vec2(1, 2) - new Vec2(3, 4));
    }

    [Fact]
    public void Length_UsesIntegerSquareRoot()
    {
        Assert.Equal(500, new Vec2(300, 400).Length);
        Assert.Equal(250_000L, new Vec2(300, 400).LengthSquared);
        Assert.Equal(141, new Vec2(100, 100).Length);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(99, 199, 0, 1)]
    [InlineData(100, 200, 1, 2)]
    [InlineData(-1, 250, -1, 2)]
    public void ToCell_FloorsToOneMetreCells(int x, int y, int cx, int cy)
    {
        Assert.Equal(new CellCoord(cx, cy), new Vec2(x, y).ToCell());
    }

    [Fact]
    public void CellCenter_IsMiddleOfCell()
    {
        Assert.Equal(new Vec2(250, 350), new CellCoord(2, 3).CenterCm);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter "FullyQualifiedName~IntMathTests|FullyQualifiedName~Vec2Tests"`
Expected: build FAILS with `The name 'IntMath' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Core/IntMath.cs`:
```csharp
namespace Nmf.Sim.Core;

public static class IntMath
{
    private const long MaxIsqrtInput = 1L << 62;

    /// <summary>Floor of the square root; exact for 0..2^62.</summary>
    public static long Isqrt(long n)
    {
        if (n < 0 || n > MaxIsqrtInput)
            throw new ArgumentOutOfRangeException(nameof(n), n, "Input must be in 0..2^62.");
        if (n < 2)
            return n;
        // Math.Sqrt is correctly rounded (IEEE 754) and the loops fix any error, so the result is exact and platform independent.
        long x = (long)Math.Sqrt(n);
        while (x * x > n) x--;
        while ((x + 1) * (x + 1) <= n) x++;
        return x;
    }

    /// <summary>Integer division rounding toward negative infinity.</summary>
    public static int FloorDiv(int a, int b)
    {
        if (b <= 0)
            throw new ArgumentOutOfRangeException(nameof(b), b, "Divisor must be positive.");
        int q = a / b;
        if (a % b != 0 && a < 0)
            q--;
        return q;
    }
}
```

`src/Nmf.Sim/Core/Vec2.cs`:
```csharp
namespace Nmf.Sim.Core;

/// <summary>Position or offset in whole centimetres.</summary>
public readonly record struct Vec2(int X, int Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public long LengthSquared => (long)X * X + (long)Y * Y;

    public int Length => (int)IntMath.Isqrt(LengthSquared);

    public CellCoord ToCell() => new(
        IntMath.FloorDiv(X, SimConstants.CentimetersPerCell),
        IntMath.FloorDiv(Y, SimConstants.CentimetersPerCell));

    public override string ToString() => $"({X},{Y})cm";
}

/// <summary>Index of a 1 m map cell.</summary>
public readonly record struct CellCoord(int X, int Y)
{
    public Vec2 CenterCm => new(
        X * SimConstants.CentimetersPerCell + SimConstants.CentimetersPerCell / 2,
        Y * SimConstants.CentimetersPerCell + SimConstants.CentimetersPerCell / 2);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter "FullyQualifiedName~IntMathTests|FullyQualifiedName~Vec2Tests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Core/IntMath.cs src/Nmf.Sim/Core/Vec2.cs src/Nmf.Sim.Tests/Core/IntMathTests.cs src/Nmf.Sim.Tests/Core/Vec2Tests.cs
git commit -m "feat(sim): add integer math, Vec2 and CellCoord

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: GridMap, CellData and MapFeatures

**Files:**
- Create: `src/Nmf.Sim/World/CellData.cs`, `src/Nmf.Sim/World/GridMap.cs`, `src/Nmf.Sim/World/MapFeatures.cs`
- Test: `src/Nmf.Sim.Tests/World/GridMapTests.cs`

**Interfaces:**
- Consumes: `Vec2`, `CellCoord` (Task 3), `SimConstants` (Task 1)
- Produces:
  - `record struct CellData(short GroundHeightCm, short ObstacleHeightCm, byte ConcealmentPerM, byte Cover, ushort TerrainId)`. Mutable properties; `ConcealmentPerM` and `Cover` are 0..255 meaning 0..1.
  - `sealed class GridMap` with `const int MaxSideCells = 4096`, ctor `GridMap(int width, int height, IReadOnlyList<string> terrainNames, MapFeatures? features = null)`, `int Width`, `int Height`, `int WidthCm`, `int HeightCm`, `IReadOnlyList<string> TerrainNames` (index = `TerrainId`), `MapFeatures Features`, `bool InBounds(CellCoord)`, `bool Contains(Vec2)`, `ref CellData this[CellCoord]` (throws `ArgumentOutOfRangeException` outside), `CellData CellAt(Vec2 posCm)`.
  - `sealed record MapZone(string Name, string Type, Vec2 Min, Vec2 Max)` (Max exclusive), `sealed record MapPoint(string Name, string Type, Vec2 Position)`, `sealed record MapPath(string Name, string Type, IReadOnlyList<Vec2> Points)`, `sealed class MapFeatures(IReadOnlyList<MapZone> zones, IReadOnlyList<MapPoint> points, IReadOnlyList<MapPath> paths)` with properties `Zones`, `Points`, `Paths` and `static MapFeatures Empty`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/World/GridMapTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.World;

public class GridMapTests
{
    private static GridMap NewMap(int w = 4, int h = 3) => new(w, h, ["none", "grass"]);

    [Fact]
    public void NewMap_HasSizeAndZeroedCells()
    {
        var map = NewMap();
        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal(400, map.WidthCm);
        Assert.Equal(300, map.HeightCm);
        Assert.Equal(default(CellData), map[new CellCoord(3, 2)]);
        Assert.Empty(map.Features.Zones);
    }

    [Fact]
    public void Indexer_ReturnsWritableReference()
    {
        var map = NewMap();
        map[new CellCoord(1, 2)] = new CellData(100, 50, 10, 20, 1);
        map[new CellCoord(1, 2)].Cover = 99;

        Assert.Equal(new CellData(100, 50, 10, 99, 1), map[new CellCoord(1, 2)]);
        Assert.Equal(default(CellData), map[new CellCoord(2, 1)]);
    }

    [Fact]
    public void CellAt_ConvertsCentimetresToCell()
    {
        var map = NewMap();
        map[new CellCoord(2, 1)].TerrainId = 1;
        Assert.Equal(1, map.CellAt(new Vec2(250, 199)).TerrainId);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(3, 2, true)]
    [InlineData(4, 2, false)]
    [InlineData(3, 3, false)]
    [InlineData(-1, 0, false)]
    public void InBounds_ChecksCellRange(int x, int y, bool expected)
    {
        Assert.Equal(expected, NewMap().InBounds(new CellCoord(x, y)));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(399, 299, true)]
    [InlineData(400, 0, false)]
    [InlineData(0, 300, false)]
    [InlineData(-1, 0, false)]
    public void Contains_ChecksCentimetreRange(int x, int y, bool expected)
    {
        Assert.Equal(expected, NewMap().Contains(new Vec2(x, y)));
    }

    [Fact]
    public void Indexer_OutsideMap_ThrowsWithCoordinates()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewMap()[new CellCoord(4, 0)]);
        Assert.Contains("4", ex.Message);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(4097, 5)]
    public void Constructor_InvalidSize_Throws(int w, int h)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridMap(w, h, ["none"]));
    }

    [Fact]
    public void Constructor_NoTerrainNames_Throws()
    {
        Assert.Throws<ArgumentException>(() => new GridMap(2, 2, []));
    }

    [Fact]
    public void Features_AreKept()
    {
        var zone = new MapZone("start", "zone", new Vec2(0, 0), new Vec2(100, 100));
        var map = new GridMap(2, 2, ["none"], new MapFeatures([zone], [], []));
        Assert.Same(zone, Assert.Single(map.Features.Zones));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~GridMapTests`
Expected: build FAILS with `The type or namespace name 'GridMap' could not be found`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/World/CellData.cs`:
```csharp
namespace Nmf.Sim.World;

/// <summary>Static terrain values of one 1 m cell.</summary>
/// <param name="GroundHeightCm">Ground elevation.</param>
/// <param name="ObstacleHeightCm">Height of what stands on the cell (trees, rocks, walls).</param>
/// <param name="ConcealmentPerM">How much one metre of this cell blocks sight, 0..255 = 0..1.</param>
/// <param name="Cover">How well the cell stops bullets, 0..255 = 0..1.</param>
/// <param name="TerrainId">Index into <see cref="GridMap.TerrainNames"/>.</param>
public record struct CellData(
    short GroundHeightCm,
    short ObstacleHeightCm,
    byte ConcealmentPerM,
    byte Cover,
    ushort TerrainId);
```

`src/Nmf.Sim/World/MapFeatures.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Named rectangle; <paramref name="Max"/> is exclusive.</summary>
public sealed record MapZone(string Name, string Type, Vec2 Min, Vec2 Max);

public sealed record MapPoint(string Name, string Type, Vec2 Position);

public sealed record MapPath(string Name, string Type, IReadOnlyList<Vec2> Points);

/// <summary>Named zones, points and paths placed by the mission author.</summary>
public sealed class MapFeatures(
    IReadOnlyList<MapZone> zones,
    IReadOnlyList<MapPoint> points,
    IReadOnlyList<MapPath> paths)
{
    public static MapFeatures Empty { get; } = new([], [], []);

    public IReadOnlyList<MapZone> Zones { get; } = zones;
    public IReadOnlyList<MapPoint> Points { get; } = points;
    public IReadOnlyList<MapPath> Paths { get; } = paths;
}
```

`src/Nmf.Sim/World/GridMap.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.World;

/// <summary>Grid of 1 m cells. Row-major, origin at the top-left corner.</summary>
public sealed class GridMap
{
    public const int MaxSideCells = 4096;

    private readonly CellData[] _cells;

    public GridMap(int width, int height, IReadOnlyList<string> terrainNames, MapFeatures? features = null)
    {
        if (width is <= 0 or > MaxSideCells)
            throw new ArgumentOutOfRangeException(nameof(width), width, $"Width must be 1..{MaxSideCells}.");
        if (height is <= 0 or > MaxSideCells)
            throw new ArgumentOutOfRangeException(nameof(height), height, $"Height must be 1..{MaxSideCells}.");
        if (terrainNames.Count == 0)
            throw new ArgumentException("At least one terrain name (id 0) is required.", nameof(terrainNames));

        Width = width;
        Height = height;
        TerrainNames = terrainNames;
        Features = features ?? MapFeatures.Empty;
        _cells = new CellData[width * height];
    }

    public int Width { get; }
    public int Height { get; }
    public int WidthCm => Width * SimConstants.CentimetersPerCell;
    public int HeightCm => Height * SimConstants.CentimetersPerCell;
    public IReadOnlyList<string> TerrainNames { get; }
    public MapFeatures Features { get; }

    public bool InBounds(CellCoord c) => c.X >= 0 && c.Y >= 0 && c.X < Width && c.Y < Height;

    public bool Contains(Vec2 p) => p.X >= 0 && p.Y >= 0 && p.X < WidthCm && p.Y < HeightCm;

    public ref CellData this[CellCoord c]
    {
        get
        {
            if (!InBounds(c))
                throw new ArgumentOutOfRangeException(nameof(c), $"Cell ({c.X},{c.Y}) is outside the {Width}x{Height} map.");
            return ref _cells[c.Y * Width + c.X];
        }
    }

    public CellData CellAt(Vec2 posCm) => this[posCm.ToCell()];
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~GridMapTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/World src/Nmf.Sim.Tests/World
git commit -m "feat(sim): add grid map, cell data and map features

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Units, orders, events and the simulation step

**Files:**
- Create: `src/Nmf.Sim/Units/Unit.cs`, `src/Nmf.Sim/Units/Movement.cs`, `src/Nmf.Sim/Orders/Orders.cs`, `src/Nmf.Sim/Events/SimEvents.cs`, `src/Nmf.Sim/Simulation.cs`
- Test: `src/Nmf.Sim.Tests/SimulationTests.cs`

**Interfaces:**
- Consumes: `Rng` (Task 2), `Vec2`, `IntMath` (Task 3), `GridMap` (Task 4)
- Produces:
  - `enum Side : byte { Blue = 0, Red = 1 }`, `readonly record struct UnitId(int Value)`
  - `sealed class Unit` with `UnitId Id`, `Side Side`, `int SpeedCmPerTick`, `Vec2 Position`, `Vec2? MoveTarget` (setters internal)
  - `abstract record Order(UnitId Unit)`, `sealed record MoveOrder(UnitId Unit, Vec2 Target) : Order`, `sealed record StopOrder(UnitId Unit) : Order`, `sealed record LoggedOrder(long Tick, Side Issuer, Order Order)`
  - `abstract record SimEvent(long Tick)`, `sealed record UnitMoved(long Tick, UnitId Unit, Vec2 From, Vec2 To)`, `sealed record UnitArrived(long Tick, UnitId Unit, Vec2 Position)`, `sealed record OrderRejected(long Tick, Order Order, string Reason)`
  - `sealed class Simulation` with ctor `Simulation(GridMap map, ulong seed)`, `GridMap Map`, `ulong Seed`, `Rng Rng`, `long Tick`, `IReadOnlyList<Unit> Units` (id order), `IReadOnlyList<LoggedOrder> OrderLog`, `Unit SpawnUnit(Side side, Vec2 position, int speedCmPerTick)`, `Unit? FindUnit(UnitId id)`, `void Submit(Side issuer, Order order)`, `IReadOnlyList<SimEvent> Step()`
  - **Tick semantics:** orders submitted while `Tick == T` are logged with tick T and applied at the start of the next `Step()`. That step stamps its events with T and then sets `Tick = T + 1`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/SimulationTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests;

public class SimulationTests
{
    private static Simulation NewSim() => new(new GridMap(20, 20, ["none"]), seed: 1);

    [Fact]
    public void Step_AdvancesTick()
    {
        var sim = NewSim();
        sim.Step();
        sim.Step();
        Assert.Equal(2, sim.Tick);
    }

    [Fact]
    public void SpawnUnit_AssignsIncreasingIdsInOrder()
    {
        var sim = NewSim();
        var a = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 7);
        var b = sim.SpawnUnit(Side.Red, new Vec2(150, 50), 7);
        Assert.Equal(new UnitId(1), a.Id);
        Assert.Equal(new UnitId(2), b.Id);
        Assert.Equal(new[] { a, b }, sim.Units);
        Assert.Same(b, sim.FindUnit(b.Id));
        Assert.Null(sim.FindUnit(new UnitId(99)));
    }

    [Fact]
    public void SpawnUnit_OutsideMap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSim().SpawnUnit(Side.Blue, new Vec2(2000, 0), 7));
    }

    [Fact]
    public void SpawnUnit_NonPositiveSpeed_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSim().SpawnUnit(Side.Blue, new Vec2(50, 50), 0));
    }

    [Fact]
    public void Submit_TakesEffectOnNextStep()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(80, 50)));
        Assert.Null(u.MoveTarget);
        sim.Step();
        Assert.NotNull(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_MovesAtSpeedAndArrives()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(80, 50)));

        var first = sim.Step();
        Assert.Equal(new Vec2(60, 50), u.Position);
        Assert.Contains<SimEvent>(new UnitMoved(0, u.Id, new Vec2(50, 50), new Vec2(60, 50)), first);

        sim.Step();
        Assert.Equal(new Vec2(70, 50), u.Position);

        var third = sim.Step();
        Assert.Equal(new Vec2(80, 50), u.Position);
        Assert.Contains<SimEvent>(new UnitArrived(2, u.Id, new Vec2(80, 50)), third);
        Assert.Null(u.MoveTarget);

        Assert.Empty(sim.Step());
    }

    [Fact]
    public void MoveOrder_SlowDiagonal_StillArrives()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), speedCmPerTick: 1);
        var target = new Vec2(350, 250);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, target));

        for (int i = 0; i < 1000 && (i == 0 || u.MoveTarget is not null); i++)
            sim.Step();

        Assert.Equal(target, u.Position);
        Assert.Null(u.MoveTarget);
    }

    [Fact]
    public void MoveOrder_ToOwnPosition_ArrivesWithoutMoving()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(50, 50)));
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new UnitArrived(0, u.Id, new Vec2(50, 50)) }, events);
    }

    [Fact]
    public void StopOrder_ClearsTarget()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(500, 50)));
        sim.Step();
        sim.Submit(Side.Blue, new StopOrder(u.Id));
        sim.Step();
        var stoppedAt = u.Position;
        sim.Step();
        Assert.Null(u.MoveTarget);
        Assert.Equal(stoppedAt, u.Position);
    }

    [Fact]
    public void Order_ForUnknownUnit_IsRejected()
    {
        var sim = NewSim();
        var order = new MoveOrder(new UnitId(42), new Vec2(10, 10));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "unknown unit") }, events);
    }

    [Fact]
    public void Order_ForOtherSidesUnit_IsRejected()
    {
        var sim = NewSim();
        var enemy = sim.SpawnUnit(Side.Red, new Vec2(50, 50), 10);
        var order = new MoveOrder(enemy.Id, new Vec2(100, 100));
        sim.Submit(Side.Blue, order);
        var events = sim.Step();
        Assert.Equal(new SimEvent[] { new OrderRejected(0, order, "unit belongs to another side") }, events);
        Assert.Null(enemy.MoveTarget);
    }

    [Fact]
    public void MoveOrder_OutsideMap_IsRejectedAndSimKeepsRunning()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var bad = new MoveOrder(u.Id, new Vec2(5000, 50));
        sim.Submit(Side.Blue, bad);
        sim.Submit(Side.Blue, new MoveOrder(u.Id, new Vec2(100, 50)));
        var events = sim.Step();
        Assert.Contains<SimEvent>(new OrderRejected(0, bad, "target outside map"), events);
        Assert.Equal(new Vec2(60, 50), u.Position);
    }

    [Fact]
    public void OrderLog_RecordsTickAndIssuer()
    {
        var sim = NewSim();
        var u = sim.SpawnUnit(Side.Blue, new Vec2(50, 50), 10);
        var first = new MoveOrder(u.Id, new Vec2(100, 50));
        var second = new StopOrder(u.Id);
        sim.Submit(Side.Blue, first);
        sim.Step();
        sim.Step();
        sim.Step();
        sim.Submit(Side.Blue, second);
        sim.Step();
        Assert.Equal(new[] { new LoggedOrder(0, Side.Blue, first), new LoggedOrder(3, Side.Blue, second) }, sim.OrderLog);
    }

    [Fact]
    public void Submit_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => NewSim().Submit(Side.Blue, null!));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~SimulationTests`
Expected: build FAILS with `The type or namespace name 'Simulation' could not be found`.

- [ ] **Step 3: Implement units, orders and events**

`src/Nmf.Sim/Units/Unit.cs`:
```csharp
using Nmf.Sim.Core;

namespace Nmf.Sim.Units;

public enum Side : byte
{
    Blue = 0,
    Red = 1,
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
    public int SpeedCmPerTick { get; }
    public Vec2 Position { get; internal set; }
    public Vec2? MoveTarget { get; internal set; }
}
```

`src/Nmf.Sim/Orders/Orders.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Units;

namespace Nmf.Sim.Orders;

public abstract record Order(UnitId Unit);

public sealed record MoveOrder(UnitId Unit, Vec2 Target) : Order(Unit);

public sealed record StopOrder(UnitId Unit) : Order(Unit);

/// <summary>An order as submitted: the tick it was submitted on and by which side.</summary>
public sealed record LoggedOrder(long Tick, Side Issuer, Order Order);
```

`src/Nmf.Sim/Events/SimEvents.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Units;

namespace Nmf.Sim.Events;

public abstract record SimEvent(long Tick);

public sealed record UnitMoved(long Tick, UnitId Unit, Vec2 From, Vec2 To) : SimEvent(Tick);

public sealed record UnitArrived(long Tick, UnitId Unit, Vec2 Position) : SimEvent(Tick);

public sealed record OrderRejected(long Tick, Order Order, string Reason) : SimEvent(Tick);
```

- [ ] **Step 4: Implement movement and the simulation**

`src/Nmf.Sim/Units/Movement.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Events;

namespace Nmf.Sim.Units;

/// <summary>Straight-line movement toward the unit's move target. Pathfinding arrives in phase 2.</summary>
internal static class Movement
{
    public static void Advance(Unit unit, long tick, List<SimEvent> events)
    {
        if (unit.MoveTarget is not { } target)
            return;

        var from = unit.Position;
        var delta = target - from;
        long distance = IntMath.Isqrt(delta.LengthSquared);
        bool arrived = distance <= unit.SpeedCmPerTick;
        Vec2 to;

        if (arrived)
        {
            to = target;
        }
        else
        {
            long speed = unit.SpeedCmPerTick;
            int mx = (int)(delta.X * speed / distance);
            int my = (int)(delta.Y * speed / distance);
            if (mx == 0 && my == 0)
            {
                // Truncation can round a slow diagonal step down to nothing; always make progress.
                if (Math.Abs(delta.X) >= Math.Abs(delta.Y))
                    mx = Math.Sign(delta.X);
                else
                    my = Math.Sign(delta.Y);
            }
            to = new Vec2(from.X + mx, from.Y + my);
        }

        unit.Position = to;
        if (to != from)
            events.Add(new UnitMoved(tick, unit.Id, from, to));
        if (arrived)
        {
            unit.MoveTarget = null;
            events.Add(new UnitArrived(tick, unit.Id, to));
        }
    }
}
```

`src/Nmf.Sim/Simulation.cs`:
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
            Movement.Advance(unit, Tick, events);

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
                unit.MoveTarget = move.Target;
                break;
            case StopOrder:
                unit.MoveTarget = null;
                break;
            default:
                events.Add(new OrderRejected(Tick, order, $"unsupported order {order.GetType().Name}"));
                break;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~SimulationTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Nmf.Sim src/Nmf.Sim.Tests/SimulationTests.cs
git commit -m "feat(sim): add units, orders, events and fixed-step simulation loop

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: State hash and replays

**Files:**
- Create: `src/Nmf.Sim/Core/StateHash.cs`, `src/Nmf.Sim/Replays/Replay.cs`
- Test: `src/Nmf.Sim.Tests/Replays/ReplayTests.cs`

**Interfaces:**
- Consumes: `Simulation`, `LoggedOrder`, `MoveOrder`, `Side` (Task 5), `Rng` (Task 2)
- Produces:
  - `static class StateHash` with `ulong Compute(Simulation sim)`: FNV-1a 64 over tick, RNG state and every unit (id, side, position, move target) in id order.
  - `sealed record Replay(ulong Seed, long EndTick, IReadOnlyList<LoggedOrder> Orders)`
  - `static class ReplayRunner` with `Replay Capture(Simulation sim)` and `void Run(Simulation freshSim, Replay replay)`. `freshSim` must be at tick 0, have the same seed and be set up identically (same map and spawns).

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Replays/ReplayTests.cs`:
```csharp
using Nmf.Sim.Core;
using Nmf.Sim.Orders;
using Nmf.Sim.Replays;
using Nmf.Sim.Units;
using Nmf.Sim.World;

namespace Nmf.Sim.Tests.Replays;

public class ReplayTests
{
    private static Simulation NewSim(ulong seed = 7)
    {
        var sim = new Simulation(new GridMap(40, 40, ["none"]), seed);
        sim.SpawnUnit(Side.Blue, new Vec2(100, 100), 7);
        sim.SpawnUnit(Side.Blue, new Vec2(300, 100), 7);
        sim.SpawnUnit(Side.Red, new Vec2(3500, 3500), 5);
        return sim;
    }

    /// <summary>Plays 600 ticks with pseudo-random orders from a separate "player" RNG.</summary>
    private static Simulation PlayRandomSession()
    {
        var sim = NewSim();
        var player = new Rng(999);
        for (int t = 0; t < 600; t++)
        {
            if (player.Chance(50))
            {
                var unit = sim.Units[player.NextInt(sim.Units.Count)];
                var target = new Vec2(player.NextInt(4000), player.NextInt(4000));
                sim.Submit(unit.Side, new MoveOrder(unit.Id, target));
            }
            sim.Step();
        }
        return sim;
    }

    [Fact]
    public void StateHash_IsStableForSameState()
    {
        Assert.Equal(StateHash.Compute(NewSim()), StateHash.Compute(NewSim()));
    }

    [Fact]
    public void StateHash_ChangesWhenUnitMoves()
    {
        var sim = NewSim();
        var before = StateHash.Compute(sim);
        sim.Submit(Side.Blue, new MoveOrder(sim.Units[0].Id, new Vec2(500, 500)));
        sim.Step();
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void StateHash_ChangesWhenRngIsDrawn()
    {
        var sim = NewSim();
        var before = StateHash.Compute(sim);
        sim.Rng.NextUInt();
        Assert.NotEqual(before, StateHash.Compute(sim));
    }

    [Fact]
    public void Replay_ReproducesIdenticalFinalState()
    {
        var original = PlayRandomSession();
        var replay = ReplayRunner.Capture(original);
        Assert.NotEmpty(replay.Orders);

        var rerun = NewSim();
        ReplayRunner.Run(rerun, replay);

        Assert.Equal(original.Tick, rerun.Tick);
        Assert.Equal(StateHash.Compute(original), StateHash.Compute(rerun));
    }

    [Fact]
    public void Replay_WithChangedOrder_DivergesFromOriginal()
    {
        var original = PlayRandomSession();
        var replay = ReplayRunner.Capture(original);
        var orders = replay.Orders.ToList();
        // Change the last order: an earlier change could be erased when the unit later arrives at the same target.
        orders[^1] = orders[^1] with { Order = new MoveOrder(orders[^1].Order.Unit, new Vec2(1, 1)) };

        var rerun = NewSim();
        ReplayRunner.Run(rerun, replay with { Orders = orders });

        Assert.NotEqual(StateHash.Compute(original), StateHash.Compute(rerun));
    }

    [Fact]
    public void Run_OnAlreadySteppedSim_Throws()
    {
        var sim = NewSim();
        sim.Step();
        Assert.Throws<InvalidOperationException>(() => ReplayRunner.Run(sim, new Replay(7, 10, [])));
    }

    [Fact]
    public void Run_WithDifferentSeed_Throws()
    {
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(seed: 8), new Replay(7, 10, [])));
    }

    [Fact]
    public void Run_WithUnsortedOrders_Throws()
    {
        var order = new MoveOrder(new UnitId(1), new Vec2(10, 10));
        var replay = new Replay(7, 10, [new LoggedOrder(5, Side.Blue, order), new LoggedOrder(2, Side.Blue, order)]);
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(), replay));
    }

    [Fact]
    public void Run_WithOrderAtOrAfterEndTick_Throws()
    {
        var order = new MoveOrder(new UnitId(1), new Vec2(10, 10));
        var replay = new Replay(7, 10, [new LoggedOrder(10, Side.Blue, order)]);
        Assert.Throws<ArgumentException>(() => ReplayRunner.Run(NewSim(), replay));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~ReplayTests`
Expected: build FAILS with `The type or namespace name 'Replays' does not exist`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Core/StateHash.cs`:
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
            h.Add(unit.Position.X);
            h.Add(unit.Position.Y);
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

`src/Nmf.Sim/Replays/Replay.cs`:
```csharp
using Nmf.Sim.Orders;

namespace Nmf.Sim.Replays;

/// <summary>Everything needed to re-run a session besides the initial setup: seed, length and the order log.</summary>
public sealed record Replay(ulong Seed, long EndTick, IReadOnlyList<LoggedOrder> Orders);

public static class ReplayRunner
{
    public static Replay Capture(Simulation sim) => new(sim.Seed, sim.Tick, sim.OrderLog.ToList());

    /// <summary>Re-runs <paramref name="replay"/> on a freshly set-up simulation (same map and spawns, tick 0).</summary>
    public static void Run(Simulation freshSim, Replay replay)
    {
        if (freshSim.Tick != 0)
            throw new InvalidOperationException("A replay must start from a simulation at tick 0.");
        if (freshSim.Seed != replay.Seed)
            throw new ArgumentException($"Replay seed {replay.Seed} does not match simulation seed {freshSim.Seed}.", nameof(replay));
        Validate(replay);

        int next = 0;
        while (freshSim.Tick < replay.EndTick)
        {
            while (next < replay.Orders.Count && replay.Orders[next].Tick == freshSim.Tick)
            {
                var logged = replay.Orders[next++];
                freshSim.Submit(logged.Issuer, logged.Order);
            }
            freshSim.Step();
        }
    }

    private static void Validate(Replay replay)
    {
        for (int i = 0; i < replay.Orders.Count; i++)
        {
            long tick = replay.Orders[i].Tick;
            if (tick < 0 || tick >= replay.EndTick)
                throw new ArgumentException($"Replay order {i} has tick {tick}, outside 0..{replay.EndTick - 1}.", nameof(replay));
            if (i > 0 && tick < replay.Orders[i - 1].Tick)
                throw new ArgumentException($"Replay orders are not in tick order at index {i}.", nameof(replay));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~ReplayTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Core/StateHash.cs src/Nmf.Sim/Replays src/Nmf.Sim.Tests/Replays
git commit -m "feat(sim): add state hash and deterministic replays

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: Fixed-step clock

**Files:**
- Create: `src/Nmf.Sim/Time/FixedStepClock.cs`
- Test: `src/Nmf.Sim.Tests/Time/FixedStepClockTests.cs`

**Interfaces:**
- Consumes: `SimConstants.TicksPerSecond` (Task 1)
- Produces: `sealed class FixedStepClock` with ctor `FixedStepClock(int maxStepsPerFrame = 5)`, `const double StepSeconds` (= 0.05), `int MaxStepsPerFrame`, `bool Paused`, `double TimeScale` (≥ 0, default 1), `double Alpha` (0..1, interpolation between the last two ticks), `int Advance(double realDeltaSeconds)` (returns how many `Simulation.Step()` calls to make this frame). The clock is presentation-side state and not part of the deterministic simulation.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Sim.Tests/Time/FixedStepClockTests.cs`:
```csharp
using Nmf.Sim.Time;

namespace Nmf.Sim.Tests.Time;

public class FixedStepClockTests
{
    [Fact]
    public void OneStepDuration_YieldsOneStep()
    {
        var clock = new FixedStepClock();
        Assert.Equal(1, clock.Advance(0.05));
        Assert.Equal(0, clock.Alpha, 6);
    }

    [Fact]
    public void PartialStep_AccumulatesAndReportsAlpha()
    {
        var clock = new FixedStepClock();
        Assert.Equal(0, clock.Advance(0.025));
        Assert.Equal(0.5, clock.Alpha, 6);
        Assert.Equal(1, clock.Advance(0.025));
    }

    [Fact]
    public void SixtyFramesPerSecond_ProducesAboutTwentyStepsPerSecond()
    {
        var clock = new FixedStepClock();
        int total = 0;
        for (int i = 0; i < 60; i++)
            total += clock.Advance(1.0 / 60);
        Assert.InRange(total, 19, 20);
    }

    [Fact]
    public void Paused_YieldsNoSteps()
    {
        var clock = new FixedStepClock { Paused = true };
        Assert.Equal(0, clock.Advance(1.0));
    }

    [Fact]
    public void TimeScale_SpeedsUpSimulation()
    {
        var clock = new FixedStepClock { TimeScale = 2 };
        Assert.Equal(2, clock.Advance(0.05));
    }

    [Fact]
    public void LongHitch_IsCappedAndBacklogDropped()
    {
        var clock = new FixedStepClock(maxStepsPerFrame: 5);
        Assert.Equal(5, clock.Advance(10.0));
        Assert.Equal(0, clock.Alpha, 6);
        Assert.Equal(1, clock.Advance(0.05));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Advance_InvalidDelta_Throws(double delta)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock().Advance(delta));
    }

    [Fact]
    public void NegativeTimeScale_Throws()
    {
        var clock = new FixedStepClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.TimeScale = -1);
    }

    [Fact]
    public void ZeroMaxSteps_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedStepClock(0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~FixedStepClockTests`
Expected: build FAILS with `The type or namespace name 'FixedStepClock' could not be found`.

- [ ] **Step 3: Implement**

`src/Nmf.Sim/Time/FixedStepClock.cs`:
```csharp
namespace Nmf.Sim.Time;

/// <summary>
/// Converts variable real frame time into whole simulation steps plus an interpolation factor for rendering.
/// Presentation-side only: it is not part of the deterministic simulation state.
/// </summary>
public sealed class FixedStepClock
{
    public const double StepSeconds = 1.0 / SimConstants.TicksPerSecond;

    private double _accumulator;
    private double _timeScale = 1.0;

    public FixedStepClock(int maxStepsPerFrame = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStepsPerFrame, 1);
        MaxStepsPerFrame = maxStepsPerFrame;
    }

    public int MaxStepsPerFrame { get; }

    public bool Paused { get; set; }

    public double TimeScale
    {
        get => _timeScale;
        set
        {
            if (!(value >= 0) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Time scale must be a finite value >= 0.");
            _timeScale = value;
        }
    }

    /// <summary>Fraction (0..1) of the next step already elapsed; use it to interpolate sprites.</summary>
    public double Alpha => _accumulator / StepSeconds;

    /// <summary>Returns how many simulation steps to run for a frame that took <paramref name="realDeltaSeconds"/>.</summary>
    public int Advance(double realDeltaSeconds)
    {
        if (!(realDeltaSeconds >= 0) || double.IsInfinity(realDeltaSeconds))
            throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), realDeltaSeconds, "Frame time must be a finite value >= 0.");
        if (Paused)
            return 0;

        _accumulator += realDeltaSeconds * _timeScale;
        int steps = (int)Math.Min(Math.Floor(_accumulator / StepSeconds), MaxStepsPerFrame + 1);
        if (steps > MaxStepsPerFrame)
        {
            // After a long hitch, drop the backlog instead of trying to catch up (avoids a spiral of slow frames).
            _accumulator = 0;
            return MaxStepsPerFrame;
        }

        _accumulator = Math.Max(0, _accumulator - steps * StepSeconds);
        return steps;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Sim.Tests --filter FullyQualifiedName~FixedStepClockTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Sim/Time src/Nmf.Sim.Tests/Time
git commit -m "feat(sim): add fixed-step clock for frame-to-tick conversion

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Tiled loader – tilesets and tile layers

**Files:**
- Create: `src/Nmf.Content/Tiled/MapLoadException.cs`, `src/Nmf.Content/Tiled/TilesetRef.cs`, `src/Nmf.Content/Tiled/TsxParser.cs`, `src/Nmf.Content/Tiled/TmxMapLoader.cs`
- Create: `src/Nmf.Content.Tests/Fixtures/terrain.tsx`, `src/Nmf.Content.Tests/Fixtures/heights.tsx`, `src/Nmf.Content.Tests/Fixtures/valid.tmx`
- Create: `src/Nmf.Content.Tests/TempMapDir.cs`, `src/Nmf.Content.Tests/TmxText.cs`
- Modify: `src/Nmf.Content.Tests/Nmf.Content.Tests.csproj` (copy fixtures to output)
- Test: `src/Nmf.Content.Tests/TmxMapLoaderTests.cs`

**Interfaces:**
- Consumes: `GridMap`, `CellData`, `MapFeatures`, `CellCoord`, `SimConstants` (Tasks 1–4)
- Produces:
  - `sealed class MapLoadException(string path, string message, Exception? inner = null) : Exception` with `string Path`. The message is `"{path}: {message}"`.
  - `static class TmxMapLoader` with `GridMap Load(string tmxPath)`. It throws `MapLoadException` for every content problem (missing file, bad XML, unsupported format, invalid values).
  - Map rules:
    - Tile layer `terrain` is required. Every cell must hold a tile with a `terrain` string property. Optional properties: `concealment_per_m` (0..1), `cover` (0..1), `obstacle_height_cm` (short).
    - `TerrainNames[0]` is `"none"`. Other names get ids in order of first appearance.
    - Optional tile layer `height`: tile property `height_cm` sets `GroundHeightCm`. Empty cells stay 0.
    - Optional tile layer `obstacles`: `obstacle_height_cm`, `concealment_per_m` and `cover` are merged with the terrain values using max.
    - Fractions are stored as `round(v * 255)`, rounding half away from zero.
    - Flip flags (bits 28–31) are ignored.
    - Features are `MapFeatures.Empty` in this task; Task 9 fills them.

- [ ] **Step 1: Create fixtures**

`src/Nmf.Content.Tests/Fixtures/terrain.tsx` (tile 3 intentionally has no properties):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="terrain" tilewidth="16" tileheight="16" tilecount="4" columns="4">
 <image source="terrain.png" width="64" height="16"/>
 <tile id="0">
  <properties>
   <property name="terrain" value="grass"/>
   <property name="concealment_per_m" type="float" value="0.05"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="terrain" value="forest"/>
   <property name="concealment_per_m" type="float" value="0.4"/>
   <property name="cover" type="float" value="0.1"/>
   <property name="obstacle_height_cm" type="int" value="1500"/>
  </properties>
 </tile>
 <tile id="2">
  <properties>
   <property name="terrain" value="swamp"/>
   <property name="concealment_per_m" type="float" value="0.02"/>
  </properties>
 </tile>
</tileset>
```

`src/Nmf.Content.Tests/Fixtures/heights.tsx`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="heights" tilewidth="16" tileheight="16" tilecount="4" columns="4">
 <image source="heights.png" width="64" height="16"/>
 <tile id="0"><properties><property name="height_cm" type="int" value="0"/></properties></tile>
 <tile id="1"><properties><property name="height_cm" type="int" value="100"/></properties></tile>
 <tile id="2"><properties><property name="height_cm" type="int" value="200"/></properties></tile>
 <tile id="3"><properties><property name="height_cm" type="int" value="300"/></properties></tile>
</tileset>
```

`src/Nmf.Content.Tests/Fixtures/valid.tmx`. The first row of `terrain` is grass, grass, forest, forest. Tile 9 is a rock and tile 10 a bush. The object layer is used by Task 9.
```xml
<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="4" height="3" tilewidth="16" tileheight="16" infinite="0" nextlayerid="5" nextobjectid="4">
 <tileset firstgid="1" source="terrain.tsx"/>
 <tileset firstgid="5" source="heights.tsx"/>
 <tileset firstgid="9" name="obstacles" tilewidth="16" tileheight="16" tilecount="2" columns="2">
  <tile id="0">
   <properties>
    <property name="obstacle_height_cm" type="int" value="120"/>
    <property name="concealment_per_m" type="float" value="1"/>
    <property name="cover" type="float" value="0.9"/>
   </properties>
  </tile>
  <tile id="1">
   <properties>
    <property name="obstacle_height_cm" type="int" value="80"/>
    <property name="concealment_per_m" type="float" value="0.6"/>
   </properties>
  </tile>
 </tileset>
 <layer id="1" name="terrain" width="4" height="3">
  <data encoding="csv">
1,1,2,2,
1,1,2,2,
3,3,3,1
</data>
 </layer>
 <layer id="2" name="height" width="4" height="3">
  <data encoding="csv">
5,6,7,8,
5,5,5,5,
0,0,0,0
</data>
 </layer>
 <layer id="3" name="obstacles" width="4" height="3">
  <data encoding="csv">
0,0,0,9,
0,10,0,0,
0,0,0,0
</data>
 </layer>
 <objectgroup id="4" name="ai">
  <object id="1" name="start_zone" type="zone" x="0" y="32" width="32" height="16"/>
  <object id="2" name="spawn_a" type="spawn" x="8" y="40">
   <point/>
  </object>
  <object id="3" name="path_north" class="patrol" x="16" y="8">
   <polyline points="0,0 32,0 32,8"/>
  </object>
 </objectgroup>
</map>
```

Add to `src/Nmf.Content.Tests/Nmf.Content.Tests.csproj`:
```xml
  <ItemGroup>
    <None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

- [ ] **Step 2: Create test helpers**

`src/Nmf.Content.Tests/TempMapDir.cs`:
```csharp
namespace Nmf.Content.Tests;

/// <summary>Temporary directory containing the fixture tilesets, for writing ad-hoc test maps.</summary>
internal sealed class TempMapDir : IDisposable
{
    public TempMapDir()
    {
        Dir = Directory.CreateTempSubdirectory("nmf-test-").FullName;
        foreach (var file in new[] { "terrain.tsx", "heights.tsx" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), Path.Combine(Dir, file));
    }

    public string Dir { get; }

    public string WriteMap(string xml)
    {
        var path = Path.Combine(Dir, "map.tmx");
        File.WriteAllText(path, xml);
        return path;
    }

    public void Dispose() => Directory.Delete(Dir, recursive: true);
}
```

`src/Nmf.Content.Tests/TmxText.cs`:
```csharp
namespace Nmf.Content.Tests;

/// <summary>Builds small TMX documents referencing the fixture tilesets (terrain gids 1-4, heights gids 5-8).</summary>
internal static class TmxText
{
    public static string Map(int width, int height, string terrainCsv, string extra = "") =>
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" orientation="orthogonal" renderorder="right-down" width="{width}" height="{height}" tilewidth="16" tileheight="16" infinite="0">
         <tileset firstgid="1" source="terrain.tsx"/>
         <tileset firstgid="5" source="heights.tsx"/>
         <layer id="1" name="terrain" width="{width}" height="{height}">
          <data encoding="csv">{terrainCsv}</data>
         </layer>
         {extra}
        </map>
        """;

    public static string Layer(string name, int width, int height, string csv) =>
        $"""<layer id="9" name="{name}" width="{width}" height="{height}"><data encoding="csv">{csv}</data></layer>""";
}
```

- [ ] **Step 3: Write the failing tests**

`src/Nmf.Content.Tests/TmxMapLoaderTests.cs`:
```csharp
using System.Globalization;
using Nmf.Content.Tiled;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content.Tests;

public class TmxMapLoaderTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private static MapLoadException LoadFails(string xml)
    {
        using var dir = new TempMapDir();
        return Assert.Throws<MapLoadException>(() => TmxMapLoader.Load(dir.WriteMap(xml)));
    }

    private static GridMap LoadText(string xml)
    {
        using var dir = new TempMapDir();
        return TmxMapLoader.Load(dir.WriteMap(xml));
    }

    [Fact]
    public void Load_ValidMap_ReadsSizeAndTerrainNames()
    {
        var map = TmxMapLoader.Load(ValidPath);
        Assert.Equal(4, map.Width);
        Assert.Equal(3, map.Height);
        Assert.Equal(new[] { "none", "grass", "forest", "swamp" }, map.TerrainNames);
    }

    [Fact]
    public void Load_ValidMap_CombinesTerrainHeightAndObstacles()
    {
        var map = TmxMapLoader.Load(ValidPath);

        // grass, height 100
        Assert.Equal(new CellData(100, 0, 13, 0, 1), map[new CellCoord(1, 0)]);
        // forest, height 200
        Assert.Equal(new CellData(200, 1500, 102, 26, 2), map[new CellCoord(2, 0)]);
        // forest + rock: obstacle/concealment/cover take the max, height 300
        Assert.Equal(new CellData(300, 1500, 255, 230, 2), map[new CellCoord(3, 0)]);
        // grass + bush
        Assert.Equal(new CellData(0, 80, 153, 0, 1), map[new CellCoord(1, 1)]);
        // swamp, empty height cell
        Assert.Equal(new CellData(0, 0, 5, 0, 3), map[new CellCoord(0, 2)]);
    }

    [Fact]
    public void Load_UnderFinnishCulture_ParsesNumbersInvariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            var map = TmxMapLoader.Load(ValidPath);
            Assert.Equal(102, map[new CellCoord(2, 0)].ConcealmentPerM);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Load_FlippedTile_IsTreatedAsBaseTile()
    {
        // 2147483649 = gid 1 with the horizontal-flip flag (bit 31) set.
        var map = LoadText(TmxText.Map(2, 1, "2147483649,2"));
        Assert.Equal("grass", map.TerrainNames[map[new CellCoord(0, 0)].TerrainId]);
    }

    [Fact]
    public void Load_EmbeddedTileset_Works()
    {
        var extra = """
            <tileset firstgid="20" name="extra" tilecount="1">
             <tile id="0"><properties><property name="terrain" value="road"/></properties></tile>
            </tileset>
            """;
        var map = LoadText(TmxText.Map(2, 1, "20,1", extra));
        Assert.Equal("road", map.TerrainNames[map[new CellCoord(0, 0)].TerrainId]);
    }

    [Fact]
    public void Load_MissingFile_ThrowsWithPath()
    {
        var ex = Assert.Throws<MapLoadException>(() => TmxMapLoader.Load("does/not/exist.tmx"));
        Assert.Contains("exist.tmx", ex.Message);
    }

    [Fact]
    public void Load_MalformedXml_Throws()
    {
        LoadFails("<map width=");
    }

    [Fact]
    public void Load_Base64Layer_ExplainsCsvRequirement()
    {
        var xml = TmxText.Map(2, 1, "1,1").Replace("encoding=\"csv\"", "encoding=\"base64\" compression=\"zlib\"");
        var ex = LoadFails(xml);
        Assert.Contains("base64", ex.Message);
        Assert.Contains("CSV", ex.Message);
    }

    [Fact]
    public void Load_InfiniteMap_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("infinite=\"0\"", "infinite=\"1\""));
        Assert.Contains("infinite", ex.Message);
    }

    [Fact]
    public void Load_NonOrthogonalMap_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("orthogonal", "isometric"));
        Assert.Contains("orthogonal", ex.Message);
    }

    [Fact]
    public void Load_NonSquareTiles_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("tileheight=\"16\"", "tileheight=\"8\""));
        Assert.Contains("square", ex.Message);
    }

    [Fact]
    public void Load_MissingTerrainLayer_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("name=\"terrain\"", "name=\"height\""));
        Assert.Contains("'terrain'", ex.Message);
    }

    [Fact]
    public void Load_UnknownLayerName_ListsExpectedNames()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("trees", 2, 1, "0,0")));
        Assert.Contains("unknown tile layer 'trees'", ex.Message);
        Assert.Contains("obstacles", ex.Message);
    }

    [Fact]
    public void Load_DuplicateLayer_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("terrain", 2, 1, "1,1")));
        Assert.Contains("duplicate tile layer 'terrain'", ex.Message);
    }

    [Fact]
    public void Load_LayerGroup_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", "<group id=\"7\" name=\"g\"/>"));
        Assert.Contains("group", ex.Message);
    }

    [Fact]
    public void Load_EmptyTerrainCell_ReportsCoordinates()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,0"));
        Assert.Contains("(1,0)", ex.Message);
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Load_TerrainTileWithoutTerrainProperty_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,4"));
        Assert.Contains("no 'terrain' property", ex.Message);
    }

    [Fact]
    public void Load_WrongTileCount_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 2, "1,1,1"));
        Assert.Contains("expected 4", ex.Message);
    }

    [Fact]
    public void Load_GidOutsideTilesets_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,99"));
        Assert.Contains("does not belong to any tileset", ex.Message);
    }

    [Fact]
    public void Load_FractionOutOfRange_Throws()
    {
        var extra = """
            <tileset firstgid="20" name="bad" tilecount="1">
             <tile id="0"><properties>
              <property name="terrain" value="x"/>
              <property name="concealment_per_m" type="float" value="1.5"/>
             </properties></tile>
            </tileset>
            """;
        var ex = LoadFails(TmxText.Map(2, 1, "20,1", extra));
        Assert.Contains("concealment_per_m", ex.Message);
        Assert.Contains("between 0 and 1", ex.Message);
    }

    [Fact]
    public void Load_HeightTileWithoutHeightProperty_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1", TmxText.Layer("height", 2, 1, "1,0")));
        Assert.Contains("'height_cm'", ex.Message);
    }

    [Fact]
    public void Load_MissingTilesetFile_Throws()
    {
        var ex = LoadFails(TmxText.Map(2, 1, "1,1").Replace("heights.tsx", "missing.tsx"));
        Assert.Contains("tileset file not found: missing.tsx", ex.Message);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Content.Tests`
Expected: build FAILS with `The type or namespace name 'Tiled' does not exist`.

- [ ] **Step 5: Implement exception, tileset types and parser**

`src/Nmf.Content/Tiled/MapLoadException.cs`:
```csharp
namespace Nmf.Content.Tiled;

/// <summary>A map or tileset file could not be loaded. The message is meant for mission authors.</summary>
public sealed class MapLoadException(string path, string message, Exception? inner = null)
    : Exception($"{path}: {message}", inner)
{
    public string Path { get; } = path;
}
```

`src/Nmf.Content/Tiled/TilesetRef.cs`:
```csharp
namespace Nmf.Content.Tiled;

/// <summary>A tileset as used by one map: its first global tile id and per-tile custom properties.</summary>
internal sealed record TilesetRef(
    int FirstGid,
    int TileCount,
    IReadOnlyDictionary<int, IReadOnlyDictionary<string, string>> Tiles);
```

`src/Nmf.Content/Tiled/TsxParser.cs`:
```csharp
using System.Xml.Linq;

namespace Nmf.Content.Tiled;

internal static class TsxParser
{
    /// <summary>Parses a &lt;tileset&gt; element (external .tsx root or embedded in a .tmx).</summary>
    public static TilesetRef Parse(XElement tileset, int firstGid, string sourcePath)
    {
        int tileCount = (int?)tileset.Attribute("tilecount") ?? 0;
        var tiles = new Dictionary<int, IReadOnlyDictionary<string, string>>();

        foreach (var tile in tileset.Elements("tile"))
        {
            int id = (int?)tile.Attribute("id")
                ?? throw new MapLoadException(sourcePath, "<tile> is missing attribute 'id'");
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in tile.Element("properties")?.Elements("property") ?? [])
            {
                string name = (string?)property.Attribute("name")
                    ?? throw new MapLoadException(sourcePath, $"tile {id} has a property without a name");
                // Multi-line string properties are stored as element text instead of a value attribute.
                properties[name] = (string?)property.Attribute("value") ?? property.Value;
            }
            tiles[id] = properties;
        }

        return new TilesetRef(firstGid, tileCount, tiles);
    }
}
```

- [ ] **Step 6: Implement the loader**

`src/Nmf.Content/Tiled/TmxMapLoader.cs`:
```csharp
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Nmf.Sim;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content.Tiled;

/// <summary>
/// Loads a Tiled map (orthogonal, square tiles, CSV layers) into a <see cref="GridMap"/>.
/// Tile layers: "terrain" (required), "height", "obstacles". One tile = one 1 m cell.
/// </summary>
public static class TmxMapLoader
{
    public static GridMap Load(string tmxPath)
    {
        try
        {
            return new Loader(Path.GetFullPath(tmxPath)).Load();
        }
        catch (MapLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is XmlException or FormatException or OverflowException
                                       or IOException or UnauthorizedAccessException)
        {
            throw new MapLoadException(tmxPath, ex.Message, ex);
        }
    }

    private sealed class Loader(string path)
    {
        private const uint GidMask = 0x0FFFFFFF; // clears Tiled flip/rotation flag bits 28-31
        private static readonly string[] KnownTileLayers = ["terrain", "height", "obstacles"];
        private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>();

        private readonly List<TilesetRef> _tilesets = [];
        private int _width;
        private int _height;
        private int _tileSize;

        private int WidthCm => _width * SimConstants.CentimetersPerCell;
        private int HeightCm => _height * SimConstants.CentimetersPerCell;

        public GridMap Load()
        {
            if (!File.Exists(path))
                throw Error("file not found");

            var map = XDocument.Load(path).Root;
            if (map is null || map.Name.LocalName != "map")
                throw Error("root element is not <map>");
            if ((string?)map.Attribute("orientation") != "orthogonal")
                throw Error("only orthogonal maps are supported");
            if (((int?)map.Attribute("infinite") ?? 0) != 0)
                throw Error("infinite maps are not supported; untick 'Infinite' in the Tiled map properties");
            if (map.Elements("group").Any())
                throw Error("layer groups are not supported; move the layers out of the group");

            _width = RequiredInt(map, "width");
            _height = RequiredInt(map, "height");
            _tileSize = RequiredInt(map, "tilewidth");
            if (_tileSize <= 0 || _tileSize != RequiredInt(map, "tileheight"))
                throw Error("tiles must be square (tilewidth == tileheight)");
            if (_width is <= 0 or > GridMap.MaxSideCells || _height is <= 0 or > GridMap.MaxSideCells)
                throw Error($"map size must be 1..{GridMap.MaxSideCells} cells per side, was {_width}x{_height}");

            foreach (var tileset in map.Elements("tileset"))
                _tilesets.Add(LoadTileset(tileset));
            _tilesets.Sort((a, b) => a.FirstGid.CompareTo(b.FirstGid));

            var layers = ReadTileLayers(map);
            if (!layers.TryGetValue("terrain", out var terrain))
                throw Error("missing required tile layer 'terrain'");

            var terrainNames = new List<string> { "none" };
            var cells = new CellData[_width * _height];
            BuildTerrain(terrain, cells, terrainNames);
            if (layers.TryGetValue("height", out var height))
                ApplyHeight(height, cells);
            if (layers.TryGetValue("obstacles", out var obstacles))
                ApplyObstacles(obstacles, cells);

            var gridMap = new GridMap(_width, _height, terrainNames, ReadFeatures(map));
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    gridMap[new CellCoord(x, y)] = cells[y * _width + x];
            return gridMap;
        }

        // Task 9 replaces this with real object-layer parsing.
        private MapFeatures ReadFeatures(XElement map) => MapFeatures.Empty;

        private TilesetRef LoadTileset(XElement tileset)
        {
            int firstGid = RequiredInt(tileset, "firstgid");
            string? source = (string?)tileset.Attribute("source");
            if (source is null)
                return TsxParser.Parse(tileset, firstGid, path);

            string tsxPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, source));
            if (!File.Exists(tsxPath))
                throw Error($"tileset file not found: {source}");
            var root = XDocument.Load(tsxPath).Root;
            if (root is null || root.Name.LocalName != "tileset")
                throw new MapLoadException(tsxPath, "root element is not <tileset>");
            return TsxParser.Parse(root, firstGid, tsxPath);
        }

        private Dictionary<string, uint[]> ReadTileLayers(XElement map)
        {
            var result = new Dictionary<string, uint[]>(StringComparer.Ordinal);
            foreach (var layer in map.Elements("layer"))
            {
                string name = (string?)layer.Attribute("name") ?? "";
                if (!KnownTileLayers.Contains(name))
                    throw Error($"unknown tile layer '{name}'; expected one of: {string.Join(", ", KnownTileLayers)}");
                if (result.ContainsKey(name))
                    throw Error($"duplicate tile layer '{name}'");
                result[name] = ReadLayerData(layer, name);
            }
            return result;
        }

        private uint[] ReadLayerData(XElement layer, string name)
        {
            if ((int?)layer.Attribute("width") != _width || (int?)layer.Attribute("height") != _height)
                throw Error($"layer '{name}' size differs from the map size {_width}x{_height}");
            var data = layer.Element("data") ?? throw Error($"layer '{name}' has no <data>");
            string encoding = (string?)data.Attribute("encoding") ?? "xml";
            if (encoding != "csv")
                throw Error($"layer '{name}' uses '{encoding}' encoding; set Tile Layer Format to CSV in the Tiled map properties");

            var parts = data.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != _width * _height)
                throw Error($"layer '{name}' has {parts.Length} tiles, expected {_width * _height}");

            var gids = new uint[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                gids[i] = uint.Parse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture) & GidMask;
            return gids;
        }

        private void BuildTerrain(uint[] gids, CellData[] cells, List<string> terrainNames)
        {
            var ids = new Dictionary<string, ushort>(StringComparer.Ordinal) { ["none"] = 0 };
            for (int i = 0; i < gids.Length; i++)
            {
                int x = i % _width, y = i / _width;
                if (gids[i] == 0)
                    throw Error($"terrain layer cell ({x},{y}) is empty; every cell needs a terrain tile");

                var props = Resolve(gids[i], "terrain", x, y);
                if (!props.TryGetValue("terrain", out var terrainName) || terrainName.Length == 0)
                    throw Error($"tile {gids[i]} used at ({x},{y}) in layer 'terrain' has no 'terrain' property");

                if (!ids.TryGetValue(terrainName, out var id))
                {
                    if (terrainNames.Count > ushort.MaxValue)
                        throw Error("too many terrain types");
                    id = (ushort)terrainNames.Count;
                    ids[terrainName] = id;
                    terrainNames.Add(terrainName);
                }

                cells[i] = new CellData(
                    GroundHeightCm: 0,
                    ObstacleHeightCm: ShortProperty(props, "obstacle_height_cm", x, y),
                    ConcealmentPerM: FractionProperty(props, "concealment_per_m", x, y),
                    Cover: FractionProperty(props, "cover", x, y),
                    TerrainId: id);
            }
        }

        private void ApplyHeight(uint[] gids, CellData[] cells)
        {
            for (int i = 0; i < gids.Length; i++)
            {
                if (gids[i] == 0)
                    continue;
                int x = i % _width, y = i / _width;
                var props = Resolve(gids[i], "height", x, y);
                if (!props.ContainsKey("height_cm"))
                    throw Error($"tile {gids[i]} used at ({x},{y}) in layer 'height' has no 'height_cm' property");
                cells[i].GroundHeightCm = ShortProperty(props, "height_cm", x, y);
            }
        }

        private void ApplyObstacles(uint[] gids, CellData[] cells)
        {
            for (int i = 0; i < gids.Length; i++)
            {
                if (gids[i] == 0)
                    continue;
                int x = i % _width, y = i / _width;
                var props = Resolve(gids[i], "obstacles", x, y);
                cells[i].ObstacleHeightCm = Math.Max(cells[i].ObstacleHeightCm, ShortProperty(props, "obstacle_height_cm", x, y));
                cells[i].ConcealmentPerM = Math.Max(cells[i].ConcealmentPerM, FractionProperty(props, "concealment_per_m", x, y));
                cells[i].Cover = Math.Max(cells[i].Cover, FractionProperty(props, "cover", x, y));
            }
        }

        private IReadOnlyDictionary<string, string> Resolve(uint gid, string layer, int x, int y)
        {
            TilesetRef? owner = null;
            foreach (var tileset in _tilesets)
            {
                if (tileset.FirstGid <= gid)
                    owner = tileset;
                else
                    break;
            }

            long local = owner is null ? -1 : gid - owner.FirstGid;
            if (owner is null || (owner.TileCount > 0 && local >= owner.TileCount))
                throw Error($"tile {gid} used at ({x},{y}) in layer '{layer}' does not belong to any tileset");
            return owner.Tiles.TryGetValue((int)local, out var props) ? props : NoProperties;
        }

        private short ShortProperty(IReadOnlyDictionary<string, string> props, string name, int x, int y)
        {
            if (!props.TryGetValue(name, out var text))
                return 0;
            if (!short.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                throw Error($"property '{name}' of the tile at ({x},{y}) must be a whole number between {short.MinValue} and {short.MaxValue}, was '{text}'");
            return value;
        }

        private byte FractionProperty(IReadOnlyDictionary<string, string> props, string name, int x, int y)
        {
            if (!props.TryGetValue(name, out var text))
                return 0;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !(value >= 0 && value <= 1))
                throw Error($"property '{name}' of the tile at ({x},{y}) must be a number between 0 and 1, was '{text}'");
            return (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
        }

        private int RequiredInt(XElement element, string attribute) =>
            (int?)element.Attribute(attribute)
            ?? throw Error($"<{element.Name.LocalName}> is missing attribute '{attribute}'");

        private MapLoadException Error(string message) => new(path, message);
    }
}
```

Note: `WidthCm` and `HeightCm` are only used from Task 9 on. Unused private properties do not produce compiler warnings, so they can stay.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test src/Nmf.Content.Tests`
Expected: PASS, 22 tests.

- [ ] **Step 8: Commit**

```bash
git add src/Nmf.Content src/Nmf.Content.Tests
git commit -m "feat(content): load Tiled maps (tilesets, terrain, height, obstacles)

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: Tiled loader – object layers to map features

**Files:**
- Modify: `src/Nmf.Content/Tiled/TmxMapLoader.cs` (replace the `ReadFeatures` stub)
- Test: `src/Nmf.Content.Tests/TmxObjectTests.cs`

**Interfaces:**
- Consumes: `MapZone`, `MapPoint`, `MapPath`, `MapFeatures` (Task 4), loader internals (Task 8)
- Produces: `GridMap.Features` filled from every `<objectgroup>`:
  - A rectangle becomes a `MapZone` (Min at the top-left, Max exclusive).
  - `<point/>` becomes a `MapPoint`.
  - `<polyline>` becomes a `MapPath` (at least 2 points).
  - Pixels are converted as `cm = round(px * 100 / tilewidth)`.
  - `Type` comes from the `type` attribute, falling back to `class` (Tiled 1.9).
  - Every object needs a unique non-empty name and must lie inside the map.
  - Tile objects, polygons, ellipses and text raise errors.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Content.Tests/TmxObjectTests.cs`:
```csharp
using Nmf.Content.Tiled;
using Nmf.Sim.Core;

namespace Nmf.Content.Tests;

public class TmxObjectTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private static MapLoadException LoadFails(string objectsXml)
    {
        using var dir = new TempMapDir();
        var xml = TmxText.Map(4, 3, "1,1,1,1,1,1,1,1,1,1,1,1", $"<objectgroup id=\"4\" name=\"ai\">{objectsXml}</objectgroup>");
        return Assert.Throws<MapLoadException>(() => TmxMapLoader.Load(dir.WriteMap(xml)));
    }

    [Fact]
    public void Load_ValidMap_ReadsZone()
    {
        var zone = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Zones);
        Assert.Equal("start_zone", zone.Name);
        Assert.Equal("zone", zone.Type);
        Assert.Equal(new Vec2(0, 200), zone.Min);
        Assert.Equal(new Vec2(200, 300), zone.Max);
    }

    [Fact]
    public void Load_ValidMap_ReadsPoint()
    {
        var point = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Points);
        Assert.Equal("spawn_a", point.Name);
        Assert.Equal("spawn", point.Type);
        Assert.Equal(new Vec2(50, 250), point.Position);
    }

    [Fact]
    public void Load_ValidMap_ReadsPathWithClassAttributeAsType()
    {
        var path = Assert.Single(TmxMapLoader.Load(ValidPath).Features.Paths);
        Assert.Equal("path_north", path.Name);
        Assert.Equal("patrol", path.Type);
        Assert.Equal(new[] { new Vec2(100, 50), new Vec2(300, 50), new Vec2(300, 100) }, path.Points);
    }

    [Fact]
    public void Load_UnnamedObject_Throws()
    {
        var ex = LoadFails("<object id=\"1\" x=\"0\" y=\"0\"><point/></object>");
        Assert.Contains("no name", ex.Message);
    }

    [Fact]
    public void Load_DuplicateNames_Throws()
    {
        var ex = LoadFails("""
            <object id="1" name="a" x="0" y="0"><point/></object>
            <object id="2" name="a" x="8" y="8"><point/></object>
            """);
        Assert.Contains("duplicate object name 'a'", ex.Message);
    }

    [Fact]
    public void Load_PointOutsideMap_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"far\" x=\"640\" y=\"0\"><point/></object>");
        Assert.Contains("outside the map", ex.Message);
    }

    [Fact]
    public void Load_ZoneOutsideMap_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"big\" x=\"32\" y=\"0\" width=\"64\" height=\"16\"/>");
        Assert.Contains("zone 'big' lies outside the map", ex.Message);
    }

    [Fact]
    public void Load_ZeroSizeZone_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"flat\" x=\"0\" y=\"0\"/>");
        Assert.Contains("zero size", ex.Message);
    }

    [Fact]
    public void Load_TileObject_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"tree\" gid=\"1\" x=\"0\" y=\"16\" width=\"16\" height=\"16\"/>");
        Assert.Contains("tile object", ex.Message);
    }

    [Fact]
    public void Load_Polygon_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"poly\" x=\"0\" y=\"0\"><polygon points=\"0,0 8,0 8,8\"/></object>");
        Assert.Contains("only rectangles, points and polylines", ex.Message);
    }

    [Fact]
    public void Load_SinglePointPath_Throws()
    {
        var ex = LoadFails("<object id=\"1\" name=\"stub\" x=\"0\" y=\"0\"><polyline points=\"0,0\"/></object>");
        Assert.Contains("at least two points", ex.Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Content.Tests --filter FullyQualifiedName~TmxObjectTests`
Expected: FAIL, e.g. `Load_ValidMap_ReadsZone` with `The collection was expected to contain a single element, but it was empty`.

- [ ] **Step 3: Implement**

In `src/Nmf.Content/Tiled/TmxMapLoader.cs`, replace the stub line
```csharp
        // Task 9 replaces this with real object-layer parsing.
        private MapFeatures ReadFeatures(XElement map) => MapFeatures.Empty;
```
with (make sure `WidthCm` and `HeightCm` exist in `Loader`, as shown in Task 8):
```csharp
        private MapFeatures ReadFeatures(XElement map)
        {
            var zones = new List<MapZone>();
            var points = new List<MapPoint>();
            var paths = new List<MapPath>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var obj in map.Elements("objectgroup").Elements("object"))
            {
                string id = (string?)obj.Attribute("id") ?? "?";
                string name = (string?)obj.Attribute("name") ?? "";
                // Tiled 1.9 wrote "class"; 1.8 and 1.10+ write "type".
                string type = (string?)obj.Attribute("type") ?? (string?)obj.Attribute("class") ?? "";

                if (obj.Attribute("gid") is not null)
                    throw Error($"object '{name}' (id {id}) is a tile object; tile objects are not supported yet");
                if (name.Length == 0)
                    throw Error($"object id {id} has no name; every map object needs a unique name");
                if (!names.Add(name))
                    throw Error($"duplicate object name '{name}'");

                var origin = new Vec2(ToCm((double?)obj.Attribute("x") ?? 0), ToCm((double?)obj.Attribute("y") ?? 0));

                if (obj.Element("point") is not null)
                {
                    points.Add(new MapPoint(name, type, RequireInside(origin, name)));
                }
                else if (obj.Element("polyline") is { } polyline)
                {
                    paths.Add(new MapPath(name, type, ParsePoints(polyline, origin, name)));
                }
                else if (obj.Elements().Any(e => e.Name.LocalName is "polygon" or "ellipse" or "text"))
                {
                    throw Error($"object '{name}': only rectangles, points and polylines are supported");
                }
                else
                {
                    var size = new Vec2(ToCm((double?)obj.Attribute("width") ?? 0), ToCm((double?)obj.Attribute("height") ?? 0));
                    if (size.X <= 0 || size.Y <= 0)
                        throw Error($"zone '{name}' has zero size");
                    var max = origin + size;
                    if (origin.X < 0 || origin.Y < 0 || max.X > WidthCm || max.Y > HeightCm)
                        throw Error($"zone '{name}' lies outside the map");
                    zones.Add(new MapZone(name, type, origin, max));
                }
            }

            return new MapFeatures(zones, points, paths);
        }

        private List<Vec2> ParsePoints(XElement polyline, Vec2 origin, string name)
        {
            string raw = (string?)polyline.Attribute("points") ?? "";
            var result = new List<Vec2>();
            foreach (var pair in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var xy = pair.Split(',');
                if (xy.Length != 2)
                    throw Error($"path '{name}' has a malformed point '{pair}'");
                var offset = new Vec2(
                    ToCm(double.Parse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture)),
                    ToCm(double.Parse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture)));
                result.Add(RequireInside(origin + offset, name));
            }
            if (result.Count < 2)
                throw Error($"path '{name}' needs at least two points");
            return result;
        }

        private int ToCm(double pixels) =>
            (int)Math.Round(pixels * SimConstants.CentimetersPerCell / _tileSize, MidpointRounding.AwayFromZero);

        private Vec2 RequireInside(Vec2 p, string name) =>
            p.X >= 0 && p.Y >= 0 && p.X < WidthCm && p.Y < HeightCm
                ? p
                : throw Error($"object '{name}' lies outside the map at {p}");
```

- [ ] **Step 4: Run all content tests to verify they pass**

Run: `dotnet test src/Nmf.Content.Tests`
Expected: PASS (Task 8 and Task 9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Nmf.Content/Tiled/TmxMapLoader.cs src/Nmf.Content.Tests/TmxObjectTests.cs
git commit -m "feat(content): read zones, points and paths from Tiled object layers

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Map summary, `nmf map-info`, core sample content and README

**Files:**
- Create: `src/Nmf.Content/MapSummary.cs`
- Modify: `src/Nmf.Cli/Program.cs`
- Create: `tools/make_placeholder_tiles.py`
- Create: `content/core/tilesets/terrain.tsx`, `content/core/tilesets/heights.tsx`, `content/core/tilesets/obstacles.tsx` (and generated `.png` files)
- Create: `content/core/maps/sandbox.tmx`
- Create: `README.md`
- Test: `src/Nmf.Content.Tests/MapSummaryTests.cs`, `src/Nmf.Content.Tests/CoreContentTests.cs`

**Interfaces:**
- Consumes: `TmxMapLoader.Load`, `MapLoadException` (Tasks 8–9), `GridMap` (Task 4)
- Produces:
  - `static class MapSummary` with `string Describe(GridMap map)`: `\n` line endings, invariant culture, terrain sorted by count (descending) then by name.
  - CLI `nmf map-info <map.tmx>`: exit code 0 and the summary on stdout; exit code 1 and `error: <message>` on stderr for a load error; exit code 2 and usage for bad arguments.
  - Core content paths used by later phases: `content/core/tilesets/*.tsx`, `content/core/maps/sandbox.tmx`.

- [ ] **Step 1: Write the failing tests**

`src/Nmf.Content.Tests/MapSummaryTests.cs`:
```csharp
using System.Globalization;
using Nmf.Content.Tiled;

namespace Nmf.Content.Tests;

public class MapSummaryTests
{
    private static readonly string ValidPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid.tmx");

    private const string Expected =
        "Size: 4 x 3 cells (4 m x 3 m)\n" +
        "Terrain:\n" +
        "  grass: 5 (41.7 %)\n" +
        "  forest: 4 (33.3 %)\n" +
        "  swamp: 3 (25.0 %)\n" +
        "Ground height: 0 .. 300 cm\n" +
        "Zones (1): start_zone\n" +
        "Points (1): spawn_a\n" +
        "Paths (1): path_north\n";

    [Fact]
    public void Describe_ValidMap()
    {
        Assert.Equal(Expected, MapSummary.Describe(TmxMapLoader.Load(ValidPath)));
    }

    [Fact]
    public void Describe_UnderFinnishCulture_IsIdentical()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fi-FI");
            Assert.Equal(Expected, MapSummary.Describe(TmxMapLoader.Load(ValidPath)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Describe_MapWithoutObjects_ShowsDashes()
    {
        using var dir = new TempMapDir();
        var map = TmxMapLoader.Load(dir.WriteMap(TmxText.Map(2, 1, "1,1")));
        var text = MapSummary.Describe(map);
        Assert.Contains("Zones (0): -\n", text);
        Assert.Contains("Paths (0): -\n", text);
    }
}
```

`src/Nmf.Content.Tests/CoreContentTests.cs`:
```csharp
using Nmf.Content.Tiled;

namespace Nmf.Content.Tests;

/// <summary>Everything shipped under content/ must load cleanly.</summary>
public class CoreContentTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NoMansForest.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (NoMansForest.slnx) not found.");
    }

    [Fact]
    public void AllCoreMaps_Load()
    {
        var maps = Directory.GetFiles(Path.Combine(RepoRoot(), "content"), "*.tmx", SearchOption.AllDirectories);
        Assert.NotEmpty(maps);
        foreach (var path in maps)
            TmxMapLoader.Load(path);
    }

    [Fact]
    public void SandboxMap_HasExpectedShapeAndFeatures()
    {
        var map = TmxMapLoader.Load(Path.Combine(RepoRoot(), "content", "core", "maps", "sandbox.tmx"));
        Assert.Equal(12, map.Width);
        Assert.Equal(8, map.Height);
        Assert.Equal(new[] { "none", "forest", "grass", "road", "swamp" }, map.TerrainNames);
        Assert.Equal(2, map.Features.Zones.Count);
        Assert.Single(map.Features.Points);
        Assert.Single(map.Features.Paths);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test src/Nmf.Content.Tests --filter "FullyQualifiedName~MapSummaryTests|FullyQualifiedName~CoreContentTests"`
Expected: build FAILS with `The name 'MapSummary' does not exist`.

- [ ] **Step 3: Implement MapSummary**

`src/Nmf.Content/MapSummary.cs`:
```csharp
using System.Text;
using Nmf.Sim.Core;
using Nmf.Sim.World;

namespace Nmf.Content;

/// <summary>Human-readable description of a map, for the CLI and for mission authors.</summary>
public static class MapSummary
{
    public static string Describe(GridMap map)
    {
        var counts = new int[map.TerrainNames.Count];
        int minHeight = int.MaxValue, maxHeight = int.MinValue;
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                var cell = map[new CellCoord(x, y)];
                counts[cell.TerrainId]++;
                minHeight = Math.Min(minHeight, cell.GroundHeightCm);
                maxHeight = Math.Max(maxHeight, cell.GroundHeightCm);
            }
        }
        int total = map.Width * map.Height;

        var sb = new StringBuilder();
        Line(sb, $"Size: {map.Width} x {map.Height} cells ({map.Width} m x {map.Height} m)");
        Line(sb, $"Terrain:");
        var terrains = counts
            .Select((count, id) => (Name: map.TerrainNames[id], Count: count))
            .Where(t => t.Count > 0)
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.Ordinal);
        foreach (var (name, count) in terrains)
            Line(sb, $"  {name}: {count} ({count * 100.0 / total:0.0} %)");
        Line(sb, $"Ground height: {minHeight} .. {maxHeight} cm");
        Line(sb, $"Zones ({map.Features.Zones.Count}): {Names(map.Features.Zones.Select(z => z.Name))}");
        Line(sb, $"Points ({map.Features.Points.Count}): {Names(map.Features.Points.Select(p => p.Name))}");
        Line(sb, $"Paths ({map.Features.Paths.Count}): {Names(map.Features.Paths.Select(p => p.Name))}");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, FormattableString text) =>
        sb.Append(FormattableString.Invariant(text)).Append('\n');

    private static string Names(IEnumerable<string> names)
    {
        string joined = string.Join(", ", names);
        return joined.Length == 0 ? "-" : joined;
    }
}
```

- [ ] **Step 4: Implement the CLI**

Replace `src/Nmf.Cli/Program.cs`:
```csharp
using Nmf.Content;
using Nmf.Content.Tiled;

if (args is ["map-info", var mapPath])
{
    try
    {
        Console.Write(MapSummary.Describe(TmxMapLoader.Load(mapPath)));
        return 0;
    }
    catch (MapLoadException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }
}

Console.Error.WriteLine("usage: nmf map-info <map.tmx>");
return 2;
```

- [ ] **Step 5: Create the placeholder tile generator and run it**

`tools/make_placeholder_tiles.py`:
```python
"""Writes flat-colour placeholder tileset images for content/core/tilesets (stdlib only).

Run from the repository root: python3 tools/make_placeholder_tiles.py
"""
import pathlib
import struct
import zlib

TILE = 16
OUT = pathlib.Path("content/core/tilesets")

TILESETS = {
    "terrain.png": [(96, 140, 60), (34, 85, 40), (90, 110, 90), (150, 120, 80)],  # grass, forest, swamp, road
    "heights.png": [(40, 40, 40), (90, 90, 90), (150, 150, 150), (210, 210, 210)],  # 0, 1, 2, 3 m
    "obstacles.png": [(128, 128, 128), (60, 120, 50)],  # rock, bush
}


def chunk(kind: bytes, data: bytes) -> bytes:
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def write_strip(path: pathlib.Path, colours: list[tuple[int, int, int]]) -> None:
    width = TILE * len(colours)
    rows = []
    for y in range(TILE):
        row = bytearray([0])  # PNG filter type: none
        for colour in colours:
            for x in range(TILE):
                edge = x == 0 or y == 0
                row.extend(max(0, c - 30) if edge else c for c in colour)
        rows.append(bytes(row))
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, TILE, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(b"".join(rows)))
        + chunk(b"IEND", b"")
    )
    path.write_bytes(png)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for name, colours in TILESETS.items():
        write_strip(OUT / name, colours)
        print(f"wrote {OUT / name}")
```

Run: `python3 tools/make_placeholder_tiles.py`
Expected: three `wrote content/core/tilesets/...png` lines.

- [ ] **Step 6: Create core tilesets**

`content/core/tilesets/terrain.tsx`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="terrain" tilewidth="16" tileheight="16" tilecount="4" columns="4">
 <image source="terrain.png" width="64" height="16"/>
 <tile id="0">
  <properties>
   <property name="terrain" value="grass"/>
   <property name="concealment_per_m" type="float" value="0.05"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="terrain" value="forest"/>
   <property name="concealment_per_m" type="float" value="0.12"/>
   <property name="cover" type="float" value="0.1"/>
   <property name="obstacle_height_cm" type="int" value="1500"/>
  </properties>
 </tile>
 <tile id="2">
  <properties>
   <property name="terrain" value="swamp"/>
   <property name="concealment_per_m" type="float" value="0.02"/>
  </properties>
 </tile>
 <tile id="3">
  <properties>
   <property name="terrain" value="road"/>
  </properties>
 </tile>
</tileset>
```

`content/core/tilesets/heights.tsx`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="heights" tilewidth="16" tileheight="16" tilecount="4" columns="4">
 <image source="heights.png" width="64" height="16"/>
 <tile id="0"><properties><property name="height_cm" type="int" value="0"/></properties></tile>
 <tile id="1"><properties><property name="height_cm" type="int" value="100"/></properties></tile>
 <tile id="2"><properties><property name="height_cm" type="int" value="200"/></properties></tile>
 <tile id="3"><properties><property name="height_cm" type="int" value="300"/></properties></tile>
</tileset>
```

`content/core/tilesets/obstacles.tsx`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.11.2" name="obstacles" tilewidth="16" tileheight="16" tilecount="2" columns="2">
 <image source="obstacles.png" width="32" height="16"/>
 <tile id="0">
  <properties>
   <property name="obstacle_height_cm" type="int" value="120"/>
   <property name="concealment_per_m" type="float" value="1"/>
   <property name="cover" type="float" value="0.9"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="obstacle_height_cm" type="int" value="80"/>
   <property name="concealment_per_m" type="float" value="0.6"/>
  </properties>
 </tile>
</tileset>
```

- [ ] **Step 7: Create the sandbox map**

`content/core/maps/sandbox.tmx`. It is 12 × 8 cells: a road runs north–south, there is swamp in the middle and forest in the corners. Gids are grass 1, forest 2, swamp 3, road 4, heights 5–8, rock 9 and bush 10.
```xml
<?xml version="1.0" encoding="UTF-8"?>
<map version="1.10" tiledversion="1.11.2" orientation="orthogonal" renderorder="right-down" width="12" height="8" tilewidth="16" tileheight="16" infinite="0" nextlayerid="5" nextobjectid="5">
 <tileset firstgid="1" source="../tilesets/terrain.tsx"/>
 <tileset firstgid="5" source="../tilesets/heights.tsx"/>
 <tileset firstgid="9" source="../tilesets/obstacles.tsx"/>
 <layer id="1" name="terrain" width="12" height="8">
  <data encoding="csv">
2,2,2,2,1,1,4,1,1,2,2,2,
2,2,2,1,1,1,4,1,1,1,2,2,
2,2,1,1,1,1,4,1,1,1,1,2,
1,1,1,1,3,3,4,3,1,1,1,1,
1,1,1,3,3,3,4,3,3,1,1,1,
1,1,1,1,3,3,4,3,1,1,1,2,
2,1,1,1,1,1,4,1,1,1,2,2,
2,2,1,1,1,1,4,1,1,2,2,2
</data>
 </layer>
 <layer id="2" name="height" width="12" height="8">
  <data encoding="csv">
8,8,7,7,6,6,5,5,5,6,7,7,
8,7,7,6,6,5,5,5,5,6,6,7,
7,7,6,6,5,5,5,5,5,5,6,6,
6,6,5,5,5,5,5,5,5,5,5,6,
5,5,5,5,5,5,5,5,5,5,5,5,
5,5,5,5,5,5,5,5,5,5,5,5,
6,5,5,5,5,5,5,5,5,5,5,6,
6,6,5,5,5,5,5,5,5,5,6,6
</data>
 </layer>
 <layer id="3" name="obstacles" width="12" height="8">
  <data encoding="csv">
0,0,0,0,0,10,0,0,0,0,0,0,
0,0,0,9,0,0,0,0,10,0,0,0,
0,0,0,0,0,0,0,0,0,0,9,0,
0,10,0,0,0,0,0,0,0,0,0,0,
0,0,0,0,0,0,0,0,0,10,0,0,
0,0,9,0,0,0,0,0,0,0,0,0,
0,0,0,0,10,0,0,0,9,0,0,0,
0,0,0,0,0,0,0,0,0,0,0,0
</data>
 </layer>
 <objectgroup id="4" name="ai">
  <object id="1" name="start_zone" type="zone" x="64" y="96" width="64" height="32"/>
  <object id="2" name="objective" type="zone" x="128" y="0" width="48" height="32"/>
  <object id="3" name="path_road" type="patrol" x="120" y="8">
   <polyline points="0,0 0,80"/>
  </object>
  <object id="4" name="spawn_blue" type="spawn" x="96" y="112">
   <point/>
  </object>
 </objectgroup>
</map>
```

- [ ] **Step 8: Write the README**

`README.md`:
````markdown
# No Man's Forest

A 2D top-down WW2 tactics game set in the Finnish Continuation War (1941–44):
Close Combat style real-time squad fighting, Jagged Alliance 2 style turn-based contact mode,
and Squad Leader style leadership and morale. Hobby project, open source.

Design: [`docs/superpowers/specs/2026-09-24-no-mans-forest-design.md`](docs/superpowers/specs/2026-09-24-no-mans-forest-design.md)

## Build and test

Requires the .NET 10 SDK.

```bash
dotnet build NoMansForest.slnx
dotnet test NoMansForest.slnx
```

## Tools

```bash
dotnet run --project src/Nmf.Cli -- map-info content/core/maps/sandbox.tmx
```

## Making maps with Tiled

Maps are made with [Tiled](https://www.mapeditor.org). One tile is one 1 m × 1 m cell.

- Orthogonal map, square tiles, **Infinite** off, **Tile Layer Format: CSV**.
- Tile layers (exact names):
  - `terrain` (required, every cell filled): tile property `terrain` (name), optional `concealment_per_m` (0–1), `cover` (0–1), `obstacle_height_cm`.
  - `height` (optional): tile property `height_cm`.
  - `obstacles` (optional): `obstacle_height_cm`, `concealment_per_m`, `cover`, combined with the terrain using the larger value.
- Object layers: rectangles become zones, points become points, polylines become paths. Every object needs a unique name; the object *type/class* is kept as its type.
- Use the tilesets in `content/core/tilesets/`. `python3 tools/make_placeholder_tiles.py` regenerates their placeholder images.

## License

Code: MIT. Art and sound: CC BY-SA 4.0.
````

- [ ] **Step 9: Run all tests**

Run: `dotnet test NoMansForest.slnx`
Expected: PASS, all Sim and Content tests.

- [ ] **Step 10: Check the CLI manually**

Run: `dotnet run --project src/Nmf.Cli -- map-info content/core/maps/sandbox.tmx`
Expected output starts with:
```
Size: 12 x 8 cells (12 m x 8 m)
Terrain:
  grass: 53 (55.2 %)
```
Also check the error and usage paths:
- `dotnet run --project src/Nmf.Cli -- map-info nope.tmx; echo $?` prints `error: <absolute path>/nope.tmx: file not found` and `1`.
- `dotnet run --project src/Nmf.Cli; echo $?` prints the usage line and `2`.

- [ ] **Step 11: Commit**

```bash
git add -A
git commit -m "feat: add map summary CLI, core tilesets, sandbox map and README

Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## Spec coverage (phase 1)

| Spec item | Task |
|---|---|
| §3 project structure, dependency direction | 1 |
| §4.1 20 ticks/s, own RNG, determinism, id order | 1, 2, 5 |
| §4.2 orders in / events out, replay = seed + order log | 5, 6 |
| §4.3 plain C# objects | 5 |
| §7.1 cell fields (ground height, obstacle height, concealment, cover, terrain) | 4, 8 |
| §10.2 Tiled layers terrain/height/obstacles + AI objects | 8, 9, 10 |
| §12 determinism test, replay regression, content check | 6, 10 |
| Interpolation for smooth animation (§4.1) | 7 (`Alpha`) |

Deliberately not in phase 1: pathfinding, vision, combat, time modes, AI, YAML/Lua and the Godot project. These belong to phases 2–7 of spec §13.
