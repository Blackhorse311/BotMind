# ADR-003: Original Code Approach

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture | Security

---

## Context

We are building a mod that provides similar functionality to two existing mods:
- **LootingBots** by Skwizzy (MIT License)
- **QuestingBots** by DanW (MIT License)

The user wants to create this mod without copying the original authors' code.

### Current State
- Both original mods are available on SPT Forge
- Source code is publicly available on GitHub
- Both use MIT License (permissive, allows derivative works)

### Requirements
- Implement full functionality of both mods
- Do not copy code from original authors
- Write original implementation from scratch

### Constraints
- Can reference original mods to understand *what* they do (functionality)
- Cannot copy *how* they implement it (code)
- Must understand SPT/EFT APIs independently

---

## Decision

**We will write all code from scratch, using the original mods only as functional reference (understanding what features to implement, not how to implement them).**

---

## Options Considered

### Option 1: Original Code from Scratch

Study the original mods to understand functionality, then implement our own solution without copying code.

**Pros:**
- Full ownership of codebase
- No licensing concerns (even though MIT allows copying)
- Opportunity to improve on original designs
- Better understanding of the code we maintain
- Respects original authors' work ethically

**Cons:**
- More development time
- Risk of missing edge cases the originals handle
- Need to reverse-engineer functionality from behavior

**Effort:** High

---

### Option 2: Fork and Modify

Fork the original repositories and update them for SPT 4.0.11.

**Pros:**
- Faster initial development
- Benefit from years of bug fixes
- MIT License explicitly allows this

**Cons:**
- User explicitly doesn't want this approach
- Inherit technical debt
- Harder to understand code we didn't write
- May miss opportunity to improve architecture

**Effort:** Medium

---

### Option 3: Partial Copy with Attribution

Copy some code (MIT allows it), write other parts fresh.

**Pros:**
- Balanced approach
- Can copy well-tested algorithms

**Cons:**
- User explicitly doesn't want this
- Mixed codebase harder to maintain
- Attribution requirements

**Effort:** Medium

---

## Rationale

### Key Factors
1. **User's explicit requirement** - User said "I want to see if we can make them from scratch using our own coding and not copying theirs at all"
2. **Ethical respect** - Even with MIT license, building original work respects the original authors
3. **Learning opportunity** - Writing from scratch ensures deep understanding
4. **Improvement opportunity** - Can design better architecture knowing what the originals do

### Trade-offs Accepted
- Longer development time
- May need to rediscover solutions to problems the originals already solved
- Risk of missing subtle features

---

## Consequences

### Positive
- Complete ownership and understanding of codebase
- Freedom to architect as we see fit
- No concerns about code provenance
- Opportunity to improve on original designs

### Negative
- Longer development time
- May miss edge cases
- Need to thoroughly test all scenarios

### Risks
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Missing functionality | Medium | Medium | Thorough analysis of original mod features before coding |
| Subtle bugs originals fixed | Medium | Low | Extensive testing, community feedback |
| Longer development | High | Low | Phased approach, prioritize core features |

---

## Implementation

### Approach
1. **Document functionality** - List all features from original mods by observing behavior and reading documentation
2. **Design independently** - Create our own architecture for implementing those features
3. **Implement from scratch** - Write all code ourselves
4. **Test thoroughly** - Verify we match expected behavior

### What We CAN Reference
- Original mod documentation and feature lists
- Original mod configuration options (to understand what's configurable)
- SPT/EFT API documentation and examples
- BigBrain/Waypoints documentation

### What We Will NOT Reference
- Original mod source code implementation details
- Original mod's specific algorithms or code patterns

---

## Related Decisions

- ADR-001: Combined Mod Architecture

---

## References

- https://forge.sp-tarkov.com/mod/812/looting-bots (for feature understanding)
- https://forge.sp-tarkov.com/mod/1109/questing-bots (for feature understanding)

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal |
| 2026-01-30 | Blackhorse311 | Accepted |
