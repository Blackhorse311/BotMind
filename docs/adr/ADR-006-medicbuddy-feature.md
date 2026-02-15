# ADR-006: MedicBuddy Feature Inclusion

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture | API

---

## Context

The user previously worked on a mod concept called "MedicBuddy" that would allow players to summon a friendly medical team during PMC raids. This feature could be integrated into the BotMind mod.

### MedicBuddy Concept

**Trigger:** Player presses configurable keybind (when lacking medical supplies)

**Behavior:**
1. 4-person friendly PMC team spawns just outside player's vision
2. Team composition: 3 "shooters" + 1 "medic"
3. Team runs to player's position
4. Shooters establish defensive perimeter
5. Medic heals the player (but cannot revive from death)
6. After healing, team moves outside player's vision
7. Team despawns

**Restrictions:**
- PMC raids only (not Scav raids)
- Player must be alive (not a revive mechanic)

### Technical Overlap with BotMind

| BotMind Feature | MedicBuddy Requirement | Shared? |
|-----------------|------------------------|---------|
| BigBrain integration | Custom brain layers for defend/heal | Yes |
| Bot spawning | Spawn friendly PMC group | Yes |
| NavMesh navigation | Navigate to/from player | Yes |
| SAIN integration | Combat AI for shooters | Yes |
| Despawn logic | Remove bots after task | Similar to quest completion |

---

## Decision

**We will include MedicBuddy as a module within BotMind, implementing it alongside the core looting and questing features.**

---

## Options Considered

### Option 1: Include in BotMind

Build MedicBuddy as a module within the combined mod.

**Pros:**
- Shares technical foundation with looting/questing
- Single mod to maintain
- Makes BotMind more unique and valuable
- User's existing design work can be leveraged

**Cons:**
- Increases scope
- Different interaction model (player-triggered vs autonomous)
- Potential balance concerns

**Effort:** Medium (foundation shared with other features)

---

### Option 2: Separate Mod Later

Focus on looting/questing first, MedicBuddy as future separate project.

**Pros:**
- Smaller initial scope
- Can release core features sooner

**Cons:**
- Duplicate technical foundation
- Two mods to maintain
- User wants it included

**Effort:** Lower initially, higher total

---

### Option 3: Include but Phase 2

Design architecture for MedicBuddy now, implement after core features work.

**Pros:**
- Ensures architecture supports it
- Phased delivery

**Cons:**
- Delays the feature user is excited about
- May need architecture changes anyway

**Effort:** Medium

---

## Rationale

### Key Factors
1. **Technical synergy** - MedicBuddy uses the same foundation (BigBrain, Waypoints, SAIN, spawning)
2. **User preference** - User explicitly chose to include it
3. **Unique value** - Makes BotMind stand out from just being a "LootingBots + QuestingBots clone"

### Trade-offs Accepted
- Larger scope means longer development time
- Need to balance the feature to prevent abuse

---

## Consequences

### BotMind Module Structure

```
BotMind/
├── Modules/
│   ├── Looting/       # Bot looting behavior
│   ├── Questing/      # Bot quest objectives
│   └── MedicBuddy/    # Player-summoned medical team
```

### Positive
- Comprehensive bot enhancement mod
- Unique feature not available elsewhere
- Shared codebase reduces duplication

### Negative
- Increased complexity
- More testing scenarios
- Balance tuning required

### Risks
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Feature is OP/exploitable | Medium | Medium | Cooldowns, resource costs, limited uses |
| Complex spawn logic | Medium | Low | Reuse patterns from questing spawns |
| AI pathfinding issues | Medium | Medium | Fallback behaviors, stuck detection |

---

## Implementation

### MedicBuddy-Specific Requirements

**Configuration Options:**
- Keybind for summoning
- Cooldown between uses
- Cost (optional: money, items, or free)
- Team size (default 4)
- Heal amount/speed
- Time before despawn

**Brain Layers Needed:**
- `MedicBuddyShooterLayer` - Defensive positioning, combat
- `MedicBuddyMedicLayer` - Navigate to player, heal, follow team

**State Machine:**
```
[Idle] → [Spawned] → [Moving to Player] → [Defending/Healing] → [Retreating] → [Despawning]
```

### Action Items
- [ ] Design MedicBuddy configuration schema
- [ ] Implement friendly bot spawning logic
- [ ] Create defensive positioning behavior
- [ ] Implement healing interaction
- [ ] Create retreat and despawn logic
- [ ] Add cooldown/cost system
- [ ] Test edge cases (player moves, combat interruption, etc.)

---

## Related Decisions

- ADR-001: Combined Mod Architecture
- ADR-004: SAIN Compatibility
- ADR-005: Mod Name Selection

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal based on user's MedicBuddy concept |
| 2026-01-30 | Blackhorse311 | Accepted inclusion in BotMind |
