# Phase 8d: alertness and alarm — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD. Branch `phase-8d-alertness`, merge `--no-ff` to main and push.

**Goal:** On a mission that asks for it, the enemy starts calm. Each man has his own alertness (calm, suspicious, alerted, combat), raised by what he sees and hears. The alarm spreads by shouts, shots, flares and a field telephone the player can cut. A search patrol can be sent to comb an area.

**Spec:** `docs/superpowers/specs/2026-10-10-alertness-design.md`

## Global Constraints
- **Deterministic.**
  - Alertness, flares, phones and line cutting change only inside `Simulation.Step` or by orders.
  - Search and patrol run outside the step, give only orders and never use `sim.Rng`.
  - Every new field and timer is in `StateHash`: per man, per side, per phone and per cut.
- **Gated per side** (`Simulation.EnableAlertness(side)`). For a side without it:
  - no rule applies
  - no extra `sim.Rng` draws
  - no new events
- **Unchanged gate:**
  - `probe-attack --mission iskuosasto --seeds 24 --minutes 8`, with `--tactic suggest` and without, must give byte-identical output to the baselines taken on main (scratchpad `probe-base-*.txt`). Re-run after each sim task.
  - All existing tests pass unchanged.
- **Shouting:** each man shouts at most once.
- **Phone condition:** the `phone` event condition reads the phone's hashed `Called` state.

## Review Focus
1. **Old missions:**
   - A side without alertness behaves exactly as before: patrols walk, the commander uses everyone, no surprise delay.
   - Pinned by the probe and by `Alertness_Disabled_NothingChanges`.
2. **The shout chain ends:**
   - Twenty calm men 40 m apart all end up alerted.
   - Each shouts once, and the number of events is bounded (`Shout_Chain_EachOnce`).
3. **Line cut during the call:**
   - The call fails, and the next alerted man does not retry on a cut line (`CutDuringCall_Fails_NoRetry`).
4. **Cutter interrupted:**
   - Any new order, being pinned or going down cancels the cut.
   - The cut never completes from a distance (`Cut_Interrupted`).
5. **The search squad's man taken over by the commander:**
   - Once released, the search gives him no more orders (`Search_HandsOver_StopsOrdering`).

---

### Task 1: Alertness core (sim)
- **Types and state:**
  - `Alertness { Calm, Suspicious, Alerted, Combat }`, with `Unit.Alertness` defaulting to Combat.
  - `Unit.AlertSinceTick`: the last stimulus.
  - `Unit.ReadyTick`: no fire before this.
  - `Unit.ShoutTick`: pending; -1 when none.
  - `Unit.Shouted`.
  - `Unit.AlarmAt`: where the alarm came from.
  - All of these go in `StateHash`.
- **Simulation:**
  - `Simulation.EnableAlertness(Side)`, which sets that side's men Calm; and `AlertnessOn(Side)`.
  - `Simulation.Noise(at, radius, kind)`: a pending noise list, cleared at each alertness pass and hashed. It is called from `GrenadeSystem.Burst` (50 000 cm) and from melee start (3 000 cm).
- **`AlertnessSystem.Update`:**
  - Called from `VisionSystem.Update` before `MovedSinceVisionUpdate` is reset.
  - Only for sides with alertness on.
  - Applies the stimuli in spec §3, the shouts (§4), and the 30 s suspicious decay.
- **Vision:** `VisionRules.ObserverPct` gets the alertness factor (calm 40 %, suspicious 120 %) only for alertness-on sides.
- **SoldierBrain:**
  - A man below Combat does nothing but the cover reaction.
  - Becoming Alerted sets `CoverReactionPending` with threat `AlarmAt`.
  - Before `ReadyTick` he does not fire or throw.
- **`AlertOrder(Unit, Alertness)`:** raises only, and does not shout.
- **Events:** `AlertnessChanged(Unit, Alertness)` and `AlarmRaised(Side, By, At, AlarmKind)`.
- **Tests (`AlertnessTests`):**
  - Disabled_NothingChanges
  - MovementHeard_Suspicious
  - Shot_Alerts
  - Shout_Chain_EachOnce
  - Calm_DoesNotFire_SurpriseTwoSeconds
  - Suspicious_CalmsAfter30s
  - FallenComrade_Alerts
  - CalmSpotsSlower
  - UnderFire_Combat
  - SameSeed
- **Probe gate.**

### Task 2: Flares
- **State:** `Simulation.SetFlares(side, n)`, `FlaresLeft(side)`, plus the last flare tick (hashed).
- **Firing:** a leader newly Alerted or Combat fires a flare 3 s later if 60 s have passed since the side's last flare, through `Unit.FlareTick`.
- **Effect:** all men of the side within 600 m are alerted. Events: `AlarmRaised(Flare)`.
- **Tests:**
  - Flare_AlertsWithin600m
  - Flare_OncePerMinute_LimitedCount
- **Probe gate.**

### Task 3: Field telephone and line cutting
- **`FieldPhone`** (Nmf.Sim.Mission or World):
  - Id, Side, At, Line, Called, Failed, CutAt?, Caller?, CallDoneTick
  - Cutter?, CutPoint, CutDoneTick
  - Kept in `Simulation.FieldPhones` and hashed.
- **`PhoneSystem.Update`:** inside the step, every tick. It handles the call (§4) and the cut.
- **`CutLineOrder(Unit, PhoneId, At)`:**
  - Validated: an enemy phone, `At` within 500 cm of the line, a path to the point exists.
  - It moves the man to the nearest point on the line.
- **Mission loading:**
  - `MissionSpec.Phones`, plus `EnemyAiSpec.Alertness` and `EnemyAiSpec.Flares`.
  - The loader validates them.
  - `MissionScenario` enables alertness, sets flares and adds phones.
- **Event condition:** `EventCondition.Phone` (reads `Called`). The `MissionEvents` constructor validates the id.
- **Tests:**
  - `FieldPhoneTests`: AlertedPost_Calls; CutLine_CallFails; CutDuringCall_Fails_NoRetry; Cut_TakesTenSeconds; Cut_Interrupted; CallerDown_NextTries; Cut_TooFar_Rejected
  - `MissionLoaderTests`: Alertness_Flares_Phones_Read; Phone_LineNeedsTwoPoints
  - `MissionEventsTests`: PhoneCondition

### Task 4: Search, patrol and commander
- **`EventActionKind.Search` (squad, zone):**
  - `MissionEvents` submits an `AlertOrder` (Alerted) for each man and adds a `SearchBehavior` to the `Scenario.Searches` list.
  - `GameSession` and the CLI tick it like patrols.
- **`SearchBehavior`:**
  - Goes to the zone centre, then the nearest unvisited point of a 20 m grid; each man has a 3 m sideways offset.
  - On Combat or fire, it hands the squad to the commanders (`Adopt`) and stops.
- **`PatrolBehavior`** (alertness-on side only):
  - Walks only when Calm.
  - Waits while Suspicious.
  - Stops for good from Alerted on.
- **`EnemyCommander`** (alertness-on side only):
  - The counterattack uses only men Alerted or above.
  - Investigate uses only men Suspicious or above.
- **Tests:**
  - `SearchTests`: Search_CombsTheZone; Search_HandsOver_StopsOrdering
  - `PatrolTests`: Calm_Walks_Suspicious_Waits_Alerted_Stops
  - `EnemyCommanderTests`: CalmMen_NotInCounterattack
- **Probe gate.**

### Task 5: Content, client and Godot
- **`vartiokoe` debug mission** (spec §8).
- **Client:**
  - `AlertnessText.Mark` and `AlertnessText.Card`, for shown enemies only.
  - Alerts: `Alarm` texts.
  - `CombatEffects`: an `AlarmRaised(Shout)` heard gives a Shout signal, reusing the counterattack path.
  - A flare effect.
  - `ContextMenu`: `MenuAction.CutLine`, through `GameSession.OrderCutLine`.
- **Godot:**
  - The mark above enemies.
  - Phone lines and the phone on the map.
  - The flare light.
  - Toasts.
- **Docs:** help and README.
- **Tests:** `AlertnessClientTests` (mark only when shown, card, menu row, alerts, shout signal).
- **Finish:** screenshot, final review, merge, push, memory.
