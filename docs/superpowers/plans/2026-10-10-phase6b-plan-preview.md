# Phase 6b: attack plan preview — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-6b-plan-preview`, merge `--no-ff` to main and push.

**Goal:** A double click on an enemy stops the game and draws the suggested attack on the map. The player can switch the tactic (1–3), drag the fire base, the form-up and route points, and start the attack with Enter or cancel with Esc. Attacks under way stay on the map, faded, and the cards tell each man's part and phase.

**Architecture:**
- Sim: `TacticPlanner.Propose` takes optional `PlanEdits` (fire base, form-up, via points). The plan stays pure data in the order, so determinism is unchanged.
- Client:
  - `AttackPreview` (pure, tested): options per tactic, the selected plan, edits, handles, confirm and cancel.
  - `PlanText`: the reason and the warnings.
  - `PlanPhaseText`: the card line.
  - `GameSession` opens the preview on a double click when the setting is on.
- Godot:
  - `PlanDrawing`: draws a plan, with or without handles.
  - a paper panel in `Hud`
  - input in `GameRoot`: handle drag, keys, pause via `OverlayPause` key "plan".

**Spec:** `docs/superpowers/specs/2026-10-10-plan-preview-design.md` (frame: `2026-10-10-tactics-design.md` §"6b").

## Global Constraints
- An unedited plan is exactly the plan `Propose` gives today: an empty or null `PlanEdits` changes nothing.
- The preview gives no order until confirmed. Cancel leaves the sim untouched.
- These are immediate, with no preview: Shift + double (queue), Alt + double, Ctrl + double, the menu's tactic rows, and attack forward.
- Preview and click agree: `ClickPreview` says *suunnittele hyökkäys* exactly when the click opens the preview.
- Every new key (R, and 1–3 / Enter / Esc in the preview) is in `ControlsHelp` in both languages.

## Review Focus
1. **Target dies or vanishes while the preview is open** (paused, but a replay or step can't happen; still: the contact may be cleared by a reload). Confirm with a target out of action gives no order and closes: `AttackPreviewTests.Confirm_TargetGone_NoOrder`.
2. **A selection change while the preview is open:** the preview keeps the men it was opened for, and confirm orders them: `AttackPreviewTests.Confirm_OrdersTheMenItWasOpenedFor`.
3. **Drag onto water or a wall, or off the map:** the old plan stays, with the "Sinne ei pääse" warning: `AttackPreviewTests.BadDrag_KeepsTheOldPlan`.
4. **Switching tactic after editing:** the edits that the new tactic has no handle for (form-up and via on long fire) are dropped, and the fire base edit carries over: `AttackPreviewTests.SwitchTactic_KeepsTheFireBaseEdit_DropsTheRest`.
5. **Only the assault option is possible** (fewer than 4 men, no fire base): the preview still opens with assault and long fire if possible, and fire and movement is greyed with its reason: `AttackPreviewTests.TooFewMen_FireAndMovementGreyed`.

---

### Task 1: Planner edits (sim)
- `public sealed record PlanEdits(Vec2? FireBase = null, Vec2? FormUp = null, IReadOnlyList<Vec2>? Via = null) { public bool IsEmpty => ...; }` in `TacticPlan.cs`.
- `Propose(sim, side, men, target, tactic, PlanEdits? edits = null)`:
  - **FireBase:** candidates by `FireBaseCandidates` limited to a 12 m window round the point, with no band and no side test. The anchor is the candidate nearest the point (ties by score). If there are none, the anchor is the nearest passable reachable cell within 12 m and the plan is blind; if there is no such cell, null.
  - **FormUp:** cells within 6 m of the point, passable and reachable, the nearest one as the anchor; none gives null. The spread is as before.
  - **Via:** the strike route is the concatenation of cost-flood paths start → via1 → … → form-up. Any unreachable leg gives null. The fire-lane cost stays.
- Tests in `TacticPlannerTests`:
  - `Edits_FireBase_IsWhereItWasDragged`
  - `Edits_FireBaseThatCannotSee_IsBlind`
  - `Edits_FormUp_IsWhereItWasDragged`
  - `Edits_Via_RoutePassesThroughIt`
  - `Edits_Unreachable_NoPlan`
  - `Edits_Empty_SamePlan`

### Task 2: AttackPreview and PlanText (client)
- `public enum PlanHandleKind { FireBase, FormUp, Via }`, `public sealed record PlanHandle(PlanHandleKind Kind, int Index, Vec2 At)`.
- `public sealed record PreviewOption(string Id, string Name, TacticPlan? Plan, string? WhyNot, bool Suggested)`, where Id is a tactic id or `"assault"`.
- `AttackPreview`:
  - `static AttackPreview? Open(GameSession s, UnitId target)`: returns null when no men are commanded or the target is unknown.
  - `Men`, `Target`, `Options`, `Selected`, `Select(int)`, `Plan`, `Edits`, `Problem` (string key), `Handles`, `HandleAt(Vec2, radiusCm)`, `RouteIndexAt(Vec2, radiusCm)`.
  - `Drag(PlanHandle, Vec2)`, `AddVia(int routeIndex, Vec2)` (at most 3), `RemoveVia(int)`, `Reset()`.
  - `Confirm(GameSession) : bool` gives `PlannedAttackOrder` or `AssaultOrder`s. It clears the queue of the men and sets `LastPlan`.
- `PlanText.Reason(option, language)` and `PlanText.Warnings(preview, session, language)`.
- Tests (`AttackPreviewTests`):
  - `Open_SelectsTheSuggestion`
  - `TooFewMen_FireAndMovementGreyed`
  - `Select_SwitchesThePlan`
  - `SwitchTactic_KeepsTheFireBaseEdit_DropsTheRest`
  - `DragFireBase_Replans`
  - `BadDrag_KeepsTheOldPlan`
  - `AddAndRemoveVia`
  - `Reset_BackToTheSuggestion`
  - `Confirm_OrdersTheEditedPlan`
  - `Confirm_Assault_GivesAssaultOrders`
  - `Confirm_OrdersTheMenItWasOpenedFor`
  - `Confirm_TargetGone_NoOrder`
  - `Cancel_NoOrder` (no submit)
  - `Texts_InBothLanguages`

### Task 3: Session wiring, menu, click preview, help (client)
- Settings and session:
  - `GameSettings.PreviewAttacks` (default true), plus a check box in `SettingsView` (Task 4).
  - `GameSession.PreviewAttacks` (bool, default false so old tests keep their behaviour; GameRoot sets it from the settings).
  - `GameSession.Preview : AttackPreview?`, `OpenPreview(UnitId)`, `ClosePreview()`, `ConfirmPreview()`.
- Clicks:
  - In `HandleLeftClick`, a double click (no Alt, no Ctrl, no Shift) on an enemy or a mark opens the preview when `PreviewAttacks` is on and there are tactics. It returns `ClickResult.AttackPreviewOpened`.
  - `ClickPreview`: the double text is *suunnittele hyökkäys* / *plan the attack* in the same case.
- Menu: `MenuAction.PlanAttack` row *Suunnittele hyökkäys…* / *Plan the attack…* (Unit = target), first among the attack rows.
- `ControlsHelp`: a preview line (1–3, drag, Enter, R, Esc), the setting, and R in `HandledKeys`.
- Tests:
  - `GameSessionTests.DoubleOnEnemy_OpensThePreview_WhenOn`
  - `…_AttacksAtOnce_WhenOff`
  - `ShiftDouble_Queues_NoPreview`
  - `AltAndCtrlDouble_AtOnce`
  - `ClickPreviewTests.Double_SaysPlan_WhenThePreviewOpens`
  - `ContextMenuTests.PlanAttackRow`
  - `ControlsHelpTests` (existing)

### Task 4: Godot preview (draw, panel, input)
- `PlanDrawing.Draw(CanvasItem, TacticPlan, Simulation, alpha, handles?, hovered?, dragging?)`, called from the `UnitView` overlay. Assault option: arrows from each man to the aim.
- `Hud.ShowPlanPreview(AttackPreview?, texts)`: the top paper panel with the tactic buttons, the reason, the warnings, and Aloita / Palauta / Peru.
- `GameRoot` input while `session.Preview` is open:
  - left press on a handle starts a drag, or on the route line it adds a via point
  - motion moves the drag ghost, and release calls `Drag` or `AddVia`
  - right click on a via point removes it
  - a double click on the target confirms
  - other clicks are swallowed
  - keys: 1–3, Enter, R and Esc first, before the overlay check; camera keys still work
- `OverlayPause.Opened("plan")` / `Closed("plan")`.
- Settings check box *Näytä hyökkäyssuunnitelma ensin*.
- Manual: screenshots with `--screenshot-at` on the testbed: `--open=plan` opens the preview on the nearest seen enemy (debug flag, like `--open=settings`).

### Task 5: Attacks under way (client + Godot)
- `PlanPhaseText.For(Simulation, UnitId, language) : string?`:
  - fire team: *Tuli: matkalla* (Deploy, not settled) / *Tuli: lamauttaa*
  - strike team: *Isku: matkalla* / *Isku: odottaa* (Suppress or Assault before launch, at the form-up) / *Isku: rynnäkkö* (launched)
  - English likewise
  - needs read access: `PlannedAttack.IsSettled(UnitId)`, `PlannedAttack.Launched` (public getters over internal state)
- Cards show the line, and `UnitView` draws the plans of running own attacks at 35 % alpha.
- Tests `PlanPhaseTextTests`: `FireTeam_DeployThenSuppress`, `StrikeTeam_WaitsThenAssaults`, `NotInAnAttack_Null`.

### Task 6: Finish
- README controls (EN and FI).
- Full tests, screenshots, the final review, merge, push and memory.
