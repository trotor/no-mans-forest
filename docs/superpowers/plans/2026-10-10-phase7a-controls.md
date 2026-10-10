# Phase 7a: controls — modifier map, cursor hints, right-click menu, order queues — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-7a-controls`, merge `--no-ff` to main and push.

**Goal:** Every click says beforehand what it will do. The right button opens the alternatives. Shift queues orders. A + click attacks forward. All keys are listed in the F1 help and in the settings window.

**Architecture:**
- Pure, tested client logic in Nmf.Client:
  - `ClickPreview`: what a click would do
  - `ContextMenu`: the rows of the right-click menu
  - `OrderQueue`: queued orders, run as plain orders after each frame's steps
  - `ControlsHelp`: the one key list, shared by F1 and settings
- `GameSession` gets the new click semantics and the orders behind them: forced-tactic attack, attack forward, take position.
- Godot: cursor shapes and a hint label, a `PopupMenu`, queue markers and the "stop and ask" bar.

**Spec:** `docs/superpowers/specs/2026-10-10-controls-design.md`

## Global Constraints
- Preview and click agree. Both go through one target resolver (`GameSession.Resolve(point, radius)`).
- Queues live only in the client and issue normal orders, so the sim and replays are unchanged.
- A queue stops and asks on a halted attack, a rejected order, or nobody fit left. It never stops on its own otherwise.
- Every key or modifier the game reacts to appears in `ControlsHelp`, in English and Finnish.

## Review Focus
1. **A click that lands in a different target from its preview** (an own man and an enemy close together; zoomed far out): `ClickPreviewTests.PreviewAndClick_AgreeOnEveryTarget`.
2. **Queue for men who are later re-selected differently:** a queue belongs to its men. Ordering a subset clears only that subset's queue: `OrderQueueTests.OrderingSomeOfTheMen_ClearsOnlyTheirQueue`.
3. **Queue head is an attack on a man who died meanwhile:** it is skipped and the next one starts, it does not stall: `OrderQueueTests.AttackOnAFallenMan_IsSkipped`.
4. **Attack forward meeting an enemy the planner can't plan for:** it falls back to fire and goes on, it does not loop: `GameSessionTests.AttackForward_WithoutAPlan_FiresAndGoesOn`.
5. **Help lists every key:** `ControlsHelpTests.EveryKeyTheGameHandles_IsInTheHelp` (a list of the keys `HandleKey` uses, kept beside it).

---

### Task 1: Modifier map and the new orders (client)
- `GameSession.Resolve(point, radius) : ClickTarget` = (Kind: Own / Enemy / Mark / Body / Ground / None, Unit?, Contact?, Point). It is the existing order of `HandleLeftClick`, factored out.
- `HandleLeftClick(point, double, shift, alt, radius, ctrl, attackForward)`, per the spec's table:
  - Alt: sneak, and Alt + double: crawl.
  - Ctrl + double on an enemy: assault. Ctrl + single: area fire, as now.
  - Alt + double on an enemy: long fire, via `OrderAttack(target, tacticId: "long_fire")`.
  - Double on a mark: attack its contact.
  - Shift on an order: queue it (Task 4 hook: `Queue.Add` instead of issuing).
  - A held: attack forward.
- New orders:
  - `OrderAttack(UnitId target, string? tacticId = null)`
  - `OrderAttackForward(Vec2 point)`: an `OrderQueue` item kind
  - `OrderTakePosition(Vec2 facing)`: each man to `CoverFinder.Find(sim, man, facing)` or his own place, at his own pace
- Tests (`GameSessionTests`):
  - `AltClick_Sneaks_AltDouble_Crawls`
  - `CtrlDoubleOnEnemy_Assaults`
  - `AltDoubleOnEnemy_LongFire`
  - `DoubleOnMark_AttacksIt`
  - `TakePosition_SendsEachManToCoverFacingThePoint`

### Task 2: Click preview, cursor and hint (client + Godot)
- `ClickPreview.Describe(GameSession, point, Modifiers, radius, language) : (CursorKind Cursor, string Click, string? Double)`.
- Godot `_Process`:
  - set `Input.SetDefaultCursorShape` from the kind
  - after 0.3 s still, draw the hint label near the cursor (hidden while the hover tip shows, and when the setting is off)
  - setting `CursorHints` in `GameSettings` (default on), with a check box in `SettingsView`
- Tests:
  - `ClickPreviewTests` per row of the table
  - `PreviewAndClick_AgreeOnEveryTarget`: for a grid of points round an own man, an enemy, a body and a mark, the preview's action equals the `ClickResult` that `HandleLeftClick` returns on a copy of the session

### Task 3: Right-click menu (client + Godot)
- `ContextMenu.Items(GameSession, point, radius, language) : IReadOnlyList<MenuItem>`. `MenuItem(string Text, MenuAction Action, bool Queue)`, where `MenuAction` is an enum plus an optional tactic id.
- `ContextMenu.Run(GameSession, MenuItem, ClickTarget)` issues the order.
- Godot: `PopupMenu` at the mouse; Esc or a click outside closes it. A right click with no target and nothing actionable clears the selection, as before.
- Tests (`ContextMenuTests`):
  - rows per target
  - tactics listed from `session.Tactics`
  - every row's order equals the matching click's
  - the "+ jonoon" rows queue

### Task 4: Order queues (client + Godot)
- `OrderQueue`:
  - `Add(IReadOnlyList<UnitId> men, QueuedOrder order)`, `Replace(...)`, `Update(GameSession, IReadOnlyList<SimEvent> events)` after the steps
  - `Paused` with a reason, `Continue(group)`, `Cancel(group)`
  - `Queues : IReadOnlyList<(men, items, paused)>` for drawing
  - `QueuedOrder` kinds: Move(point, mode), Attack(target, tactic?), AttackForward(point), FireAt(target), AreaFire(point), TakePosition(point), Loot(body)
- Done rules: as in the spec (move: all fit arrived or 120 s; attack: Ended success; fire/area: 15 s or target out; position: in stance; attack forward: arrived).
- Stop and ask: `PlannedAttackHalted` for its attack, an `OrderRejected` for its men, or nobody fit.
- `GameSession.Update` runs `Queue.Update` after the steps. A non-shift order to some men calls `Queue.ClearFor(men)`, which drops them from their queues.
- Attack forward: while moving, the first seen enemy within 150 m of any of its men gets `OrderAttack` (planned). On success the move goes on; on a halt the queue asks.
- Godot:
  - numbered markers and lines (in `UnitView` overlay)
  - an ask bar above the cards with "Jatka jonoa" and "Peru jono" buttons, plus a toast
- Tests (`OrderQueueTests`):
  - `ShiftOrders_AreQueued_FirstStartsAtOnce`
  - `MoveDone_NextStarts`
  - `AttackSuccess_NextStarts`
  - `AttackHalted_StopsAndAsks_WithWhatIsLeft`
  - `Continue_And_Cancel`
  - `OrderingSomeOfTheMen_ClearsOnlyTheirQueue`
  - `AttackOnAFallenMan_IsSkipped`
  - `AllDown_Stops`
  - `AttackForward_AttacksWhatItMeets_ThenGoesOn`
  - `AttackForward_WithoutAPlan_FiresAndGoesOn`

### Task 5: Help everywhere
- `ControlsHelp.Lines(language) : IReadOnlyList<(string Keys, string What)>`: the full list, the new map included.
- `Hud`'s F1 panel and a "Näppäimet" / "Keys" section (scrollable) in `SettingsView` both render it.
- README controls table updated (EN and FI).
- Test: `ControlsHelpTests.EveryKeyTheGameHandles_IsInTheHelp`. `GameRoot.HandledKeys` is a static list kept next to `HandleKey`; the test checks each appears in the help, in both languages.

### Task 6: Finish
- Full tests, screenshots (cursor hint, menu, queue markers, ask bar) on the testbed, final review, merge, push, memory.
