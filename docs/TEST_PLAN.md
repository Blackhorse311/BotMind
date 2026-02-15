# BotMind Test Plan

**Version:** 1.1.0
**Date:** 2026-02-15
**Module:** Blackhorse311.BotMind

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Test Environment Setup](#test-environment-setup)
3. [Module Tests](#module-tests)
   - [Core Plugin Tests](#core-plugin-tests)
   - [Looting Module Tests](#looting-module-tests)
   - [Questing Module Tests](#questing-module-tests)
   - [MedicBuddy Module Tests](#medicbuddy-module-tests)
4. [Integration Tests](#integration-tests)
5. [Performance Tests](#performance-tests)
6. [Edge Case Tests](#edge-case-tests)
7. [Regression Tests](#regression-tests)
8. [Test Checklist](#test-checklist)

---

## Prerequisites

### Required Software
- [ ] SPT (Single Player Tarkov) installed and working
- [ ] BepInEx framework installed
- [ ] BigBrain mod installed and configured
- [ ] SAIN mod installed (optional but recommended)

### Required Files
- [ ] `Blackhorse311.BotMind.dll` in `BepInEx/plugins/`
- [ ] Server mod files in `user/mods/Blackhorse311.BotMind.Server/`

### Recommended Tools
- [ ] BepInEx console enabled for log viewing
- [ ] Unity Explorer or RuntimeUnityEditor for debugging (optional)
- [ ] F12 debug overlay enabled in SPT

---

## Test Environment Setup

### Step 1: Fresh Installation
1. Back up your existing SPT installation
2. Install BotMind mod files:
   - Client: `BepInEx/plugins/Blackhorse311.BotMind.dll`
   - Server: `user/mods/Blackhorse311.BotMind.Server/`
3. Verify no conflicting mods are installed

### Step 2: Configuration Baseline
1. Launch SPT once to generate config file
2. Locate config at: `BepInEx/config/Blackhorse311.BotMind.cfg`
3. Set all modules to ENABLED for initial testing:
   ```ini
   [General]
   EnableLooting = true
   EnableQuesting = true
   EnableMedicBuddy = true
   DebugMode = true
   ```

### Step 3: Map Selection for Testing
Recommended test maps (in order):
1. **Factory** - Small map, fast bot spawns, quick iteration
2. **Customs** - Medium complexity, good container variety
3. **Interchange** - Large map with many containers, stress test
4. **Reserve** - Complex extracts, good for extraction testing

---

## Module Tests

### Core Plugin Tests

#### TEST-CORE-001: Plugin Initialization
**Objective:** Verify the plugin loads without errors

**Steps:**
1. Launch SPT client
2. Open BepInEx console (press ` key if enabled)
3. Search for "BotMind" in console output

**Expected Results:**
- [ ] Log shows: `[Info] BotMind is loading...`
- [ ] Log shows: `[Debug] Step 1: Initializing configuration...`
- [ ] Log shows: `[Debug] Step 2: Applying Harmony patches...`
- [ ] Log shows: `[Debug] Step 3: Registering BigBrain layers...`
- [ ] Log shows: `[Info] BotMind loaded successfully!`
- [ ] No error messages related to BotMind

**Failure Indicators:**
- `[Error] BotMind failed to load:`
- Missing assembly errors
- Harmony patch failures

---

#### TEST-CORE-002: Configuration Loading
**Objective:** Verify configuration values are loaded correctly

**Steps:**
1. Modify `BotMind.cfg` with custom values:
   ```ini
   [Looting]
   LootingSearchRadius = 50
   MinItemValue = 5000
   ```
2. Launch game and enter raid
3. Check console for configuration messages

**Expected Results:**
- [ ] Custom values are applied (verify via bot behavior)
- [ ] No configuration parsing errors in log
- [ ] Default values used for unspecified settings

---

#### TEST-CORE-003: SAIN Integration
**Objective:** Verify SAIN interop works when SAIN is present

**Steps:**
1. Ensure SAIN mod is installed
2. Launch game and enter raid
3. Check console for SAIN detection messages

**Expected Results:**
- [ ] Log shows: `SAIN detected and initialized`
- [ ] No SAIN-related errors
- [ ] Bots respect combat state (don't loot during fights)

---

#### TEST-CORE-004: SAIN Graceful Degradation
**Objective:** Verify mod works without SAIN installed

**Steps:**
1. Temporarily remove/disable SAIN mod
2. Launch game and enter raid
3. Observe bot behavior

**Expected Results:**
- [ ] Log shows: `SAIN not detected - using fallback behavior`
- [ ] Mod continues to function
- [ ] No crashes or errors
- [ ] Bots still loot/quest (without combat awareness)

---

### Looting Module Tests

#### TEST-LOOT-001: Corpse Looting
**Objective:** Verify bots loot dead bodies

**Steps:**
1. Enter Factory as PMC
2. Kill a scav and move away (20+ meters)
3. Wait for another scav to approach the corpse
4. Observe behavior for 2-3 minutes

**Expected Results:**
- [ ] Bot navigates to corpse location
- [ ] Bot crouches near corpse
- [ ] Bot spends time "looting" (visible pause)
- [ ] Console shows: `LootCorpseLogic started`
- [ ] Console shows state transitions (MovingToCorpse -> Initial -> LootWeapon, etc.)

**Verification:**
- [ ] Check corpse inventory after bot leaves - items should be missing

---

#### TEST-LOOT-002: Container Looting
**Objective:** Verify bots loot containers (crates, bags, etc.)

**Steps:**
1. Enter Customs raid
2. Position yourself near dorms or warehouse area
3. Wait for bots to spawn and roam
4. Observe bot interaction with containers

**Expected Results:**
- [ ] Bot navigates to closed container
- [ ] Bot plays door/container opening animation
- [ ] Bot pauses to loot items
- [ ] Console shows: `LootContainerLogic started`
- [ ] Console shows: `Opening container` state

**Verification:**
- [ ] Container should be open after bot leaves
- [ ] Some items should be removed from container

---

#### TEST-LOOT-003: Loose Item Pickup
**Objective:** Verify bots pick up valuable loose items

**Steps:**
1. Enter raid with valuable items (bitcoin, GPU, etc.)
2. Drop items on ground in bot patrol area
3. Move away and observe
4. Wait for bot to discover and pick up items

**Expected Results:**
- [ ] Bot navigates to item location
- [ ] Bot plays pickup animation
- [ ] Item disappears from ground
- [ ] Console shows: `PickupItemLogic started`

---

#### TEST-LOOT-004: Minimum Value Filter
**Objective:** Verify bots ignore low-value items

**Steps:**
1. Set `MinItemValue = 10000` in config
2. Drop low-value items (bandages, screws) near bots
3. Drop high-value items (bitcoin) near bots
4. Observe behavior

**Expected Results:**
- [ ] Bots ignore items below threshold
- [ ] Bots pick up items above threshold
- [ ] Console shows target filtering in debug logs

---

#### TEST-LOOT-005: Combat Interruption
**Objective:** Verify bots stop looting when threatened

**Steps:**
1. Wait for bot to begin looting
2. Fire shots near the bot (not at them)
3. Observe reaction

**Expected Results:**
- [ ] Bot stops looting behavior
- [ ] Bot enters combat/alert state
- [ ] Console shows layer deactivation
- [ ] Bot resumes looting after threat passes (if SAIN installed)

---

#### TEST-LOOT-006: Looting Disabled
**Objective:** Verify looting can be disabled via config

**Steps:**
1. Set `EnableLooting = false` in config
2. Enter raid and observe bot behavior
3. Kill a scav near another bot

**Expected Results:**
- [ ] Bots do not approach corpses to loot
- [ ] Bots do not interact with containers
- [ ] Console shows no LootingLayer activation

---

### Questing Module Tests

#### TEST-QUEST-001: PMC Exploration
**Objective:** Verify PMC bots explore the map

**Steps:**
1. Enter Customs as Scav
2. Observe PMC bot movement patterns
3. Track bot over 5-10 minutes

**Expected Results:**
- [ ] PMC bot moves purposefully (not random)
- [ ] Bot visits multiple areas
- [ ] Console shows: `QuestingLayer started`
- [ ] Console shows: `ExploreAreaLogic` state changes

---

#### TEST-QUEST-002: Scav Patrol Behavior
**Objective:** Verify scav bots patrol their areas

**Steps:**
1. Enter Factory as PMC
2. Observe scav movement in office/tunnels area
3. Track patrol patterns

**Expected Results:**
- [ ] Scavs patrol defined areas
- [ ] Scavs return to patrol after disturbances
- [ ] Console shows: `Patrol` objective type

---

#### TEST-QUEST-003: Extraction Behavior
**Objective:** Verify bots attempt extraction late in raid

**Steps:**
1. Enter long raid (Customs, 35+ minutes remaining)
2. Wait until ~10 minutes remaining
3. Observe PMC bot behavior

**Expected Results:**
- [ ] PMC bots begin moving toward extracts
- [ ] Console shows: `ExtractLogic started`
- [ ] Console shows: `Extract` objective priority increase
- [ ] Bots reach extraction point (may or may not extract depending on SAIN)

---

#### TEST-QUEST-004: Questing Disabled
**Objective:** Verify questing can be disabled via config

**Steps:**
1. Set `EnableQuesting = false` in config
2. Enter raid and observe PMC behavior

**Expected Results:**
- [ ] PMC bots use default SPT/SAIN behavior
- [ ] No QuestingLayer activation in console
- [ ] Bots still function normally

---

#### TEST-QUEST-005: PMC Quest Toggle
**Objective:** Verify PMC-specific quest toggle works

**Steps:**
1. Set `PMCsDoQuests = false` in config
2. Set `ScavsDoQuests = true` in config
3. Observe behavior differences

**Expected Results:**
- [ ] PMCs do not show questing behavior
- [ ] Scavs still patrol/explore
- [ ] Console confirms layer activation only for scavs

---

### MedicBuddy Module Tests

#### TEST-MEDIC-001: Summon Keybind
**Objective:** Verify MedicBuddy summon hotkey works

**Steps:**
1. Enter raid as PMC
2. Take some damage (fall damage, barbed wire)
3. Press F10 (default summon key)
4. Observe console and surroundings

**Expected Results:**
- [ ] Console shows: `Spawning MedicBuddy team...`
- [ ] Console shows: `Requested spawn of X MedicBuddy bots`
- [ ] Bots spawn behind player position
- [ ] Console shows: `MedicBuddy team complete`

---

#### TEST-MEDIC-002: Team Movement to Player
**Objective:** Verify medic team navigates to player

**Steps:**
1. Summon MedicBuddy team
2. Remain stationary
3. Observe team approach

**Expected Results:**
- [ ] All team members navigate toward player
- [ ] Team arrives within ~8 meters of player
- [ ] Console shows: `MovingToPlayer` state
- [ ] Console shows transition to `Defending` state

---

#### TEST-MEDIC-003: Healing Functionality
**Objective:** Verify medic heals the player

**Steps:**
1. Take significant damage (multiple body parts)
2. Summon MedicBuddy
3. Wait for team to arrive
4. Observe health restoration

**Expected Results:**
- [ ] Health begins restoring after team arrives
- [ ] Console shows: `MedicBuddy starting healing`
- [ ] Health ticks up periodically (every ~1 second)
- [ ] All damaged body parts receive healing

---

#### TEST-MEDIC-004: Shooter Defense
**Objective:** Verify shooters defend during healing

**Steps:**
1. Summon MedicBuddy team
2. Have a friend (or second account) approach
3. Observe shooter behavior

**Expected Results:**
- [ ] Shooters take defensive positions around player
- [ ] Shooters face outward (perimeter defense)
- [ ] Shooters engage threats that approach
- [ ] Console shows: `DefendPerimeterLogic` active

---

#### TEST-MEDIC-005: Team Retreat
**Objective:** Verify team retreats after healing

**Steps:**
1. Complete a healing session (full health or timeout)
2. Observe team behavior

**Expected Results:**
- [ ] Console shows: `Player fully healed - MedicBuddy retreating`
- [ ] Team begins moving away from player
- [ ] Team despawns after reaching retreat distance
- [ ] Console shows: `Despawning MedicBuddy team`

---

#### TEST-MEDIC-006: Cooldown Enforcement
**Objective:** Verify summon cooldown works

**Steps:**
1. Summon MedicBuddy team
2. Wait for team to despawn
3. Immediately try to summon again
4. Check console for cooldown message

**Expected Results:**
- [ ] Console shows: `MedicBuddy on cooldown: Xs remaining`
- [ ] No new team spawns
- [ ] After cooldown expires, summon works again

---

#### TEST-MEDIC-007: PMC-Only Restriction
**Objective:** Verify MedicBuddy only works for PMCs (if configured)

**Steps:**
1. Set `MedicBuddyPMCOnly = true` in config
2. Enter raid as Scav
3. Attempt to summon MedicBuddy

**Expected Results:**
- [ ] Console shows: `MedicBuddy only available to PMC players`
- [ ] No team spawns

---

#### TEST-MEDIC-008: Custom Keybind
**Objective:** Verify custom keybind configuration

**Steps:**
1. Set `MedicBuddyKeybind = F9` in config
2. Enter raid
3. Press F10 (old key) - should do nothing
4. Press F9 (new key) - should summon

**Expected Results:**
- [ ] Old keybind inactive
- [ ] New keybind triggers summon
- [ ] Console confirms key detection

---

#### TEST-MEDIC-009: MedicBuddy Disabled
**Objective:** Verify MedicBuddy can be disabled

**Steps:**
1. Set `EnableMedicBuddy = false` in config
2. Enter raid and press summon key

**Expected Results:**
- [ ] No response to summon key
- [ ] No console messages about MedicBuddy
- [ ] No bots spawn

---

#### TEST-MEDIC-010: Hostile Bot Detection
**Objective:** Verify MedicBuddy detects and handles team bots that become hostile

**Steps:**
1. Install SameSideIsFriendly mod alongside BotMind
2. Summon MedicBuddy team
3. Wait for team to arrive and begin healing
4. Kill one of the MedicBuddy team members (triggers SSIF teamkill mechanic)
5. Observe remaining team behavior

**Expected Results:**
- [ ] Console shows: `MedicBuddy bot [name] became hostile to player - aborting mission`
- [ ] Remaining team bots are immediately despawned (not retreated)
- [ ] No MedicBuddy bots attack the player
- [ ] Console shows: `Despawning MedicBuddy team`
- [ ] State machine returns to Idle

---

#### TEST-MEDIC-011: Hostile Bot Detection Without SSIF
**Objective:** Verify hostile detection works even without SameSideIsFriendly

**Steps:**
1. Summon MedicBuddy team (without SSIF installed)
2. Use console commands or another method to manually add player as enemy to a team bot
3. Observe behavior

**Expected Results:**
- [ ] CheckTeamHostility detects the hostility change
- [ ] Team is immediately despawned
- [ ] No hostile bots remain active

---

## Integration Tests

#### TEST-INT-001: All Modules Enabled
**Objective:** Verify all modules work together without conflicts

**Steps:**
1. Enable all modules in config
2. Enter extended raid (20+ minutes)
3. Observe various bot behaviors
4. Summon MedicBuddy during raid

**Expected Results:**
- [ ] Bots loot when safe
- [ ] Bots quest/patrol when not looting
- [ ] MedicBuddy functions independently
- [ ] No console errors about module conflicts
- [ ] No performance degradation

---

#### TEST-INT-002: SAIN + BotMind Compatibility
**Objective:** Verify full compatibility with SAIN

**Steps:**
1. Install SAIN with all features enabled
2. Enable all BotMind modules
3. Play extended raid
4. Trigger combat scenarios

**Expected Results:**
- [ ] SAIN combat behavior takes priority
- [ ] BotMind activities resume after combat
- [ ] No conflicting layer activations
- [ ] Smooth transitions between behaviors

---

#### TEST-INT-003: BigBrain Layer Priority
**Objective:** Verify layer priorities work correctly

**Steps:**
1. Create scenario where multiple layers could activate
2. Example: Bot near loot AND in quest area AND being healed

**Expected Results:**
- [ ] Higher priority layer takes precedence
- [ ] Clean transitions between layers
- [ ] No "stuck" behavior

---

## Performance Tests

#### TEST-PERF-001: Memory Stability
**Objective:** Verify no memory leaks during extended play

**Steps:**
1. Monitor game memory usage (Task Manager)
2. Play for 30+ minutes across multiple raids
3. Record memory at start, 15 min, 30 min

**Expected Results:**
- [ ] Memory usage stable (no continuous growth)
- [ ] Memory released between raids
- [ ] No out-of-memory errors

**Acceptable Range:** +/- 200MB variance is normal

---

#### TEST-PERF-002: Frame Rate Impact
**Objective:** Verify minimal FPS impact

**Steps:**
1. Record baseline FPS without BotMind
2. Enable BotMind, record FPS
3. Test in high-bot scenarios (Factory, horde mode)

**Expected Results:**
- [ ] FPS drop < 5% in normal scenarios
- [ ] FPS drop < 15% in stress scenarios
- [ ] No frame stuttering during bot decisions

---

#### TEST-PERF-003: Many Bots Stress Test
**Objective:** Verify performance with many active bots

**Steps:**
1. Configure high bot count (15+ bots)
2. Enter Interchange or Reserve
3. Monitor console for errors
4. Check for lag spikes

**Expected Results:**
- [ ] All bots function correctly
- [ ] No duplicate processing errors
- [ ] Console shows reasonable log volume
- [ ] Game remains playable

---

## Edge Case Tests

#### TEST-EDGE-001: Player Death During MedicBuddy
**Objective:** Verify cleanup when player dies

**Steps:**
1. Summon MedicBuddy team
2. Die before team arrives/finishes

**Expected Results:**
- [ ] Team stops healing attempt
- [ ] Team retreats and despawns
- [ ] No orphaned bots
- [ ] Clean state for next raid

---

#### TEST-EDGE-002: Raid End During Activities
**Objective:** Verify cleanup on raid end

**Steps:**
1. Trigger various bot activities
2. End raid (extract or MIA)
3. Start new raid

**Expected Results:**
- [ ] All state cleared between raids
- [ ] No carryover bugs
- [ ] Fresh initialization in new raid

---

#### TEST-EDGE-003: Bot Death During Looting
**Objective:** Verify handling when looting bot dies

**Steps:**
1. Wait for bot to start looting
2. Kill the bot mid-loot

**Expected Results:**
- [ ] No errors in console
- [ ] Target not permanently locked
- [ ] Other bots can loot same target

---

#### TEST-EDGE-004: Invalid Spawn Position
**Objective:** Verify handling when spawn position invalid

**Steps:**
1. Position yourself in corner/enclosed space
2. Summon MedicBuddy

**Expected Results:**
- [ ] Console shows spawn position search
- [ ] Either finds valid position or gracefully fails
- [ ] No crash or infinite loop
- [ ] Clear error message if spawn fails

---

#### TEST-EDGE-005: Config Hot Reload
**Objective:** Verify config changes mid-session

**Steps:**
1. Start game with default config
2. Alt-tab and modify config file
3. Return to game (may require new raid)

**Expected Results:**
- [ ] New config values applied
- [ ] No crashes from config changes
- [ ] Graceful handling of invalid values

---

## Regression Tests

These tests verify that previously fixed issues remain fixed.

#### TEST-REG-001: Event Listener Cleanup (Issue 1)
**Objective:** Verify no memory leak from event listeners

**Steps:**
1. Summon and dismiss MedicBuddy 10+ times
2. Monitor memory usage
3. Check console for unsubscribe messages

**Expected Results:**
- [ ] Memory stable across summons
- [ ] Console shows: `UnsubscribeFromSpawner` calls
- [ ] No duplicate event firing

---

#### TEST-REG-002: Null Reference Safety (Issues 2, 13)
**Objective:** Verify no null reference crashes

**Steps:**
1. Play through various scenarios
2. Trigger edge cases (dead targets, missing objects)
3. Monitor console for NullReferenceException

**Expected Results:**
- [ ] No NullReferenceException errors
- [ ] Graceful handling of null objects
- [ ] Appropriate fallback behavior

---

#### TEST-REG-003: Thread Safety (Issues 7, 47, 55-59)
**Objective:** Verify no race conditions

**Steps:**
1. Trigger rapid state changes
2. Multiple bots accessing shared resources
3. Quick summon/dismiss cycles

**Expected Results:**
- [ ] No inconsistent state
- [ ] No deadlocks
- [ ] No data corruption
- [ ] Console shows proper lock usage

---

#### TEST-REG-004: Path Failure Handling (Issue 8)
**Objective:** Verify bots handle unreachable targets

**Steps:**
1. Place loot in unreachable location
2. Observe bot attempting to path

**Expected Results:**
- [ ] Bot gives up after reasonable attempts
- [ ] Target blacklisted
- [ ] Bot moves to next target
- [ ] No infinite pathing loops

---

#### TEST-REG-005: Division by Zero (Issue 17)
**Objective:** Verify no math errors in priority calculations

**Steps:**
1. Create zero-distance scenario (item at bot feet)
2. Create zero-value item scenario
3. Check console for math errors

**Expected Results:**
- [ ] No DivideByZeroException
- [ ] Reasonable priority values
- [ ] Smooth operation continues

---

## Test Checklist

### Pre-Release Checklist

#### Critical (Must Pass)
- [ ] TEST-CORE-001: Plugin loads without errors
- [ ] TEST-CORE-004: Graceful degradation without SAIN
- [ ] TEST-LOOT-001: Basic corpse looting works
- [ ] TEST-QUEST-001: Basic exploration works
- [ ] TEST-MEDIC-001: MedicBuddy summon works
- [ ] TEST-MEDIC-003: Healing functionality works
- [ ] TEST-INT-001: All modules work together
- [ ] TEST-PERF-001: Memory stability

#### Important (Should Pass)
- [ ] TEST-CORE-002: Configuration loading
- [ ] TEST-CORE-003: SAIN integration
- [ ] TEST-LOOT-002: Container looting
- [ ] TEST-LOOT-003: Loose item pickup
- [ ] TEST-LOOT-005: Combat interruption
- [ ] TEST-QUEST-003: Extraction behavior
- [ ] TEST-MEDIC-002: Team movement
- [ ] TEST-MEDIC-004: Shooter defense
- [ ] TEST-MEDIC-005: Team retreat
- [ ] TEST-PERF-002: Frame rate impact

#### Nice to Have (Can Defer)
- [ ] TEST-LOOT-004: Minimum value filter
- [ ] TEST-QUEST-002: Scav patrol behavior
- [ ] TEST-MEDIC-006: Cooldown enforcement
- [ ] TEST-MEDIC-007: PMC-only restriction
- [ ] TEST-PERF-003: Many bots stress test
- [ ] All edge case tests
- [ ] All regression tests

---

## Issue Reporting Template

When reporting test failures, use this template:

```
## Test Failure Report

**Test ID:** TEST-XXX-000
**Test Name:** [Name from this document]
**Date:** YYYY-MM-DD
**Tester:** [Your name]

### Environment
- SPT Version: X.X.X
- BotMind Version: X.X.X
- SAIN Version: X.X.X (or N/A)
- Map Tested: [Map name]

### Steps to Reproduce
1. Step one
2. Step two
3. Step three

### Expected Result
[What should have happened]

### Actual Result
[What actually happened]

### Console Log
```
[Paste relevant console output here]
```

### Screenshots/Video
[Attach if applicable]

### Additional Notes
[Any other relevant information]
```

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Developer | | | |
| QA Tester | | | |
| Release Manager | | | |

---

**Document Version History**

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0.0 | 2026-02-01 | Claude | Initial test plan creation |
| 1.1.0 | 2026-02-15 | Claude | Added TEST-MEDIC-010/011 for hostile bot detection; updated SPT target to 4.0.12 |
