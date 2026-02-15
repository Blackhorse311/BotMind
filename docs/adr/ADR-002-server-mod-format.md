# ADR-002: Server Mod Format (.NET 9 DLL)

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture | Infrastructure

---

## Context

SPT 4.0.x introduced a new server architecture. The server was rewritten in C# (.NET 9) replacing the previous TypeScript/Node.js implementation. Server mods can now be written as .NET 9 class libraries.

### Current State
- SPT 4.0.11 uses .NET 9 C# server
- Old TypeScript mods may still work via compatibility layer but are deprecated
- The user's KeepStartingGear4 mod successfully uses the new .NET 9 DLL format

### Requirements
- Must work with SPT 4.0.11
- Should use modern, supported approach
- Should integrate with SPT's dependency injection system

### Constraints
- Must target .NET 9
- Must use SPTarkov.DI.Annotations for dependency injection
- Server DLL goes in `SPT/user/mods/{ModName}/`

---

## Decision

**We will use the modern .NET 9 DLL format for the server-side component.**

---

## Options Considered

### Option 1: .NET 9 DLL (Modern Approach)

Use .NET 9 class library with `[Injectable]` attributes and SPT's DI system.

**Pros:**
- Native integration with SPT 4.0.x server
- Full access to SPT's C# services via DI
- Type-safe, compiled code
- Better IDE support and debugging
- Follows KeepStartingGear4 proven pattern

**Cons:**
- Requires understanding of SPT's DI system
- Less documentation available (newer approach)

**Effort:** Medium

---

### Option 2: TypeScript (Legacy Approach)

Use TypeScript with `@spt/` imports like older mods.

**Pros:**
- More examples available from older mods
- Familiar to those who know the old system

**Cons:**
- Deprecated approach
- May break in future SPT versions
- Less integration with native server
- User explicitly said to use new version

**Effort:** Medium

---

## Rationale

### Key Factors
1. **User requirement** - User explicitly stated "Yes we need to use the new version as that is what SPT 4.0.11 uses"
2. **Future-proofing** - TypeScript approach is deprecated
3. **Proven pattern** - KeepStartingGear4 demonstrates this works well

### Trade-offs Accepted
- Less community documentation (but we have KeepStartingGear4 as reference)
- Need to learn SPT's DI patterns (but this is well-documented in server-mod-examples)

---

## Consequences

### Positive
- Native SPT 4.0.x integration
- Type-safe compiled code
- Consistent with KeepStartingGear4 architecture
- Access to all SPT services via DI

### Negative
- Steeper learning curve for DI patterns
- Fewer community examples to reference

### Risks
| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| DI pattern complexity | Low | Low | Reference KeepStartingGear4 and server-mod-examples |
| Breaking changes in future SPT | Low | Medium | Follow SPT's architectural patterns closely |

---

## Implementation

### Action Items
- [ ] Create .NET 9 class library project for server component
- [ ] Add SPTarkov NuGet package references
- [ ] Implement IOnLoad for initialization
- [ ] Register services with DI container

### Project Structure
```
src/
├── server/           # BepInEx client plugin (.NET 4.7.1)
└── servermod/        # SPT server mod (.NET 9)
```

---

## Related Decisions

- ADR-001: Combined Mod Architecture

---

## References

- https://github.com/sp-tarkov/server-csharp
- https://github.com/sp-tarkov/server-mod-examples
- KeepStartingGear4 project: `I:\spt-dev\Blackhorse311.KeepStartingGear4`

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal |
| 2026-01-30 | Blackhorse311 | Accepted |
