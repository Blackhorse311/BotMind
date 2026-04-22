using Blackhorse311.BotMind.Configuration;
using Comfort.Common;
using EFT;
using System;
using System.Collections.Generic;

namespace Blackhorse311.BotMind.Patches
{
    /// <summary>
    /// Manages bot limit for MedicBuddy spawning using a just-in-time approach.
    ///
    /// Instead of pre-reserving slots at raid start (which other mods like ABPS overwrite),
    /// this reads the CURRENT live bot limit when MedicBuddy needs to spawn and temporarily
    /// expands it by the team size. After spawning completes, the original limit is restored.
    ///
    /// Compatible with any bot limit mod (ABPS, Donuts, SAIN, etc.) because we never
    /// interfere with their chosen limit during normal gameplay.
    /// </summary>
    public static class BotLimitManager
    {
        private static readonly object _lock = new object();

        /// <summary>The bot limit that was active before MedicBuddy spawn started. Restored after spawn.</summary>
        private static int _preMedicBuddyLimit;

        /// <summary>
        /// The configured slider value (0-31). 0 means "use game defaults".
        /// When > 0, BotLimitPatch overrides the game's initial bot limit.
        /// </summary>
        public static int SliderValue => BotMindConfig.MaxBotsPerMap?.Value ?? 0;

        /// <summary>
        /// Number of slots reserved for MedicBuddy. Used for logging and test calculations only.
        /// The actual reservation is done just-in-time via BeginMedicBuddySpawn/EndMedicBuddySpawn.
        /// </summary>
        public static int ReservedSlots
        {
            get
            {
                if (BotMindConfig.EnableMedicBuddy?.Value != true) return 0;
                return BotMindConfig.MedicBuddyTeamSize?.Value ?? 0;
            }
        }

        /// <summary>
        /// The effective max bots for BotLimitPatch to inject.
        /// When slider > 0: returns slider value (no pre-reservation).
        /// When slider = 0: returns 0 (patch is a no-op, game default used).
        /// </summary>
        public static int EffectiveMaxBots
        {
            get
            {
                int slider = SliderValue;
                if (slider <= 0) return 0;
                return slider;
            }
        }

        /// <summary>
        /// Reads the current live bot limit from the game's BotsController.
        /// This reflects whatever value ABPS, SAIN, Donuts, or any other mod has set.
        /// </summary>
        private static int GetCurrentLiveBotLimit()
        {
            try
            {
                var botGame = Singleton<IBotGame>.Instance;
                var controller = botGame?.BotsController;
                return controller?.MaxCount ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Counts alive bots currently in the game. Other mods (ABPS, etc.) may reduce
        /// MaxCount below the actual alive count, so we need this to ensure the expanded
        /// limit is high enough for SpawnBotByTypeForce to actually create new bots.
        /// </summary>
        private static int GetAliveBotCount()
        {
            try
            {
                var botGame = Singleton<IBotGame>.Instance;
                var spawner = botGame?.BotsController?.BotSpawner;
                var bots = spawner?.Bots?.BotOwners;
                if (bots == null) return 0;

                int alive = 0;
                foreach (var bot in bots)
                {
                    if (bot != null && !bot.IsDead)
                        alive++;
                }
                return alive;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Called by MedicBuddyController BEFORE spawning to temporarily expand
        /// the bot cap by team size above the current baseline.
        ///
        /// The baseline is the HIGHER of MaxCount and actual alive bot count, because
        /// other mods (ABPS, Donuts, etc.) may reduce MaxCount mid-raid below the number
        /// of bots already alive. Expanding from a reduced MaxCount produces a limit that's
        /// still below the alive count, so SpawnBotByTypeForce silently fails.
        /// </summary>
        public static void BeginMedicBuddySpawn()
        {
            int currentLimit = GetCurrentLiveBotLimit();
            int aliveBots = GetAliveBotCount();

            lock (_lock)
            {
                _preMedicBuddyLimit = currentLimit;
            }

            int baseline = Math.Max(currentLimit, aliveBots);
            int teamSize = ReservedSlots;
            int expandedLimit = baseline + teamSize;

            ApplyBotLimit(expandedLimit);
            BotMindPlugin.Log?.LogInfo(
                $"[BotLimitManager] MedicBuddy spawn started - expanded limit to {expandedLimit} " +
                $"(maxCount={currentLimit}, alive={aliveBots}, +{teamSize} team slots)");
        }

        /// <summary>
        /// Called by MedicBuddyController AFTER all bots have spawned
        /// (or on timeout/failure) to restore the original bot limit.
        /// </summary>
        public static void EndMedicBuddySpawn()
        {
            int restoredLimit;
            lock (_lock)
            {
                restoredLimit = _preMedicBuddyLimit;
            }

            if (restoredLimit > 0)
            {
                ApplyBotLimit(restoredLimit);
                BotMindPlugin.Log?.LogInfo(
                    $"[BotLimitManager] MedicBuddy spawn ended - restored limit to {restoredLimit}");
            }
        }

        /// <summary>
        /// Called by MedicBuddyController when the team is despawned.
        /// Clears the spawning flag. Freed bot slots fill naturally on the next spawn cycle.
        /// </summary>
        public static void OnMedicBuddyDespawned()
        {
            // No-op in JIT approach. Freed bot slots return to the pool naturally.
            // Method kept for API compatibility with MedicBuddyController call sites.
        }

        /// <summary>
        /// Applies a specific bot limit to both BotSpawner and BotsController.
        /// </summary>
        private static void ApplyBotLimit(int maxBots)
        {
            try
            {
                var botGame = Singleton<IBotGame>.Instance;
                var controller = botGame?.BotsController;
                if (controller == null) return;

                var spawner = controller.BotSpawner;
                if (spawner == null) return;

                if (maxBots <= 0) return;

                spawner.SetMaxBots(maxBots);
                controller.MaxCount = maxBots;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[BotLimitManager] Failed to apply bot limit: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets all state. Called on raid end (GameWorld.Dispose).
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _preMedicBuddyLimit = 0;
            }
        }

        /// <summary>
        /// Pure calculation of effective max bots. Extracted for unit testing.
        /// Note: With the JIT approach, this simply returns sliderValue when > 0.
        /// Pre-reservation is no longer used; expansion happens at spawn time.
        /// </summary>
        public static int CalculateEffectiveMax(int sliderValue, int teamSize, bool medicEnabled, bool isMedicBuddySpawning)
        {
            if (sliderValue <= 0) return 0;
            return sliderValue;
        }

        /// <summary>
        /// Pure calculation of reserved slots. Extracted for unit testing.
        /// </summary>
        public static int CalculateReservedSlots(int sliderValue, int teamSize, bool medicEnabled)
        {
            if (sliderValue <= 0) return 0;
            if (!medicEnabled) return 0;
            return teamSize;
        }
    }
}
