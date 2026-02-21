using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.Looting
{
    /// <summary>
    /// Logic for a bot to loot a corpse.
    /// Based on BotDeadBodyWork state machine pattern from EFT.
    /// </summary>
    public class LootCorpseLogic : CustomLogic
    {
        private enum State
        {
            MovingToCorpse,
            Initial,
            LootWeapon,
            CheckBackpack,
            LootAllCalculations,
            LootAllItemsMoving,
            Complete
        }

        private State _currentState = State.MovingToCorpse;
        private LootTarget _target;
        private float _startTime;
        private float _nextMoveTime;
        private float _unpauseTime;
        private readonly Vector3 _cachedOffset;

        // Looting state
        private readonly List<Item> _itemsCache = new List<Item>(32);
        private readonly List<CompoundItem> _containersCache = new List<CompoundItem>(4);
        private readonly List<(float Weight, Item Item)> _weightedItems = new List<(float, Item)>(32);
        private int _takeItemsLeft;
        private int _curItemIndex;
        private bool _moveInProgress;
        // Healthcare Critical: Track stopped state to prevent callbacks modifying state after Stop()
        // Callbacks can fire asynchronously after logic has been stopped
        private volatile bool _isStopped;
        // Seventh Review Fix (Issue 14): Track when move operation started for emergency timeout
        private float _moveOperationStartTime;

        /// <summary>Minimum distance to corpse before looting can begin (meters).</summary>
        private const float CLOSE_DISTANCE = 1.7f;
        /// <summary>Delay between moving items to simulate realistic looting speed (seconds).</summary>
        private const float MOVE_ITEMS_DELAY = 0.5f;
        /// <summary>Delay between checking items to reduce processing load (seconds).</summary>
        private const float CHECK_ITEMS_DELAY = 0.3f;
        /// <summary>
        /// Seventh Review Fix (Issue 14): Emergency timeout for callback-only completion.
        /// If the inventory transaction callback never fires (due to EFT bug), this prevents
        /// the bot from being stuck forever. Set very high to avoid false timeouts.
        /// </summary>
        private const float MOVE_OPERATION_TIMEOUT = 30f;
        /// <summary>
        /// Overall timeout for the entire corpse looting operation.
        /// Prevents bots from being stuck indefinitely when they can't reach a corpse.
        /// </summary>
        private const float OVERALL_TIMEOUT = 60f;
        // Stuck detection: if distance hasn't decreased after several GoToPoint attempts, abort
        private float _lastMoveDistance = float.MaxValue;
        private int _noProgressCount;
        private const int MAX_NO_PROGRESS = 5;

        // Standards Compliance Fix: Cache reflection PropertyInfo to avoid hot-path allocation
        // The corpse body type varies at runtime, so we cache per-type to avoid repeated lookups
        // Fifth Review Fix (Issue 47): Use ConcurrentDictionary for thread-safe access without lock contention
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo> _playerPropertyCache =
            new System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo>();

        // Seventh Review Fix (Issue 167): Static comparison delegate to avoid lambda allocation in Sort
        private static readonly System.Comparison<(float Weight, Item Item)> _weightComparer =
            (a, b) => -1 * a.Weight.CompareTo(b.Weight);

        public LootCorpseLogic(BotOwner botOwner) : base(botOwner)
        {
            // Random offset to not stand exactly on corpse
            float x = UnityEngine.Random.Range(0.15f, 0.3f) * Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));
            float z = UnityEngine.Random.Range(0.15f, 0.3f) * Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));
            _cachedOffset = new Vector3(x, 0f, z);
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
                _currentState = State.MovingToCorpse;
                _startTime = Time.time;
                _unpauseTime = -1f;
                _isStopped = false; // Reset stopped flag on start
                _moveInProgress = false; // Reset move flag on start
                _lastMoveDistance = float.MaxValue;
                _noProgressCount = 0;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootCorpseLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootCorpseLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Complete;
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                // Healthcare Critical: Set stopped flag FIRST to prevent in-flight callbacks
                // from modifying state after we've stopped
                _isStopped = true;
                _moveInProgress = false; // Reset to prevent stuck state on restart

                // v1.4.0 Fix: Reset pose/speed to defaults (was leaving bots crouched)
                if (BotOwner != null)
                {
                    BotOwner.SetPose(1f);
                    BotOwner.SetTargetMoveSpeed(1f);
                }

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootCorpseLogic stopped");
                _itemsCache.Clear();
                _containersCache.Clear();
                _weightedItems.Clear();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootCorpseLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Standards Compliance Fix: Wrap framework callback in try-catch per ERROR_HANDLING.md
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

                // Overall timeout: prevent being stuck on any single corpse forever
                if (Time.time - _startTime > OVERALL_TIMEOUT)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[{BotOwner?.name ?? "Unknown"}] Corpse looting timed out after {OVERALL_TIMEOUT}s in state {_currentState}. Aborting.");
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
                    case State.MovingToCorpse:
                        UpdateMovingToCorpse();
                        break;
                    case State.Initial:
                        SetPauseTime(0.5f);
                        _currentState = State.LootWeapon;
                        break;
                    case State.LootWeapon:
                        UpdateLootWeapon();
                        break;
                    case State.CheckBackpack:
                        UpdateCheckBackpack();
                        break;
                    case State.LootAllCalculations:
                        UpdateLootAllCalculations();
                        break;
                    case State.LootAllItemsMoving:
                        UpdateLootAllItemsMoving();
                        break;
                    case State.Complete:
                        break;
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] LootCorpseLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Complete; // Fail safe - don't crash, just complete
            }
        }

        private void SetPauseTime(float delay)
        {
            _unpauseTime = Time.time + delay;
        }

        private void UpdateMovingToCorpse()
        {
            float dist = Vector3.Distance(BotOwner.Position, _target.Position);

            if (dist < CLOSE_DISTANCE)
            {
                // Arrived - start looting
                BotOwner.SetPose(0f); // Crouch
                LookAtCorpse();
                _currentState = State.Initial;
                return;
            }

            // Navigate to corpse
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            // LookToMovingDirection removed — blocks EFT's LookSensor from detecting enemies

            if (_nextMoveTime < Time.time)
            {
                _nextMoveTime = Time.time + 3f;

                // Stuck detection: abort if not making progress toward the corpse
                if (dist < _lastMoveDistance - 0.3f)
                {
                    _noProgressCount = 0;
                }
                else
                {
                    _noProgressCount++;
                    if (_noProgressCount >= MAX_NO_PROGRESS)
                    {
                        BotMindPlugin.Log?.LogWarning(
                            $"[{BotOwner?.name ?? "Unknown"}] Stuck moving to corpse ({dist:F1}m away, no progress for {MAX_NO_PROGRESS} attempts). Aborting.");
                        _currentState = State.Complete;
                        return;
                    }
                }
                _lastMoveDistance = dist;

                Vector3 targetPos = _target.Position + _cachedOffset;
                Vector3 dir = (targetPos - BotOwner.Position).normalized;
                Vector3 destination = targetPos - dir;

                if (BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false) != NavMeshPathStatus.PathComplete)
                {
                    // Can't path - abort
                    _currentState = State.Complete;
                }
            }
        }

        private void LookAtCorpse()
        {
            Vector3 dir = _target.Position - BotOwner.Position;
            if (dir.sqrMagnitude > 0.0225f)
            {
                dir = dir.normalized;
                if (dir.y > 0f)
                {
                    dir.y = -0.01f;
                    dir = dir.normalized;
                }
                BotOwner.Steering.LookToDirection(dir);
            }
        }

        private void UpdateLootWeapon()
        {
            // Get the corpse's equipment
            var corpsePlayer = GetCorpsePlayer();
            if (corpsePlayer == null)
            {
                _currentState = State.Complete;
                return;
            }

            var corpseEquipment = corpsePlayer.InventoryController?.Inventory?.Equipment;
            var myInventory = BotOwner.GetPlayer?.InventoryController;

            if (corpseEquipment == null || myInventory == null)
            {
                _currentState = State.CheckBackpack;
                return;
            }

            _itemsCache.Clear();

            // Check for weapons
            AddItemIfNotNull(_itemsCache, corpseEquipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem);
            AddItemIfNotNull(_itemsCache, corpseEquipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem);
            AddItemIfNotNull(_itemsCache, corpseEquipment.GetSlot(EquipmentSlot.Holster)?.ContainedItem);

            foreach (var item in _itemsCache)
            {
                var targetSlot = myInventory.FindSlotToPickUp(item);
                if (targetSlot != null)
                {
                    TryMoveItem(item, targetSlot, myInventory);
                    SetPauseTime(MOVE_ITEMS_DELAY);
                    break;
                }
            }

            _currentState = State.CheckBackpack;
        }

        private void UpdateCheckBackpack()
        {
            var corpsePlayer = GetCorpsePlayer();
            if (corpsePlayer == null)
            {
                _currentState = State.LootAllCalculations;
                return;
            }

            var myInventory = BotOwner.GetPlayer?.InventoryController;
            var myEquipment = myInventory?.Inventory?.Equipment;
            var corpseEquipment = corpsePlayer.InventoryController?.Inventory?.Equipment;

            if (myEquipment == null || corpseEquipment == null)
            {
                _currentState = State.LootAllCalculations;
                return;
            }

            // Check if I need a backpack
            var myBackpackSlot = myEquipment.GetSlot(EquipmentSlot.Backpack);
            if (myBackpackSlot?.ContainedItem == null)
            {
                var corpseBackpack = corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
                if (corpseBackpack != null)
                {
                    TryMoveItem(corpseBackpack, myBackpackSlot.CreateItemAddress(), myInventory);
                    SetPauseTime(MOVE_ITEMS_DELAY);
                }
            }

            _currentState = State.LootAllCalculations;
        }

        private void UpdateLootAllCalculations()
        {
            var corpsePlayer = GetCorpsePlayer();
            if (corpsePlayer == null)
            {
                _currentState = State.Complete;
                return;
            }

            var corpseEquipment = corpsePlayer.InventoryController?.Inventory?.Equipment;
            var myInventory = BotOwner.GetPlayer?.InventoryController;
            var myEquipment = myInventory?.Inventory?.Equipment;

            if (corpseEquipment == null || myEquipment == null)
            {
                _currentState = State.Complete;
                return;
            }

            _itemsCache.Clear();
            _containersCache.Clear();

            // Gather items from corpse's containers
            AddContainerIfNotNull(_containersCache, corpseEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containersCache, corpseEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containersCache, corpseEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem);

            foreach (var container in _containersCache)
            {
                container.GetAllAssembledItems(_itemsCache);
            }

            // Remove containers from item list
            for (int i = _itemsCache.Count - 1; i >= 0; i--)
            {
                if (_itemsCache[i] is SearchableItemItemClass)
                {
                    _itemsCache.RemoveAt(i);
                }
            }

            if (_itemsCache.Count == 0)
            {
                _currentState = State.Complete;
                return;
            }

            // Sort by value-weighted random selection (like BotDeadBodyWork)
            _weightedItems.Clear();
            foreach (var item in _itemsCache)
            {
                var cellSize = item.CalculateCellSize();
                // Issue 17 Fix: Guard against division by zero
                // Fifth Review Fix (Issue 75): Use long to prevent integer overflow on high-value items
                int slotCount = cellSize.X * cellSize.Y;
                long priceRaw = item.Template.CreditsPrice;
                int pricePerSlot = slotCount > 0 ? (int)Math.Min(priceRaw / slotCount, int.MaxValue) : 0;
                // Avoid division by zero in Pow calculation
                float weight = pricePerSlot > 0
                    ? Mathf.Pow(UnityEngine.Random.Range(0f, 1f), 1f / pricePerSlot)
                    : UnityEngine.Random.Range(0f, 1f);
                _weightedItems.Add((weight, item));
            }
            // Seventh Review Fix (Issue 167): Use static delegate to avoid lambda allocation
            _weightedItems.Sort(_weightComparer);

            _itemsCache.Clear();
            foreach (var (_, item) in _weightedItems)
            {
                _itemsCache.Add(item);
            }

            _takeItemsLeft = UnityEngine.Random.Range(1, _itemsCache.Count + 1);
            _curItemIndex = 0;

            // Get my containers for receiving items
            _containersCache.Clear();
            AddContainerIfNotNull(_containersCache, myEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containersCache, myEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containersCache, myEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem);

            SetPauseTime(CHECK_ITEMS_DELAY);
            _currentState = State.LootAllItemsMoving;
        }

        private void UpdateLootAllItemsMoving()
        {
            var myInventory = BotOwner.GetPlayer?.InventoryController;
            if (myInventory == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Healthcare Critical: CALLBACK-ONLY completion mechanism
            // The previous code used BOTH timeout AND callback which caused races:
            // - Callback clears _moveInProgress
            // - Timeout also clears _moveInProgress and advances index
            // - Result: index advanced twice, items skipped, state corruption
            if (_moveInProgress)
            {
                // Seventh Review Fix (Issue 14): Emergency timeout as last-resort safety net
                // If callback never fires (due to EFT inventory bug), prevent infinite stuck state
                if (Time.time - _moveOperationStartTime > MOVE_OPERATION_TIMEOUT)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[{BotOwner?.name ?? "Unknown"}] Move operation timed out after {MOVE_OPERATION_TIMEOUT}s - " +
                        "callback may have failed. Aborting corpse looting to prevent stuck state.");
                    _moveInProgress = false;
                    _currentState = State.Complete;
                    return;
                }
                // Wait for callback to clear the flag
                return;
            }

            // Check if done
            if (_curItemIndex >= _itemsCache.Count || _takeItemsLeft <= 0)
            {
                _currentState = State.Complete;
                return;
            }

            // Try to move next item
            var item = _itemsCache[_curItemIndex];
            var moveResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                item,
                myInventory,
                _containersCache,
                InteractionsHandlerClass.EMoveItemOrder.PickUp,
                true);

            if (moveResult.Succeeded)
            {
                // Set flag BEFORE starting transaction to prevent race
                _moveInProgress = true;
                // Seventh Review Fix (Issue 14): Track start time for emergency timeout
                _moveOperationStartTime = Time.time;

                myInventory.TryRunNetworkTransaction(moveResult, (result) =>
                {
                    // Healthcare Critical: Check _isStopped to prevent modifying state after Stop()
                    if (_isStopped) return;

                    // Always advance on completion (success or failure)
                    _moveInProgress = false;
                    _curItemIndex++;

                    if (result.Succeed)
                    {
                        _takeItemsLeft--;
                    }
                    else
                    {
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name}] Item move transaction failed: {result.Error}");
                    }
                });
            }
            else
            {
                _curItemIndex++;
            }
        }

        private Player GetCorpsePlayer()
        {
            // The target object should be a GClass386 (dead body reference)
            // which has a Player property
            if (_target?.Target == null)
            {
                return null;
            }

            // Standards Compliance Fix: Cache reflection PropertyInfo per-type to avoid hot-path allocation
            // Fifth Review Fix (Issue 47): Use ConcurrentDictionary.GetOrAdd for lock-free thread-safe access
            try
            {
                var bodyType = _target.Target.GetType();
                var playerProp = _playerPropertyCache.GetOrAdd(bodyType, type => type.GetProperty("Player"));

                return playerProp?.GetValue(_target.Target) as Player;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"GetCorpsePlayer reflection failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private void TryMoveItem(Item item, ItemAddress targetAddress, InventoryController inventory)
        {
            var moveResult = InteractionsHandlerClass.Move(item, targetAddress, inventory, true);
            if (moveResult.Succeeded)
            {
                inventory.TryRunNetworkTransaction(moveResult, (result) =>
                {
                    // Healthcare Critical: Check _isStopped to prevent logging after Stop()
                    if (_isStopped) return;

                    if (result.Failed)
                    {
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name}] TryMoveItem transaction failed: {result.Error}");
                    }
                });
            }
        }

        private void AddItemIfNotNull(List<Item> list, Item item)
        {
            if (item != null)
            {
                list.Add(item);
            }
        }

        private void AddContainerIfNotNull(List<CompoundItem> list, CompoundItem container)
        {
            if (container != null)
            {
                list.Add(container);
            }
        }

        public bool IsComplete => _currentState == State.Complete;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("LootCorpseLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Items Left: {_takeItemsLeft}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
