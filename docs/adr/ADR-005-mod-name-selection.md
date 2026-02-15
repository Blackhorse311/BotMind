# ADR-005: Mod Name Selection

**Status:** Accepted
**Date:** 2026-01-30
**Deciders:** Blackhorse311, Claude
**Categories:** Architecture

---

## Context

We need to select a name for the combined mod that will encompass:
- Bot looting behavior (from LootingBots functionality)
- Bot questing/objectives (from QuestingBots functionality)
- MedicBuddy feature (player-summoned medical team)

The name should reflect the mod's purpose of giving bots intelligent behaviors.

### Requirements
- Name should be memorable and descriptive
- Should work with Blackhorse311 author prefix
- Should encompass all three feature sets

---

## Decision

**We will name the mod "BotMind" (full name: Blackhorse311.BotMind or Blackhorse311-BotMind).**

---

## Options Considered

### Option 1: BotMind

The "mind" that drives bot decision-making for looting, questing, and responding to player requests.

**Pros:**
- Encompasses all intelligent bot behaviors
- Short and memorable
- Technical but approachable
- Works well with MedicBuddy inclusion

**Cons:**
- Slightly generic

---

### Option 2: LivingBots

Bots that feel "alive" through purposeful behavior.

**Pros:**
- Evocative name
- Describes the goal well

**Cons:**
- Doesn't clearly convey MedicBuddy feature
- Longer name

---

### Option 3: SmartBots

Simple, describes intelligent behavior.

**Pros:**
- Very clear meaning

**Cons:**
- Too generic
- "Smart" is overused in tech

---

### Option 4: TacticalBots

Strategic and objective-based behavior.

**Pros:**
- Military theme fits Tarkov

**Cons:**
- Doesn't convey looting or healing aspects

---

## Rationale

### Key Factors
1. **User preference** - User said "I like BotMind"
2. **Scope expansion** - With MedicBuddy included, "BotMind" works because it's about the decision-making "mind" of bots
3. **Versatility** - Can add more intelligent behaviors under this umbrella in the future

---

## Consequences

### Naming Convention
- **Mod folder name:** `Blackhorse311-BotMind`
- **BepInEx plugin GUID:** `com.blackhorse311.botmind`
- **Server mod namespace:** `Blackhorse311.BotMind.Server`
- **Client mod namespace:** `Blackhorse311.BotMind`

---

## Related Decisions

- ADR-001: Combined Mod Architecture
- ADR-006: MedicBuddy Feature Inclusion

---

## Changelog

| Date | Author | Change |
|------|--------|--------|
| 2026-01-30 | Claude | Initial proposal |
| 2026-01-30 | Blackhorse311 | Accepted "BotMind" |
