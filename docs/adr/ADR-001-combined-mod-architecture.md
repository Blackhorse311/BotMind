# ADR-001: Combined Mod Architecture

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture

---

## Context

We are building a mod that combines the functionality of two existing SPT mods:
- **LootingBots** (by Skwizzy) - Enables bots to loot items, containers, and corpses
- **QuestingBots** (by DanW) - Enables bots to complete quest objectives and adds dynamic spawning

These mods were originally separate and had compatibility issues when used together (DanW's page explicitly states LootingBots is not compatible).

### Current State
- Both original mods exist for SPT 3.11.x
- Neither has been officially updated for SPT 4.0.11
- User has a cobbled-together version working but wants a clean implementation

### Requirements
- Full functionality from both original mods
- Must work with SPT 4.0.11
- Must be compatible with SAIN
- Must be original code (not copied from original authors)

### Constraints
- SPT 4.0.11 uses new .NET 9 server architecture
- Must integrate with BigBrain for AI brain layers
- Must integrate with Waypoints for NavMesh navigation

---

## Decision

**We will build a single combined mod rather than two separate mods.**

---

## Options Considered

### Option 1: Single Combined Mod

Build one mod that integrates both looting and questing functionality with a unified architecture.

**Pros:**
- Unified codebase, easier to maintain
- Can design looting and questing to work together from the start
- No interop complexity between separate mods
- Single configuration system
- Avoids the compatibility issues the original mods had

**Cons:**
- Larger, more complex single mod
- Users can't use just looting or just questing separately
- More work upfront to design integrated architecture

**Effort:** High

---

### Option 2: Two Separate Mods

Build two separate mods (like the originals) that can optionally work together.

**Pros:**
- Users can choose which functionality they want
- Smaller, more focused codebases
- Follows the pattern of the original mods

**Cons:**
- Need to design and maintain interop layer
- Risk of compatibility issues (like the originals had)
- Duplicate code for shared functionality
- More complex testing matrix

**Effort:** Medium per mod, but higher total

---

### Option 3: Modular Monorepo

Single project with optional modules that can be enabled/disabled.

**Pros:**
- Flexibility for users
- Shared codebase
- Single build/release process

**Cons:**
- Complex configuration system needed
- Still need to handle module interactions
- More testing complexity

**Effort:** High

---

## Rationale

### Key Factors
1. **Compatibility was the main issue** - The original mods had compatibility problems. A unified design solves this by making them work together from the start.
2. **Shared dependencies** - Both mods use BigBrain and interact with bot AI systems. A combined mod avoids duplicate integration work.
3. **User's stated goal** - User explicitly wants to "combine the two into one mod."

### Trade-offs Accepted
- Users cannot use looting-only or questing-only (acceptable since most users want both)
- Larger single codebase to maintain

---

## Consequences

### Positive
- No compatibility issues between looting and questing features
- Single configuration file for all settings
- Unified logging and debugging
- Can optimize interactions (e.g., bot decides whether to loot or continue quest based on unified logic)

### Negative
- All-or-nothing for users
- More complex initial development

### Risks
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Scope creep from combined features | Medium | Medium | Clear feature boundaries, phased implementation |
| Complex debugging | Low | Medium | Good logging, modular internal design |

---

## Implementation

### Action Items
- [ ] Design unified bot behavior state machine
- [ ] Create shared configuration system
- [ ] Implement looting brain layer
- [ ] Implement questing brain layer
- [ ] Design interaction logic between looting and questing

---

## Related Decisions

- ADR-002: Server Mod Format
- ADR-003: Original Code Approach
- ADR-004: SAIN Compatibility

---

## References

- https://forge.sp-tarkov.com/mod/812/looting-bots
- https://forge.sp-tarkov.com/mod/1109/questing-bots

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal |
| 2026-01-30 | Blackhorse311 | Accepted |
