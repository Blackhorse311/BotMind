using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using Blackhorse311.BotMind.Configuration;
using Blackhorse311.BotMind.Interop;
using Blackhorse311.BotMind.Modules;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.Looting
{
    /// <summary>
    /// BigBrain layer that handles bot looting behavior.
    /// Activates when a bot has identified valuable loot nearby and is safe to collect it.
    /// </summary>
    public class LootingLayer : CustomLayer
    {
        // Third Review Fix: Added readonly modifier for immutability
        private readonly LootFinder _lootFinder;
        private float _lastLootScanTime;
        private LootTarget _currentTarget;
        // v1.4.0 Fix: Increased from 2f — 2s scans made bots find loot too aggressively
        private const float SCAN_INTERVAL = 8f;

        // v1.4.0 Fix: Post-loot cooldown prevents vacuum behavior between targets
        private float _lastLootCompleteTime;
        private const float POST_LOOT_COOLDOWN = 15f;

        // v1.4.0 Fix: Session limit — max targets per time window prevents hoover behavior
        private int _lootActionsThisSession;
        private const int MAX_LOOT_ACTIONS_PER_SESSION = 3;
        private float _sessionResetTime;
        private const float SESSION_RESET_DURATION = 120f;

        // Track the current logic instance for completion checking
        private ICompletableLogic _currentLogic;

        // v1.6.0 Fix: Action-level timeout as safety net for loot hang.
        // If the logic's RegisterLogic() call is missed (e.g., BigBrain doesn't pass ActionData),
        // the logic reference on the layer stays null and IsCurrentActionEnding() can't detect
        // completion. This timeout catches that case without depending on the logic reference.
        private float _currentActionStartTime;
        private const float ACTION_TIMEOUT = 70f; // OVERALL_TIMEOUT (60s) + 10s buffer

        public LootingLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            _lootFinder = new LootFinder(botOwner);
        }

        public override string GetName()
        {
            return "BotMind_Looting";
        }

        public override bool IsActive()
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                // Third Review Fix: Added BotOwner null check
                if (BotOwner?.Profile == null)
                {
                    return false;
                }

                // Don't activate if looting is disabled
                if (!BotMindConfig.EnableLooting.Value)
                {
                    return false;
                }

                // Don't loot if bot is in combat — duration configurable via F12
                if (SAINInterop.IsBotInCombat(BotOwner, BotMindConfig.CombatAlertDuration.Value))
                {
                    return false;
                }

                // v1.4.0 Fix: Cooldown between loot targets to prevent vacuum behavior
                if (Time.time - _lastLootCompleteTime < POST_LOOT_COOLDOWN)
                {
                    return false;
                }

                // v1.4.0 Fix: Session limit — max 3 loot actions per 2-minute window
                if (_lootActionsThisSession >= MAX_LOOT_ACTIONS_PER_SESSION)
                {
                    if (Time.time - _sessionResetTime < SESSION_RESET_DURATION)
                    {
                        return false;
                    }
                    // Session expired, reset counter
                    _lootActionsThisSession = 0;
                }

                // Bug Fix: Call periodic cleanup to remove stale targets (destroyed objects).
                // This was defined but never called, causing null targets to accumulate.
                _lootFinder.PerformPeriodicCleanup();

                // Check if there's valid loot nearby
                if (Time.time - _lastLootScanTime > SCAN_INTERVAL)
                {
                    _lastLootScanTime = Time.time;
                    _lootFinder.ScanForLoot();
                }

                return _lootFinder.HasTargetLoot;
            }
            catch (Exception ex)
            {
                // Fifth Review Fix (Issue 50): Include stack trace and null-safe bot name
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootingLayer.IsActive error: {ex.Message}\n{ex.StackTrace}");
                return false; // Fail safe - deactivate layer on error
            }
        }

        public override Action GetNextAction()
        {
            // Sixth Review Fix (Issue 101): Add try-catch to framework callback
            try
            {
                // Review 10 Fix: Clear stale logic ref before assigning new action
                _currentLogic = null;

                _currentTarget = _lootFinder.ClaimNextTarget();
                _currentActionStartTime = Time.time;
                if (_currentTarget == null)
                {
                    return new Action(typeof(LootCorpseLogic), "No target");
                }

                // Create ActionData to pass target information
                var actionData = new LootingActionData
                {
                    Target = _currentTarget,
                    Layer = this
                };

                // Determine action based on target type
                if (_currentTarget.IsCorpse)
                {
                    return new Action(typeof(LootCorpseLogic), "Looting corpse", actionData);
                }
                else if (_currentTarget.IsContainer)
                {
                    return new Action(typeof(LootContainerLogic), "Opening container", actionData);
                }
                else
                {
                    return new Action(typeof(PickupItemLogic), "Picking up item", actionData);
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootingLayer.GetNextAction error: {ex.Message}\n{ex.StackTrace}");
                return new Action(typeof(LootCorpseLogic), "Error");
            }
        }

        public override bool IsCurrentActionEnding()
        {
            // Sixth Review Fix (Issue 102): Add try-catch to framework callback
            try
            {
            // Check if the current logic is complete.
            // The _currentLogic reference is set via RegisterLogic(), called from the first
            // Update() frame of each action. If registration races or the logic instance is
            // reused across cycles, the reference can be null while the logic has already
            // finished. The timeout fallback below catches that degenerate case.
            if (_currentLogic != null && _currentLogic.IsComplete)
            {
                // Blacklist unreachable targets so the bot doesn't retry the same container/corpse
                if (_currentLogic.HasFailed && _currentTarget != null)
                {
                    _lootFinder.BlacklistTarget(_currentTarget.Target);
                    BotMindPlugin.Log?.LogDebug(
                        $"[{BotOwner?.name ?? "Unknown"}] Blacklisted unreachable loot target");
                }
                _lootFinder.MarkCurrentTargetComplete();
                _currentLogic = null;
                RecordLootCompletion();
                return true;
            }

            // v1.6.0 Fix: Action-level timeout as last-resort safety net.
            // If the logic completed but RegisterLogic() was never called (null reference on layer),
            // none of the IsComplete checks above will fire. Without this timeout the bot stays
            // stuck until all loot targets expire — the root cause of the "state Complete" loot hang.
            if (_currentActionStartTime > 0f && Time.time - _currentActionStartTime > ACTION_TIMEOUT)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{BotOwner?.name ?? "Unknown"}] Looting action timed out after {ACTION_TIMEOUT:F0}s — " +
                    "forcing action end (logic reference may have been lost)");
                _lootFinder.MarkCurrentTargetComplete();
                _currentLogic = null;
                RecordLootCompletion();
                return true;
            }

            // Check if layer should still be active
            return !_lootFinder.HasTargetLoot;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootingLayer.IsCurrentActionEnding error: {ex.Message}\n{ex.StackTrace}");
                return true; // End action on error
            }
        }

        public void RegisterLogic(ICompletableLogic logic)
        {
            _currentLogic = logic;
            // Issue 13 Fix: Null check before setting target
            if (_currentTarget != null)
            {
                if (logic is LootCorpseLogic corpse) corpse.SetTarget(_currentTarget);
                else if (logic is LootContainerLogic container) container.SetTarget(_currentTarget);
                else if (logic is PickupItemLogic pickup) pickup.SetTarget(_currentTarget);
            }
        }

        /// <summary>v1.4.0: Records loot completion for cooldown and session tracking.</summary>
        private void RecordLootCompletion()
        {
            _lastLootCompleteTime = Time.time;
            _lootActionsThisSession++;
            if (_lootActionsThisSession >= MAX_LOOT_ACTIONS_PER_SESSION)
            {
                _sessionResetTime = Time.time;
            }
        }

        public LootTarget GetCurrentTarget()
        {
            return _currentTarget;
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 149): Add try-catch to framework callback
            try
            {
                // v1.8.0: Pause EFT's native patrol system while looting to prevent movement conflicts
                BotOwner?.PatrollingData?.Pause();

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootingLayer started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootingLayer.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 150): Add try-catch to framework callback
            try
            {
                // v1.4.0 Fix: Reset pose/speed so SAIN/vanilla AI doesn't inherit our values
                if (BotOwner != null)
                {
                    BotOwner.ResetToDefaultStance();
                }

                // v1.8.0: Resume EFT's native patrol system when looting ends
                BotOwner?.PatrollingData?.Unpause();

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootingLayer stopped");
                // Third Review Fix: Call Cleanup instead of just ClearTargets for full resource cleanup
                _lootFinder.Cleanup();
                _currentLogic = null;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootingLayer.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            // Seventh Review Fix (Issue 5): Add try-catch to framework callback
            try
            {
                stringBuilder.AppendLine("BotMind Looting Layer");
                stringBuilder.AppendLine($"  Has Target: {_lootFinder?.HasTargetLoot ?? false}");
                stringBuilder.AppendLine($"  Targets Found: {_lootFinder?.TargetCount ?? 0}");
                stringBuilder.AppendLine($"  Session: {_lootActionsThisSession}/{MAX_LOOT_ACTIONS_PER_SESSION}");
                float cooldownLeft = POST_LOOT_COOLDOWN - (Time.time - _lastLootCompleteTime);
                if (cooldownLeft > 0f)
                {
                    stringBuilder.AppendLine($"  Cooldown: {cooldownLeft:F0}s");
                }
                if (_currentTarget != null)
                {
                    stringBuilder.AppendLine($"  Current: {(_currentTarget.IsCorpse ? "Corpse" : _currentTarget.IsContainer ? "Container" : "Item")}");
                }
            }
            catch (Exception ex)
            {
                stringBuilder.AppendLine($"  Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Custom ActionData that passes looting target information to logic classes.
    /// </summary>
    public class LootingActionData : CustomLayer.ActionData
    {
        public LootTarget Target { get; set; }
        public LootingLayer Layer { get; set; }
    }
}
