using BepInEx.Configuration;
using System;
using UnityEngine;

namespace Blackhorse311.BotMind.Configuration
{
    /// <summary>
    /// Central configuration for the BotMind mod.
    /// Contains all user-configurable settings organized by feature module.
    /// Settings are persisted via BepInEx configuration system.
    /// </summary>
    public static class BotMindConfig
    {
        #region General Settings

        /// <summary>
        /// Master toggle for bot looting behavior.
        /// When enabled, bots will search for and loot corpses, containers, and loose items.
        /// </summary>
        public static ConfigEntry<bool> EnableLooting { get; private set; }

        /// <summary>
        /// Master toggle for bot questing/objective behavior.
        /// When enabled, bots will pursue objectives like exploration, item finding, and extraction.
        /// </summary>
        public static ConfigEntry<bool> EnableQuesting { get; private set; }

        /// <summary>
        /// Master toggle for the MedicBuddy summon feature.
        /// When enabled, players can summon a medical team for on-demand healing.
        /// </summary>
        public static ConfigEntry<bool> EnableMedicBuddy { get; private set; }

        #endregion

        #region Looting Settings

        /// <summary>
        /// Maximum distance (in meters) bots will search for lootable items.
        /// Higher values increase CPU usage but allow bots to find more loot.
        /// </summary>
        public static ConfigEntry<float> LootingSearchRadius { get; private set; }

        /// <summary>
        /// Minimum ruble value for an item to be considered worth looting.
        /// Items below this value will be ignored by bots.
        /// </summary>
        public static ConfigEntry<int> MinItemValue { get; private set; }

        /// <summary>
        /// Allow bots to loot dead bodies (corpses).
        /// </summary>
        public static ConfigEntry<bool> LootCorpses { get; private set; }

        /// <summary>
        /// Allow bots to loot static containers (crates, bags, weapon boxes, etc.).
        /// </summary>
        public static ConfigEntry<bool> LootContainers { get; private set; }

        /// <summary>
        /// Allow bots to pick up loose items on the ground.
        /// </summary>
        public static ConfigEntry<bool> LootLooseItems { get; private set; }

        #endregion

        #region Questing Settings

        /// <summary>
        /// Enable quest objective behavior for PMC bots (USEC/BEAR).
        /// </summary>
        public static ConfigEntry<bool> PMCsDoQuests { get; private set; }

        /// <summary>
        /// Enable objective behavior for Scav bots.
        /// Scav objectives are typically simpler (patrol, investigate).
        /// </summary>
        public static ConfigEntry<bool> ScavsDoQuests { get; private set; }

        /// <summary>
        /// How much bots prioritize quest objectives over other behaviors (0-100).
        /// Higher values make bots more focused on objectives.
        /// </summary>
        public static ConfigEntry<float> QuestPriority { get; private set; }

        #endregion

        #region MedicBuddy Settings

        /// <summary>
        /// Key combination to summon the medical team.
        /// Default is Ctrl+Alt+F10. Supports modifier keys via BepInEx KeyboardShortcut.
        /// </summary>
        public static ConfigEntry<KeyboardShortcut> MedicBuddyKeybind { get; private set; }

        /// <summary>
        /// Cooldown duration (in seconds) between MedicBuddy summons.
        /// Prevents spamming the summon feature.
        /// </summary>
        public static ConfigEntry<float> MedicBuddyCooldown { get; private set; }

        /// <summary>
        /// Number of bots in the medical team (including 1 medic).
        /// First bot is the healer, remaining bots provide perimeter defense.
        /// </summary>
        public static ConfigEntry<int> MedicBuddyTeamSize { get; private set; }

        /// <summary>
        /// Restrict MedicBuddy feature to PMC raids only.
        /// When true, Scav runs cannot summon the medical team.
        /// </summary>
        public static ConfigEntry<bool> MedicBuddyPMCOnly { get; private set; }

        /// <summary>
        /// Key to set a Casualty Collection Point (rally point) for the medical team.
        /// When pressed during an active MedicBuddy mission, all bots converge on the
        /// player's current position instead of tracking the moving player.
        /// </summary>
        public static ConfigEntry<KeyboardShortcut> MedicBuddyRallyKeybind { get; private set; }

        /// <summary>
        /// Enable voice line audio playback during MedicBuddy events.
        /// Requires .ogg files in the voicelines/ subfolder.
        /// Text notifications always display regardless of this setting.
        /// </summary>
        public static ConfigEntry<bool> MedicBuddyEnableVoice { get; private set; }

        /// <summary>
        /// Volume for MedicBuddy voice line audio (0-100).
        /// </summary>
        public static ConfigEntry<int> MedicBuddyVoiceVolume { get; private set; }

        /// <summary>
        /// Combat difficulty of escort (shooter) bots. Higher difficulty = better accuracy,
        /// faster reactions, and tighter aim. Maps to EFT's BotDifficulty enum.
        /// 0 = easy, 1 = normal, 2 = hard (default), 3 = impossible.
        /// </summary>
        public static ConfigEntry<int> MedicBuddyEscortDifficulty { get; private set; }

        #endregion

        #region Performance Settings

        /// <summary>
        /// Override the maximum number of bots allowed on the map.
        /// 0 = use game defaults (no override, no slot reservation).
        /// When set above 0 and MedicBuddy is enabled, team-size slots are automatically
        /// reserved so the medical team can always spawn without delay.
        /// Example: 31 with Team Size 6 = 25 regular bots + 6 reserved for MedicBuddy.
        /// Higher values increase CPU load — see README for hardware recommendations.
        /// </summary>
        public static ConfigEntry<int> MaxBotsPerMap { get; private set; }

        #endregion

        public static void Init(ConfigFile config)
        {
            // Sixth Review Fix (Issue 111): Validate ConfigFile parameter
            // Seventh Review Fix (Issue 7): Throw exception to fail fast instead of silent degradation
            // Callers must handle this - silent return leaves all ConfigEntry fields null causing
            // NullReferenceExceptions when any module tries to read config values
            if (config == null)
            {
                var message = "BotMindConfig.Init: ConfigFile parameter is null. " +
                    "Configuration cannot be initialized. " +
                    "Check BepInEx plugin initialization order.";
                BotMindPlugin.Log?.LogError(message);
                throw new ArgumentNullException(nameof(config), message);
            }

            // General
            EnableLooting = config.Bind(
                "1. General",
                "Enable Looting",
                true,
                "Enable bot looting behavior");

            EnableQuesting = config.Bind(
                "1. General",
                "Enable Questing",
                true,
                "Enable bot questing/objective behavior");

            EnableMedicBuddy = config.Bind(
                "1. General",
                "Enable MedicBuddy",
                true,
                "Enable the MedicBuddy summon feature");

            // Looting
            LootingSearchRadius = config.Bind(
                "2. Looting",
                "Search Radius",
                50f,
                new ConfigDescription(
                    "Maximum distance bots will search for loot (meters)",
                    new AcceptableValueRange<float>(10f, 200f)));

            MinItemValue = config.Bind(
                "2. Looting",
                "Minimum Item Value",
                5000,
                new ConfigDescription(
                    "Minimum ruble value for bots to consider looting an item",
                    new AcceptableValueRange<int>(0, 100000)));

            LootCorpses = config.Bind(
                "2. Looting",
                "Loot Corpses",
                true,
                "Allow bots to loot dead bodies");

            LootContainers = config.Bind(
                "2. Looting",
                "Loot Containers",
                true,
                "Allow bots to loot containers (crates, bags, etc.)");

            LootLooseItems = config.Bind(
                "2. Looting",
                "Loot Loose Items",
                true,
                "Allow bots to pick up loose items on the ground");

            // Questing
            PMCsDoQuests = config.Bind(
                "3. Questing",
                "PMCs Do Quests",
                true,
                "PMC bots will pursue quest objectives");

            ScavsDoQuests = config.Bind(
                "3. Questing",
                "Scavs Do Quests",
                false,
                "Scav bots will pursue objectives (patrol points, etc.)");

            QuestPriority = config.Bind(
                "3. Questing",
                "Quest Priority",
                50f,
                new ConfigDescription(
                    "How much bots prioritize quests over other behaviors (0-100)",
                    new AcceptableValueRange<float>(0f, 100f)));

            // MedicBuddy
            MedicBuddyKeybind = config.Bind(
                "4. MedicBuddy",
                "Summon Keybind",
                new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl, KeyCode.LeftAlt),
                "Key combination to summon the medical team (default: Ctrl+Alt+F10)");

            MedicBuddyCooldown = config.Bind(
                "4. MedicBuddy",
                "Cooldown",
                300f,
                new ConfigDescription(
                    "Cooldown between MedicBuddy summons (seconds)",
                    new AcceptableValueRange<float>(60f, 1800f)));

            MedicBuddyTeamSize = config.Bind(
                "4. MedicBuddy",
                "Team Size",
                4,
                new ConfigDescription(
                    "Number of bots in the medical team",
                    new AcceptableValueRange<int>(2, 6)));

            MedicBuddyPMCOnly = config.Bind(
                "4. MedicBuddy",
                "PMC Raids Only",
                true,
                "Only allow MedicBuddy in PMC raids (not Scav runs)");

            MedicBuddyRallyKeybind = config.Bind(
                "4. MedicBuddy",
                "Rally Point Keybind",
                new KeyboardShortcut(KeyCode.Y),
                "Key to set a Casualty Collection Point (rally point) during an active MedicBuddy mission. " +
                "Team will converge on your position when pressed.");

            MedicBuddyEnableVoice = config.Bind(
                "4. MedicBuddy",
                "Enable Voice Lines",
                true,
                "Play voice line audio during MedicBuddy events (requires .ogg files in voicelines/ folder)");

            MedicBuddyVoiceVolume = config.Bind(
                "4. MedicBuddy",
                "Voice Volume",
                80,
                new ConfigDescription(
                    "Volume for MedicBuddy voice lines (0-100)",
                    new AcceptableValueRange<int>(0, 100)));

            MedicBuddyEscortDifficulty = config.Bind(
                "4. MedicBuddy",
                "Escort Difficulty",
                2,
                new ConfigDescription(
                    "Combat skill of escort bots. Higher = better accuracy and faster reactions. " +
                    "0 = easy, 1 = normal, 2 = hard (recommended), 3 = impossible",
                    new AcceptableValueRange<int>(0, 3)));

            // Performance
            MaxBotsPerMap = config.Bind(
                "5. Performance",
                "Max Bots Per Map",
                0,
                new ConfigDescription(
                    "Override max bots per map (0 = use game default). " +
                    "When MedicBuddy is enabled, team-size slots are auto-reserved so the " +
                    "medical team always has room to spawn. " +
                    "Example: 31 with Team Size 6 = 25 regular bots + 6 reserved. " +
                    "Higher values increase CPU load (see README for hardware recommendations).",
                    new AcceptableValueRange<int>(0, 31)));
        }
    }
}
