using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Interop
{
    /// <summary>
    /// Provides interop with SAIN mod for accessing bot combat state, hearing, and extraction info.
    /// Uses reflection to avoid hard compile-time dependency on SAIN.dll internals.
    /// </summary>
    public static class SAINInterop
    {
        // Fifth Review Fix (Issue 53): Add volatile for thread-safe visibility of init flags
        private static volatile bool _checkedSAINLoaded;
        private static volatile bool _initialized;
        private static volatile bool _isSAINLoaded;
        // Sixth Review Fix (Issue 109): Mark volatile for thread-safe visibility across threads
        private static volatile Type _sainExternalType;

        // Fifth Review Fix (Issue 51, 52): Lock objects for thread-safe initialization
        private static readonly object _loadCheckLock = new object();
        private static readonly object _initLock = new object();

        // Seventh Review Fix (Issue 10): Added volatile for thread-safe visibility
        // These fields are written inside a lock during Init() but read without locking in public methods
        private static volatile MethodInfo _canBotQuestMethod;
        private static volatile MethodInfo _timeSinceSenseEnemyMethod;
        private static volatile MethodInfo _ignoreHearingMethod;
        private static volatile MethodInfo _getPersonalityMethod;
        private static volatile MethodInfo _tryExtractBotMethod;
        private static volatile MethodInfo _trySetExfilForBotMethod;
        private static volatile MethodInfo _getExtractedBotsMethod;

        /// <summary>
        /// Check if SAIN is loaded in the client.
        /// Fifth Review Fix (Issue 52): Thread-safe double-check locking pattern
        /// </summary>
        public static bool IsSAINLoaded()
        {
            if (!_checkedSAINLoaded)
            {
                lock (_loadCheckLock)
                {
                    if (!_checkedSAINLoaded)
                    {
                        _isSAINLoaded = Chainloader.PluginInfos.ContainsKey("me.sol.sain");
                        _checkedSAINLoaded = true;
                    }
                }
            }
            return _isSAINLoaded;
        }

        /// <summary>
        /// Initialize the SAIN interop. Must be called before using other methods.
        /// Fifth Review Fix (Issue 51): Thread-safe double-check locking pattern
        /// </summary>
        /// <returns>True if SAIN is available and interop was initialized successfully.</returns>
        public static bool Init()
        {
            if (!IsSAINLoaded())
            {
                return false;
            }

            if (!_initialized)
            {
                lock (_initLock)
                {
                    if (!_initialized)
                    {
                        // SAIN 4.4.0 moved External API from SAIN.Plugin.External to SAIN.Interop.SAINExternal.
                        // Try the new path first, then fall back to the old one for backwards compatibility.
                        _sainExternalType = Type.GetType("SAIN.Interop.SAINExternal, SAIN")
                                        ?? Type.GetType("SAIN.Plugin.External, SAIN");

                        if (_sainExternalType == null)
                        {
                            BotMindPlugin.Log?.LogWarning(
                                "SAINInterop.Init: Could not resolve SAIN External type. " +
                                "Tried SAIN.Interop.SAINExternal (4.4.0+) and SAIN.Plugin.External (older). " +
                                "SAIN integration will be disabled.");
                        }
                        else
                        {
                            BotMindPlugin.Log?.LogInfo(
                                $"SAINInterop.Init: Resolved SAIN External type: {_sainExternalType.FullName}");
                        }

                        if (_sainExternalType != null)
                        {
                            _canBotQuestMethod = AccessTools.Method(_sainExternalType, "CanBotQuest");
                            _timeSinceSenseEnemyMethod = AccessTools.Method(_sainExternalType, "TimeSinceSenseEnemy");
                            _ignoreHearingMethod = AccessTools.Method(_sainExternalType, "IgnoreHearing");
                            _getPersonalityMethod = AccessTools.Method(_sainExternalType, "GetPersonality");
                            _tryExtractBotMethod = AccessTools.Method(_sainExternalType, "ExtractBot");
                            _trySetExfilForBotMethod = AccessTools.Method(_sainExternalType, "TrySetExfilForBot");
                            _getExtractedBotsMethod = AccessTools.Method(_sainExternalType, "GetExtractedBots");

                            // Healthcare Critical: Validate that reflection found the methods
                            // Log warnings for any methods that weren't found so users know which features are unavailable
                            ValidateReflectedMethod(_canBotQuestMethod, "CanBotQuest");
                            ValidateReflectedMethod(_timeSinceSenseEnemyMethod, "TimeSinceSenseEnemy");
                            ValidateReflectedMethod(_ignoreHearingMethod, "IgnoreHearing");
                            ValidateReflectedMethod(_getPersonalityMethod, "GetPersonality");
                            ValidateReflectedMethod(_tryExtractBotMethod, "ExtractBot");
                            ValidateReflectedMethod(_trySetExfilForBotMethod, "TrySetExfilForBot");
                            ValidateReflectedMethod(_getExtractedBotsMethod, "GetExtractedBots");
                        }

                        _initialized = true; // Set LAST after all reflection complete
                    }
                }
            }

            return _sainExternalType != null;
        }

        /// <summary>
        /// Healthcare Critical: Log warning if a reflected method wasn't found.
        /// This helps diagnose SAIN version incompatibility issues.
        /// </summary>
        private static void ValidateReflectedMethod(MethodInfo method, string methodName)
        {
            if (method == null)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop: Could not find method '{methodName}' in {_sainExternalType?.FullName ?? "null"}. " +
                    $"This feature will be unavailable. SAIN version may be incompatible.");
            }
        }

        /// <summary>
        /// Check if a bot is currently able to perform quest/looting activities.
        /// Returns false if the bot is in combat or has recently sensed an enemy.
        /// </summary>
        /// <param name="bot">The bot to check.</param>
        /// <param name="targetPosition">The position the bot wants to move to.</param>
        /// <param name="dotThreshold">Threshold for determining if path leads toward enemy.</param>
        /// <returns>True if the bot can safely quest/loot.</returns>
        public static bool CanBotQuest(BotOwner bot, Vector3 targetPosition, float dotThreshold = 0.33f)
        {
            if (bot == null) return false;
            if (!Init()) return true; // If SAIN not available, assume bot can quest
            if (_canBotQuestMethod == null) return true;

            try
            {
                return (bool)_canBotQuestMethod.Invoke(null, new object[] { bot, targetPosition, dotThreshold });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.CanBotQuest failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"SAIN API may have changed. Defaulting to allow quest behavior. " +
                    $"Check SAIN version compatibility if this persists.");
                return true;
            }
        }

        /// <summary>
        /// Get the time since the bot last sensed an enemy.
        /// </summary>
        /// <param name="bot">The bot to check.</param>
        /// <returns>Time in seconds, or float.MaxValue if no enemy sensed or SAIN unavailable.</returns>
        public static float TimeSinceSenseEnemy(BotOwner bot)
        {
            if (bot == null) return float.MaxValue;
            if (!Init()) return float.MaxValue;
            if (_timeSinceSenseEnemyMethod == null) return float.MaxValue;

            try
            {
                return (float)_timeSinceSenseEnemyMethod.Invoke(null, new object[] { bot });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.TimeSinceSenseEnemy failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"Unable to determine combat state. Returning MaxValue (no enemy). " +
                    $"Verify SAIN External API is available.");
                return float.MaxValue;
            }
        }

        /// <summary>
        /// Set a bot to ignore hearing for a specified duration.
        /// Useful for making bots focus on tasks without being distracted by sounds.
        /// </summary>
        /// <param name="bot">The bot to modify.</param>
        /// <param name="ignore">Whether to ignore hearing.</param>
        /// <param name="ignoreUnderFire">Whether to also ignore being under fire.</param>
        /// <param name="duration">Duration in seconds (0 = until enemy seen).</param>
        /// <returns>True if successfully set.</returns>
        public static bool IgnoreHearing(BotOwner bot, bool ignore, bool ignoreUnderFire = false, float duration = 0f)
        {
            if (bot == null) return false;
            if (!Init()) return false;
            if (_ignoreHearingMethod == null) return false;

            try
            {
                return (bool)_ignoreHearingMethod.Invoke(null, new object[] { bot, ignore, ignoreUnderFire, duration });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.IgnoreHearing failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"Bot hearing behavior will not be modified. " +
                    $"SAIN External API method signature may have changed.");
                return false;
            }
        }

        /// <summary>
        /// Get the SAIN personality assigned to a bot.
        /// </summary>
        /// <param name="bot">The bot to check.</param>
        /// <returns>Personality name string, or empty if unavailable.</returns>
        public static string GetPersonality(BotOwner bot)
        {
            if (bot == null) return string.Empty;
            if (!Init()) return string.Empty;
            if (_getPersonalityMethod == null) return string.Empty;

            try
            {
                return (string)_getPersonalityMethod.Invoke(null, new object[] { bot });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.GetPersonality failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"Bot personality-based behavior will use defaults. " +
                    $"Check SAIN is properly initialized.");
                return string.Empty;
            }
        }

        /// <summary>
        /// Force a bot to extract from the raid.
        /// </summary>
        /// <param name="bot">The bot to extract.</param>
        /// <returns>True if the bot was set to extract.</returns>
        public static bool TryExtractBot(BotOwner bot)
        {
            if (bot == null) return false;
            if (!Init()) return false;
            if (_tryExtractBotMethod == null) return false;

            try
            {
                return (bool)_tryExtractBotMethod.Invoke(null, new object[] { bot });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.TryExtractBot failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"Bot will not extract via SAIN. " +
                    $"Extraction handling will fall back to default behavior.");
                return false;
            }
        }

        /// <summary>
        /// Try to assign an extraction point to a bot.
        /// </summary>
        /// <param name="bot">The bot to assign an exfil to.</param>
        /// <returns>True if an exfil was successfully assigned.</returns>
        public static bool TrySetExfilForBot(BotOwner bot)
        {
            if (bot == null) return false;
            if (!Init()) return false;
            if (_trySetExfilForBotMethod == null) return false;

            try
            {
                return (bool)_trySetExfilForBotMethod.Invoke(null, new object[] { bot });
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.TrySetExfilForBot failed for bot '{bot?.name ?? "null"}': {ex.Message}. " +
                    $"No extraction point assigned via SAIN. " +
                    $"Bot may not have a valid extraction path.");
                return false;
            }
        }

        /// <summary>
        /// Get a list of profile IDs for all bots that have extracted.
        /// </summary>
        /// <param name="list">List to populate with extracted bot profile IDs.</param>
        /// <returns>True if the list was populated successfully.</returns>
        public static bool GetExtractedBots(List<string> list)
        {
            if (list == null) return false;
            if (!Init()) return false;
            if (_getExtractedBotsMethod == null) return false;

            try
            {
                _getExtractedBotsMethod.Invoke(null, new object[] { list });
                return true;
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Error messages with WHAT/WHY/HOW per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogWarning(
                    $"SAINInterop.GetExtractedBots failed: {ex.Message}. " +
                    $"Cannot retrieve list of extracted bots. " +
                    $"List will remain unchanged.");
                return false;
            }
        }

        /// <summary>
        /// Check if a bot is currently in a combat state where it shouldn't be interrupted.
        /// </summary>
        /// <param name="bot">The bot to check.</param>
        /// <param name="safeCombatDelay">Seconds since last enemy sense to consider safe.</param>
        /// <returns>True if the bot is in combat and should not be interrupted.</returns>
        public static bool IsBotInCombat(BotOwner bot, float safeCombatDelay = 10f)
        {
            if (bot == null) return false;

            // SAIN-based check: use TimeSinceSenseEnemy for accurate combat awareness
            float timeSinceEnemy = TimeSinceSenseEnemy(bot);
            if (timeSinceEnemy < safeCombatDelay)
            {
                return true;
            }

            // Non-SAIN fallback: check native EFT enemy awareness
            // Without this, bots without SAIN would never yield to combat since
            // TimeSinceSenseEnemy returns float.MaxValue when SAIN isn't loaded
            if (!IsSAINLoaded())
            {
                try
                {
                    if (bot.Memory?.GoalEnemy != null) return true;
                    if (bot.Memory?.IsUnderFire == true) return true;
                }
                catch
                {
                    // Memory access can throw if bot is being despawned
                }
            }

            return false;
        }
    }
}
