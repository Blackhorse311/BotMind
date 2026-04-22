# ADR-007: MedicBuddyController Decomposition

## Status

Proposed

## Date

2026-03-08

## Context

`MedicBuddyController.cs` is a 1,907-line class with approximately 15 distinct responsibilities:

- Singleton management and lifecycle
- Keybind detection and modifier handling
- Spawn position calculation (multi-pass with FOV checking)
- Bot spawning via BotSpawner integration
- Bot-spawner event subscription and tracking
- Bot teleportation (movement timeout fallback)
- Friendship management (player-to-bot and bot-to-bot cross-linking)
- Medical item equipping on spawned bots
- State machine with 7 phases (Idle, Spawning, MovingToPlayer, Defending, Healing, Retreating, Despawning)
- Healing logic (HP restoration, bleed clearing, fracture fixing, limb restoration)
- Retreat coordination
- Despawn and resource cleanup
- Team health tracking and wipe detection
- Hostility detection and emergency despawn
- Medic promotion (when medic KIA)
- Rally point (CCP) management
- Player stance manipulation and medical inventory scanning

This violates the Single Responsibility Principle. Any change to spawning, healing, friendship, or the state machine requires modifying this single file.

## Decision

Decompose `MedicBuddyController` into focused classes along the natural seams already visible in the code:

| Extracted Class | Responsibilities | Source Methods |
|----------------|-----------------|----------------|
| `SpawnPositionFinder` | Multi-pass spawn position calculation, FOV checking, NavMesh validation | `CalculateSpawnPosition`, `TrySpawnCandidate`, `IsOutOfPlayerView` |
| `PatientHealer` | HP restoration, negative effect clearing, limb restoration, healing state | `ApplyHealing`, `ClearNegativeEffects`, `RestoreDestroyedLimbs`, `IsPlayerFullyHealed` |
| `TeamFriendshipManager` | Player-to-bot and bot-to-bot friendship cross-linking | `MakeBotFriendlyToPlayer`, `MakeTeamBotsFriendly`, `FinalizeTeamFriendship` |
| `BotEquipmentSetup` | Medical gear equipping on spawned bots | `EquipBotWithMedicalGear`, `TryAddItemToEquipmentGrid` |

`MedicBuddyController` remains the orchestrator, owning the state machine and delegating to these focused classes.

## Consequences

### Positive
- Each class has a single reason to change
- Easier to test spawning logic independently of healing logic
- Easier to read and navigate
- Reduces merge conflict risk when multiple features touch MedicBuddy

### Negative
- More files to manage (4 new classes)
- Risk of introducing regressions in a working, runtime-tested feature
- Cross-class communication adds some complexity

### Risk Mitigation
- Extract one class at a time, building and testing between each
- Start with `SpawnPositionFinder` (most self-contained, fewest state dependencies)
- Full runtime test after each extraction before proceeding to the next

## Review History

This issue was independently identified by all 5 reviewers in the Legends Code Review (Review 11, 2026-03-08):
- Linus Torvalds: "That's not a class, that's an entire application jammed into one file"
- Donald Knuth: "Approximately 15 distinct responsibilities"
- Brian Kernighan: "Flag it for future work"
- Michael Fagan: Categorized as Suggestion (not blocking)
- Sandi Metz: "The seams are already there" (identified the exact extraction points)

All 5 recommended deferring the refactor until after v1.8.0 ships to avoid pre-release risk.
