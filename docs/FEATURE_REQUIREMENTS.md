# BotMind Feature Requirements

This document details the specific requirements for each module in the BotMind mod.

---

## Module 1: Looting

### Overview
Enables bots to intelligently loot items from the environment - corpses, containers, and loose items.

### Functional Requirements

#### FR-L1: Loot Detection
- [ ] Scan for lootable targets within configurable radius
- [ ] Identify corpses (dead players/bots)
- [ ] Identify containers (crates, bags, etc.)
- [ ] Identify loose items on the ground
- [ ] Calculate item value for prioritization

#### FR-L2: Loot Prioritization
- [ ] Sort targets by value-to-distance ratio
- [ ] Respect minimum item value threshold
- [ ] Prioritize based on bot's inventory needs (ammo, meds, etc.)
- [ ] Skip items bot can't carry (no space)

#### FR-L3: Corpse Looting
- [ ] Navigate to corpse position
- [ ] Open corpse inventory interaction
- [ ] Evaluate items in corpse inventory
- [ ] Take items meeting value threshold
- [ ] Handle inventory space constraints

#### FR-L4: Container Looting
- [ ] Navigate to container
- [ ] Open container interaction
- [ ] Evaluate container contents
- [ ] Take valuable items
- [ ] Close/leave container

#### FR-L5: Loose Item Pickup
- [ ] Navigate to item position
- [ ] Pick up item interaction
- [ ] Add to bot inventory

#### FR-L6: Safety Checks
- [ ] Don't loot while in combat (SAIN integration)
- [ ] Don't loot if enemy was recently seen/heard
- [ ] Abort looting if threat detected
- [ ] Don't loot during extraction

### Configuration Options
- `EnableLooting` (bool): Master toggle
- `LootingSearchRadius` (float): Search distance in meters
- `MinItemValue` (int): Minimum ruble value to consider
- `LootCorpses` (bool): Allow corpse looting
- `LootContainers` (bool): Allow container looting
- `LootLooseItems` (bool): Allow loose item pickup

### BigBrain Integration
- **Layer**: `LootingLayer` - Activates when loot available and safe
- **Logic Classes**:
  - `LootCorpseLogic` - Corpse looting state machine
  - `LootContainerLogic` - Container looting state machine
  - `PickupItemLogic` - Loose item pickup

### Dependencies
- BigBrain (for brain layers)
- SAIN (for combat state checks)
- EFT.InventoryLogic (for item handling)

---

## Module 2: Questing

### Overview
Enables bots to pursue objectives similar to player quests - visiting locations, finding items, etc.

### Functional Requirements

#### FR-Q1: Objective Management
- [ ] Generate objectives based on bot type (PMC vs Scav)
- [ ] Track objective completion
- [ ] Prioritize objectives by importance and time
- [ ] Handle objective failure/timeout

#### FR-Q2: Location Objectives
- [ ] Navigate to specific map locations
- [ ] Mark location as visited when reached
- [ ] Support multiple waypoints for a single objective

#### FR-Q3: Item Objectives
- [ ] Search areas for specific items
- [ ] Pick up quest items when found
- [ ] Track items in bot inventory

#### FR-Q4: Exploration Objectives
- [ ] Visit points of interest
- [ ] Cover specified areas
- [ ] Random patrol patterns

#### FR-Q5: Extraction
- [ ] Determine when to extract (time, loot value, health)
- [ ] Navigate to extraction point
- [ ] Use SAIN extraction system

#### FR-Q6: Safety Integration
- [ ] Pause questing during combat
- [ ] Resume after threat cleared
- [ ] Abort objectives if too dangerous

### Configuration Options
- `EnableQuesting` (bool): Master toggle
- `PMCsDoQuests` (bool): PMC bots quest
- `ScavsDoQuests` (bool): Scav bots quest
- `QuestPriority` (float): Balance with other behaviors

### BigBrain Integration
- **Layer**: `QuestingLayer` - Activates when objectives available
- **Logic Classes**:
  - `GoToLocationLogic` - Navigate to position
  - `FindItemLogic` - Search for items
  - `PlaceItemLogic` - Place quest items
  - `ExploreAreaLogic` - Area exploration
  - `ExtractLogic` - Extraction behavior

### Dependencies
- BigBrain (for brain layers)
- SAIN (for extraction, combat checks)
- Waypoints (for NavMesh navigation)

---

## Module 3: MedicBuddy

### Overview
Player-activated feature that spawns a friendly medical team to heal the player.

### Functional Requirements

#### FR-M1: Summoning
- [ ] Detect keybind press
- [ ] Validate cooldown not active
- [ ] Validate PMC-only restriction (if enabled)
- [ ] Validate player is alive
- [ ] Validate no team already active

#### FR-M2: Team Spawning
- [ ] Calculate spawn position (behind player, out of sight)
- [ ] Spawn 3 shooter bots + 1 medic bot
- [ ] Set bots as friendly to player
- [ ] Apply MedicBuddy brain layers to bots

#### FR-M3: Approach Phase
- [ ] Navigate team to player position
- [ ] Avoid obvious threats during approach
- [ ] Signal arrival to controller

#### FR-M4: Defensive Phase
- [ ] Shooters spread out around player
- [ ] Shooters face outward, watch for threats
- [ ] Engage any enemies that appear
- [ ] Maintain defensive positions

#### FR-M5: Healing Phase
- [ ] Medic moves to player
- [ ] Medic applies healing to player
- [ ] Heal all body parts
- [ ] Stop bleeding, fix fractures
- [ ] Does NOT revive from death

#### FR-M6: Retreat Phase
- [ ] Signal healing complete
- [ ] Team moves away from player
- [ ] Move outside player's vision

#### FR-M7: Despawn
- [ ] Remove bots from the game
- [ ] Clean up resources
- [ ] Reset controller state

#### FR-M8: Hostile Bot Detection
- [ ] Detect when team bots become hostile to the player (e.g., another mod's teamkill mechanic)
- [ ] Immediately despawn hostile team bots to prevent them from attacking the player
- [ ] Skip retreat phase for hostile bots (retreating hostiles would attack en route)
- [ ] Log warning when hostility is detected for debugging

### Configuration Options
- `EnableMedicBuddy` (bool): Master toggle
- `MedicBuddyKeybind` (string): Summon key
- `MedicBuddyCooldown` (float): Seconds between uses
- `MedicBuddyTeamSize` (int): Number of bots (2-6)
- `MedicBuddyPMCOnly` (bool): Only in PMC raids

### BigBrain Integration
- **Layers**:
  - `MedicBuddyShooterLayer` - Shooter defensive behavior
  - `MedicBuddyMedicLayer` - Medic healing behavior
- **Logic Classes**:
  - `DefendPerimeterLogic` - Hold defensive position
  - `MoveToPatientLogic` - Navigate to player
  - `HealPatientLogic` - Perform healing
  - `FollowTeamLogic` - Follow during retreat

### Dependencies
- BigBrain (for brain layers)
- SAIN (for combat AI on shooters)
- BotSpawner (for spawning friendly bots)
- HealthController (for healing player)

---

## Cross-Module Requirements

### CR-1: SAIN Integration
All modules must integrate with SAIN:
- Check combat state before non-combat activities
- Use SAIN extraction for bots
- Respect SAIN's hearing/awareness system

### CR-2: Performance
- Minimize per-frame overhead
- Use configurable scan intervals
- Cache expensive calculations

### CR-3: Logging
- Debug logging for development
- Info logging for key events
- Error logging for failures
- Configurable log levels

### CR-4: Configuration
- BepInEx ConfigFile for all settings
- Runtime reloading where possible
- Sensible defaults

---

## Implementation Status

**Last Updated:** 2026-02-17

| Feature | Status | Notes |
|---------|--------|-------|
| Looting Layer | **COMPLETE** | LootingLayer.cs with full state machine |
| Loot Detection | **COMPLETE** | LootFinder.cs with Physics.OverlapSphereNonAlloc |
| Corpse Looting | **COMPLETE** | LootCorpseLogic.cs with equipment value estimation |
| Container Looting | **COMPLETE** | LootContainerLogic.cs with item filtering |
| Loose Item Pickup | **COMPLETE** | PickupItemLogic.cs with path validation |
| Questing Layer | **COMPLETE** | QuestingLayer.cs with objective management |
| Objective Generation | **COMPLETE** | NavMesh-validated waypoints for PMCs/Scavs |
| Navigation | **COMPLETE** | GoToLocationLogic.cs with stuck detection |
| Exploration | **COMPLETE** | ExploreAreaLogic.cs with random waypoints |
| Extraction | **COMPLETE** | ExtractLogic.cs with SAIN integration |
| MedicBuddy Controller | **COMPLETE** | Full state machine with 7 phases |
| Bot Spawning | **COMPLETE** | BotSpawner integration with friendly assignment |
| Inter-Team Friendship | **COMPLETE** | MakeTeamBotsFriendly() + FinalizeTeamFriendship() cross-links all BotsGroups |
| Healing Interaction | **COMPLETE** | Comprehensive: HP, bleeds, fractures, destroyed limbs via ActiveHealthController |
| Hostile Bot Detection | **COMPLETE** | CheckTeamHostility() detects and despawns hostile team bots |
| CCP Rally Point | **COMPLETE** | Y-key sets Casualty Collection Point; bots converge on fixed position |
| Medic Promotion | **COMPLETE** | TryPromoteMedic() promotes surviving shooter when medic KIA |
| Pre-Summon Validation | **COMPLETE** | Health check, medical supplies check, PMC-only check with notifications |
| Medical Gear on Bots | **IN PROGRESS** | EquipBotWithMedicalGear() implemented but silently failing - see known issues |
| Voice Lines + Notifications | **COMPLETE** | MedicBuddyNotifier (EN/RU) + MedicBuddyAudio with 60 voice lines |
| Unit Tests | **COMPLETE** | 82 passing tests covering all modules |

### Code Review Status

The codebase has undergone 9 comprehensive code reviews:
- 151 issues identified
- 149 issues fixed
- 2 issues skipped (by design)

See `docs/CODE_REVIEW_FIXES.md` for complete review history.

---

## Known Issues (As of 2026-02-17)

### Medical Gear Not Appearing on Bot Corpses
- `EquipBotWithMedicalGear()` is implemented and builds clean, but runtime test showed NO "Equipped X medical items" success log messages
- All error paths were logging at `LogDebug` (hidden). Fixed in Review 9 to use `LogWarning`
- **Next step:** Re-run in SPT and check logs for the specific failure reason
- Likely causes: `ItemFactoryClass` not available at bot creation time, inventory not yet initialized, or `FindGridToPickUp()` finds no space

### Remaining Warning-Level Issues Not Yet Fixed
- **W10:** `FollowTeamLogic` retreat target might not be on NavMesh (low risk - NavMesh.SamplePosition fallback exists)
- **W3:** Audio cleanup doesn't stop playing audio (cosmetic - audio is short, self-terminates)
- **W4:** Promoted medic may not properly transition between BigBrain layers (needs runtime verification)
- **W5:** `_effectsCleared` reset on medic promotion could re-clear effects (harmless - re-clearing is idempotent)

---

## Runtime Test Results (2026-02-17)

### First Successful Runtime Test
- **Environment:** SPT 4.0.12
- **Result:** MedicBuddy feature works end-to-end
- **Details:**
  - Summon keybind detected correctly
  - 4 bots spawned (1 medic + 3 shooters), all captured successfully
  - BigBrain layers attached and activated for all bots
  - Bots navigated to player (2 arrived normally, 2 timed out and were teleported)
  - CCP rally point set and acknowledged
  - Healing completed: negative effects cleared, player fully healed
  - Clean retreat and despawn
  - No GoToPoint NRE spam (EBotState.Active check works)
  - No inter-team hostility (friendship cross-linking works)
- **Issues Found:**
  - Medical gear equip silently failing (fixed error visibility in Review 9)
  - Movement timeout hit for 2 bots (teleport fallback worked correctly)

---

## Next Steps

1. **Fix Medical Gear** - Re-run in SPT to see why EquipBotWithMedicalGear fails (now with visible warnings)
2. **Second Runtime Test** - Verify 9th review fixes work in-game
3. **Looting/Questing Runtime Test** - Test the other two modules in actual gameplay
4. **SAIN Compatibility Testing** - Verify SAIN interop works correctly
5. **Performance Profiling** - Measure actual per-frame overhead
6. **Edge Case Testing** - Multiple summons, mid-raid spawns, player death during healing
7. **Packaging** - LICENSE, README.md, distribution zip for SPT Forge

