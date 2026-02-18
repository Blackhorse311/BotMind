# BotMind

**Smarter bots. On-demand medics.** Three AI modules in one package.

[![SPT 4.0.12](https://img.shields.io/badge/SPT-4.0.12-green.svg)](https://forge.sp-tarkov.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

---

## What It Does

BotMind enhances bot AI with three modules: intelligent **looting**, objective-based **questing**, and the **MedicBuddy** on-demand medical team. Bots make smarter decisions. You get a medic on speed dial.

### 30 Seconds to Understand

```
Looting:     Bots scan area → Find valuable loot → Navigate → Pick up items
Questing:    Bots get objectives → Navigate map → Explore → Extract
MedicBuddy:  Press LCtrl+LAlt+F10 → Team spawns → Medic heals you → Team retreats
```

---

## Key Features

| Feature | Description |
|---------|-------------|
| **Intelligent Looting** | Bots loot corpses, containers, and loose items based on value |
| **Objective Questing** | PMC/Scav bots pursue objectives, explore areas, and extract |
| **MedicBuddy** | Summon a friendly medical team with one keypress |
| **Voice Lines** | 60 voice lines (EN/RU) for immersive medic interactions |
| **CCP Rally Points** | Direct your medical team to a specific treatment location |
| **Medic Promotion** | If the medic dies, a shooter gets promoted automatically |
| **SAIN Integration** | Optional combat awareness and extraction support |
| **Full Healing** | HP, bleeds, fractures, destroyed limbs - all restored |

---

## Quick Start

### Installation

**Manual Installation**

1. Download the [latest release](https://github.com/Blackhorse311/BotMind/releases/latest)
2. Extract the archive directly into your SPT root folder (where `SPT.Server.exe` is located)
3. The folder structure should look like:

```
[SPT Root]/
├── BepInEx/
│   └── plugins/
│       └── Blackhorse311-BotMind/
│           ├── Blackhorse311.BotMind.dll
│           └── voicelines/
│               ├── en/  (30 voice lines)
│               └── ru/  (30 voice lines)
└── SPT.Server.exe
```

### Dependencies

| Mod | Required | Version |
|-----|----------|---------|
| [SPT](https://www.sp-tarkov.com/) | Yes | 4.0.12 |
| [BigBrain](https://forge.sp-tarkov.com/mods/DrakiaXYZ-BigBrain) | Yes | 1.4.x |
| [SAIN](https://forge.sp-tarkov.com/mods/Solarint-SAIN) | Recommended | 3.x |

### Default Behavior

- **Looting** and **Questing** are active on all bots automatically
- **MedicBuddy** is activated on-demand with **LCtrl+LAlt+F10**
- All three modules can be individually toggled

### Configuration

Press **F12** → Find "BotMind" → Adjust settings

Config file location: `BepInEx/config/com.blackhorse311.botmind.cfg`

---

## MedicBuddy

### Controls

| Key | Action |
|-----|--------|
| **LCtrl+LAlt+F10** | Summon medical team |
| **Y** | Set Casualty Collection Point (CCP) at your position |

### How It Works

1. Press **LCtrl+LAlt+F10** to summon the medical team
2. A medic and shooter escorts spawn behind you and navigate to your position
3. Press **Y** to set a CCP rally point where you want treatment
4. The team establishes a defensive perimeter around the CCP
5. The medic approaches and begins preparation (~10 seconds)
6. You are placed into a prone position for treatment
7. The medic heals all injuries (HP, bleeds, fractures, destroyed limbs)
8. After treatment completes, you stand back up and the team retreats

### Tips

- **Use open areas for your CCP.** Tight corridors, buildings, and cluttered areas can cause navigation issues. If the medic gets stuck, move to an open space and press **Y** to reset the rally point.
- Shooter escorts will engage any hostile bots that approach during treatment.
- There is a 5-minute cooldown between summons (configurable).
- MedicBuddy works in PMC raids only by default (configurable).
- If a team bot becomes hostile (e.g., due to another mod's teamkill mechanic), it is immediately despawned for your safety.
- If the medic is killed, a surviving shooter is automatically promoted to medic.
- Team bots carry medical items (IFAK, CMS, bandages) that you can loot from their corpses.
- You must be injured and have no medical equipment to call the medical team. They will not respond if you have gear, or are not injured. 

---

## Configuration Reference

Access mod settings via **F12** (BepInEx Configuration Manager) or edit the config file directly.

### General

| Setting | Default | Description |
|---------|---------|-------------|
| Enable Looting | true | Master toggle for bot looting |
| Enable Questing | true | Master toggle for bot questing |
| Enable MedicBuddy | true | Master toggle for MedicBuddy |

### Looting

| Setting | Default | Description |
|---------|---------|-------------|
| Search Radius | 50m | How far bots scan for loot |
| Minimum Item Value | 5000 | Minimum ruble value to pick up |
| Loot Corpses | true | Allow corpse looting |
| Loot Containers | true | Allow container looting |
| Loot Loose Items | true | Allow loose item pickup |

### Questing

| Setting | Default | Description |
|---------|---------|-------------|
| PMCs Do Quests | true | PMC bots pursue objectives |
| Scavs Do Quests | false | Scav bots pursue objectives |
| Quest Priority | 50 | Balance questing vs other behaviors |

### MedicBuddy

| Setting | Default | Description |
|---------|---------|-------------|
| Summon Keybind | LCtrl+LAlt+F10 | Key to summon the medical team |
| Cooldown | 300s | Seconds between summons |
| Team Size | 4 | Number of bots in the team (2-6) |
| PMC Raids Only | true | Only available in PMC raids |

### Performance

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Max Bots Per Map | 0 (off) | 0-31 | Override max bots per map. When MedicBuddy is enabled, team-size slots are auto-reserved so your medical team always has room to spawn. **0 = use game defaults** (no override). Example: 31 with Team Size 6 = 25 regular bots + 6 reserved. |

> **Slot Reservation:** When `Max Bots Per Map` is set above 0 and MedicBuddy is enabled, the mod automatically reserves slots equal to your Team Size. Regular bot spawns fill the remaining capacity, and MedicBuddy temporarily uses the full limit during spawning.

#### Hardware Guidance

Higher bot counts increase CPU load. Bots are the most performance-intensive element in SPT — each one runs AI decisions, pathfinding, perception, and combat simulation. Use this table to choose a bot count appropriate for your hardware:

| Bot Count | CPU Recommendation | RAM | Expected Impact |
|-----------|-------------------|-----|-----------------|
| Default (18-22) | Any modern quad-core | 16 GB+ | Baseline — no change from vanilla |
| 25 | 6+ cores with strong single-thread | 16 GB+ | Mild FPS dip (5-15%) |
| 28 | 8+ cores (Ryzen 5/7, i5/i7 12th+) | 32 GB+ | Moderate on heavy maps (10-20%) |
| 31 (max) | High-end (Ryzen 7/9, i7/i9 12th+) | 32 GB+ | Noticeable on Streets (15-30%), fine elsewhere |

**Notes:**
- Bot AI is primarily single-threaded — single-core clock speed matters most
- Streets of Tarkov is the heaviest map due to scene complexity + bot count
- Factory is the smallest map — 31 bots there may cause pathfinding congestion
- These estimates are approximate and vary by system configuration

---

## Compatibility

| Mod/Version | Status | Notes |
|-------------|--------|-------|
| **SPT 4.0.12** | Supported | Tested and verified |
| **BigBrain 1.4.x** | Required | AI layer framework |
| **SAIN 3.x** | Recommended | Combat awareness and extraction |
| **LootingBots** | Compatible* | BotMind auto-disables its Looting module when LootingBots is detected. MedicBuddy and Questing work normally alongside LootingBots. |
| **SWAG + Donuts** | Compatible | Bot limit slider works alongside spawn wave mods |
| **AI Limit** | Compatible | Distance-based AI toggle is independent of spawn limits |
| **Custom Items** | Full Support | Works with any modded gear |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| MedicBuddy not responding to LCtrl+LAlt+F10 | Check mod is enabled in F12 settings. Verify PMC-only setting if playing as Scav. |
| Medic getting stuck | Move to an open area and press **Y** to set a new CCP. |
| Team bots not spawning | Check BepInEx console for errors. Ensure BigBrain is installed. |
| Bots not looting | Verify looting is enabled. Check search radius and minimum value settings. |
| SAIN features not working | SAIN is optional. Install SAIN 3.x for combat awareness and extraction. |

---

## Building from Source

### Prerequisites

- .NET 9 SDK
- SPT 4.0.12 installation

### Build Steps

1. Clone the repository
2. Set `SPT_PATH` environment variable to your SPT installation
3. Build:

```bash
# Client plugin
dotnet build src/client/Blackhorse311.BotMind.csproj

# Server mod
dotnet build src/server/Blackhorse311.BotMind.Server.csproj

# Run tests
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj
```

---

## Security & Compliance

Source code is available on [GitHub](https://github.com/Blackhorse311/BotMind) for independent verification and building.

### Forge Compliance

This mod meets all [SPT Forge Content Guidelines](https://forge.sp-tarkov.com/content-guidelines) and [Community Standards](https://forge.sp-tarkov.com/community-standards).

**Highlights:**
- MIT licensed, fully open source
- Zero network activity (fully offline)
- No obfuscation, no data collection
- Comprehensive error handling throughout
- Operational-only logging (no ASCII art, no credits, no links)
- 112 unit tests, 9 code reviews

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Credits

**Author:** Blackhorse311

### Version 1.0.0 - Human + AI Collaboration

BotMind v1.0.0 was developed through a collaboration between Blackhorse311 and [Claude](https://claude.ai), an AI assistant created by Anthropic. This partnership brought together human creativity, game testing, and design direction with AI-assisted code implementation, architecture design, and quality assurance.

Together, we delivered:
- 3 feature modules (Looting, Questing, MedicBuddy)
- 60 voice lines with EN/RU localization
- 112 unit tests with full coverage
- 9 code reviews with 149 fixes
- 3 successful runtime tests
- Comprehensive documentation and architecture decision records

**Thanks to:**
- **SPT Team** - For the amazing SPT project
- **BepInEx Team** - For the modding framework
- **DrakiaXYZ** - For BigBrain AI framework
- **Solarint** - For SAIN combat AI
- **Anthropic** - For creating Claude

**Inspiration:**
- **[DanW](https://github.com/dwesterwick/SPTQuestingBots)** - QuestingBots for SPT 3.x inspired the Questing module
- **[Skwizzy](https://github.com/Skwizzy/SPT-LootingBots)** - LootingBots for SPT 3.x inspired the Looting module

### Community Contributors

- **[Th3Kenix](https://github.com/Th3Kenix)** - Reported questing idle/stuck bug and PMC non-engagement ([#1](https://github.com/Blackhorse311/BotMind/issues/1))
- **LO010OL** - Identified LootingBots compatibility question on SPT Forge

---

## Changelog

### v1.1.1 (2026-02-18)
- **Fix:** Bots no longer go idle after reaching quest waypoints — objective rotation is now immediate ([#1](https://github.com/Blackhorse311/BotMind/issues/1))
- **Fix:** Stuck bots (counter at 3) now properly transition to the next objective instead of standing still
- **Fix:** PMC bots from different teams now detect and engage each other during questing — removed `LookToMovingDirection()` override that was blocking EFT's natural head-scanning and enemy perception
- Stuck/navigation failure logging upgraded from Debug (hidden) to Warning (visible)

### v1.1.0 (2026-02-17)
- **Bot Limit Slider:** New "Max Bots Per Map" config (0-31) with automatic MedicBuddy slot reservation
  - When MedicBuddy is enabled, team-size slots are auto-reserved so the medical team always has room to spawn
  - Harmony patch on `BotsController.SetSettings` — compatible with SWAG+Donuts and AI Limit
  - Hardware guidance table added to README
- **Escort Combat Awareness:** Shooter bots now actively scan for and engage nearby threats
  - Scans `AllAlivePlayersList` every 1s for hostiles within 80m
  - Registers up to 4 nearest threats via `CheckAndAddEnemy()` — EFT's combat AI handles engagement
  - Escorts face the nearest threat instead of looking randomly outward
- **Escort Difficulty Config:** New "Escort Difficulty" setting (0-3, default 2=hard) for escort bot combat skill
- **LootingBots Compatibility:** Auto-detects LootingBots and disables BotMind's Looting module to avoid conflicts
  - MedicBuddy and Questing continue to work alongside LootingBots
- 30 new unit tests for bot limit reservation math (112 total)

### v1.0.0 (2026-02-17)
- Initial release for SPT 4.0.12
- **Looting:** Corpse, container, and loose item looting with value-based prioritization
- **Questing:** Objective management, navigation, exploration, extraction with SAIN integration
- **MedicBuddy:** On-demand medical team with phased healing sequence
  - Voice lines (60 lines, EN/RU) and toast notifications
  - CCP rally point system (Y-key)
  - Medic promotion on medic KIA
  - Hostile bot detection and immediate despawn
  - Medical items on bot corpses (lootable IFAK, CMS, bandages)
  - Player forced prone during treatment
- 82 unit tests, 9 code reviews (151 issues found, 149 fixed)

---

## Support

### Bug Reports

Please use our [GitHub Issue Tracker](https://github.com/Blackhorse311/BotMind/issues) with the bug report template. Include:
- SPT and mod versions
- Which module is affected (MedicBuddy, Looting, Questing)
- Steps to reproduce
- Client log: `BepInEx/LogOutput.log`
- Server log: `SPT/user/logs/`

**Tip:** Search for "BotMind" or "Blackhorse311" in your logs to find relevant lines.

### Feature Requests

Have an idea? [Open a feature request](https://github.com/Blackhorse311/BotMind/issues/new?template=feature_request.yml) on GitHub.

### Community

- [SPT Discord](https://discord.com/invite/Xn9msqQZan) - Community help and discussion
