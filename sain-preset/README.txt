BotMind SAIN Preset
===================

This is an OPTIONAL SAIN configuration preset tuned for use with BotMind.
It makes bots detect and engage enemies faster, reducing the "walk past each other" behavior.

WHAT IT CHANGES (compared to SAIN's default "hard" difficulty):
  - GainSightCoef:        1.0 -> 1.5   (50% faster visual target acquisition)
  - AggressionCoef:       1.0 -> 1.3   (30% more aggressive combat decisions)
  - HearingDistanceCoef:  1.0 -> 1.15  (15% better hearing range)
  - VisibleDistCoef:      1.0 -> 1.1   (10% further vision range)

HOW TO INSTALL:
  1. Make sure SAIN is installed
  2. Copy the "BotMind" folder into:  BepInEx/plugins/SAIN/Presets/
  3. Start a raid, press F6 to open SAIN GUI
  4. Select the "BotMind" preset from the preset dropdown
  5. Done! Changes take effect immediately.

HOW TO REVERT:
  - Press F6 in-game and select any other preset (e.g., "hard")
  - Your original SAIN settings are never modified

NOTE: This preset is entirely optional. BotMind works fine with any SAIN preset.
This just optimizes the experience by making bots more combat-aware.
