# Phase 9a: the campaign and a lasting roster — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-9a-campaign`, merge `--no-ff` to main and push.

**Goal:** A campaign of chapters (missions) played with one platoon whose men live on from mission to mission: the fallen stay fallen, the badly wounded sit one mission out, everyone gains experience points.

**Architecture:**
- `Nmf.Content.Campaigns`: the campaign file (`CampaignSpec`, `CampaignLoader`).
- `Nmf.Client.Campaign`, pure and testable:
  - the saved state (`CampaignState`, JSON)
  - the filling of a mission's slots (`CampaignMission.Apply`)
  - the experience tally kept during the mission (`CampaignTally`, fed by `GameSession.StepOnce`)
- `Nmf.Game`: the menu entry, a campaign view, the launch path and the end screen.
- The simulation does not change.

**Spec:** `docs/superpowers/specs/2026-10-11-campaign-design.md` §9a

## Global Constraints
- **The simulation and plain mission play are untouched:**
  - `CampaignMission.Apply` runs only on the campaign path.
  - `probe-attack --mission iskuosasto --seeds 24 --minutes 8` stays byte-identical to the main baselines (`gate.sh`).
- **No weapon in the save.** A slot keeps its weapon, grenade, position, squad, leadership, skis, ahkio and items. The campaign man brings his name, nerve, morale, marksmanship, experience and leadership.
- **Determinism:**
  - The fill order is total: name, then leadership or experience, then roster order.
  - The tally only reads events and sim state; it never touches the sim.
- **A broken `campaign.json`** is a fresh campaign. The save is written aside and then moved into place, like `progress.json`.

## Review Focus
1. **A dead or recovering radioman or leader:**
   - The mortar goes to whoever fills the radio slot. With the slot empty, the mission has no mortar and does not throw.
   - Each leader slot gets the best remaining leader.
2. **Fewer fit men than slots, or none:**
   - With too few, the last slots in fill order are dropped, and a squad left without a leader gets one promoted.
   - With none, the mission refuses to start with a message instead of a crash.
3. **A failed chapter** is repeated with the losses kept. Quitting or restarting records nothing.
4. **Kill credit:**
   - Grenade kills go to the thrower, by the grenade's id; mortar hits credit nobody.
   - A melee win is credited to the winner, not to an earlier shooter.
   - No enemy is credited twice.
5. **Summer probe byte-identical; renamed hylky men:** the hylky probe still runs, since the names change but not the numbers.

---

### Task 1: The campaign file and its loader
**Files:**
- Create `src/Nmf.Content/Campaigns/CampaignSpec.cs` and `CampaignLoader.cs`.
- Create `content/core/campaigns/jatkosota/campaign.yaml`.
- Test `src/Nmf.Content.Tests/CampaignLoaderTests.cs` and `CoreContentTests`.

**Produces:**
- `record CampaignMan(string Name, int Nerve, int? Morale, int Marksmanship, int Leadership, int Experience)`
- `record CampaignChapter(string Mission, Localized Before, Localized After)`
- `record CampaignSpec(string Id, Localized Title, Localized Intro, IReadOnlyList<CampaignMan> Roster, IReadOnlyList<CampaignChapter> Chapters)`
- `CampaignLoader.Load(string directory)` throws `ContentLoadException` with the reason.
- `CampaignLoader.LoadAll(string campaignsDirectory)`

**Content:**
- The roster is "Lehdon joukkue":
  - Kers. Lehto
  - the seven men of iskuosasto (Korpela, Mäkinen, Laine, Hakala, Virtanen, Nieminen, Rantanen)
  - Sotm. Aho
  - two spares, Alik. Kauppinen and Sotm. Kivelä
- The hylky ski patrol is renamed from the platoon, with the same numbers:

  | Old name | New name |
  |---|---|
  | Kauppinen | Kpl. Virtanen (the LS) |
  | Kivelä | Sotm. Mäkinen |
  | Niemi | Sotm. Laine |
  | Rinne | Sotm. Rantanen |

  This way the losses of chapter 1 show in chapter 2.
- Hylky's ahkio message and briefing become generic ("ahkiomies").
- Chapters: iskuosasto, then hylky, each with `before` and `after` texts in fi and en.

**Tests:**
- `Loads_Jatkosota`
- `Rejects_` cases:
  - no chapters
  - an empty roster
  - a duplicate name
  - nobody with leadership ≥ 60
  - a value out of range
- `CoreContent_Campaigns_PointAtRealMissions`: every chapter's mission loads, and its non-stranded named men are all in the roster.

### Task 2: The campaign state and the filling of a mission
**Files:**
- Create `src/Nmf.Client/Campaign/CampaignState.cs` and `CampaignMission.cs`.
- Test `src/Nmf.Client.Tests/CampaignTests.cs`.

**Produces:**
- `enum ManStatus { Fit, Recovering, Fallen }`
- `class CampaignManState { Name, Nerve, Morale, Marksmanship, Leadership, Experience, Xp, Status, Missions, Kills, Hits }`
- `class CampaignState`:
  - fields `Campaign`, `Chapter`, `Men`, `History`, `Completed`
  - `static New(CampaignSpec)`
  - `ToJson()` and `static FromJson(string)`
- `CampaignMission.Apply(MissionSpec, CampaignState) → MissionSpec`. It throws `CampaignException` when no man is fit. Steps:
  1. Slots are the non-stranded player entries. The radio slot index is taken before anything changes.
  2. Named fit men take their own slots.
  3. Leader slots, in slot order, take the best remaining by leadership, then experience, then roster order.
  4. Other slots take the most experienced remaining, with roster order breaking ties.
  5. Unfilled slots are dropped.
  6. A squad left with men but no leader gets its best-leadership man as leader.
  7. The radio goes to the radio slot's man, or the mortar is removed when that slot is empty.
  8. Stranded entries are kept as they are.

**Tests:**
- `Fill_NamedMenKeepTheirSlots`
- `Fill_LeaderSlotsTakeBestLeaders`
- `Fill_TooFewMen_LastSlotsDropped_SquadGetsALeader`
- `Fill_NoFitMan_Throws`
- `Radio_FollowsTheSlot_WhenVirtanenIsOut`
- `Radio_SlotEmpty_NoMortar`
- `Stranded_Untouched`
- `Slot_KeepsWeaponAndKit_ManBringsHisSkills`
- `Json_RoundTrip`
- `Json_Broken_IsFresh`

### Task 3: Experience during the mission and after it
**Files:**
- Create `src/Nmf.Client/Campaign/CampaignTally.cs`.
- Modify `GameSession` to add the optional `Tally`, fed in `StepOnce`.
- Add `CampaignState.Record(...)`.
- Tests in `CampaignTests`.

**Produces:**
- `CampaignTally.Observe(Simulation sim, IReadOnlyList<SimEvent> stepEvents)` keeps, per own man by name:
  - Hits: an enemy hit by his shot or by his grenade's burst
  - Kills: an enemy put out of action (incapacitated, dead or captured), credited to the last of ours to hit him. A melee win moves the credit to the winner. A mortar shell clears the last hitter. Each enemy is credited at most once.
- Grenade throwers come from `sim.Grenades`, taken after every step and kept by id. A wound after `GrenadeExploded` counts for the thrower only when the man is within that grenade's blast radius of the burst. A wound after `ShellExploded` within the shell's blast radius clears the last hitter.
- `CampaignOutcome(bool Success, IReadOnlyDictionary<string, WoundLevel> Wounds, IReadOnlyDictionary<string, bool> Captured, CampaignTally Tally)` is built from the session.
- `CampaignState.Record(MissionSpec applied, CampaignOutcome outcome) → IReadOnlyList<ManReport>`:
  1. Recovering men who sat this mission out become fit.
  2. Each man who took part gets these points:
     - +10 for coming through (not dead or captured)
     - +3 per hit
     - +10 per kill
     - +10 more when the mission succeeded
  3. He gets one more mission counted.
  4. His status follows his wound: dead or captured → Fallen; serious or incapacitated → Recovering; light or none → Fit.
  5. On success the chapter advances, and `Completed` is set after the last. On failure the chapter stays.
  6. A history line is added.

**Tests:**
- `Tally_ShotHitAndKill`
- `Tally_GrenadeKill_ToTheThrower`
- `Tally_MortarKill_NoCredit`
- `Tally_MeleeWinner_TakesTheKill`
- `Tally_NoDoubleCredit`
- `Record_DeadFallen_IncapacitatedRecovers_LightPlays`
- `Record_RecoveringSitsOutThenReturns`
- `Record_Success_Advances_Xp`
- `Record_Failure_Repeats_LossesKept`
- `Record_LastChapter_Completes`
- `Session_Tally_FedEachStep`: a short real iskuosasto session with the tally gives the same hit count as counting `ShotFired` events with hits on red units.

**Ruling:** a serious wound also sits a mission out. The spec names only incapacitated and light; serious is closer to incapacitated in recovery.

### Task 4: Playing the campaign (Godot)
**Files:**
- Create `src/Nmf.Client/Campaign/CampaignText.cs`, which is testable:
  - the menu button label
  - the roster lines: name, weapon of the slot he would fill, XP, status
  - the end-screen lines
- Create `src/Nmf.Game/CampaignView.cs`.
- Modify `MenuView`, `GameRoot` and `Hud`.

**Behaviour:**
- **Menu:** a *Kampanja* group on top, with *Jatka kampanjaa: <luku>* or *Aloita kampanja*.
- **Campaign view:**
  - the title and intro, or the chapter's `before` text
  - the mission's name and the lineup: slot weapon, name, XP
  - who sits out and why
  - the fallen
  - *Aloita tehtävä*, *Aloita kampanja alusta* and *Takaisin*
  - When the campaign is done, the last `after` text and *Aloita kampanja alusta*.
- **Launch:** `_launch` carries a campaign flag. `GameRoot` loads the campaign, applies the state and builds the session with a tally. A `CampaignException` goes back to the campaign view with the message.
- **End:**
  - On `MissionEnded` in a campaign, the result is recorded and `campaign.json` saved (not for automated runs).
  - The end screen lists each man's points and status and offers *Takaisin kampanjaan*.
  - *Yritä uudelleen* in a campaign goes back to the campaign view.
- Plain progress is recorded for campaign missions too.

**Tests:** `CampaignTextTests` for the labels, lines and statuses in fi and en.

**Check:** screenshots of the campaign view (`--campaign` command-line option opens it) and of the end screen.

### Task 5: Finish
- Docs:
  - README: campaign
  - help text, if the controls help mentions the menu
- Gates:
  - the suite
  - `gate.sh` (probe byte-identical)
  - `probe-rescue --mission hylky --seeds 24 --minutes 30`, which must not change, because only names changed
- Final review (opus), fixes, merge, push, memory.

---

## Amendments before Task 2 (after the advisor's read)
These take precedence over the task text above.

1. **Leadership.**
   - Roster entries give leadership explicitly. A missing one defaults to **40** in the campaign loader, because the mission default of 100 would rank every private above Korpela.
   - Test: `Fill_PrivateWithoutLeadership_DoesNotOutrankLeaders`.
2. **No soft lock.**
   - The men available are the fit ones. When none is fit, the recovering come back, because recovery only happens when a mission is recorded.
   - `CampaignException` is thrown only when every man has fallen.
   - Test: `Fill_OnlyRecovering_TheyComeBack`.
3. **Kit slots before plain ones.** The fill order is named, then leader, then kit (the radio slot, an ahkio, items), then plain, each in slot order.
   - "Experienced" means the `Experience` attribute, not XP, in 9a.
   - **Ruling (against the spec's literal "viimeiset paikat"):** too few men drop a rifleman before the ahkio or the radio. Hylky's ahkio slot is the last entry, and reordering the YAML would shift unit ids.
   - Test: `Fill_TooFewMen_AhkioKeptBeforeRifle`.
4. **The tally.**
   - Wounds are attributed by event order:
     - A `ShotFired` names its hit.
     - A wound after the step's most recent `GrenadeExploded` or `ShellExploded` belongs to that burst when the man is within its blast radius. The radius only guards against a later bleeding wound in the same step.
   - Kills are decided at the end of each step, from out-of-action state, because melee emits the loser's wound before `MeleeEnded`. Credit goes to this step's melee winner, else to the last hitter.
   - Enemies already down when the tally starts are never credited.
5. **Restarts.** Every campaign launch rebuilds from `campaign.json`, never from the bare mission id. That covers start, *Yritä uudelleen* and *Aloita alusta*.
