# Phase 3c: looting — implementation plan

> **For agentic workers:** executed inline (superpowers:executing-plans). Steps are TDD: write the named tests, watch them fail, implement, run `dotnet test NoMansForest.slnx`.

**Goal:** Fallen men (dead, incapacitated, captured) can be looted for magazines, grenades, a weapon and mission papers. You can click a body to loot it, and a man short of ammo loots nearby bodies on his own.

**Architecture:**
- New `LootSystem` in `Nmf.Sim/Combat`, with pure transfer rules and the Looting action countdown.
- `SoldierBrain` walks the looter there and picks bodies for automatic looting.
- Ammo becomes finite through `WeaponDef.SpareMagazines` and `Unit.Magazines`.
- The client adds a body click and the texts.

**Tech Stack:** .NET 10 / C#, xUnit, Godot 4.7 .NET.

**Spec:** `docs/superpowers/specs/2026-09-25-looting-design.md`

## Global Constraints
- Deterministic sim: integer cm/ticks, PCG32 only, units in id order, every new state in `StateHash`.
- `Nmf.Sim` has no dependencies. YAML stays in `Nmf.Content`.
- Order rejection texts: "invalid target", "already looted", "target not reachable", "unit is pinned".
- Constants: `LootRangeCm = 150`, `LootTicks = 40`, `AutoLootRangeCm = 1000`, `LowOnMagazines = 1`, default `spare_magazines = 4`.

## Review Focus
1. **Two men loot the same body in the same tick.** Only the first in id order gets the goods; the second gets nothing and ends his loot.
2. **The looter is killed or captured while looting.** No transfer happens, and his own body stays lootable.
3. **Weapon swap onto a man who is mid-reload or aiming.** The action is reset, and ammo and magazines come from the new weapon.
4. **Auto-loot loop.** A body with nothing useful must never be chosen, so a man never shuttles between bodies.
5. **A captured prisoner as looter or body.** A prisoner can be looted but never loots himself.

---

### Task 1: Finite ammo
**Files:**
- Modify: `WeaponDef.cs` (`int SpareMagazines { get; init; } = 4`, validated ≥ 0)
- Modify: `WeaponLoader.cs` (optional `spare_magazines`)
- Modify: the 6 weapon YAMLs
- Modify: `Unit.cs` (`Magazines`)
- Modify: `Firing.cs` (a reload consumes a magazine; no aiming without ammo)
- Modify: `SoldierBrain.ChooseTarget` (skip when out of ammo)
- Modify: `StateHash`

**Interfaces:**
- Produces: `Unit.Magazines`, `Unit.OutOfAmmo => Weapon is null || (Ammo <= 0 && Magazines <= 0)`

**Tests:**
- `FiringTests.EmptyMagazine_ReloadUsesASpareMagazine`
- `FiringTests.NoSpareMagazines_NoReload_OutOfAmmo`
- `FiringTests.OutOfAmmo_DoesNotAim`
- `WeaponDefTests.SpareMagazines_MustNotBeNegative`
- `WeaponLoaderTests.SpareMagazines_OptionalDefaultsToFour`, `WeaponLoaderTests.SpareMagazines_Read`

### Task 2: Items and transfer rules
**Files:**
- Create: `Nmf.Sim/Combat/Item.cs` (`record Item(string Id, string Name)`)
- Create: `LootSystem.cs` (`Transfer`)
- Modify: `Unit.cs` (`Items`, `Looted`; `Weapon` gets an internal set)
- Modify: `SimEvents.cs` (`UnitLooted`)
- Modify: `StateHash`

**Interfaces:**
- Produces:
  - `LootSystem.Transfer(Simulation, Unit looter, Unit body, long tick, List<SimEvent>)`
  - `LootSystem.HasUsefulLoot(Unit looter, Unit body)`
  - `UnitLooted(long Tick, UnitId Looter, UnitId Body, int Magazines, int Grenades, string? WeaponTaken, IReadOnlyList<Item> Items)`

**Tests** (`LootTests`):
- `SameWeapon_TakesSpareAndFullMagazines`, `PartialMagazine_IsLeft`
- `OtherWeapon_NoAmmoTaken`
- `SameGrenades_Taken`, `OtherGrenadesWhenOwnGone_TakenAndTypeSwitches`, `OtherGrenadesWhileHoldingOwn_Left`
- `EmptyWeapon_SwappedForLoadedOne`
- `Items_MoveToTheLooter`
- `LootedBody_GivesNothingTwice`
- `HasUsefulLoot_FalseForNothingUseful`

### Task 3: Loot order and action
**Files:**
- Modify: `Orders.cs` (`LootOrder`)
- Modify: `CombatTypes.cs` (`CombatAction.Looting`)
- Modify: `Simulation.Apply` (rejections; the other orders clear `LootTarget`), `Simulation.Step` (`LootSystem.Update` after melee)
- Modify: `Unit.LootTarget`
- Modify: `SoldierBrain` (approach at auto pace, start looting within range)
- Modify: `Movement` (holds still while Looting)
- Modify: `MeleeSystem.Start` (clears the loot)

**Interfaces:**
- Produces: `LootOrder(UnitId Unit, UnitId Body)`, `Unit.LootTarget`, `LootSystem.Update(Simulation, long, List<SimEvent>)`

**Tests** (`LootOrderTests`):
- `Rejections`
- `Looter_WalksOver_TakesTwoSeconds_ThenGetsTheGoods`
- `MoveOrder_CancelsTheLoot`
- `MeleeInterruptsLooting`
- `TwoLootersSameTick_OnlyFirstGetsTheGoods`
- `LooterKilledWhileLooting_NoTransfer`
- `Prisoner_CanBeLooted_ButDoesNotLoot`

### Task 4: Automatic looting
**Files:**
- Modify: `SoldierBrain` (`ChooseLootTarget`)

**Tests** (`AutoLootTests`):
- `LowOnAmmo_LootsNearbyBodyWithHisAmmo`
- `EnoughAmmo_DoesNotLoot`
- `UnderFire_DoesNotLoot`
- `EnemyInSight_DoesNotLoot`
- `BodyWithNothingUseful_Ignored`
- `BodyAlreadyTargetedByAFriend_Ignored`

### Task 5: Scenario papers and the real map
**Files:**
- Modify: `SkirmishScenario` (the Soviet leader carries `soviet_orders`)

**Tests:**
- `SkirmishScenarioTests.SovietLeader_CarriesOrders`
- `SkirmishFightTests.FightWithLooting_IsDeterministic`: a loot order on the first body at the end; no exception; deterministic

### Task 6: Client
**Files:**
- Modify: `GameSession` (`BodyAt`, the click, `OrderLoot` goes to the nearest commanded man)
- Modify: `ClickResult.LootOrdered`
- Modify: `UnitStatus` (Looting, Out of ammo)
- Create: `LootText.Describe(UnitLooted, Func<string,string> weaponName)`
- Modify: `GameSession.CarriedPapers`

**Tests:**
- `ClickControlTests.ClickBody_SendsNearestCommandedMan`, `ClickBodyNearLiveEnemy_EnemyWins`, `ClickLootedBody_Moves`
- `UnitStatusTests.Looting`, `UnitStatusTests.OutOfAmmo`
- `LootTextTests`
- `GameSessionTests.CarriedPapers_ListsItemsOfOwnMenInAction`

### Task 7: Godot
- Modify: `UnitView` (bag marker on unlooted bodies shown to the player; loot text float)
- Modify: `Hud` (cards `Ammo a+m`, "Papers: …")
- Modify: `GameRoot` (outcome marker)
- Modify: README (controls and features)
- Verify: build, headless smoke, screenshot.
