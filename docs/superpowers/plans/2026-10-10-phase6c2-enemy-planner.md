# Phase 6c-2: the enemy uses the planner, the old attack goes — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans), one final review. TDD: named tests first, watch them fail, implement, run `dotnet test NoMansForest.slnx`. Branch `phase-6c2-enemy-planner`, merge `--no-ff` to main and push.

**Goal:** The enemy commander's counterattack is a planned attack. The men who hold the post are the fire team, firing from where they are, and the reserve is the strike team. The old attack (`AttackOrder`, `CounterattackOrder`, `AttackGroup`, `AttackPlanner`) is removed, and balance is measured both ways.

**Spec:** `docs/superpowers/specs/2026-10-10-infiltration-enemy-design.md` (6c-2).

## Global Constraints
- Determinism: everything stays in orders. The commander still acts only through orders.
- The player's plans are unchanged. The new `PlanTeams` parameter is null for them.
- The shout (`CounterattackStarted`) still comes when the enemy goes in, so the client's toast and voice keep working.
- Without tactics, nobody plans: the player's double click fires, and the enemy does not counterattack.

## Review Focus
1. **The post that cannot see the target** (target in a thicket): the plan is blind, the fire team area-fires at where he is believed to be, and the strike team still goes. Test: `EnemyCommanderTests.ThePost_FiresAtWhereAHiddenTargetWasLastSeen` (kept, adapted).
2. **A refused counterattack** (no plan possible): that target is not tried again for a minute, and the commander does not loop on it. Test: `EnemyCommanderTests.NoPlanPossible_TargetRested`.
3. **A halted enemy attack:** the commander's cooldown and return to the post work as after the old attack. Test: `EnemyCommanderTests.AfterAHalt_TheyGoBackToTheirPost`.
4. **Old saves and replays:** orders live in memory only, so there are none. Confirm with grep that no serializer names `AttackOrder`.
5. **StateHash** covers the planned attack instead of the attack groups. The determinism tests (`StateHashTests`, replay tests) stay green.

---

### Task 0: Probe reports enemy counterattacks (baseline on the old code)
- `AttackProbe` counts `CounterattackStarted` per run and blue men lost while a red attack is under way, and prints the means.
- Record the baseline with `--tactic suggest --seeds 24`.

### Task 1: Planner teams
- `public sealed record PlanTeams(IReadOnlyList<UnitId> FireTeam, bool FireTeamStays)`.
- `Propose(..., PlanEdits? edits = null, PlanTeams? teams = null)`:
  - the given men are the fire team, the rest are the strike team
  - with `FireTeamStays`, each fire spot is the man's own place, there is no fire route, and the plan is blind when none of them sees the aim
- Tests:
  - `Teams_Given_FireTeamStaysPut`
  - `Teams_Given_BlindWhenThePostCannotSee`
  - `Teams_Given_StrikeIsTheRest_FormUpOffTheirLine`

### Task 2: Order shout
- `PlannedAttackOrder(..., bool Shout = false)`. When it starts, `CounterattackStarted(tick, side, order unit, his position, plan target)`.
- Test: `PlannedAttackTests.Shout_WhenOrderedSo`.

### Task 3: The commander plans
- `EnemyAiSpec(..., string? Tactic = null)` and the loader `enemy_ai.tactic`. Content test: the id exists.
- `EnemyCommander(..., IReadOnlyList<TacticDef>? tactics = null)` and `MissionScenario.Create(..., tactics)`.
  - Tactic: the named one, else `fire_and_movement`, else the first.
  - Team split as before (`holding` and `going`). The plan's fire team is `holding` and stays put. Without `holding`, the planner's own split is used.
- Shout, attack-under-way and cooldown work through `sim.PlannedAttacks`. A refused plan rests that target.
- The supporting area fire goes: the fire team's `Distribute` does it now.
- Tests (`EnemyCommanderTests`, adapted):
  - `RedAttack` is the red `PlannedAttack`. Members include the fire team, and assertions use `FireTeam` and `StrikeTeam`.
  - new: `NoPlanPossible_TargetRested`, `AfterAHalt_TheyGoBackToTheirPost`, `WithoutTactics_NoCounterattack`
  - removed: `ThePostStopsItsAreaFire_OnceTheTargetShowsAgain`, because the fire team keeps the position down until the attack ends (ruling)

### Task 4: Remove the old attack
- Delete `AttackOrder`, `CounterattackOrder`, `AttackGroup`, `AttackPlanner`, `Unit.AttackGroupId`, and the Simulation update, apply and list for them, plus the StateHash part, the SoldierBrain checks and `OrderQueue`.
- `GameSession.OrderAttack` without tactics fires.
- The probe's `legacy` option goes.
- Tests:
  - `AttackTests` deleted (its planner counterparts are in `PlannedAttackTests`)
  - `ExperienceAndProneTests` experience case moved to a planned attack
  - client tests adapted

### Task 5: Measure and finish
- Probe, 24 seeds, compared with Task 0. Record it in the spec.
- README, final review, merge, push, memory.
