using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using Blackhorse311.BotMind.Configuration;
using Blackhorse311.BotMind.Interop;
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
        private const float SCAN_INTERVAL = 2f;

        // Track the current logic instance for communication
        private LootCorpseLogic _corpseLogic;
        private LootContainerLogic _containerLogic;
        private PickupItemLogic _pickupLogic;

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

                // Don't loot if bot is in combat (SAIN check)
                if (SAINInterop.IsBotInCombat(BotOwner))
                {
                    return false;
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
                _currentTarget = _lootFinder.ClaimNextTarget();
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
            // Check if the current logic is complete
            if (_corpseLogic != null && _corpseLogic.IsComplete)
            {
                _lootFinder.MarkCurrentTargetComplete();
                _corpseLogic = null;
                return true;
            }
            if (_containerLogic != null && _containerLogic.IsComplete)
            {
                _lootFinder.MarkCurrentTargetComplete();
                _containerLogic = null;
                return true;
            }
            if (_pickupLogic != null && _pickupLogic.IsComplete)
            {
                _lootFinder.MarkCurrentTargetComplete();
                _pickupLogic = null;
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

        public void RegisterLogic(LootCorpseLogic logic)
        {
            _corpseLogic = logic;
            // Issue 13 Fix: Null check before setting target
            if (_currentTarget != null)
            {
                _corpseLogic.SetTarget(_currentTarget);
            }
        }

        public void RegisterLogic(LootContainerLogic logic)
        {
            _containerLogic = logic;
            // Issue 13 Fix: Null check before setting target
            if (_currentTarget != null)
            {
                _containerLogic.SetTarget(_currentTarget);
            }
        }

        public void RegisterLogic(PickupItemLogic logic)
        {
            _pickupLogic = logic;
            // Issue 13 Fix: Null check before setting target
            if (_currentTarget != null)
            {
                _pickupLogic.SetTarget(_currentTarget);
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
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootingLayer stopped");
                // Third Review Fix: Call Cleanup instead of just ClearTargets for full resource cleanup
                _lootFinder.Cleanup();
                _corpseLogic = null;
                _containerLogic = null;
                _pickupLogic = null;
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
