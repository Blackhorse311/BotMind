# BotMind Research Status

This document tracks what code extracts we have and what we still need for each module.

## Research Complete!

We found the source code for BigBrain, Waypoints, and SAIN in the KeepStartingGear4 research folder:
- `I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\research\mods\SPT-BigBrain-master`
- `I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\research\mods\SPT-Waypoints-master`
- `I:\spt-dev\Blackhorse311.KeepStartingGear4\Blackhorse311.KeepStartingGear\research\mods\SAIN-master`

---

## BigBrain API (COMPLETE)

Key classes analyzed:

### BrainManager (`Brains/BrainManager.cs`)
- `AddCustomLayer(Type, List<string> brainNames, int priority)` - Register custom layer
- `RemoveLayer(string layerName, List<string> brainNames)` - Remove existing layer
- `IsCustomLayerActive(BotOwner)` - Check if custom layer active
- `GetActiveLayer(BotOwner)` - Get current active layer
- Layer IDs start at 9000

### CustomLayer (`Brains/CustomLayer.cs`)
Abstract base class for custom brain layers:
```csharp
public abstract class CustomLayer
{
    public BotOwner BotOwner { get; }
    public int Priority { get; }
    public Action CurrentAction { get; set; }

    public abstract string GetName();
    public abstract bool IsActive();
    public abstract Action GetNextAction();
    public abstract bool IsCurrentActionEnding();

    public virtual void Start() { }
    public virtual void Stop() { }
    public virtual void BuildDebugText(StringBuilder sb) { }
}
```

### CustomLogic (`Brains/CustomLogic.cs`)
Abstract base class for logic within layers:
```csharp
public abstract class CustomLogic
{
    public BotOwner BotOwner { get; }

    public virtual void Start() { }
    public virtual void Stop() { }
    public abstract void Update(ActionData data);
    public virtual void BuildDebugText(StringBuilder sb) { }
}
```

---

## SAIN Interop API (COMPLETE)

Key methods from `Plugin/External.cs`:

| Method | Purpose |
|--------|---------|
| `CanBotQuest(BotOwner, Vector3, float)` | Check if bot can safely quest |
| `TimeSinceSenseEnemy(BotOwner)` | Time since enemy detected |
| `IgnoreHearing(BotOwner, bool, bool, float)` | Temporarily disable hearing |
| `GetPersonality(BotOwner)` | Get bot's SAIN personality |
| `ExtractBot(BotOwner)` | Force bot to extract |
| `TrySetExfilForBot(BotOwner)` | Assign extraction point |
| `GetExtractedBots(List<string>)` | Get list of extracted bots |
| `IsPathTowardEnemy(NavMeshPath, BotOwner, float, float)` | Check if path leads to enemy |

SAIN plugin GUID: `me.sol.sain`

---

## Waypoints API (ANALYZED)

From `WaypointsPlugin.cs`:
- Main purpose is NavMesh improvements and door linking
- Patches bot pathfinding via `FindPathPatch`
- Doesn't expose public API methods - works via patches
- We can use Unity's `NavMesh` API directly for position queries

Key Unity NavMesh methods:
- `NavMesh.SamplePosition(Vector3, out NavMeshHit, float, int)` - Find valid NavMesh position
- `NavMesh.CalculatePath(Vector3, Vector3, int, NavMeshPath)` - Calculate path

---

## Current Extract Inventory

### From 4.0.7 (May Need Refresh for 4.0.11)

| Category | Extract File | Relevance |
|----------|-------------|-----------|
| **Bot Core** | BotOwner 4.0.7 | HIGH - Core bot class |
| **Bot Core** | BotSpawner 4.0.7 | HIGH - Bot spawning |
| **Bot Core** | StandardBotBrain 4.0.7 | HIGH - Brain system |
| **Bot Core** | BotsController 4.0.7 | HIGH - Bot management |
| **Bot Core** | BotsGroup 4.0.7 | MEDIUM - Group behavior |
| **Movement** | BotMover Class 4.0.7 | HIGH - Bot movement/navigation |
| **Movement** | BotOwner.Mover 4.0.7 | HIGH - Movement commands |
| **Movement** | BotOwner.StopMove 4.0.7 | MEDIUM - Stop movement |
| **Movement** | PatrollingData 4.0.7 | MEDIUM - Patrol behavior |
| **Spawning** | BotSpawnerClass 4.0.7 | HIGH - Spawn implementation |
| **Spawning** | BotCreationDataClass 4.0.7 | HIGH - Bot creation data |
| **Spawning** | IBotCreator 4.0.7 | HIGH - Bot creator interface |
| **Spawning** | GenerateBotsRequestData 4.0.7 | MEDIUM - Spawn requests |
| **Memory** | BotMemoryClass 4.0.7 | MEDIUM - Bot memory |
| **Health** | ActiveHealthController 4.0.7 | HIGH - Health management |
| **Health** | PlayerHealthController 4.0.7 | HIGH - Player health |
| **Health** | BotMedicine 4.0.7 | HIGH - Bot healing |
| **Health** | Bleeding 4.0.7 | MEDIUM - Status effects |
| **Health** | Fracture 4.0.7 | MEDIUM - Status effects |
| **Health** | EBodyPart 4.0.7 | LOW - Body part enum |
| **Inventory** | EFT.InventoryLogic 4.0.7 | HIGH - Inventory system |
| **Inventory** | BotInventoryContainerServices 4.0.7 | HIGH - Bot inventory |
| **Inventory** | BackpackItem Class 4.0.7 | MEDIUM - Backpack items |
| **Inventory** | TacticalVest 4.0.7 | MEDIUM - Vest items |
| **Inventory** | CompoundItem 4.0.7 | MEDIUM - Compound items |
| **Inventory** | LootContainer Setting 4.0.7 | HIGH - Loot containers |
| **Settings** | BotGlobalCoreSettings 4.0.7 | MEDIUM - Bot settings |
| **Settings** | BotGlobalMindSettings 4.0.7 | MEDIUM - AI mind settings |
| **Settings** | BotGlobalPatrolSettings 4.0.7 | MEDIUM - Patrol settings |
| **Navigation** | ZoneLeaveControllerClass 4.0.7 | MEDIUM - Zone management |

### From 4.0.8

| Category | Extract File | Relevance |
|----------|-------------|-----------|
| **Inventory** | StashGridClass 4.0.8 | MEDIUM - Grid system |
| **Inventory** | LocationInGrid 4.0.8 | MEDIUM - Grid positions |
| **Inventory** | item.CurrentAddress 4.0.8 | LOW - Item addresses |

### From 4.0.11 (Current Version!)

| Category | Extract File | Relevance |
|----------|-------------|-----------|
| **World** | Gameworld Class 4.0.11 | HIGH - Game world access |
| **World** | ClientApplication 4.0.11 | MEDIUM - App context |
| **Player** | PlayerClass 4.0.11 | HIGH - Player class |
| **Player** | LocalPlayer 4.0.11 | HIGH - Local player |
| **Player** | Profiles 4.0.11 | MEDIUM - Profile data |
| **Insurance** | InsuranceController 4.0.11 | LOW - Not needed |
| **Trader** | ITraderInteractions 4.0.11 | LOW - Not needed |

---

## Bot Loot-Related Classes (COMPLETE!)

User provided extracts on 2026-01-30 in `research/extracts/`:

### BotItemTaker (`SPT BotItemTaker Extract 4.0.11.txt`)
Key methods for picking up loose items:
- `Activate()` - Subscribes to `GameWorld.OnThrowItem` and `OnTakeItem` events
- `RefreshClosestItems()` - Scans `ThrownItems` for nearby loot
- `method_0(Item)` - Finds slot/grid space for item (`FindSlotToPickUp`, `FindGridToPickUp`)
- `method_6(Vector3)` - Navigates to item using `BotOwner.GoToPoint()`
- `method_9(LootItem)` - Takes the item using `InteractionsHandlerClass.Move()`
- Uses `InteractionsHandlerClass.QuickFindAppropriatePlace()` for placement

### BotLootOpener (`SPT BotLootOpener Extract 4.0.11.txt`)
Simple container interaction:
- `Interact(LootableContainer, EInteractionType)` - Opens container door
- Uses `Player.CurrentManagedState.StartDoorInteraction()`

### BotDeadBodyWork (`SPT BotDeadBodyWork Extract 4.0.11.txt`)
Full corpse looting state machine:
- `ELookState` enum: Initial, LootWeapon, CheckBackpack, LootAllCalculations, LootAllItemsMoving, DropBodyVest, DropBodyBackpack, Exit
- `method_4()` - Finds nearby bodies via `BotsGroup.DeadBodiesController.BodiesByGroup()`
- Uses `body.IsFreeFor(BotOwner)` to check if body is being looted
- Uses `body.IsOnNavMesh` to verify reachability
- Loots weapons from slots: FirstPrimaryWeapon, SecondPrimaryWeapon, Holster
- Loots backpack if bot doesn't have one
- Uses `item.Template.CreditsPrice` for value-weighted item selection
- Uses `InteractionsHandlerClass.Move()` for transferring items

---

### Priority 5: Friendly Bot Spawning (MEDIUM)

For MedicBuddy, we need to understand how to spawn friendly bots.

**Need:**
- [ ] How to spawn bots with specific side (friendly to player)
- [ ] How to set bot group membership
- [ ] `EPlayerSide` enum values
- [ ] `WildSpawnType` enum (we have partial from StandardBotBrain)

**What we might already have:**
- BotSpawner has `SpawnBotByTypeForce` method
- BotCreationDataClass has spawn parameters
- BotsGroup shows group membership

---

### Priority 6: Refresh Key 4.0.7 Extracts (MEDIUM)

Some 4.0.7 extracts should be refreshed to 4.0.11 to catch any API changes:

**Recommend refreshing:**
- [ ] BotOwner (CRITICAL - core class)
- [ ] BotSpawner (HIGH - spawning may have changed)
- [ ] BotMover (HIGH - movement may have changed)

---

## Module-Specific Research Status

### Looting Module

| Need | Have? | Source |
|------|-------|--------|
| Bot inventory access | Partial | BotOwner properties |
| Item value lookup | No | Need from Assembly-CSharp |
| Container interaction | No | Need BotLootOpener |
| Corpse interaction | No | Need BotDeadBodyWork |
| Item pickup | No | Need BotItemTaker |
| BigBrain layer creation | **YES** | SPT-BigBrain-master source |

### Questing Module

| Need | Have? | Source |
|------|-------|--------|
| Bot navigation | Partial | BotMover |
| NavMesh pathfinding | **YES** | Unity NavMesh API |
| Zone/objective data | Partial | ZoneLeaveController |
| BigBrain layer creation | **YES** | SPT-BigBrain-master source |
| Bot spawning | Yes | BotSpawner |

### MedicBuddy Module

| Need | Have? | Source |
|------|-------|--------|
| Friendly bot spawning | Partial | BotSpawner |
| Bot group management | Partial | BotsGroup |
| Bot healing ability | Partial | BotMedicine |
| Player health access | Yes | PlayerHealthController |
| BigBrain custom behavior | **YES** | SPT-BigBrain-master source |
| Bot despawning | Partial | BotOwner.LeaveData |
| SAIN integration | **YES** | SAIN-master External.cs |

---

## Implementation Progress

### Completed
- [x] BigBrain API research
- [x] SAIN Interop API research
- [x] Waypoints analysis
- [x] SAINInterop.cs created with all needed methods
- [x] LootingLayer framework and logic classes
- [x] QuestingLayer framework and logic classes
- [x] MedicBuddyController with spawning and team management
- [x] MedicBuddy healing logic with player health restoration
- [x] All navigation logic using BotOwner.GoToPoint()
- [x] NavMesh validation for all movement targets

### Module Status (Updated 2026-02-17)

#### Looting Module: COMPLETE
- LootingLayer.cs - Layer management with loot priority
- LootFinder.cs - Item/container/corpse scanning
- LootContainerLogic.cs - Container looting with item value filtering
- LootCorpseLogic.cs - Corpse looting with equipment checking
- PickupItemLogic.cs - Loose item pickup

#### Questing Module: COMPLETE
- QuestingLayer.cs - Layer with QuestingActionData passing
- QuestManager.cs - Quest objective management
- GoToLocationLogic.cs - Navigation with stuck detection
- ExploreAreaLogic.cs - Random waypoint exploration
- ExtractLogic.cs - SAIN-integrated extraction
- FindItemLogic.cs - Area search with container scanning
- PlaceItemLogic.cs - Item placement at quest locations

#### MedicBuddy Module: COMPLETE
- MedicBuddyController.cs - Team spawning, state machine, healing, hostile bot detection, medical gear equip
- MedicBuddyMedicLayer.cs - Medic bot AI layer (uses RallyPoint for distance check)
- MedicBuddyShooterLayer.cs - Shooter bot AI layer
- MedicBuddyNotifier.cs - Toast notifications with EN/RU variants (5 per event)
- MedicBuddyAudio.cs - Voice line playback (60 voice lines)
- MoveToPatientLogic.cs - Navigation to player/rally point
- HealPatientLogic.cs - Healing behavior coordination
- DefendPerimeterLogic.cs - Defensive positioning around rally point
- FollowTeamLogic.cs - Retreat behavior

### Testing Status

**Build Status:** All modules compile successfully against SPT 4.0.12 (verified 2026-02-17, 0 errors, 0 warnings)

**Unit Tests:** 82 passing tests in `src/tests/`:
- `Core/BlacklistTests.cs` - Thread-safe blacklist with stable IDs
- `Core/LootTargetTests.cs` - Priority calculation validation
- `Core/QuestObjectiveTests.cs` - Objective management
- `Core/MedicBuddyStateTests.cs` - State machine transitions
- `Core/ReflectionCacheTests.cs` - PropertyInfo cache thread safety
- `Core/ItemValueCalculationTests.cs` - Corpse value estimation
- `Core/TimerCleanupTests.cs` - Periodic cleanup logic
- `Core/ErrorHandlingTests.cs` - Exception handling patterns

**Plugin Implementation:**
- BotMindPlugin.cs fully implemented with:
  - BigBrain layer registration for all 3 modules
  - GameStartedPatch for module initialization
  - GameWorldDisposePatch for cleanup

**Code Reviews:** 9 reviews completed, 151 issues found, 149 fixed
- See `docs/CODE_REVIEW_FIXES.md` for complete history

**Runtime Testing:** First successful MedicBuddy test completed 2026-02-17
- See `docs/FEATURE_REQUIREMENTS.md` "Runtime Test Results" section for details

---

## Notes

- The 4.0.7 extracts are mostly still valid for 4.0.12
- BigBrain, Waypoints, and SAIN source code found in KeepStartingGear4 research folder
- SAIN integration is required per ADR-004
- All bot spawning uses existing EFT BotSpawner APIs
- Health restoration uses ActiveHealthController.ChangeHealth()
- SPT 4.0.12 compatibility verified: all EFT APIs unchanged from 4.0.11, server NuGet packages updated
- SameSideIsFriendly v2.0.0 compatibility verified: no patch conflicts, CheckTeamHostility handles edge cases

### EFT Inventory Item Creation API (Discovered 2026-02-17)

For adding items to bot inventories at runtime:

```csharp
// Create item from template ID
Item item = Singleton<ItemFactoryClass>.Instance.CreateItem(MongoID.Generate(), templateId, null);

// Find inventory space (checks vest, pockets, backpack)
ItemAddress location = inventoryController.FindGridToPickUp(item);

// Move item into inventory
var moveResult = InteractionsHandlerClass.Move(item, location, inventoryController, true);
if (moveResult.Succeeded)
{
    inventoryController.RunNetworkTransaction(moveResult.Value, null);
}
```

Source: `pitvenin-friendlypmc` mod (`research/mods/pitvenin-friendlypmc-e1f5e8c2b463/client/Actions/FollowerTakeLoot.cs`)

**Known issue:** This may fail silently when called in `OnBotCreated` - the bot's inventory may not be fully initialized yet. Need to verify timing or add a delay.
