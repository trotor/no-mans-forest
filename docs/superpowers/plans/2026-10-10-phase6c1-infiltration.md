# Phase 6c-1: infiltration, men in the suggestion, mission tactics — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-6c1-infiltration`, merge `--no-ff` to main and push.

**Goal:** Experienced men can creep up unseen and go in with grenades, with no fire base. The suggestion picks it when the fire base would be blind. A mission can prefer or forbid tactics.

**Spec:** `docs/superpowers/specs/2026-10-10-infiltration-enemy-design.md` (6c-1).

## Global Constraints
- Plans of the existing tactics are unchanged: the fire-and-movement and long-fire tests and `Edits_Empty_SamePlan` stay green as they are.
- Determinism holds: integer maths only and ordered tie-breaks in Nmf.Sim. The plan rides in the order.
- No code may assume `FireTeam[0]`. Use `TacticPlan.FirstMan`.
- A man's own fire policy is restored when an infiltration ends in any way: finished, halted, disbanded, or he leaves.

## Review Focus
1. **Infiltration with an empty fire team in every consumer:**
   - Deploy and Suppress checks (`fire.All` over an empty list is true)
   - `NoFire` (no fire team must not halt with NoFire)
   - `Distribute` with an empty fire list
   - preview handles, PlanDrawing (no line from (0,0)), PlanText, PlanPhaseText, AttackProbe
   - Tests: `PlannedAttackTests.Infiltration_NeverHaltsForNoFire` and `AttackPreviewTests.Infiltration_NoFireBaseHandle`.
2. **Fire policy restored after a halt, a disband, or a man killed or broken mid-creep:** `PlannedAttackTests.Infiltration_FirePolicyRestored_OnHalt`.
3. **A form-up the planner cannot hide:** no plan, so infiltration is greyed with "ei sopivaa paikkaa". Test: `TacticPlannerTests.Infiltration_NoHiddenFormUp_NoPlan`.
4. **Replan (position moved) of an infiltration** keeps the sneak and the hold-fire policy: `PlannedAttackTests.Infiltration_Replan_StillSneaks`.
5. **Mission forbids every tactic:** the session has no tactics, and a double click fires (the old path). Test: `TacticChoiceTests.ForbidAll_Empty`.

---

### Task 1: TacticDef infiltration + loader + content
- `public sealed record TacticInfiltration(int FormUpMinCm, int FormUpMaxCm, int MinExperience);`
- `TacticDef(..., TacticInfiltration? Infiltrate = null)`.
- Validation:
  - not both a maneuver and infiltrate
  - 0 < min ≤ max ≤ 6000
  - experience 0..100
  - with infiltrate, fire_base may be absent; the loader then uses the form-up band
- `content/core/tactics/15_infiltration.yaml`.
- Tests:
  - `TacticLoaderTests.Infiltration_Read`
  - `TacticDefTests.Infiltration_AndManeuver_Rejected` (in the existing validation tests file)

### Task 2: Planner
- `TacticPlan.FirstMan => FireTeam.Count > 0 ? FireTeam[0].Man : StrikeTeam[0]`. Replace every `FireTeam[0].Man` with it.
- `Propose` for infiltrate:
  - all fit men strike (at least 2)
  - form-up candidates: band from the infiltrate def, not `SeenExactly`, reachable, scored by cover, nearest by route cost
  - route flood: extra = 3 × exposure + `HeardStepCost` within the sneak noise radius of any threat source
  - `RouteExposed` as before
  - `PlanReason(0, side, false)`
  - edits: FormUp and Via honoured, FireBase ignored
- `ThreatMap.NearSource(Vec2, radiusCm)`.
- `Suggest` risk rules per the spec (§2).
- Tests:
  - `Infiltration_FormUp_InBand_Unseen`
  - `Infiltration_Route_AvoidsSeenAndHeardGround`
  - `Infiltration_NoFireTeam`
  - `Infiltration_NoHiddenFormUp_NoPlan`
  - `Suggest_BlindFireBase_ExperiencedMen_Infiltrate`
  - `Suggest_BlindFireBase_GreenMen_LongFire`

### Task 3: Runner
- `HaltReason.Discovered`.
- Infiltration:
  - Deploy sends along the route with `MoveMode.Sneak` and sets HoldFire, saving `PolicyBefore`
  - no hold spots
  - discovered (any member suppressed this tick, `LastSuppressedTick` ≥ start) → assault when the centre is within 40 m, else Halt(Discovered)
  - all arrived or 4 min → Assault: restore policies, throw at known men where `GrenadeSystem.CanThrowAt`, `GrenadeWaitTicks` (2 s) later GoIn all
  - never NoFire
- Restore policies in `Halt`, `Finish` and when a member leaves. Replan keeps the infiltration's mode.
- Alerts text for Discovered (fi/en).
- Tests (`PlannedAttackTests`):
  - `Infiltration_Sneaks_HoldingFire`
  - `Infiltration_AtTheFormUp_GrenadesThenIn`
  - `Infiltration_DiscoveredClose_GoesIn`
  - `Infiltration_DiscoveredFar_Halts`
  - `Infiltration_FirePolicyRestored_OnHalt`
  - `Infiltration_NeverHaltsForNoFire`
  - `Infiltration_Replan_StillSneaks`
  - `AlertsTests` (Discovered)

### Task 4: Client (preview, texts, drawing, probe)
- `AttackPreview.Handles`: a FireBase handle only with a fire team. `Select` keeps FormUp and Via for infiltration.
- `PlanText.Reason` for infiltration. `PlanText.Warnings`: blind does not apply; the route warning applies.
- `PlanDrawing`: skip the fire base when the fire team is empty.
- `AttackProbe` uses `FirstMan`.
- Tests:
  - `AttackPreviewTests.Infiltration_NoFireBaseHandle`
  - `Texts_Infiltration`

### Task 5: Mission tactics
- `MissionSpec.Tactics : MissionTactics(Prefer, Forbid)` and the loader `tactics: { prefer, forbid }`.
- `TacticChoice.For(all, rules)` and `TacticChoice.Unknown(all, rules)` (Nmf.Sim.Tactics).
- GameRoot applies them and fails the mission on unknown ids. The CLI probe applies them too (`--mission`).
- Content test: every mission's tactic ids exist.
- Tests:
  - `TacticChoiceTests.Forbid_Removes`
  - `TacticChoiceTests.Prefer_First_InOrder`
  - `TacticChoiceTests.ForbidAll_Empty`
  - `TacticChoiceTests.Unknown_Listed`
  - `MissionLoaderTests.Tactics_Read`
  - `ContentTests.MissionTactics_Exist`

### Task 6: Measure and finish
- `probe-attack --mission iskuosasto --tactic suggest --seeds 24`, with and without `prefer: [infiltration]`, plus `--tactic infiltration`. Record the results in the spec. Set iskuosasto's prefer only if it is not worse.
- Help, README, screenshots (preview of infiltration), final review, merge, push, memory.
