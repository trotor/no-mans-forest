# Phase 5d: settings window, sounds shown as marks, hints on suspected enemies — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-5d-settings`, merge `--no-ff` to main and push.

**Goal:** Hovering a "?" or a last-seen mark tells what is known of him. Heard sounds can be shown as marks (V, or a setting), so the game can be played silent. A settings window (main menu and Esc menu) holds volumes, mute, sound marks, language, full screen, UI scale, card size and follow.

**Architecture:**
- **Pure client logic, tested:** `ContactInfo`, `SoundMarks`, `GameSettings` (Nmf.Client), and `AudioDirector` reporting `HeardSound`s.
- **Godot:** draws the marks, builds the settings window and applies the settings.
- **The one sim change:** `Contact.HeardShots`.

**Spec:** `docs/superpowers/specs/2026-10-10-settings-hints-design.md`

## Global Constraints
- The hints and marks tell only what the player's side knows: the same hearing rules as the sounds, and contact data only.
- Marks run on game time and stop while paused.
- Settings live in `user://settings.json`, written to a temporary file and moved into place. A missing or broken file gives the defaults. `--lang=` beats the setting, and `--mute` mutes and turns marks on.
- Automated runs (`--demo`, `--screenshot…`) never play sound and never write settings.

## Review Focus
1. **A broken or partial `settings.json`** (old version, missing fields, values out of range): defaults for what is missing, values clamped, no crash. Test: `GameSettingsTests.PartialAndOutOfRange_FallBackAndClamp`.
2. **Hint on a contact whose man has since died or been captured:** no hint, or no wrong hint. Test: `ContactInfoTests.DeadMansMark_HasNoHint`.
3. **Marks with sound muted** (no `AudioView` playback): the director still runs and hearing still works. Test: `AudioDirectorTests.HeardSounds_ComeWithoutFiles`. The Godot side is checked by hand.
4. **Pausing:** marks age only on game time. Test: `SoundMarksTests.Marks_DoNotAgeWhilePaused`.
5. **Many shots at x8:** marks merge within 10 m, so there is no flood. Test: `SoundMarksTests.ShotsClose_Merge`.

---

### Task 1: What was heard (sim)
- `Contact.HeardShots` (bool, `internal set`). `VisionSystem.UpdateHearing` sets it from `fired` whenever it updates a Suspected contact, and a sighting sets it false. Add it to `StateHash`.
- Tests (`Nmf.Sim.Tests/Vision`, next to the hearing tests): `HeardShots_SetByAShot_ClearedByMovementOnly`.

### Task 2: Hints on marks (client + Godot)
- `GameSession.ContactAt(Vec2 point, int radiusCm) : Contact?` (as `ContactMarkAt`, returning the contact; `ContactMarkAt` uses it).
- `ContactInfo.Describe(Contact c, Unit? unit, IEnumerable<Unit> own, long nowTick, string language) : IReadOnlyList<string>`:
  - **Suspected:** `"Kuultu vihollinen: laukauksia|liikettä"`, `"noin N m"` (nearest fit own man, rounded to 5 m), `"S s sitten"`.
  - **LastKnown:** `"Viimeksi nähty S s sitten: <role>, <weapon>"`, `"noin N m"`. Role and weapon come from `EnemyInfo`'s role/weapon helpers; make those internal-static and reusable.
  - Empty when the unit is out of action or the contact is unknown or visible.
- `GameRoot` hover: when no seen enemy is under the mouse, use `ContactAt` and `ContactInfo`.
- Tests:
  - `ContactInfoTests.HeardShots_SaysShots_DistanceAndTime`
  - `HeardMovement_SaysMovement`
  - `LastSeen_SaysRoleWeaponAndWhen`
  - `Distance_FromNearestFitOwnMan_RoundedTo5m`
  - `DeadMansMark_HasNoHint`
  - `GameSessionTests.ContactAt_FindsTheMark`

### Task 3: Heard sounds (client)
- `AudioDirector.Update` also fills `Heard : IReadOnlyList<HeardSound>` for this frame: `HeardSound(HeardKind Kind, Vec2 At, long Tick)`, where `HeardKind` is Shot, Crack, Explosion, Fall or Melee.
  - Recorded where a cue would be queued, whether or not the catalogue has files, and at the cue's own tick, so a sound delayed by distance shows with its delay.
  - Not for own shots, reloads, loot, footsteps or UI.
- Tests:
  - `AudioDirectorTests.HeardSounds_ComeWithoutFiles` (an empty catalogue: a hidden enemy shot in earshot gives a Shot at the rough place, and a crack)
  - `HeardSounds_NotForOwnShotsNorQuietSounds`
  - `HeardSounds_NotWhenNotHeard`

### Task 4: Sound marks (client + Godot)
- `SoundMarks` (Nmf.Client.Effects):
  - `Add(IEnumerable<HeardSound>)`, `Update(double gameSeconds)`, and `Marks : IReadOnlyList<SoundMark>`, where `SoundMark(HeardKind, Vec2 At, double Age, double Life, Vec2? From)`.
  - Life is 1.5 s, and 2.5 s for an Explosion.
  - Marks of the same kind within 10 m merge: the age resets and the place moves.
- `UnitView` draws the marks at a fixed screen size when `SoundMarksOn`:
  - Shot: two arcs
  - Crack: a short slash
  - Explosion: a star
  - Fall and Melee: a small cross or star.
- Feed the marks in `GameRoot._Process` from `AudioView`'s director output, with `steps / TicksPerSecond` as the game seconds.
- Key **V** toggles them, with a toast "Äänet kuvina: päällä / pois" ("Sounds as marks: on / off").
- Tests:
  - `SoundMarksTests.Marks_Expire`
  - `Explosions_LastLonger`
  - `ShotsClose_Merge`
  - `DifferentKinds_DoNotMerge`
  - `Marks_DoNotAgeWhilePaused` (Update(0))

### Task 5: Settings (client)
- `GameSettings` record:
  - volumes: Master 80, Sfx 100, Voice 100, Ambience 70, Music 60
  - `Mute` false, `SoundMarks` false, `Language` (null = from the system), `Fullscreen` false, `UiScalePct` 100
  - `CardSize` "small", `FollowAtStart` false
- Methods:
  - `ToJson()` / `FromJson(string)` (System.Text.Json, unknown or missing fields tolerated, values clamped)
  - `static double VolumeDb(int pct)`: 0 → −80 dB (and a mute flag), else 20·log10(pct/100)
  - `bool SoundMarksEffective(bool muteArg)`: `SoundMarks || Mute || Master == 0 || muteArg`
- Tests (`GameSettingsTests`):
  - `Defaults`
  - `RoundTrip`
  - `BrokenJson_GivesDefaults`
  - `PartialAndOutOfRange_FallBackAndClamp`
  - `VolumeDb_ZeroIsSilent_HundredIsZeroDb`
  - `SoundMarks_OnWhenSoundIsOff`

### Task 6: Settings window (Godot)
- `SettingsView : CanvasLayer`. A paper panel like the menu, with sliders (5 volumes), check boxes (mute, sound marks, full screen, follow at start) and option buttons (language, UI scale, card size). Close is a "Valmis" / "Done" button or Esc.
- Applying:
  - bus volumes and mute on `AudioServer`
  - `DisplayServer.WindowSetMode`
  - `GetTree().Root.ContentScaleFactor`
  - `Hud.CardSizeChoice`, the follow default, `SoundMarksOn`
  - language for the menu and the next mission
- The settings are loaded at start in `GameRoot` (before the menu or the mission) and applied. They are saved when the window closes.
- `AudioView` is created also with `--mute` (still never in automated runs), and `--mute` mutes the Master bus.
- Buttons:
  - main menu (`MenuView`): "Asetukset" / "Settings"
  - game menu (`Hud`, Esc): "Asetukset" between "Aloita alusta" and "Tehtävävalikko". Opening it keeps the game paused, as the menu does.
- Check: build, then `--menu --screenshot-at=1` with the window opened by `--open=settings` (a new screenshot arg) to look at it.

### Task 7: Finish
- README:
  - controls: V = sounds as marks, and the settings window
  - hover on "?" gives a hint
  - EN and FI.
- Full test run, a screenshot of the marks on the testbed (`--mission=testikentta --demo=tactic --open=marks` or V toggled by arg `--sound-marks`), final review, merge, push, memory.
