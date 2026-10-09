# Phase 5a: sound core and effects — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, then run `dotnet test NoMansForest.slnx` and the Python tool tests. Work on branch `phase-5a-sound`, merge to main with `--no-ff` ("Merge phase 5a: …") and push.

**Goal:** The game plays positional sound effects for what the player's side can hear (shots, cracks, impacts, grenades, falls, melee, looting, reloads, footsteps, UI). Sounds come from a data catalogue that is silent until the user imports AI-made files.

**Architecture:** Every audio decision lives in pure, unit-tested C#: `AudioDirector`, `AudioMix` and `VoicePool` in `Nmf.Client/Audio`. They read sim events and unit state and return `SoundCue`s. `AudioView` (Godot) is a thin player pool on five buses. The catalogue (`content/core/audio/sounds.yaml`) is loaded by `Nmf.Content`; `Nmf.Client` gains a project reference to `Nmf.Content` for its types (a new but acyclic direction: Client → Content → Sim). A Python tool trims, normalises and converts the user's files to Ogg Vorbis and records their source.

**Tech Stack:** C# / .NET (net8.0 libraries), xUnit, YamlDotNet, Godot 4.7.2 (`AudioStreamPlayer2D`, `AudioStreamOggVorbis.LoadFromFile`, `AudioServer`), Python 3 + `soundfile` + numpy.

**Spec:** `docs/superpowers/specs/2026-10-09-sound-design.md`

## Global Constraints

- The sim stays untouched and deterministic. The client never draws from `Simulation.Rng`; the director has its own `System.Random`.
- Hearing rule: own sounds always; hidden enemy shot only if a living own man is within the weapon's `NoiseRadiusCm` of the muzzle; an unseen shooter's shot sounds from the 10 m square centre (`VisionRules.SuspectedGridCm`); enemy quiet sounds (reload, loot, footsteps) only while shown.
- Mixing by view size: `r` = distance from the camera centre / half the view diagonal. `r ≤ 1` full cue volume. `loud` cues fall linearly to -18 dB at `r = 3` and stay there. Other cues fade to silence at `r = 2`.
- Near/far switch at 150 m from the nearest own man. Delay = distance to the nearest own man / 343 m/s, in game ticks (waits out a pause).
- Limits: 32 pooled voices, `max_voices` per cue (default 6), at least 50 ms of *real* time between one shooter's shot sounds measured as tick gap / (20 × speed) (so at x1 an SMG's 1-tick rounds all play; at x8 they thin out), footsteps and quiet sounds off at speed ≥ x4 or zoom < 0.4, footsteps from at most the 4 movers nearest the camera centre.
- Version choice never repeats the last file of a cue (when it has ≥ 2), pitch 1 ± 0.04.
- Missing or empty `files` = silence, never a crash. A bad `sounds.yaml` logs an error and the game runs without sound.
- Automated runs (`--demo`, `--screenshot…`) and `--mute` make no sound.
- Pause stops the pooled players (Sfx and Voice buses) and resumes them.
- The code and comments are in English; the brief is in English (it goes to external AI tools).

## Review Focus

1. **Many men firing at x8:** a 7 v 9 firefight must not stack hundreds of players or clip. Expect throttled shots, the pool capped at 32 and quiet sounds dropped. Test: `AudioDirectorTests.EightTimesSpeed_ThrottlesOneShootersShots_AndDropsFootsteps`.
2. **Shots that end outside the map** (a miss flying past the edge): impact lookup must not throw. Test: `AudioDirectorTests.ShotEndingOutsideTheMap_PlaysNoImpact_AndDoesNotThrow`.
3. **All own men out of action:** there is no "nearest own man" for hearing, delay or near/far. Hidden enemy shots must stay silent without throwing. Test: `AudioDirectorTests.NoOwnMenLeft_HiddenShotsAreSilent`.
4. **A cue whose files are all missing on disk** (the yaml lists a file the user deleted): the game plays nothing for it and warns once. Test: `AudioCatalogLoaderTests.MissingFiles_AreListed` (load side); `AudioView` keeps a warned set (manual check).
5. **A man who falls (Incapacitated) and later dies:** two `UnitWounded` events must give one fall sound. Test: `AudioDirectorTests.AManFallsOnce_EvenWhenHeLaterDies`.
6. **A miss that passes close and flies on:** `ShotFired.To` is where the bullet stopped (range or obstacle), often far behind the man it passed. The crack is tested against the segment From→To. Test: `AudioDirectorTests.BulletPassingClose_Cracks_EvenWhenItEndsFarBehind`.
7. **An SMG burst at x1:** the Suomi and the PPŠ fire a round every tick (50 ms), and every round must sound. Test: `AudioDirectorTests.SmgBurstAtNormalSpeed_PlaysEveryRound`.

---

### Task 1: Sound brief and spec amendment

The brief comes first so the user can start making sounds while the system is built.

**Files:**
- Create: `docs/audio/sound-brief.md`
- Modify: `docs/superpowers/specs/2026-10-09-sound-design.md`

- [ ] **Step 1: Amend the spec — reloads are seen, not signalled.** Replace the sentence about `ReloadStarted` in "Arkkitehtuuri" and the paragraph after the footstep paragraph (and the sim test line in "Testaus"). The new text says the director notices a reload when `Unit.Action` turns to `CombatAction.Reloading`, so the sim needs no change at all. A reload lasts 80–120 ticks, so a frame (at most 10 ticks) never misses one.

- [ ] **Step 2: Write `docs/audio/sound-brief.md`** in the style of `docs/art/character-sprite-brief.md` (English, addressed to the person or tool making the sounds). Sections:
  1. **The game and the sound.** Finnish Continuation War, summer 1942, pine and spruce forest by Lake Onega, seen from above. Recorded outdoors: dry, short tail, no hall reverb, no music, no voices in effects, no modern weapons. Mono for effects.
  2. **Technical rules.** Up to 48 kHz, mp3 or wav accepted. One sound per file with no silence padding (the tool trims it anyway). Name each file `<cue>.<n>.<ext>`, e.g. `shot.mosin_m39.2.mp3`. Give each version a different take, not the same file re-exported.
  3. **Licence.** Record the service, plan and date. Use a paid plan, or check that the terms allow redistribution under CC BY-SA, before the files go into the public repo.
  4. **Round 1 (about 20 files).** One of each `shot.<weapon>` (6), `shot_far.rifle`, `shot_far.smg`, `shot_far.lmg`, `grenade.explode`, `grenade.explode_far`, `bullet.crack` ×2, `impact.wood`, `impact.ground`, `body.fall`, `grenade.throw`, `ui.click`, `ui.paper`.
  5. **Round 2.** The remaining versions up to the counts below, plus the reloads, footsteps, crawl, melee, loot and water and body impacts.
  6. **The table.** One row per cue: name, versions, length and an English prompt. Rows and prompt essentials:

| Cue | Ver. | Length | Prompt essentials |
|---|---|---|---|
| `shot.suomi_kp31` | 3 | 0.3 s | Suomi KP/-31 9 mm submachine gun, single shot from a fast-firing SMG, sharp pistol-calibre crack, close, outdoors in a forest |
| `shot.mosin_m39` | 3 | 0.8 s | Finnish M/39 Mosin-Nagant rifle, 7.62×53R, one shot, heavy bolt-action rifle report, close, short forest echo |
| `shot.lahti_saloranta` | 3 | 0.4 s | Lahti-Saloranta M/26 light machine gun, one round of a burst, full-power rifle cartridge, close |
| `shot.ppsh41` | 3 | 0.3 s | PPSh-41 7.62×25 submachine gun, one shot, high-pitched snappy report, close |
| `shot.mosin_9130` | 3 | 0.8 s | Mosin-Nagant 91/30 rifle, one shot, heavy rifle report, close |
| `shot.dp27` | 3 | 0.4 s | DP-27 light machine gun, one round of a burst, rifle cartridge, close |
| `shot_far.rifle` / `.smg` / `.lmg` | 3 each | 1.2 s | the same weapon type 300 m away through forest: dull thump with a long rolling tail, no crack |
| `bullet.crack` | 4 | 0.2 s | supersonic rifle bullet passing just overhead: sharp crack and snap, no gunshot |
| `impact.ground` / `.wood` / `.water` / `.body` | 3 each | 0.3 s | bullet striking forest soil / a tree trunk / lake water / a body (a dull thud, no gore) |
| `reload.rifle` / `.smg` / `.lmg` | 2 each | 1–2 s | bolt worked and a five-round stripper clip pushed in / SMG drum or magazine changed / LMG magazine changed |
| `grenade.throw` | 2 | 0.5 s | stick grenade thrown: cloth swish and a grunt-free whoosh |
| `grenade.explode` / `_far` | 3 each | 1.5 s / 2 s | WW2 hand grenade exploding 20 m away in a forest / 300 m away, muffled boom |
| `body.fall` | 3 | 0.6 s | soldier with gear collapsing onto forest floor |
| `melee.scuffle` | 2 | 1.5 s | two soldiers wrestling, rifle stocks knocking, boots scraping, no voices |
| `loot.rustle` | 2 | 1 s | searching a soldier's pockets and pouches: cloth, leather, a clink |
| `footstep.forest` / `.grass` / `.road` / `.swamp` / `.water` | 4 each | 0.3 s | one boot step on pine needles and twigs / meadow grass / a dirt road / wet bog moss / shallow water |
| `crawl.rustle` | 4 | 0.8 s | a soldier crawling through undergrowth |
| `ui.click` / `ui.paper` | 2 each | 0.1 s / 0.4 s | soft pencil tick on paper / a paper sheet unfolded |

  The brief ends with the import command: `python3 -m tools.audio <folder> --service <name> --licence <text>` (the tool from Task 6).

- [ ] **Step 3: Commit**

```bash
git add docs/audio/sound-brief.md docs/superpowers/specs/2026-10-09-sound-design.md
git commit -m "Sound brief: what to make, in two rounds; reloads seen from the man's action, not a new sim event"
```

---

### Task 2: Audio catalogue (content)

**Files:**
- Create: `src/Nmf.Content/Audio/AudioCatalog.cs`, `src/Nmf.Content/Audio/AudioCatalogLoader.cs`, `content/core/audio/sounds.yaml`, `content/core/audio/SOURCES.md`
- Modify: `src/Nmf.Client/Nmf.Client.csproj` (add a reference to `Nmf.Content`; Client → Content → Sim stays acyclic), `src/Nmf.Content.Tests/CoreContentTests.cs`
- Test: `src/Nmf.Content.Tests/AudioCatalogLoaderTests.cs`

**Interfaces:**
- Produces:
  - `Nmf.Content.Audio.AudioBus { Sfx, Voice, Ambience, Music, Ui }`
  - `CueDef(string Id, AudioBus Bus, IReadOnlyList<string> Files, double VolumeDb = 0, int Priority = 50, bool Loud = false, int MaxVoices = 6)`
  - `AudioCatalog(IReadOnlyDictionary<string, CueDef> cues, IReadOnlyDictionary<string, string> surfaces)` with `Cues`, `Surfaces`, `static Empty`, `CueDef? Find(params string[] ids)`. `Find` returns the first id that exists and has files, else null.
  - `AudioCatalogLoader.Load(string path) : AudioCatalog` (throws `ContentLoadException`)
  - `AudioCatalogLoader.MissingFiles(AudioCatalog, string audioRoot) : IReadOnlyList<string>`

- [ ] **Step 1: Write the failing tests** in `AudioCatalogLoaderTests` (use `TempMapDir`-style temp folders as in the other loader tests):

```csharp
[Fact] public void ValidFile_Loads_WithDefaults()
// yaml: cues: { shot.rifle: { bus: sfx, files: [sfx/a.ogg], loud: true }, ui.click: { bus: ui, files: [] } }
//       surfaces: { forest: forest }
// expect: Bus Sfx, Loud true, Priority 50, MaxVoices 6, VolumeDb 0; ui.click Files empty; Surfaces["forest"] == "forest"

[Fact] public void Find_SkipsCuesWithoutFiles()
// shot.mosin_m39 files [] and shot.rifle files [x.ogg] -> Find("shot.mosin_m39", "shot.rifle").Id == "shot.rifle"; Find("nope") == null

[Theory]
[InlineData("{ bus: loud }", "bus")]
[InlineData("{ bus: sfx, volume_db: 7 }", "volume_db")]
[InlineData("{ bus: sfx, priority: 101 }", "priority")]
[InlineData("{ bus: sfx, max_voices: 0 }", "max_voices")]
[InlineData("{ bus: sfx, files: [a.wav] }", ".ogg")]
public void BadCue_IsAClearError(string cue, string mentions)
// ContentLoadException whose Message contains `mentions` and the cue id

[Fact] public void MissingFiles_AreListed()
// a.ogg exists, b.ogg does not -> MissingFiles == ["sfx/b.ogg"]
```

- [ ] **Step 2: Run** `dotnet test src/Nmf.Content.Tests --filter AudioCatalogLoaderTests`. Expected: build fails (types missing).

- [ ] **Step 3: Implement.** `AudioCatalog.cs`:

```csharp
namespace Nmf.Content.Audio;

public enum AudioBus { Sfx, Voice, Ambience, Music, Ui }

/// <summary>One sound cue: its bus, the file versions to pick from, and how it mixes (spec 2026-10-09-sound-design).</summary>
public sealed record CueDef(string Id, AudioBus Bus, IReadOnlyList<string> Files, double VolumeDb = 0, int Priority = 50,
    bool Loud = false, int MaxVoices = 6);

public sealed class AudioCatalog(IReadOnlyDictionary<string, CueDef> cues, IReadOnlyDictionary<string, string> surfaces)
{
    public static AudioCatalog Empty { get; } = new(new Dictionary<string, CueDef>(), new Dictionary<string, string>());

    public IReadOnlyDictionary<string, CueDef> Cues => cues;

    /// <summary>Map terrain name → footstep surface.</summary>
    public IReadOnlyDictionary<string, string> Surfaces => surfaces;

    /// <summary>The first of <paramref name="ids"/> that has files; null = silence.</summary>
    public CueDef? Find(params string[] ids)
    {
        foreach (var id in ids)
            if (cues.TryGetValue(id, out var cue) && cue.Files.Count > 0)
                return cue;
        return null;
    }
}
```

`AudioCatalogLoader.cs` follows `WeaponLoader`: the same deserializer, the same IO/YAML exception wrapping, and `ArgumentException` → `ContentLoadException(path, …)`. YAML classes:

```csharp
private sealed class CatalogYaml
{
    public Dictionary<string, CueYaml>? Cues { get; set; }
    public Dictionary<string, string>? Surfaces { get; set; }
}

private sealed class CueYaml
{
    public string? Bus { get; set; }
    public List<string>? Files { get; set; }
    public double? VolumeDb { get; set; }
    public int? Priority { get; set; }
    public bool? Loud { get; set; }
    public int? MaxVoices { get; set; }
}
```

Validation, with messages of the form `cue '<id>': …`:
- `bus` ∈ sfx|voice|ambience|music|ui
- `volume_db` ≤ 6
- `priority` 0..100
- `max_voices` 1..32
- every file ends with `.ogg`

`MissingFiles` returns the listed files (relative to `audioRoot`) that `File.Exists` rejects, in cue-id order.

- [ ] **Step 4: Write `content/core/audio/sounds.yaml`** with every cue of the Task 1 table and `files: []`. Shots, `shot_far.*`, `grenade.explode*` and `bullet.crack` are `loud: true` with priority 70 (explosions 80). Use priority 30 and `max_voices: 4` for impacts, 20 for reloads and loot, 10 and `max_voices: 4` with `volume_db: -14` for footsteps and crawl, and 40 for `body.fall` and melee. The `ui.*` cues are on the `ui` bus. Add `surfaces: { forest: forest, grass: grass, road: road, swamp: swamp, water: water, none: forest }`. Add an empty `SOURCES.md` table header: `| File | Service | Date | Prompt | Licence |`.

- [ ] **Step 5: Add to `CoreContentTests`:**

```csharp
[Fact]
public void CoreSounds_Load_AndEveryListedFileExists()
{
    string audio = Path.Combine(RepoRoot(), "content", "core", "audio");
    var catalog = AudioCatalogLoader.Load(Path.Combine(audio, "sounds.yaml"));
    Assert.Contains("shot.rifle", catalog.Cues.Keys);
    Assert.Empty(AudioCatalogLoader.MissingFiles(catalog, audio));
}
```

- [ ] **Step 6: Run** `dotnet test NoMansForest.slnx`. Expected: all pass.

- [ ] **Step 7: Commit** — "Audio catalogue: sounds.yaml with every phase 5a cue (silent until files come), loader and checks".

---

### Task 3: Mixing rules and the voice pool (client)

**Files:**
- Create: `src/Nmf.Client/Audio/SoundCue.cs`, `src/Nmf.Client/Audio/AudioMix.cs`, `src/Nmf.Client/Audio/VoicePool.cs`
- Test: `src/Nmf.Client.Tests/Audio/AudioMixTests.cs`, `src/Nmf.Client.Tests/Audio/VoicePoolTests.cs`

**Interfaces:**
- Produces:
  - `SoundCue(string CueId, string File, AudioBus Bus, Vec2? At, double VolumeDb, double Pitch, int Priority, int MaxVoices)`. `At` is null for UI sounds.
  - `AudioListener(Vec2 CenterCm, long HalfDiagonalCm, double Zoom, double Speed)` (readonly record struct)
  - `AudioMix`: consts `LoudFloorDb = -18`, `FarCm = 15_000`, `CrackCm = 500`, `ImpactNearCm = 4_000`, `ExplosionHeardCm = 80_000`, `QuietZoom = 0.4`, `QuietSpeed = 4`, `ShooterGapSeconds = 0.05`; `double? Volume(double cueDb, bool loud, Vec2 at, AudioListener l)` (null = inaudible); `long DelayTicks(long distanceCm)`; `Vec2 Rough(Vec2 p)`; `long? NearestSq(Vec2 p, IReadOnlyList<Vec2> men)`; `bool PassesWithin(Vec2 from, Vec2 to, Vec2 man, int radiusCm)` (distance from the man to the segment); `bool QuietSoundsOn(AudioListener l)`
  - `VoicePool(int slots = 32)`: `void Advance(double realSeconds)`, `int Claim(SoundCue cue, double lengthSeconds)` (slot index or -1), `int Slots`

- [ ] **Step 1: Write the failing tests.**

```csharp
public class AudioMixTests
{
    private static readonly AudioListener L = new(new Vec2(0, 0), 10_000, 1.0, 1.0);

    [Theory]
    [InlineData(5_000, true, 0.0)]     // on screen: full
    [InlineData(20_000, true, -9.0)]   // r = 2: halfway to the floor
    [InlineData(30_000, true, -18.0)]  // r = 3: the floor
    [InlineData(90_000, true, -18.0)]  // beyond: stays at the floor
    public void LoudCues_FallToTheFloor(int x, bool loud, double expected) =>
        Assert.Equal(expected, AudioMix.Volume(0, loud, new Vec2(x, 0), L)!.Value, 3);

    [Fact] public void QuietCues_AreGoneAtTwiceTheHalfDiagonal()
    {
        Assert.NotNull(AudioMix.Volume(-14, false, new Vec2(15_000, 0), L));
        Assert.Null(AudioMix.Volume(-14, false, new Vec2(20_000, 0), L));
    }

    [Fact] public void SoundTakesItsTime() => Assert.Equal(17, AudioMix.DelayTicks(30_000)); // 300 m ≈ 0.87 s at 20 ticks/s

    [Fact] public void RoughPosition_IsTheMiddleOfTheTenMetreSquare() =>
        Assert.Equal(new Vec2(1_500, 2_500), AudioMix.Rough(new Vec2(1_234, 2_999)));

    [Fact] public void NoMen_NoNearest() => Assert.Null(AudioMix.NearestSq(new Vec2(0, 0), []));

    [Theory]
    [InlineData(5_000, 200, true)]    // beside the line, 2 m off
    [InlineData(5_000, 800, false)]   // 8 m off
    [InlineData(-300, 0, true)]       // just behind the muzzle, within 5 m of it
    [InlineData(12_000, 0, false)]    // on the line but beyond where the bullet stopped (10 m)
    public void PassesWithin_MeasuresToTheBulletsPath(int x, int y, bool expected) =>
        Assert.Equal(expected, AudioMix.PassesWithin(new Vec2(0, 0), new Vec2(10_000, 0), new Vec2(x, y), 500));

    [Theory]
    [InlineData(1.0, 1.0, true)]
    [InlineData(0.3, 1.0, false)]
    [InlineData(1.0, 4.0, false)]
    public void QuietSounds_OffFarOut_OrFast(double zoom, double speed, bool on) =>
        Assert.Equal(on, AudioMix.QuietSoundsOn(L with { Zoom = zoom, Speed = speed }));
}

public class VoicePoolTests
{
    private static SoundCue Cue(string id, double db, int priority, int max = 6) =>
        new(id, id + ".ogg", AudioBus.Sfx, new Vec2(0, 0), db, 1, priority, max);

    [Fact] public void AFullPool_ReplacesTheQuietestOfLowerPriority()
    // pool(2): claim A(-10, p10), B(-3, p70); claim C(-5, p70) -> returns A's slot

    [Fact] public void AFullPool_RefusesWhenEverythingIsMoreImportant()
    // pool(1): claim A(0, p80); claim B(0, p10) -> -1

    [Fact] public void MaxVoices_ReplacesTheQuietestOfTheSameCue()
    // pool(8): three of X (max 2): third claim returns the slot of the quieter of the first two

    [Fact] public void EndedVoices_FreeTheirSlot()
    // pool(1): claim A length 0.5; Advance(0.6); claim B (p0) -> 0
}
```

- [ ] **Step 2: Run** `dotnet test src/Nmf.Client.Tests --filter "AudioMixTests|VoicePoolTests"`. Expected: build fails.

- [ ] **Step 3: Implement.**

```csharp
namespace Nmf.Client.Audio;

public static class AudioMix
{
    public const double LoudFloorDb = -18;
    public const double SilentDb = -80;
    public const int FarCm = 15_000, CrackCm = 500, ImpactNearCm = 4_000, ExplosionHeardCm = 80_000;
    public const double QuietZoom = 0.4, QuietSpeed = 4, ShooterGapSeconds = 0.05;
    private const long SoundCmPerSecond = 34_300;

    public static double? Volume(double cueDb, bool loud, Vec2 at, AudioListener l)
    {
        double r = Math.Sqrt((at - l.CenterCm).LengthSquared) / Math.Max(1, l.HalfDiagonalCm);
        if (r <= 1)
            return cueDb;
        if (loud)
            return cueDb + (Math.Min(cueDb, LoudFloorDb) - cueDb) * Math.Min(1, (r - 1) / 2);
        return r >= 2 ? null : cueDb + (SilentDb - cueDb) * (r - 1);
    }

    public static long DelayTicks(long distanceCm) => distanceCm * SimConstants.TicksPerSecond / SoundCmPerSecond;

    public static Vec2 Rough(Vec2 p)
    {
        const int grid = VisionRules.SuspectedGridCm;
        return new Vec2(IntMath.FloorDiv(p.X, grid) * grid + grid / 2, IntMath.FloorDiv(p.Y, grid) * grid + grid / 2);
    }

    public static long? NearestSq(Vec2 p, IReadOnlyList<Vec2> men) =>
        men.Count == 0 ? null : men.Min(m => (m - p).LengthSquared);

    public static bool QuietSoundsOn(AudioListener l) => l.Zoom >= QuietZoom && l.Speed < QuietSpeed;

    /// <summary>Whether a bullet flying from <paramref name="from"/> to <paramref name="to"/> passed within the radius.</summary>
    public static bool PassesWithin(Vec2 from, Vec2 to, Vec2 man, int radiusCm)
    {
        var d = to - from;
        var rel = man - from;
        long lenSq = Math.Max(1, d.LengthSquared);
        long along = (long)rel.X * d.X + (long)rel.Y * d.Y;
        Vec2 closest = along <= 0 ? from : along >= lenSq ? to
            : new Vec2(from.X + (int)(d.X * along / lenSq), from.Y + (int)(d.Y * along / lenSq));
        return (man - closest).LengthSquared <= (long)radiusCm * radiusCm;
    }
}
```

`VoicePool` holds `Voice?[]` where `record Voice(string CueId, int Priority, double VolumeDb, double EndsAt)`, plus `Now`. `Advance` adds to `Now` and nulls the voices that have ended. `Claim` works as follows:
1. If the voices of the same cue already number `MaxVoices`, take the quietest of them if its volume ≤ the new cue's, else return -1.
2. Otherwise use a free slot.
3. Otherwise take the quietest voice with priority ≤ the new cue's (ties go to the lower priority), or return -1.

The chosen slot gets `new Voice(…, Now + length)`.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** — "Audio mixing by view size, sound delay, rough positions; a 32-voice pool with per-cue limits".

---

### Task 4: AudioDirector — events (client)

**Files:**
- Create: `src/Nmf.Client/Audio/AudioDirector.cs`
- Test: `src/Nmf.Client.Tests/Audio/AudioDirectorTests.cs`

**Interfaces:**
- Consumes: `AudioCatalog`, `CueDef` (Task 2); `SoundCue`, `AudioListener`, `AudioMix` (Task 3); sim events `ShotFired`, `GrenadeThrown`, `GrenadeExploded`, `UnitWounded`, `MeleeStarted`, `UnitLooted`.
- Produces: `AudioDirector(AudioCatalog catalog, int seed = 1)` with
  - `IReadOnlyList<SoundCue> Update(Simulation sim, Side playerSide, IReadOnlyList<SimEvent> events, Func<Unit, bool> shown, AudioListener listener, double realDeltaSeconds)`
  - `SoundCue? Ui(string cueId)`

Test setup: a 400 × 400 m `GridMap(400, 400, ["none", "forest", "water"])` and a `Simulation` with `SpawnUnit`. The weapon is `new WeaponDef("mosin_m39", "M/39", WeaponClass.Rifle, 5, 30, 1, 0, 24, 80, 6, 30_000, 45, 80, 30_000)` (noise 300 m). The catalogue gives every cue one file named `<cue>.ogg`; one test uses a catalogue with only `shot.rifle`. The listener is `new AudioListener(own.Position, 5_000, 1, 1)`. Events are built by hand with the sim's current tick.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public void HiddenEnemyShot_BeyondEarshot_IsSilent()          // enemy 350 m from the only own man, not shown
[Fact] public void HiddenEnemyShot_InEarshot_SoundsFromTheRoughSquare() // 250 m: one shot cue, At == AudioMix.Rough(from), after the delay
[Fact] public void ShownShooter_SoundsFromHisExactPlace()
[Fact] public void FarShot_UsesTheFarCue_NearShot_TheNearCue()     // 160 m -> "shot_far.rifle", 100 m -> "shot.mosin_m39"
[Fact] public void UnknownWeapon_FallsBackToItsClass()             // catalogue with only shot.rifle -> "shot.rifle"
[Fact] public void DelayedShot_WaitsOutAPause()                    // Update with the event, then Update twice with no sim step -> nothing; step the sim 13 ticks -> the cue (200 m = 11 ticks)
[Fact] public void BulletPassingClose_Cracks_EvenWhenItEndsFarBehind() // enemy shot passes 2 m from the own man and ends 60 m behind -> "bullet.crack" at the closest point
[Fact] public void SmgBurstAtNormalSpeed_PlaysEveryRound()        // Speed 1: three ShotFired from one shooter on ticks t, t+1, t+2 (stepping the sim between Updates) -> three shot cues
[Fact] public void Impacts_FollowTheSurface()                      // To on a water cell -> impact.water; obstacle height 1500 -> impact.wood; Hit != null -> impact.body
[Fact] public void ShotEndingOutsideTheMap_PlaysNoImpact_AndDoesNotThrow()
[Fact] public void NoOwnMenLeft_HiddenShotsAreSilent()             // own man Wound = Dead
[Fact] public void AManFallsOnce_EvenWhenHeLaterDies()             // UnitWounded Incapacitated then Dead -> one body.fall
[Fact] public void HiddenEnemyLooting_IsSilent_OwnLooting_IsHeard()
[Fact] public void GrenadeFarAway_UsesTheFarExplosion()           // 200 m -> grenade.explode_far; 900 m -> nothing
[Fact] public void EightTimesSpeed_ThrottlesOneShootersShots_AndDropsFootsteps()
    // Speed 8: three ShotFired from one own shooter on ticks t, t+1, t+2 -> one shot cue (1 tick = 6 ms real); (footsteps: Task 5 extends this test)
[Fact] public void Versions_NeverRepeatBackToBack()               // cue with files [a, b]: 20 plays, no two equal in a row; pitch within 0.96..1.04
[Fact] public void Ui_HasNoPlace_AndIsSilentWithoutFiles()
```

- [ ] **Step 2: Run** `dotnet test src/Nmf.Client.Tests --filter AudioDirectorTests`. Expected: build fails.

- [ ] **Step 3: Implement.**

```csharp
namespace Nmf.Client.Audio;

/// <summary>
/// Turns what happened into sounds the player's side could hear (spec 2026-10-09-sound-design). Pure: Godot only plays the
/// returned cues. Uses its own random generator, never the simulation's.
/// </summary>
public sealed class AudioDirector(AudioCatalog catalog, int seed = 1)
{
    private readonly Random _rng = new(seed);
    private readonly List<(long Tick, CueDef Cue, Vec2 At)> _pending = [];
    private readonly Dictionary<string, string> _lastFile = [];
    private readonly Dictionary<UnitId, long> _lastShotTick = [];
    private readonly HashSet<UnitId> _fallen = [];

    public IReadOnlyList<SoundCue> Update(Simulation sim, Side playerSide, IReadOnlyList<SimEvent> events,
        Func<Unit, bool> shown, AudioListener listener, double realDeltaSeconds)
    {
        var own = sim.Units.Where(u => u.Side == playerSide && !u.IsOutOfAction).Select(u => u.Position).ToList();
        bool Heard(Unit u) => u.Side == playerSide || shown(u);
        foreach (var e in events)
        {
            switch (e)
            {
                case ShotFired shot when sim.FindUnit(shot.Shooter) is { Weapon: { } weapon } shooter:
                    Shot(sim, shot, shooter, weapon, Heard(shooter), shooter.Side == playerSide, own, listener.Speed);
                    break;
                case GrenadeThrown thrown when sim.FindUnit(thrown.Thrower) is { } thrower && Heard(thrower):
                    Queue(e.Tick, thrown.From, "grenade.throw");
                    break;
                case GrenadeExploded blast when AudioMix.NearestSq(blast.At, own) is { } sq
                                                && sq <= (long)AudioMix.ExplosionHeardCm * AudioMix.ExplosionHeardCm:
                {
                    long d = IntMath.Isqrt(sq);
                    Queue(e.Tick + AudioMix.DelayTicks(d), blast.At, d > AudioMix.FarCm ? "grenade.explode_far" : "grenade.explode", "grenade.explode");
                    break;
                }
                case UnitWounded { Level: >= WoundLevel.Incapacitated } hit
                    when sim.FindUnit(hit.Unit) is { } man && Heard(man) && _fallen.Add(man.Id):
                    Queue(e.Tick, man.Position, "body.fall");
                    break;
                case MeleeStarted melee when sim.FindUnit(melee.A) is { } a && sim.FindUnit(melee.B) is { } b && (Heard(a) || Heard(b)):
                    Queue(e.Tick, a.Position, "melee.scuffle");
                    break;
                case UnitLooted loot when sim.FindUnit(loot.Looter) is { } looter && Heard(looter):
                    Queue(e.Tick, looter.Position, "loot.rustle");
                    break;
            }
        }
        return Release(sim.Tick, listener);
    }
}
```

`Shot(...)` handles three cues:
- **The shot.** It sounds when the shooter is heard, or when `NearestSq(from, own) ≤ NoiseRadiusCm²` (no own men = not heard). It is throttled when `(shot.Tick - _lastShotTick[shooter]) / (20.0 × speed) < ShooterGapSeconds`, i.e. in real seconds at the current speed; a played shot records its tick. It is placed at `shot.From` when heard, else at `AudioMix.Rough(shot.From)`. The cue is `Find("shot_far."+cls, "shot."+id, "shot."+cls)` when nearest > `FarCm`, else `Find("shot."+id, "shot."+cls)`. `cls` = `weapon.Class.ToString().ToLowerInvariant()`. The delay is `DelayTicks(nearest)`.
- **The crack.** It plays when the shooter is not own and `AudioMix.PassesWithin(From, To, man, CrackCm)` holds for some own man: `bullet.crack` at that man's position, no delay (the bullet beats its sound). Only one crack per shot.
- **The impact.** It plays when the shooter is heard or an own man is within `ImpactNearCm` of `To`, and `sim.Map.Contains(To)`. With `Hit` set the cue is `impact.body`. Otherwise the cell decides: terrain name `water` gives `impact.water`, `ObstacleHeightCm > 0` gives `impact.wood`, else `impact.ground`.

`Queue(tick, at, params ids)` adds the found cue (if any) to `_pending`. `Release(tick, listener)` takes the pending entries with `Tick ≤ tick` and makes a `SoundCue` from each:
- the volume from `AudioMix.Volume`; null drops the entry
- the file from `Pick(cue)`, a random index that avoids `_lastFile[cue.Id]` when there are ≥ 2 files
- the pitch `1 + (_rng.NextDouble() * 0.08 - 0.04)`.

`Ui(id)` is `Find(id)` → a cue with `At = null` and the cue's own volume.

- [ ] **Step 4: Run the tests.** Expected: PASS.

- [ ] **Step 5: Commit** — "AudioDirector: shots, cracks, impacts, grenades, falls, melee and looting, heard as the player's side hears them".

---

### Task 5: AudioDirector — reloads and footsteps (client)

**Files:**
- Modify: `src/Nmf.Client/Audio/AudioDirector.cs`
- Test: `src/Nmf.Client.Tests/Audio/AudioDirectorTests.cs`

**Interfaces:**
- Consumes: `Unit.Action`, `Unit.MoveMode`, `Unit.Position`, `GridMap.TerrainNames`, `AudioCatalog.Surfaces`.
- Produces: no new public API; `Update` also returns reload and step cues.

- [ ] **Step 1: Write the failing tests** (set `unit.Action`, `unit.Position` and `unit.MoveMode` directly; the internals are visible to `Nmf.Client.Tests`):

```csharp
[Fact] public void Reload_SoundsOnce_WhenItStarts()               // Action None -> Reloading: one reload.rifle; still Reloading next Update: none
[Fact] public void HiddenEnemyReload_IsSilent()
[Fact] public void Walking_StepsEveryElevenTicks_OnTheCellsSurface() // walk 22 ticks over forest: two footstep.forest
[Fact] public void Crawling_Rustles()
[Fact] public void OnlyTheFourMenNearestTheCamera_Step()            // six walkers -> steps only from the nearest four
// extend EightTimesSpeed_ThrottlesOneShootersShots_AndDropsFootsteps: Speed 8 -> no footstep cue
```

- [ ] **Step 2: Run them.** Expected: FAIL.

- [ ] **Step 3: Implement** at the end of `Update`, before `Release`. Pass `elapsed = sim.Tick - _lastTick`, then set `_lastTick = sim.Tick`.
- **Reloads.** For every unit: `bool now = u.Action == CombatAction.Reloading && !u.IsOutOfAction`. When `now && _reloading.Add(u.Id)`, `Heard(u)` holds and `QuietSoundsOn(listener)`, queue `reload.<cls>` at its position. When `!now`, `_reloading.Remove(u.Id)`.
- **Steps.**
  - If `!QuietSoundsOn(listener)`, clear `_stepTimer` and skip the rest.
  - The movers are units with `Heard(u)`, `!IsOutOfAction` and `u.Position != _lastPos[u.Id]`. Order them by distance to `listener.CenterCm` and take 4.
  - For each mover, `_stepTimer[id] -= elapsed`. When it is ≤ 0, queue the step and add the interval: Walk 11, Run 7, Sneak 16, Crawl 20 ticks, others 11.
  - The step cue is `crawl.rustle` for Crawl, else `footstep.<surface>`. The surface is `catalog.Surfaces.GetValueOrDefault(terrainName, "forest")` of `sim.Map.CellAt(pos)` (only if `sim.Map.Contains(pos)`).
  - A man who does not move gets his timer reset to 0, so his first step sounds at once.
  - Finally, record `_lastPos` for every unit.

- [ ] **Step 4: Run** `dotnet test src/Nmf.Client.Tests`. Expected: PASS.

- [ ] **Step 5: Commit** — "AudioDirector: reloads when they start, footsteps by surface from the four movers nearest the camera".

---

### Task 6: Import tool (Python)

**Files:**
- Create: `tools/audio/__init__.py`, `tools/audio/import_sounds.py`, `tools/audio/__main__.py` (the entry point), `tools/audio/tests/__init__.py`, `tools/audio/tests/test_import.py`
- Modify: `.github/workflows/ci.yml` (add `soundfile` to the pip install and run `python -m unittest discover -s tools/audio/tests -t .`), the spec's command line (`python3 -m tools.audio`, needs `soundfile` and `pyyaml`)

`import` is a Python keyword, so `tools.audio.import` cannot be a module. The command is `python3 -m tools.audio <folder> [--service NAME] [--licence TEXT]`, and the brief says so.

**Interfaces:**
- Produces: `import_folder(raw: Path, audio_root: Path, service: str, licence: str, today: str) -> list[Path]`, plus pure helpers `trim(samples, rate, threshold_db=-50)`, `to_mono(samples)`, `normalise(samples, peak_db=-1)`, `cue_of(filename) -> tuple[str, int]`.

- [ ] **Step 1: Write the failing tests** with a synthetic 440 Hz tone and 0.3 s of silence on each side, written as wav into a temp dir:

```python
def test_cue_of_reads_the_cue_and_version(self):
    self.assertEqual(cue_of("shot.mosin_m39.2.mp3"), ("shot.mosin_m39", 2))

def test_trim_cuts_the_silence(self):          # 1.0 s tone padded -> 1.0 s ± 0.02
def test_stereo_becomes_mono_for_effects(self):
def test_normalise_sets_the_peak(self):        # peak == 10 ** (-1/20) ± 1e-3
def test_import_writes_ogg_lists_it_and_records_the_source(self):
    # sounds.yaml with cue shot.rifle files: [] in a temp audio root; import shot.rifle.1.wav
    # -> sfx/shot.rifle.1.ogg exists and reads back; sounds.yaml lists it; SOURCES.md has a row with the service
def test_unknown_cue_is_refused(self):         # nope.1.wav -> ValueError naming the file
```

- [ ] **Step 2: Run** `python3 -m unittest discover -s tools/audio/tests -t .`. Expected: ImportError.

- [ ] **Step 3: Implement.**
- Read with `soundfile.read(path, always_2d=True)`.
- The bus comes from the cue in `sounds.yaml`; read the yaml with PyYAML (`pip install pyyaml`; add it to the CI install line). Music and ambience keep stereo, everything else goes mono.
- Write `soundfile.write(out, data, rate, format="OGG", subtype="VORBIS")` into `<audio_root>/<bus>/<cue>.<n>.ogg`.
- Add `"<bus>/<cue>.<n>.ogg"` to the cue's `files` when it is missing. Rewrite only that line of the YAML with a regex on `^  <cue>:` so comments and order stay intact.
- Append a row `| <bus>/<file> | <service> | <today> | <prompt or -> | <licence> |` to `SOURCES.md`.

- [ ] **Step 4: Run the Python tests.** Expected: PASS.

- [ ] **Step 5: Commit** — "Sound import tool: trim, mono, normalise, Ogg Vorbis, listed in sounds.yaml and SOURCES.md".

---

### Task 7: Godot player and wiring

**Files:**
- Create: `src/Nmf.Game/AudioView.cs`
- Modify: `src/Nmf.Game/GameRoot.cs`

**Interfaces:**
- Consumes: `AudioDirector`, `VoicePool`, `SoundCue`, `AudioListener`, `AudioCatalogLoader`.
- Produces: `AudioView : Node` with `static AudioView? Create(string contentRoot)` (null when the catalogue fails to load), `void Frame(GameSession session, IReadOnlyList<SimEvent> events, bool revealAll, AudioListener listener, double delta)`, `void PlayUi(string cueId)`.

- [ ] **Step 1: Implement `AudioView`.**
- **Buses.** `EnsureBuses()` adds `Sfx`, `Voice`, `Ambience`, `Music` and `Ui` if `AudioServer.GetBusIndex(name) < 0`. It uses `AudioServer.AddBus()`, `SetBusName(AudioServer.BusCount - 1, name)` and `SetBusSend(…, "Master")`.
- **Players.** `_Ready` creates 32 `AudioStreamPlayer2D` with `Attenuation = 0`, `MaxDistance = 1e7f` and `PanningStrength = 1`, plus one `AudioStreamPlayer` for UI.
- **Streams.** `Load(file)` caches `AudioStreamOggVorbis.LoadFromFile(Path.Combine(_audioRoot, file))`. A missing file goes into a warned set and gives one `GD.PushWarning`.
- **Play.** UI cues go to the UI player. Other cues: `slot = _pool.Claim(cue, stream.GetLength() / cue.Pitch)`, and on `slot ≥ 0` the player in that slot is stopped, given the stream, bus, `VolumeDb`, `PitchScale` and `GlobalPosition = Coords.ToPixels(cue.At)`, then played.
- **Frame.** If `session.Clock.Paused` changed, set `StreamPaused` on every pooled player. When not paused, call `_pool.Advance(delta)`. Then play every cue from `_director.Update(session.Sim, session.PlayerSide, events, u => session.IsShownToPlayer(u, revealAll), listener, delta)`.
- **Startup log.** `Create` prints `[NMF] audio: <n> cues with files, <m> files missing`.

- [ ] **Step 2: Wire into `GameRoot`.**
- After `automated` is known (around `GameRoot.cs:208`; it is declared after the camera block), create `_audio = automated || args.Contains("--mute") ? null : AudioView.Create(_contentRoot)` and `AddChild` it. Godot's `AudioListener2D` is not needed: 2D panning follows the current camera.
- In `_Process`, after `TakeEvents`:

```csharp
if (_audio is not null)
{
    var size = GetViewportRect().Size / _camera.Zoom;
    var listener = new AudioListener(Coords.ToCm(_camera.GetScreenCenterPosition()),
        (long)(size.Length() / 2 / Coords.PixelsPerCm), _camera.Zoom.X, _session.Clock.TimeScale);
    _audio.Frame(_session, events, _units.RevealAll, listener, delta);
}
```

- Add `_audio?.PlayUi("ui.click");` after each `AddOrderFlash` call (`GameRoot.cs` lines ~650, ~653, ~677, ~849, ~916).
- In `_Process`, play `ui.paper` when `_orders.Visible || _mapView.Visible` differs from the last frame.
- The relaunch path (menu ↔ mission) frees the old `AudioView` along with the other children. Check that `Relaunch` does so; if it rebuilds by reloading the scene, nothing more is needed.

- [ ] **Step 3: Build and run.**
- `dotnet build src/Nmf.Game/Nmf.Game.sln` passes.
- First run `git status content/core/audio`: the user may already have imported real sounds there (the brief came first). Never clean or check out that folder.
- Make a test tone in the scratchpad: `mkdir -p <scratchpad>/tone && python3 -c "import numpy as np, soundfile as sf; t=np.arange(4410)/44100; sf.write('<scratchpad>/tone/shot.mosin_m39.9.wav', 0.5*np.sin(2*np.pi*440*t), 44100)"`, then `python3 -m tools.audio <scratchpad>/tone --service test-tone`. Version 9 keeps clear of the user's files.
- Run `tools/run_game.sh --mission=testikentta` and order a Finn to fire. The beep plays, panned by screen side, quieter when scrolled away, and stops while paused. Start `tools/run_game.sh --mission=testikentta --mute` and it is silent.
- **Remove only the tone:** delete `content/core/audio/sfx/shot.mosin_m39.9.ogg`, its entry in `sounds.yaml` and its `test-tone` row in `SOURCES.md`, then check `git diff content/core/audio` shows nothing of it. The test tone must not be committed.

- [ ] **Step 4: Commit** — "Sound in the game: a 32-voice positional player on five buses, fed by the director; UI ticks; --mute; silent in automated runs".

---

### Task 8: Finish

- [ ] Run `dotnet test NoMansForest.slnx` and the three Python test suites (`tools/mapgen`, `tools/art`, `tools/audio`). All pass.
- [ ] README: a short "Sounds" paragraph (EN + FI) pointing to `docs/audio/sound-brief.md` and the import command.
- [ ] Final review of the branch: one review agent (superpowers:requesting-code-review) checking the Review Focus list. Fix its findings.
- [ ] Merge `--no-ff` to main as "Merge phase 5a: sound core (director, mixing, voice pool, catalogue, import tool, brief)", push to origin, update the project memory.
