# BotMind

**Smarter bots. On-demand medics.** Three AI modules in one package.

[![SPT 4.0.12](https://img.shields.io/badge/SPT-4.0.12_REQUIRED-green.svg)](https://forge.sp-tarkov.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

> **Found a bug?** Report it on the [GitHub Issue Tracker](https://github.com/Blackhorse311/BotMind/issues) with your mod list and BepInEx log.

> **REQUIRES SPT 4.0.12** — This mod is built exclusively for SPT 4.0.12. It will NOT work on 4.0.11 or earlier versions. If you are not on 4.0.12, do not install this mod.

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
│       ├── Blackhorse311-BotMind/
│       │   ├── Blackhorse311.BotMind.dll
│       │   └── voicelines/
│       │       ├── en/  (30 voice lines)
│       │       └── ru/  (30 voice lines)
│       └── SAIN/
│           └── Presets/
│               └── BotMind/          (optional SAIN preset)
│                   ├── Info.json
│                   └── GlobalSettings.json
└── SPT.Server.exe
```

> **SAIN Preset (Optional):** The `BotMind` preset folder auto-installs into SAIN's Presets directory. To activate it, press **F6** in-game and select "BotMind" from the preset dropdown. See [SAIN BotMind Preset](#sain-botmind-preset) for details.

### Dependencies

| Mod | Required | Version | Notes |
|-----|----------|---------|-------|
| [SPT](https://www.sp-tarkov.com/) | **YES** | **4.0.12 only** | Will NOT work on 4.0.11 or earlier |
| [BigBrain](https://forge.sp-tarkov.com/mods/DrakiaXYZ-BigBrain) | **YES** | 1.4.x | AI layer framework |
| [SAIN](https://forge.sp-tarkov.com/mods/Solarint-SAIN) | Recommended | 3.x+ | Combat awareness and extraction |
| [Waypoints](https://forge.sp-tarkov.com/mods/DrakiaXYZ-Waypoints) | Recommended | 1.8.x+ | Improved NavMesh for bot navigation |

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
| Combat Alert Duration | 30s | How long bots stay in combat mode after sensing an enemy (10-120s). Higher = bots fight longer before returning to questing/looting. |

### Looting

| Setting | Default | Description |
|---------|---------|-------------|
| Search Radius | 35m | How far bots scan for loot |
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

## SAIN BotMind Preset

BotMind includes an **optional** SAIN configuration preset that tunes bot detection and aggression to complement BotMind's questing and looting behavior. Without it, SAIN's default settings can cause bots to walk past enemies before engaging.

### What It Changes

| Setting | SAIN Default | BotMind Preset | Effect |
|---------|-------------|----------------|--------|
| GainSightCoef | 1.0 | 1.5 | 50% faster visual target acquisition |
| AggressionCoef | 1.0 | 1.3 | 30% more aggressive combat decisions |
| HearingDistanceCoef | 1.0 | 1.15 | 15% better hearing range |
| VisibleDistCoef | 1.0 | 1.1 | 10% further vision range |

### How to Activate

1. The preset auto-installs to `BepInEx/plugins/SAIN/Presets/BotMind/` when you extract the mod
2. In-game, press **F6** to open the SAIN GUI
3. Select **"BotMind"** from the preset dropdown
4. Changes take effect immediately

### How to Revert

Press **F6** and select any other preset (e.g., "hard"). Your original SAIN settings are never modified — the BotMind preset is a separate option, not an overwrite.

---

## Compatibility

| Mod/Version | Status | Notes |
|-------------|--------|-------|
| **SPT 4.0.12** | Supported | **Only supported version** — 4.0.11 and earlier are NOT compatible |
| **BigBrain 1.4.x** | Required | AI layer framework |
| **SAIN 3.x** | Recommended | Combat awareness and extraction |
| **Waypoints** | Recommended | Improved NavMesh data prevents bots from freezing or getting stuck |
| **LootingBots** | Compatible* | BotMind auto-disables its Looting module when LootingBots is detected. MedicBuddy and Questing work normally alongside LootingBots. |
| **SWAG + Donuts** | Compatible | Bot limit slider works alongside spawn wave mods |
| **AI Limit** | Compatible | Distance-based AI toggle is independent of spawn limits |
| **FIKA** | Compatible | Confirmed working by community |
| **Custom Items** | Full Support | Works with any modded gear |

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Mod doesn't work / strange behavior | **Verify you are on SPT 4.0.12.** This mod does not support 4.0.11 or earlier. Check your SPT version in the launcher. |
| MedicBuddy not responding to LCtrl+LAlt+F10 | Check mod is enabled in F12 settings. Verify PMC-only setting if playing as Scav. |
| Medic getting stuck | Move to an open area and press **Y** to set a new CCP. |
| Team bots not spawning | Check BepInEx console for errors. Ensure BigBrain is installed. |
| Bots not looting | Verify looting is enabled. Check search radius and minimum value settings. |
| Bots walking past enemies | Activate the BotMind SAIN preset (F6 menu). Increase Combat Alert Duration in F12. Install Waypoints mod. |
| Bots freezing or getting stuck | Install [Waypoints](https://forge.sp-tarkov.com/mods/DrakiaXYZ-Waypoints) — BotMind's navigation relies on improved NavMesh data. |
| Bots looting too aggressively | Lower Search Radius (default 35m) and check session limit (3 targets per 2 minutes). |
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
- 171 unit tests, 9 code reviews

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
- **ExcellentBug, wookie143, Legitimancer, DigitalB** - Reported bot movement and looting behavior issues that led to v1.4.0 improvements
- **thesubnautica19881** - Confirmed FIKA compatibility

---

## Changelog

### v1.4.0 (2026-02-21)
- **Fix:** Bot movement speeds increased across all modules — bots now jog/sprint instead of creeping (GoToLocation, ExploreArea, LootContainer, FindItem, PlaceItem)
- **Fix:** Pose and move speed reset in all Stop() methods — bots no longer stay crouched or slow after transitioning between AI layers
- **Fix:** Looting throttle — scan interval increased to 8s, 15s cooldown between targets, max 3 targets per 2-minute session window. Prevents "hoover" looting behavior.
- **Fix:** Default loot search radius reduced from 50m to 35m (configurable 10-200m)
- **Fix:** 5-second cooldown between questing objectives for natural-looking behavior transitions
- **Fix:** Combat detection now always checks EFT's native enemy awareness (GoalEnemy, IsUnderFire) alongside SAIN, preventing bots from walking past visible enemies
- **New:** Combat Alert Duration config (default 30s, range 10-120s) — controls how long bots stay in fight mode after sensing an enemy before resuming questing/looting
- **New:** Waypoints soft dependency — warns at startup if DrakiaXYZ-Waypoints is not installed (navigation relies on improved NavMesh data)
- **New:** Optional SAIN "BotMind" preset — tunes detection speed (+50%), aggression (+30%), and hearing (+15%) to complement BotMind behavior
- 37 new unit tests for behavior throttle logic (171 total)

### v1.3.0 (2026-02-19)
- **Fix:** Looting collider buffer increased from 64 to 256 — prevents missing containers when loot-adding mods (Lots of Loot Redux, etc.) are installed
- **Fix:** Overall timeout (60s) added to all looting logic — bots will no longer get stuck indefinitely at unreachable containers, corpses, or items
- **Fix:** Stuck detection added to container and corpse navigation — bots abort after 5 consecutive attempts with no distance progress
- **Fix:** Non-SAIN combat fallback — bots without SAIN now properly yield to combat via native EFT enemy detection (`GoalEnemy`, `IsUnderFire`)
- 22 new unit tests for timeout, stuck detection, and combat fallback (134 total)

### v1.2.0 (2026-02-18)
- **Fix:** Bots no longer get stuck in "failed" or "standby" questing states ([#2](https://github.com/Blackhorse311/BotMind/issues/2), [#3](https://github.com/Blackhorse311/BotMind/issues/3))
- **Fix:** NavMesh path validation added to objective generation — waypoints that can't be reached are rejected
- **Fix:** SamplePosition tolerance reduced from 20m to 5m — prevents snapping to wrong floors/buildings
- **Fix:** Local Explore fallback when long-range waypoints fail (40m PMC, 30m Scav)
- **Fix:** QuestingLayer now registered for all brain types — F12 toggle works at runtime
- **Fix:** Removed 5 additional `LookToMovingDirection()` calls from looting logic that were blocking enemy detection
- Separate path fail counter from stuck counter in GoToLocationLogic

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
