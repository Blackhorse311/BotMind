# BotMind

BotMind is a combined bot AI enhancement mod for SPT (Single Player Tarkov) 4.0.12. It unifies the functionality of looting and questing behaviors into a single, compatible package while adding the MedicBuddy feature for player-summoned medical teams. The mod integrates with BigBrain for AI brain layers and optionally with SAIN for combat state awareness.

## Tech Stack

| Layer | Technology | Version | Purpose |
|-------|------------|---------|---------|
| Runtime (Client) | .NET Standard | 2.1 | BepInEx plugin compatibility |
| Runtime (Server) | .NET | 9.x | SPT server mod |
| Language | C# | 12.x | Game modding with Unity interop |
| Framework | BepInEx | 5.x | Plugin loading and configuration |
| AI Framework | BigBrain | 1.4.x | Custom brain layer registration |
| AI Integration | SAIN | 3.x | Combat state and extraction (optional) |
| Build | MSBuild | Latest | .NET SDK project builds |
| Testing | xUnit | 2.9.x | Unit testing with FluentAssertions and Moq |

## Quick Start

```bash
# Prerequisites
# - .NET 9 SDK installed
# - SPT 4.0.12 installation
# - SPT_PATH environment variable set to SPT folder

# Set environment variable (PowerShell)
$env:SPT_PATH = "H:\SPT"

# Build client plugin
dotnet build src/client/Blackhorse311.BotMind.csproj

# Build server mod
dotnet build src/server/Blackhorse311.BotMind.Server.csproj

# Run tests
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj

# Build outputs are auto-copied to SPT folders when SPT_PATH is set:
# - Client: BepInEx/plugins/Blackhorse311-BotMind/
# - Server: SPT/user/mods/Blackhorse311-BotMind/
```

## Project Structure

```
Blackhorse311.BotMind/
├── src/
│   ├── client/                          # BepInEx client plugin
│   │   ├── Blackhorse311.BotMind.csproj # Client project (netstandard2.1)
│   │   ├── BotMindPlugin.cs             # Plugin entry point, layer registration
│   │   ├── Configuration/
│   │   │   └── BotMindConfig.cs         # BepInEx config bindings
│   │   ├── Interop/
│   │   │   └── SAINInterop.cs           # SAIN integration via reflection
│   │   ├── Modules/
│   │   │   ├── Looting/                 # Bot looting behavior
│   │   │   │   ├── LootingLayer.cs      # BigBrain layer
│   │   │   │   ├── LootFinder.cs        # Loot detection
│   │   │   │   ├── LootCorpseLogic.cs   # Corpse looting state
│   │   │   │   ├── LootContainerLogic.cs# Container looting
│   │   │   │   └── PickupItemLogic.cs   # Loose item pickup
│   │   │   ├── Questing/                # Bot questing behavior
│   │   │   │   ├── QuestingLayer.cs     # BigBrain layer
│   │   │   │   ├── QuestManager.cs      # Objective management
│   │   │   │   ├── GoToLocationLogic.cs # Navigation
│   │   │   │   ├── ExploreAreaLogic.cs  # Area exploration
│   │   │   │   ├── ExtractLogic.cs      # SAIN extraction
│   │   │   │   ├── FindItemLogic.cs     # Item searching
│   │   │   │   └── PlaceItemLogic.cs    # Item placement
│   │   │   └── MedicBuddy/              # Player medical team
│   │   │       ├── MedicBuddyController.cs  # Team spawn and state machine
│   │   │       ├── MedicBuddyMedicLayer.cs  # Medic bot AI
│   │   │       ├── MedicBuddyShooterLayer.cs# Shooter bot AI
│   │   │       ├── MoveToPatientLogic.cs    # Navigate to player
│   │   │       ├── HealPatientLogic.cs      # Healing behavior
│   │   │       ├── DefendPerimeterLogic.cs  # Defensive positions
│   │   │       └── FollowTeamLogic.cs       # Retreat behavior
│   │   └── Patches/                     # Harmony patches (reserved)
│   ├── server/                          # SPT server mod
│   │   ├── Blackhorse311.BotMind.Server.csproj # Server project (net9.0)
│   │   └── BotMindMod.cs                # Server mod callbacks
│   └── tests/                           # Test project
│       └── Blackhorse311.BotMind.Tests.csproj
├── docs/
│   ├── FEATURE_REQUIREMENTS.md          # Detailed feature specs
│   ├── RESEARCH_STATUS.md               # API research notes
│   ├── CODE_REVIEW_FIXES.md             # Review issue tracking
│   ├── TEST_PLAN.md                     # Testing strategy
│   └── adr/                             # Architecture Decision Records
│       ├── ADR-001-combined-mod-architecture.md
│       ├── ADR-002-server-mod-format.md
│       ├── ADR-003-original-code-approach.md
│       ├── ADR-004-sain-compatibility.md
│       ├── ADR-005-mod-name-selection.md
│       └── ADR-006-medicbuddy-feature.md
├── research/                            # Reference code and extracts
└── bin/                                 # Build outputs (empty in repo)
```

## Architecture Overview

BotMind uses a modular architecture with three feature modules that integrate via BigBrain's custom layer system. Each module provides a `CustomLayer` subclass that the BigBrain framework evaluates for activation, and `CustomLogic` subclasses that implement the actual behavior states.

```
┌─────────────────────────────────────────────────────────────────┐
│                        BotMindPlugin                            │
│  (BepInEx entry point, layer registration, lifecycle hooks)    │
└──────────────────────────────┬──────────────────────────────────┘
                               │
           ┌───────────────────┼───────────────────┐
           ▼                   ▼                   ▼
    ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
    │ LootingLayer │     │QuestingLayer│     │MedicBuddy   │
    │ (priority 22)│     │ (priority 21)│     │ (priority 95)│
    └──────┬──────┘     └──────┬──────┘     └──────┬──────┘
           │                   │                   │
           ▼                   ▼                   ▼
    ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
    │ Logic Classes│     │ Logic Classes│     │ Controller  │
    │ (state machines)   │ (state machines)   │ (team mgmt) │
    └─────────────┘     └─────────────┘     └─────────────┘
           │                   │                   │
           └───────────────────┴───────────────────┘
                               │
                               ▼
                    ┌─────────────────┐
                    │   SAINInterop   │
                    │ (optional combat│
                    │  state checks)  │
                    └─────────────────┘
```

### Key Modules

| Module | Location | Purpose |
|--------|----------|---------|
| BotMindPlugin | `src/client/BotMindPlugin.cs` | Plugin entry point, registers BigBrain layers, manages lifecycle |
| BotMindConfig | `src/client/Configuration/` | BepInEx configuration with categorized settings |
| SAINInterop | `src/client/Interop/` | Reflection-based SAIN integration for combat state |
| LootingLayer | `src/client/Modules/Looting/` | Enables bots to loot corpses, containers, loose items |
| QuestingLayer | `src/client/Modules/Questing/` | Enables bots to pursue objectives and extract |
| MedicBuddyController | `src/client/Modules/MedicBuddy/` | Player-summoned medical team with healing, defense, and hostile bot detection |
| BotMindMod | `src/server/BotMindMod.cs` | Server-side mod callbacks (stub for future expansion) |

## Development Guidelines

### Code Style

- File names: PascalCase (`BotMindPlugin.cs`, `LootingLayer.cs`)
- Class/interface names: PascalCase (`BotMindPlugin`, `IModCallbacks`)
- Method names: PascalCase (`GetNextAction`, `TrySummonMedicBuddy`)
- Local variables: camelCase (`botOwner`, `lootTarget`)
- Private fields: `_camelCase` (`_instance`, `_medicBot`)
- Constants: SCREAMING_SNAKE_CASE (`SCAN_INTERVAL`, `HEAL_AMOUNT_PER_TICK`)
- Namespace: `Blackhorse311.BotMind[.Module]`

### Error Handling Pattern

All BigBrain callback methods and Unity callbacks must be wrapped in try-catch:

```csharp
public override bool IsActive()
{
    try
    {
        // Implementation
    }
    catch (Exception ex)
    {
        BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? \"Unknown\"}] IsActive error: {ex.Message}\n{ex.StackTrace}");
        return false; // Fail safe
    }
}
```

### Thread Safety

- Use `volatile` for singleton instances and initialization flags
- Use `lock` for shared mutable state (team lists, state machines)
- Use `Interlocked` for atomic counter operations

### Import Order

1. System namespaces
2. Third-party (BepInEx, Comfort, EFT, DrakiaXYZ.BigBrain)
3. Unity namespaces
4. Project namespaces (`Blackhorse311.BotMind.*`)

## Available Commands

| Command | Description |
|---------|-------------|
| `dotnet build src/client/Blackhorse311.BotMind.csproj` | Build client plugin |
| `dotnet build src/server/Blackhorse311.BotMind.Server.csproj` | Build server mod |
| `dotnet build src/client/Blackhorse311.BotMind.csproj -c Release` | Release build |
| `dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj` | Run unit tests |
| `dotnet clean` | Clean build artifacts |

## Environment Variables

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `SPT_PATH` | Yes* | SPT installation folder for reference DLLs and output copy | `H:\SPT` |

*Required for local development with auto-copy. CI builds use NuGet packages.

## Configuration

All settings are in BepInEx config (`BepInEx/config/com.blackhorse311.botmind.cfg`):

### 1. General
- `Enable Looting` (bool, default: true)
- `Enable Questing` (bool, default: true)
- `Enable MedicBuddy` (bool, default: true)

### 2. Looting
- `Search Radius` (float, 10-200, default: 50m)
- `Minimum Item Value` (int, 0-100000, default: 5000 rubles)
- `Loot Corpses` (bool, default: true)
- `Loot Containers` (bool, default: true)
- `Loot Loose Items` (bool, default: true)

### 3. Questing
- `PMCs Do Quests` (bool, default: true)
- `Scavs Do Quests` (bool, default: false)
- `Quest Priority` (float, 0-100, default: 50)

### 4. MedicBuddy
- `Summon Keybind` (string, default: F10)
- `Cooldown` (float, 60-1800s, default: 300s)
- `Team Size` (int, 2-6, default: 4)
- `PMC Raids Only` (bool, default: true)

## Dependencies

### Client Plugin
- BepInEx 5.x (core, 0Harmony)
- SPT plugins (spt-common, spt-reflection)
- EFT assemblies (Assembly-CSharp, Comfort, Unity modules)
- DrakiaXYZ-BigBrain 1.4.x
- SAIN (soft dependency - optional)

### Server Mod
- SPTarkov.Common 4.0.12
- SPTarkov.DI 4.0.12
- SPTarkov.Server.Core 4.0.12
- SPTarkov.Reflection 4.0.12

## Testing

- **Unit tests:** `src/tests/` using xUnit, FluentAssertions, Moq
- **Coverage:** Coverlet integration
- **Test naming:** `MethodName_Scenario_ExpectedResult`

Note: Integration tests require running SPT environment and are documented in @docs/TEST_PLAN.md.

## Additional Resources

- @docs/FEATURE_REQUIREMENTS.md - Detailed module specifications
- @docs/RESEARCH_STATUS.md - API research and code extracts
- @docs/adr/README.md - Architecture Decision Records
- [SPT Forge](https://forge.sp-tarkov.com/) - SPT mod hub
- [BigBrain Source](https://github.com/DrakiaXYZ/SPT-BigBrain) - AI framework


## Skill Usage Guide

When working on tasks involving these technologies, invoke the corresponding skill:

| Skill | Invoke When |
|-------|-------------|
| dotnet | Configures .NET SDK, build processes, and project structure |
| csharp | Manages C# syntax, patterns, and language features for .NET projects |
| bepinex | Develops BepInEx plugins with hooks, patches, and mod initialization |
| xunit | Writes and manages xUnit tests with FluentAssertions and test helpers |
| harmony | Creates Harmony patches for method interception and runtime modification |
| unity | Integrates Unity APIs, MonoBehaviours, and game engine components |
