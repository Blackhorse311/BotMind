using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.Looting
{
    /// <summary>
    /// Logic for a bot to pick up a loose item from the ground.
    /// Based on BotItemTaker patterns from EFT.
    /// </summary>
    public class PickupItemLogic : CustomLogic
    {
        private enum State
        {
            MovingToItem,
            LookingAtItem,
            PickingUp,
            Complete
        }

        private State _currentState = State.MovingToItem;
        private LootTarget _target;
        private float _startTime;
        private float _nextMoveTime;
        private float _unpauseTime;
        private float _lookEndTime;
        private Vector3 _cachedDirection;
        private float _directionUpdateTime;
        private bool _pickupInProgress;
        // Healthcare Critical: Track stopped state to prevent callbacks modifying state after Stop()
        private volatile bool _isStopped;

        // Constants from BotItemTaker
        private const float DIST_TO_TAKE = 1.5f;
        private const float SDIST_TO_TAKE = 2.25f; // squared distance
        private const float LOOK_PERIOD = 1.5f;
        private const float DIR_UPDATE_PERIOD = 2f;
        /// <summary>
        /// Bug Fix: Emergency timeout for pickup callback.
        /// Matches LootCorpseLogic's MOVE_OPERATION_TIMEOUT to prevent infinite stuck state.
        /// </summary>
        private const float PICKUP_OPERATION_TIMEOUT = 30f;
        private float _pickupOperationStartTime;
        // Issue 8 Fix: Track path failures separately to avoid premature abortion
        private int _pathFailureCount;
        private const int MAX_PATH_FAILURES = 3;

        // Third Review Fix: Cached containers list to avoid allocation in hot path
        private readonly List<CompoundItem> _containersCache = new List<CompoundItem>(3);

        public PickupItemLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public void SetTarget(LootTarget target)
        {
            _target = target;
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _currentState = State.MovingToItem;
                _startTime = Time.time;
                _unpauseTime = -1f;
                _lookEndTime = -1f;
                _directionUpdateTime = 0f;
                _pickupInProgress = false;
                _isStopped = false; // Reset stopped flag on start
                _pathFailureCount = 0;  // Reset failure count on start
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] PickupItemLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] PickupItemLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Complete;
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                // Healthcare Critical: Set stopped flag FIRST to prevent in-flight callbacks
                _isStopped = true;
                _pickupInProgress = false; // Reset to prevent stuck state on restart

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] PickupItemLogic stopped");
                _containersCache.Clear();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] PickupItemLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                // Extract target from ActionData if not set
                if (_target == null && data is LootingActionData lootingData)
                {
                    _target = lootingData.Target;
                    lootingData.Layer?.RegisterLogic(this);
                }

                if (_target == null)
                {
                    _currentState = State.Complete;
                    return;
                }

                // Handle delayed actions
                if (_unpauseTime > 0f)
                {
                    if (Time.time < _unpauseTime)
                    {
                        return;
                    }
                    _unpauseTime = -1f;
                }

                switch (_currentState)
                {
                    case State.MovingToItem:
                        UpdateMovingToItem();
                        break;
                    case State.LookingAtItem:
                        UpdateLookingAtItem();
                        break;
                    case State.PickingUp:
                        UpdatePickingUp();
                        break;
                    case State.Complete:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Fifth Review Fix (Issue 49): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] PickupItemLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Complete; // Fail safe
            }
        }

        private void SetPauseTime(float delay)
        {
            _unpauseTime = Time.time + delay;
        }

        private void UpdateMovingToItem()
        {
            var lootItem = _target.Target as LootItem;
            if (lootItem == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Update direction periodically (like BotItemTaker.method_6)
            if (Time.time > _directionUpdateTime + DIR_UPDATE_PERIOD)
            {
                _cachedDirection = lootItem.transform.position - BotOwner.Position;
                _directionUpdateTime = Time.time;
            }

            float sqrDistance = _cachedDirection.sqrMagnitude;

            // Check if close enough to pick up
            if (sqrDistance < SDIST_TO_TAKE)
            {
                _lookEndTime = Time.time + LOOK_PERIOD;
                _currentState = State.LookingAtItem;
                return;
            }

            // Navigate to item
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            // LookToMovingDirection removed — blocks EFT's LookSensor from detecting enemies

            if (_nextMoveTime < Time.time)
            {
                _nextMoveTime = Time.time + 2f;

                Vector3 itemPos = lootItem.transform.position;
                Vector3 dir = (itemPos - BotOwner.Position).normalized;
                Vector3 destination = itemPos - dir * 1f;

                var pathResult = BotOwner.GoToPoint(destination, true, -1f, false, true, true, false, false);

                // Issue 8 Fix: Use failure counter instead of immediate abort for transient path issues
                if (pathResult != NavMeshPathStatus.PathComplete)
                {
                    _pathFailureCount++;
                    if (_pathFailureCount >= MAX_PATH_FAILURES)
                    {
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] PickupItem path failed after {MAX_PATH_FAILURES} attempts");
                        _currentState = State.Complete;
                    }
                    return;
                }

                // Reset failure count on successful path
                _pathFailureCount = 0;
            }
        }

        private void UpdateLookingAtItem()
        {
            var lootItem = _target.Target as LootItem;
            if (lootItem == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Look at the item (from BotItemTaker.method_4)
            Vector3 vector = lootItem.transform.position - BotOwner.Position;
            float sqrHorizontalDist = vector.x * vector.x + vector.z * vector.z;

            if (sqrHorizontalDist > 0.0225f) // 0.15 squared
            {
                vector = vector.normalized;
                if (vector.y > 0f)
                {
                    vector.y = -0.01f;
                    vector = vector.normalized;
                }
                BotOwner.Steering.LookToDirection(vector);
            }

            // Check if look period complete
            if (Time.time >= _lookEndTime)
            {
                _currentState = State.PickingUp;
            }
        }

        private void UpdatePickingUp()
        {
            var lootItem = _target.Target as LootItem;
            if (lootItem == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Bug Fix: Added emergency timeout (was missing, unlike LootCorpseLogic).
            // If Pickup callback never fires due to EFT bug, prevent infinite stuck state.
            if (_pickupInProgress)
            {
                if (Time.time - _pickupOperationStartTime > PICKUP_OPERATION_TIMEOUT)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[{BotOwner?.name ?? "Unknown"}] Pickup operation timed out after {PICKUP_OPERATION_TIMEOUT}s - " +
                        "callback may have failed. Completing pickup to prevent stuck state.");
                    _pickupInProgress = false;
                    _currentState = State.Complete;
                    return;
                }
                return;
            }

            // Third Review Fix: Added explicit null check for lootItem.ItemOwner
            if (lootItem.ItemOwner == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Attempt to pick up the item (based on BotItemTaker.method_9)
            Item rootItem = lootItem.ItemOwner.RootItem;
            Player player = BotOwner.GetPlayer;
            InventoryController inventoryController = player?.InventoryController;

            if (inventoryController == null || rootItem == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Find a place for the item (from BotItemTaker.method_0)
            ItemAddress targetAddress = inventoryController.FindSlotToPickUp(lootItem.Item);
            if (targetAddress == null && !(lootItem.Item is Weapon))
            {
                targetAddress = inventoryController.FindGridToPickUp(lootItem.Item);
            }

            if (targetAddress == null)
            {
                // No space for item
                _currentState = State.Complete;
                return;
            }

            // Skip if it's equipment (from BotItemTaker.method_9)
            if (rootItem is InventoryEquipment)
            {
                _currentState = State.Complete;
                return;
            }

            // Third Review Fix: Reuse cached containers list to avoid allocation
            _containersCache.Clear();
            var equipment = inventoryController.Inventory?.Equipment;

            var backpack = equipment?.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem;
            var vest = equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem;
            var pockets = equipment?.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem;

            if (backpack != null) _containersCache.Add(backpack);
            if (vest != null) _containersCache.Add(vest);
            if (pockets != null) _containersCache.Add(pockets);

            // Try to find appropriate place and move item
            var moveResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                lootItem.Item,
                inventoryController,
                _containersCache,
                InteractionsHandlerClass.EMoveItemOrder.PickUp,
                true);

            if (moveResult.Succeeded)
            {
                // Sixth Review Fix (Issue 113): Check CurrentManagedState for null
                if (player.CurrentManagedState == null)
                {
                    _currentState = State.Complete;
                    return;
                }

                // Healthcare Critical: Set flag BEFORE calling Pickup to prevent race
                // Previous code set flag AFTER registering callbacks - if callback fired
                // synchronously, flag was false when callback ran, breaking state machine
                _pickupInProgress = true;
                // Bug Fix: Track start time for emergency timeout
                _pickupOperationStartTime = Time.time;

                player.CurrentManagedState.Pickup(true, () =>
                {
                    inventoryController.TryRunNetworkTransaction(moveResult, (result) =>
                    {
                        // Healthcare Critical: Check _isStopped to prevent state modification after Stop()
                        if (_isStopped) return;

                        _pickupInProgress = false;

                        if (result.Succeed)
                        {
                            _currentState = State.Complete;
                        }
                        else
                        {
                            BotMindPlugin.Log?.LogDebug($"[{player?.Profile?.Nickname}] Pickup transaction failed: {result.Error}");
                            _currentState = State.Complete; // Fail but don't retry infinitely
                        }
                    });
                });
            }
            else
            {
                _currentState = State.Complete;
            }
        }

        public bool IsComplete => _currentState == State.Complete;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("PickupItemLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
