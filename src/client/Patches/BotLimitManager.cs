using Blackhorse311.BotMind.Configuration;
using Comfort.Common;
using EFT;
using System;

namespace Blackhorse311.BotMind.Patches
{
    /// <summary>
    /// Manages bot limit overrides and MedicBuddy slot reservation.
    ///
    /// When MaxBotsPerMap slider > 0:
    ///   - Regular bots are capped at (sliderValue - reservedSlots)
    ///   - MedicBuddy temporarily raises the cap to sliderValue during spawning
    ///   - After spawning completes, the cap returns to (sliderValue - reservedSlots)
    ///
    /// When slider is 0 (default): vanilla behavior, no override, no reservation.
    /// </summary>
    public static class BotLimitManager
    {
        private static readonly object _lock = new object();

        /// <summary>Whether MedicBuddy is currently spawning and needs full capacity.</summary>
        private static volatile bool _medicBuddySpawning;

        /// <summary>
        /// The configured slider value (0-31). 0 means "use game defaults".
        /// </summary>
        public static int SliderValue => BotMindConfig.MaxBotsPerMap?.Value ?? 0;

        /// <summary>
        /// Number of slots reserved for MedicBuddy.
        /// Returns TeamSize when slider > 0 AND MedicBuddy enabled, else 0.
        /// </summary>
        public static int ReservedSlots
        {
            get
            {
                if (SliderValue <= 0) return 0;
                if (BotMindConfig.EnableMedicBuddy?.Value != true) return 0;
                return BotMindConfig.MedicBuddyTeamSize?.Value ?? 0;
            }
        }

        /// <summary>
        /// The effective max bots for the current state.
        /// Returns (slider - reserved) normally, full slider during MedicBuddy spawn,
        /// or 0 when slider is 0 (use game default — patch is a no-op).
        /// </summary>
        public static int EffectiveMaxBots
        {
            get
            {
                int slider = SliderValue;
                if (slider <= 0) return 0;

                if (_medicBuddySpawning)
                {
                    return slider;
                }

                return Math.Max(1, slider - ReservedSlots);
            }
        }

        /// <summary>
        /// Called by MedicBuddyController BEFORE spawning to temporarily raise
        /// the bot cap to the full slider value.
        /// </summary>
        public static void BeginMedicBuddySpawn()
        {
            if (SliderValue <= 0) return;

            lock (_lock)
            {
                _medicBuddySpawning = true;
            }

            ApplyBotLimit();
            BotMindPlugin.Log?.LogInfo(
                $"[BotLimitManager] MedicBuddy spawn started - raised max to {SliderValue}");
        }

        /// <summary>
        /// Called by MedicBuddyController AFTER all bots have spawned
        /// (or on timeout/failure) to restore the reduced cap.
        /// </summary>
        public static void EndMedicBuddySpawn()
        {
            if (SliderValue <= 0) return;

            lock (_lock)
            {
                _medicBuddySpawning = false;
            }

            ApplyBotLimit();
            BotMindPlugin.Log?.LogInfo(
                $"[BotLimitManager] MedicBuddy spawn ended - restored max to {EffectiveMaxBots} " +
                $"({ReservedSlots} reserved)");
        }

        /// <summary>
        /// Called by MedicBuddyController when the team is despawned.
        /// Clears the spawning flag. Freed bot slots fill naturally on the next spawn cycle.
        /// </summary>
        public static void OnMedicBuddyDespawned()
        {
            lock (_lock)
            {
                _medicBuddySpawning = false;
            }
        }

        /// <summary>
        /// Applies the current EffectiveMaxBots to both BotSpawner and ZonesLeaveController.
        /// Called from BeginMedicBuddySpawn/EndMedicBuddySpawn to update limits mid-raid.
        /// </summary>
        public static void ApplyBotLimit()
        {
            try
            {
                var botGame = Singleton<IBotGame>.Instance;
                var controller = botGame?.BotsController;
                if (controller == null) return;

                var spawner = controller.BotSpawner;
                if (spawner == null) return;

                int effectiveMax = EffectiveMaxBots;
                if (effectiveMax <= 0) return;

                spawner.SetMaxBots(effectiveMax);
                controller.MaxCount = effectiveMax;
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
                _medicBuddySpawning = false;
            }
        }

        /// <summary>
        /// Pure calculation of effective max bots. Extracted for unit testing.
        /// </summary>
        public static int CalculateEffectiveMax(int sliderValue, int teamSize, bool medicEnabled, bool isMedicBuddySpawning)
        {
            if (sliderValue <= 0) return 0;
            if (isMedicBuddySpawning) return sliderValue;
            int reserved = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);
            return Math.Max(1, sliderValue - reserved);
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
