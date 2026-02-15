# ADR-004: SAIN Compatibility Requirement

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture | API

---

## Context

SAIN (SPT AI Overhaul) is a popular mod that significantly enhances bot AI behavior in SPT. It provides:
- Advanced combat AI
- Extraction logic for bots
- Hearing and awareness systems
- Personality systems for bots

Many users run SAIN alongside other bot enhancement mods.

### Current State
- SAIN is installed in user's SPT 4.0.11
- Original QuestingBots had optional SAIN integration
- SAIN provides APIs that other mods can use

### Requirements
- Must work when SAIN is installed
- Should leverage SAIN's features when available
- Must not break if SAIN is not installed

### Constraints
- Cannot create hard dependency (some users don't use SAIN)
- Must handle SAIN API changes gracefully

---

## Decision

**We will require SAIN compatibility, implementing SAIN integration as a core feature rather than optional.**

---

## Options Considered

### Option 1: Required SAIN Integration

Design with SAIN as a required dependency, deeply integrating with its systems.

**Pros:**
- Simpler architecture (no optional code paths)
- Can fully leverage SAIN's capabilities
- User explicitly requires SAIN compatibility

**Cons:**
- Users without SAIN cannot use our mod
- Tied to SAIN's release cycle

**Effort:** Medium

---

### Option 2: Optional SAIN Integration

Support both SAIN and non-SAIN configurations with runtime detection.

**Pros:**
- Wider user base
- Works even if SAIN breaks

**Cons:**
- More complex codebase (two code paths)
- More testing required
- Features may differ based on SAIN presence

**Effort:** High

---

### Option 3: No SAIN Integration

Ignore SAIN entirely, potentially conflicting with it.

**Pros:**
- Simplest implementation
- No external dependencies

**Cons:**
- May conflict with SAIN
- Misses opportunity to leverage SAIN's capabilities
- User explicitly requires SAIN compatibility

**Effort:** Low

---

## Rationale

### Key Factors
1. **User requirement** - User answered "Yes, required" when asked about SAIN compatibility
2. **User has SAIN installed** - SAIN is present in their SPT 4.0.11 installation
3. **SAIN provides valuable features** - Extraction logic, hearing, etc. that we can leverage

### Trade-offs Accepted
- Users without SAIN cannot use our mod (acceptable given user's requirement)
- We become dependent on SAIN's API stability

---

## Consequences

### Positive
- Can leverage SAIN's extraction logic for bot behavior
- Can use SAIN's hearing/awareness for looting decisions
- Consistent behavior with other SAIN-integrated mods
- Simpler codebase without optional paths

### Negative
- Hard dependency on SAIN
- Must update when SAIN updates its API

### Risks
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| SAIN API changes | Medium | Medium | Abstract SAIN interactions behind interface |
| SAIN becomes unmaintained | Low | High | Design to allow future fallback |
| SAIN conflicts | Low | Medium | Test thoroughly with current SAIN version |

---

## Implementation

### Integration Points
- **Extraction Logic**: Use SAIN's bot extraction system for quest completion
- **Hearing System**: Check if bot heard gunfire before looting decisions
- **Combat State**: Don't loot while SAIN has bot in combat
- **Personality**: Potentially adjust looting behavior based on SAIN personality

### Technical Approach
```csharp
// Create SAIN interop class
public class SAINInterop
{
    public static bool IsBotInCombat(BotOwner bot) { ... }
    public static bool HasBotHeardThreat(BotOwner bot) { ... }
    public static bool IsBotExtracting(BotOwner bot) { ... }
}
```

### Action Items
- [ ] Research SAIN's public API
- [ ] Create SAINInterop abstraction layer
- [ ] Document required SAIN version
- [ ] Test with current SAIN version in user's install

---

## Related Decisions

- ADR-001: Combined Mod Architecture

---

## References

- SAIN in user's install: `H:\SPT\BepInEx\plugins\SAIN`
- SAIN documentation (to be researched)

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal |
| 2026-01-30 | Blackhorse311 | Accepted |
