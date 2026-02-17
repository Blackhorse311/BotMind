# BotMind

A combined bot AI enhancement mod for **SPT 4.0.12** that adds intelligent looting, questing, and the MedicBuddy on-demand medical team feature.

## Features

### Looting
Bots intelligently loot corpses, containers, and loose items based on value and inventory space. Configurable search radius, minimum item value, and per-category toggles.

### Questing
PMC and Scav bots pursue objectives - visiting locations, exploring areas, searching for items, and extracting. Integrates with SAIN for extraction and combat awareness.

### MedicBuddy
Summon a friendly medical team to your position for healing. A medic and shooter escorts spawn behind you, navigate to your location, establish a defensive perimeter, and the medic treats your injuries - restoring HP, stopping bleeds, fixing fractures, and repairing destroyed limbs.

## Installation

1. Requires **SPT 4.0.12** with **BigBrain 1.4.x** installed
2. **SAIN 3.x** recommended (optional - enables combat awareness and extraction)
3. Extract the release zip directly into your SPT root folder (the zip contains the correct `BepInEx/plugins/` path structure)
4. Launch SPT

## Usage

### MedicBuddy Controls

| Key | Action |
|-----|--------|
| **F10** | Summon medical team |
| **Y** | Set Casualty Collection Point (CCP) at your position |

### How MedicBuddy Works

1. Press **F10** to summon the medical team
2. A medic and shooter escorts spawn and navigate to your position
3. Press **Y** to set a CCP rally point where you want treatment
4. The team establishes a defensive perimeter
5. The medic approaches and begins preparation (~10 seconds)
6. You are placed into a prone position for treatment
7. The medic heals all injuries (HP, bleeds, fractures, destroyed limbs)
8. After treatment, you stand back up and the team retreats and despawns

### Tips

- **Use open areas for your CCP.** If you summon the team in a tight corridor, building, or cluttered area, bots may have trouble navigating to you. Move to an open space and reset your CCP with **Y** if the medic gets stuck.
- The medic team is friendly and will not engage you. Shooter escorts will defend against hostile bots.
- There is a 5-minute cooldown between summons (configurable).
- MedicBuddy works in PMC raids only by default (configurable).
- If a team bot becomes hostile (e.g., due to another mod's teamkill mechanic), it is immediately despawned for your safety.
- If the medic is killed, a surviving shooter will be promoted to medic.

## Configuration

All settings are in `BepInEx/config/com.blackhorse311.botmind.cfg`. Settings can be edited while the game is closed, or through BepInEx's in-game configuration manager.

### General
| Setting | Default | Description |
|---------|---------|-------------|
| Enable Looting | true | Master toggle for bot looting |
| Enable Questing | true | Master toggle for bot questing |
| Enable MedicBuddy | true | Master toggle for MedicBuddy |

### Looting
| Setting | Default | Description |
|---------|---------|-------------|
| Search Radius | 50m | How far bots look for loot |
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
| Summon Keybind | F10 | Key to summon the medical team |
| Cooldown | 300s | Seconds between summons |
| Team Size | 4 | Number of bots (2-6) |
| PMC Raids Only | true | Only available in PMC raids |

## Dependencies

| Mod | Required | Version |
|-----|----------|---------|
| [SPT](https://www.sp-tarkov.com/) | Yes | 4.0.12 |
| [BigBrain](https://forge.sp-tarkov.com/mods/DrakiaXYZ-BigBrain) | Yes | 1.4.x |
| [SAIN](https://forge.sp-tarkov.com/mods/Solarint-SAIN) | Recommended | 3.x |

## Building from Source

```bash
# Set SPT_PATH environment variable
$env:SPT_PATH = "C:\path\to\SPT"

# Build
dotnet build src/client/Blackhorse311.BotMind.csproj

# Run tests
dotnet test src/tests/Blackhorse311.BotMind.Tests.csproj
```

## Changelog

### v1.0.0 (2026-02-17)
- Initial release for SPT 4.0.12
- Looting module: corpse, container, and loose item looting with value-based prioritization
- Questing module: objective management, navigation, exploration, extraction
- MedicBuddy module: on-demand medical team with phased healing sequence
  - Voice lines (60 lines, EN/RU) and toast notifications
  - CCP rally point system (Y-key)
  - Medic promotion on medic KIA
  - Hostile bot detection and immediate despawn
  - Medical items on bot corpses (lootable)
  - Player forced prone during treatment
- 82 unit tests, 9 code reviews (151 issues found, 149 fixed)

## Security & Compliance

Source code is available on [GitHub](https://github.com/Blackhorse311/BotMind) for independent verification and building.

### Forge Compliance

This mod meets all SPT Forge Content Guidelines and Community Standards.

- MIT licensed, fully open source
- Zero network activity (fully offline)
- No obfuscation, no data collection
- Comprehensive error handling throughout
- Operational-only logging (no ASCII art, no credits, no links)
- Comprehensive documentation

## License

[MIT](LICENSE)

## Credits

**Author:** Blackhorse311

### Human + AI Collaboration

BotMind v1.0.0 was developed through a collaboration between Blackhorse311 and Claude, an AI assistant created by Anthropic. This partnership brought together human creativity, game testing, and design direction with AI-assisted code implementation, architecture design, and quality assurance.

Together, we delivered:
- 3 feature modules (Looting, Questing, MedicBuddy)
- 82 unit tests with full coverage
- 9 code reviews with 149 fixes
- 3 successful runtime tests
- Comprehensive documentation and architecture decision records

### Thanks to:
- **SPT Team** - For the amazing SPT project
- **BepInEx Team** - For the modding framework
- **DrakiaXYZ** - For BigBrain AI framework
- **Solarint** - For SAIN combat AI
- **Anthropic** - For creating Claude
