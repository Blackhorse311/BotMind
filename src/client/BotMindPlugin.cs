using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Blackhorse311.BotMind.Configuration;
using Blackhorse311.BotMind.Modules.Looting;
using Blackhorse311.BotMind.Modules.Questing;
using Blackhorse311.BotMind.Modules.MedicBuddy;
using Blackhorse311.BotMind.Patches;

namespace Blackhorse311.BotMind
{
    [BepInPlugin("com.blackhorse311.botmind", "Blackhorse311-BotMind", "1.1.0")]
    [BepInDependency("com.SPT.core", "4.0.0")]
    [BepInDependency("xyz.drakia.bigbrain", "1.4.0")]
    [BepInDependency("me.sol.sain", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("me.skwizzy.lootingbots", BepInDependency.DependencyFlags.SoftDependency)]
    public class BotMindPlugin : BaseUnityPlugin
    {
        // Healthcare-grade singleton with proper thread safety
        // Lock object for singleton initialization
        private static readonly object _instanceLock = new object();
        private static volatile BotMindPlugin _instance;
        private static volatile ManualLogSource _log;

        // Track initialization state to detect partial initialization
        private static volatile bool _initializationComplete;
        private static volatile bool _initializationFailed;

        public static ManualLogSource Log => _log;
        public static BotMindPlugin Instance => _instance;

        /// <summary>Whether the plugin initialized successfully without errors.</summary>
        public static bool IsFullyInitialized => _initializationComplete && !_initializationFailed;

        private MedicBuddyController _medicBuddyController;

        // Third Review Fix: Store patch instances for proper cleanup
        private GameStartedPatch _gameStartedPatch;
        private GameWorldDisposePatch _gameWorldDisposePatch;
        private BotLimitPatch _botLimitPatch;

        // Brain names for different bot types
        // CRITICAL: These must match BaseBrain.ShortName() return values exactly (case-sensitive)
        // PmcUsec/PmcBear are the actual brain names for PMC bots (matches EBrain enum ToString())
        private static readonly List<string> AllBrains = new List<string>
        {
            "Assault", "PMC", "ExUsec", "PmcUsec", "PmcBear", "Obdolbs", "CursAssault",
            "BossTest", "Knight", "BigPipe", "BirdEye",
            "FollowerGluharAssault", "FollowerGluharProtect", "FollowerGluharScout",
            "Tagilla", "Killa", "BossBully", "FollowerBully",
            "BossSanitar", "FollowerSanitar", "BossKojaniy", "FollowerKojaniy",
            "SectantPriest", "SectantWarrior", "Gifter", "Marksman",
            "ArenaFighter"
        };

        private static readonly List<string> PMCBrains = new List<string>
        {
            "PMC", "ExUsec", "PmcUsec", "PmcBear"
        };

        private static readonly List<string> ScavBrains = new List<string>
        {
            "Assault", "CursAssault", "Marksman"
        };

        public void Awake()
        {
            // Healthcare Critical: ALL initialization must be inside lock to prevent TOCTOU races
            // Previous code had instance check inside lock but actual initialization outside,
            // allowing two threads to both pass the check and initialize simultaneously
            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    Logger.LogWarning("BotMindPlugin.Awake called but instance already exists - skipping initialization");
                    return;
                }
                _instance = this;
                _log = Logger;

                Log.LogInfo("BotMind is loading...");

                try
                {
                    // Track initialization steps for debugging partial failures
                    string currentStep = "pre-init";

                    currentStep = "configuration";
                    Log.LogDebug($"Step 1: Initializing {currentStep}...");
                    InitializeConfiguration();

                    currentStep = "SAIN interop";
                    Log.LogDebug($"Step 2: Initializing {currentStep}...");
                    InitializeInterop();

                    currentStep = "brain layers";
                    Log.LogDebug($"Step 3: Registering {currentStep}...");
                    RegisterBrainLayers();

                    currentStep = "game event patches";
                    Log.LogDebug($"Step 4: Enabling {currentStep}...");
                    EnablePatches();

                    // Mark as fully initialized only after all steps complete
                    _initializationComplete = true;
                    Log.LogInfo("BotMind loaded successfully!");
                }
                catch (System.Exception ex)
                {
                    // Healthcare-grade error handling: mark as failed, log full context
                    _initializationFailed = true;
                    Log.LogError($"BotMind failed to load: {ex.Message}\n{ex.StackTrace}");

                    // Healthcare Critical: Clean up patches on failure to prevent resource leaks
                    try
                    {
                        _gameStartedPatch?.Disable();
                        _gameWorldDisposePatch?.Disable();
                        _botLimitPatch?.Disable();
                    }
                    catch (Exception cleanupEx)
                    {
                        Log.LogDebug($"Patch cleanup during failure: {cleanupEx.Message}");
                    }

                    // CRITICAL: Do not silently continue - warn user that plugin is degraded
                    Log.LogWarning(
                        "BotMind is in a DEGRADED state due to initialization failure. " +
                        "Some or all features may not work. Check the error above for details.");
                }
            }
        }

        public void OnDestroy()
        {
            // Third Review Fix: Added try-catch to Unity callback and patch cleanup
            try
            {
                // Healthcare Critical: Clear singleton reference to support hot-reload
                // Without this, subsequent plugin loads check _instance != null and skip initialization
                lock (_instanceLock)
                {
                    if (_instance == this)
                    {
                        _instance = null;
                        _log = null;
                        _initializationComplete = false;
                        _initializationFailed = false;
                    }
                }

                // Disable Harmony patches to prevent memory leaks
                _gameStartedPatch?.Disable();
                _gameWorldDisposePatch?.Disable();
                _botLimitPatch?.Disable();

                if (_medicBuddyController != null)
                {
                    Destroy(_medicBuddyController);
                    _medicBuddyController = null;
                }
            }
            catch (Exception ex)
            {
                // Can't use Log here as we may have just nulled it - use Unity debug
                UnityEngine.Debug.LogWarning($"[BotMind] OnDestroy cleanup error: {ex.Message}");
            }
        }

        private void EnablePatches()
        {
            Log.LogDebug("Enabling game event patches...");
            // Third Review Fix: Store patch instances for cleanup in OnDestroy
            _gameStartedPatch = new GameStartedPatch();
            _gameWorldDisposePatch = new GameWorldDisposePatch();
            _botLimitPatch = new BotLimitPatch();
            _gameStartedPatch.Enable();
            _gameWorldDisposePatch.Enable();
            _botLimitPatch.Enable();
        }

        internal void OnGameWorldCreated(GameWorld gameWorld)
        {
            // Healthcare-grade: Check initialization state before proceeding
            if (!IsFullyInitialized)
            {
                Log?.LogWarning("OnGameWorldCreated skipped - BotMind not fully initialized");
                return;
            }

            Log.LogDebug("GameWorld created - initializing BotMind modules");

            // Initialize MedicBuddy controller
            if (BotMindConfig.EnableMedicBuddy.Value)
            {
                InitializeMedicBuddy(gameWorld);
            }
        }

        internal void OnGameWorldDestroyed()
        {
            // Fifth Review Fix (Issue 54): Add try-catch for defensive error handling
            try
            {
                Log.LogDebug("GameWorld destroyed - cleaning up BotMind modules");

                // Reset bot limit state for next raid
                BotLimitManager.Reset();

                // Clean up audio clips before destroying controller
                MedicBuddyAudio.Cleanup();

                if (_medicBuddyController != null)
                {
                    Destroy(_medicBuddyController);
                    _medicBuddyController = null;
                }
            }
            catch (Exception ex)
            {
                Log?.LogDebug($"GameWorld cleanup error (may be expected during shutdown): {ex.Message}");
            }
        }

        private void InitializeMedicBuddy(GameWorld gameWorld)
        {
            // Third Review Fix: Add null check for gameWorld.gameObject
            var go = gameWorld?.gameObject;
            if (go == null)
            {
                Log.LogError("GameWorld has no GameObject - cannot initialize MedicBuddy");
                return;
            }

            _medicBuddyController = go.AddComponent<MedicBuddyController>();

            // Set the player reference when available
            var mainPlayer = gameWorld.MainPlayer;
            if (mainPlayer != null)
            {
                _medicBuddyController.SetPlayer(mainPlayer);
            }

            // Initialize audio system (loads voice lines from voicelines/ subfolder)
            MedicBuddyAudio.Initialize(_medicBuddyController);

            Log.LogInfo("MedicBuddy controller initialized");
        }

        private void InitializeConfiguration()
        {
            Log.LogDebug("Initializing configuration...");
            BotMindConfig.Init(Config);
        }

        private void InitializeInterop()
        {
            Log.LogDebug("Initializing SAIN interop...");
            if (!Interop.SAINInterop.Init())
            {
                Log.LogWarning("SAIN interop not available - some features may be limited");
            }
        }

        private void RegisterBrainLayers()
        {
            Log.LogDebug("Registering BigBrain layers...");

            // Layer priorities - higher = checked first
            // We want our layers below combat but above idle behaviors
            const int LOOTING_PRIORITY = 22;
            const int QUESTING_PRIORITY = 21;
            const int MEDICBUDDY_PRIORITY = 95; // High priority for MedicBuddy team

            // Register Looting layer for PMCs and Scavs
            // Skip if LootingBots is installed — their layers would conflict (priority, loot caching, inventory transactions)
            bool lootingBotsDetected = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("me.skwizzy.lootingbots");
            if (lootingBotsDetected)
            {
                Log.LogWarning(
                    "LootingBots detected — BotMind's Looting module has been auto-disabled to avoid conflicts. " +
                    "LootingBots will handle all bot looting behavior. " +
                    "MedicBuddy and Questing modules remain active.");
            }

            if (BotMindConfig.EnableLooting.Value && !lootingBotsDetected)
            {
                var lootingBrains = new List<string>();
                lootingBrains.AddRange(PMCBrains);
                lootingBrains.AddRange(ScavBrains);

                BrainManager.AddCustomLayer(
                    typeof(LootingLayer),
                    lootingBrains,
                    LOOTING_PRIORITY);

                Log.LogInfo($"Registered LootingLayer for {lootingBrains.Count} brain types");
            }

            // Register Questing layer for PMCs (and optionally Scavs)
            if (BotMindConfig.EnableQuesting.Value)
            {
                var questingBrains = new List<string>();

                if (BotMindConfig.PMCsDoQuests.Value)
                {
                    questingBrains.AddRange(PMCBrains);
                }

                if (BotMindConfig.ScavsDoQuests.Value)
                {
                    questingBrains.AddRange(ScavBrains);
                }

                if (questingBrains.Count > 0)
                {
                    BrainManager.AddCustomLayer(
                        typeof(QuestingLayer),
                        questingBrains,
                        QUESTING_PRIORITY);

                    Log.LogInfo($"Registered QuestingLayer for {questingBrains.Count} brain types");
                }
            }

            // Register MedicBuddy layers for all bots (filtered by controller)
            if (BotMindConfig.EnableMedicBuddy.Value)
            {
                BrainManager.AddCustomLayer(
                    typeof(MedicBuddyMedicLayer),
                    AllBrains,
                    MEDICBUDDY_PRIORITY);

                BrainManager.AddCustomLayer(
                    typeof(MedicBuddyShooterLayer),
                    AllBrains,
                    MEDICBUDDY_PRIORITY);

                Log.LogInfo("Registered MedicBuddy layers");
            }
        }
    }

    /// <summary>
    /// Patch to hook into GameWorld.OnGameStarted for module initialization.
    /// Issue 3 Fix: Added try-catch to prevent game crashes from initialization failures.
    /// </summary>
    internal class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        private static void PatchPostfix(GameWorld __instance)
        {
            try
            {
                BotMindPlugin.Instance?.OnGameWorldCreated(__instance);
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"GameStartedPatch failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Patch to hook into GameWorld.Dispose for module cleanup.
    /// Issue 3 Fix: Added try-catch to prevent game crashes from cleanup failures.
    /// </summary>
    internal class GameWorldDisposePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.Dispose));
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            try
            {
                BotMindPlugin.Instance?.OnGameWorldDestroyed();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"GameWorldDisposePatch failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
