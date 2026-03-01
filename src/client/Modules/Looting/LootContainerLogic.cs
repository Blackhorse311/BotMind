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
    /// Logic for a bot to loot a container (crate, bag, etc.).
    /// Based on BotLootOpener patterns from EFT.
    /// </summary>
    public class LootContainerLogic : CustomLogic
    {
        private enum State
        {
            MovingToContainer,
            OpeningContainer,
            LootingItems,
            Complete
        }

        private State _currentState = State.MovingToContainer;
        private LootTarget _target;
        private float _startTime;
        private float _nextMoveTime;
        private float _unpauseTime;
        private float _interactionEndTime;
        private Vector3 _cachedOffset;

        // Looting state
        // Seventh Review Fix (Issue 119-120): Add readonly modifier for immutable references
        private readonly List<Item> _itemsCache = new List<Item>(32);
        private readonly List<CompoundItem> _myContainers = new List<CompoundItem>(4);
        private int _curItemIndex;
        private int _takeItemsLeft;
        private bool _moveInProgress;
        // Healthcare Critical: Track stopped state to prevent callbacks modifying state after Stop()
        private volatile bool _isStopped;

        private const float CLOSE_DISTANCE = 2.0f;
        private const float INTERACTION_DURATION = 2.5f;
        private const float MOVE_ITEMS_DELAY = 0.4f;
        // Stuck detection: if distance hasn't decreased after several GoToPoint attempts, abort
        private float _lastMoveDistance = float.MaxValue;
        private int _noProgressCount;
        private const int MAX_NO_PROGRESS = 5;
        /// <summary>
        /// Bug Fix: Emergency timeout for callback-only completion.
        /// If the inventory transaction callback never fires (due to EFT bug), this prevents
        /// the bot from being stuck forever. Matches LootCorpseLogic's MOVE_OPERATION_TIMEOUT.
        /// </summary>
        private const float MOVE_OPERATION_TIMEOUT = 30f;
        private float _moveOperationStartTime;
        /// <summary>
        /// Overall timeout for the entire container looting operation.
        /// Prevents bots from being stuck indefinitely when they can't reach a container
        /// (e.g., locked door, geometry obstruction) or the interaction bugs out.
        /// </summary>
        private const float OVERALL_TIMEOUT = 60f;
        /// <summary>
        /// v1.5.0 Fix: Guard flag to prevent timeout warning from logging every frame.
        /// Without this, a single timed-out bot produces ~10K+ log lines per minute.
        /// </summary>
        private bool _hasTimedOut;

        // Seventh Review Fix (Issue 166): Static comparison delegate to avoid lambda allocation in Sort
        private static readonly System.Comparison<Item> _valueComparer = (a, b) =>
        {
            int valueA = a.Template?.CreditsPrice ?? 0;
            int valueB = b.Template?.CreditsPrice ?? 0;
            return valueB.CompareTo(valueA);
        };

        public LootContainerLogic(BotOwner botOwner) : base(botOwner)
        {
            float x = UnityEngine.Random.Range(0.1f, 0.25f) * Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));
            float z = UnityEngine.Random.Range(0.1f, 0.25f) * Mathf.Sign(UnityEngine.Random.Range(-1f, 1f));
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
                _currentState = State.MovingToContainer;
                _startTime = Time.time;
                _unpauseTime = -1f;
                _interactionEndTime = -1f;
                _isStopped = false; // Reset stopped flag on start
                _moveInProgress = false; // Reset move flag on start
                _hasTimedOut = false; // Reset timeout guard on start
                _lastMoveDistance = float.MaxValue;
                _noProgressCount = 0;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootContainerLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootContainerLogic.Start error: {ex.Message}\n{ex.StackTrace}");
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
                _moveInProgress = false; // Reset to prevent stuck state on restart

                // v1.4.0 Fix: Reset pose/speed to defaults
                if (BotOwner != null)
                {
                    BotOwner.SetPose(1f);
                    BotOwner.SetTargetMoveSpeed(1f);
                }

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] LootContainerLogic stopped");
                _itemsCache.Clear();
                _myContainers.Clear();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] LootContainerLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
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

                // Overall timeout: prevent being stuck on any single container forever
                // v1.5.0 Fix: Log only once — without the guard this fires every frame
                if (Time.time - _startTime > OVERALL_TIMEOUT)
                {
                    if (!_hasTimedOut)
                    {
                        _hasTimedOut = true;
                        BotMindPlugin.Log?.LogWarning(
                            $"[{BotOwner?.name ?? "Unknown"}] Container looting timed out after {OVERALL_TIMEOUT}s in state {_currentState}. Aborting.");
                    }
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
                    case State.MovingToContainer:
                        UpdateMovingToContainer();
                        break;
                    case State.OpeningContainer:
                        UpdateOpeningContainer();
                        break;
                    case State.LootingItems:
                        UpdateLootingItems();
                        break;
                    case State.Complete:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Fifth Review Fix (Issue 48): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] LootContainerLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Complete; // Fail safe
            }
        }

        private void SetPauseTime(float delay)
        {
            _unpauseTime = Time.time + delay;
        }

        private void UpdateMovingToContainer()
        {
            float dist = Vector3.Distance(BotOwner.Position, _target.Position);

            if (dist < CLOSE_DISTANCE)
            {
                // Arrived - start opening container
                BotOwner.SetPose(0.5f);
                LookAtContainer();
                _currentState = State.OpeningContainer;
                return;
            }

            // Navigate to container
            BotOwner.SetPose(1f);
            // v1.4.0 Fix: Sprint when far, jog when close — 0.8f constant was too slow
            BotOwner.SetTargetMoveSpeed(dist > 15f ? 1f : 0.85f);
            // LookToMovingDirection removed — blocks EFT's LookSensor from detecting enemies

            if (_nextMoveTime < Time.time)
            {
                _nextMoveTime = Time.time + 3f;

                // Stuck detection: abort if not making progress toward the container
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
                            $"[{BotOwner?.name ?? "Unknown"}] Stuck moving to container ({dist:F1}m away, no progress for {MAX_NO_PROGRESS} attempts). Aborting.");
                        _currentState = State.Complete;
                        return;
                    }
                }
                _lastMoveDistance = dist;

                Vector3 targetPos = _target.Position + _cachedOffset;
                Vector3 dir = (targetPos - BotOwner.Position).normalized;
                Vector3 destination = targetPos - dir * 0.5f;

                if (BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false) != NavMeshPathStatus.PathComplete)
                {
                    _currentState = State.Complete;
                }
            }
        }

        private void LookAtContainer()
        {
            Vector3 dir = _target.Position - BotOwner.Position;
            if (dir.sqrMagnitude > 0.01f)
            {
                dir = dir.normalized;
                BotOwner.Steering.LookToDirection(dir);
            }
        }

        private void UpdateOpeningContainer()
        {
            var container = _target.Target as LootableContainer;
            if (container == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Check if we're still in the interaction animation
            if (_interactionEndTime > 0f)
            {
                if (Time.time < _interactionEndTime)
                {
                    return;
                }
                _interactionEndTime = -1f;

                // Interaction complete - move to looting
                PrepareLooting(container);
                _currentState = State.LootingItems;
                return;
            }

            // Start the door/container interaction using EFT's native method
            // Based on BotLootOpener.Interact pattern
            if (container.DoorState == EDoorState.Shut)
            {
                var player = BotOwner.GetPlayer;
                // Sixth Review Fix (Issue 112): Check CurrentManagedState for null
                if (player != null && player.CurrentManagedState != null)
                {
                    var interactionResult = new InteractionResult(EInteractionType.Open);
                    player.CurrentManagedState.StartDoorInteraction(container, interactionResult, null);
                    _interactionEndTime = Time.time + INTERACTION_DURATION;
                }
                else
                {
                    _currentState = State.Complete;
                }
            }
            else
            {
                // Container already open - go directly to looting
                PrepareLooting(container);
                _currentState = State.LootingItems;
            }
        }

        private void PrepareLooting(LootableContainer container)
        {
            var myInventory = BotOwner.GetPlayer?.InventoryController;
            var myEquipment = myInventory?.Inventory?.Equipment;

            if (myEquipment == null)
            {
                return;
            }

            // Third Review Fix: Added null check for container.ItemOwner
            if (container.ItemOwner == null || container.ItemOwner.RootItem == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Get items from the container
            _itemsCache.Clear();
            var containerItem = container.ItemOwner.RootItem as CompoundItem;
            if (containerItem != null)
            {
                containerItem.GetAllAssembledItems(_itemsCache);

                // Remove nested containers from item list
                for (int i = _itemsCache.Count - 1; i >= 0; i--)
                {
                    if (_itemsCache[i] is SearchableItemItemClass)
                    {
                        _itemsCache.RemoveAt(i);
                    }
                }
            }

            if (_itemsCache.Count == 0)
            {
                return;
            }

            // Sort items by value (highest value first)
            // Seventh Review Fix (Issue 166): Use static delegate to avoid lambda allocation
            _itemsCache.Sort(_valueComparer);

            // Fifth Review Fix (Issue 77): Random.Range upper bound is exclusive, use 7 to allow taking up to 6 items
            _takeItemsLeft = UnityEngine.Random.Range(1, Mathf.Min(_itemsCache.Count + 1, 7));
            _curItemIndex = 0;

            // Get my containers for receiving items
            _myContainers.Clear();
            AddContainerIfNotNull(_myContainers, myEquipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_myContainers, myEquipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_myContainers, myEquipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem);

            SetPauseTime(0.3f);
        }

        private void UpdateLootingItems()
        {
            var myInventory = BotOwner.GetPlayer?.InventoryController;
            if (myInventory == null)
            {
                _currentState = State.Complete;
                return;
            }

            // Bug Fix: Added emergency timeout (was missing, unlike LootCorpseLogic).
            // If callback never fires due to EFT inventory bug, prevent infinite stuck state.
            if (_moveInProgress)
            {
                if (Time.time - _moveOperationStartTime > MOVE_OPERATION_TIMEOUT)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[{BotOwner?.name ?? "Unknown"}] Container loot move timed out after {MOVE_OPERATION_TIMEOUT}s - " +
                        "callback may have failed. Completing container looting to prevent stuck state.");
                    _moveInProgress = false;
                    _currentState = State.Complete;
                    return;
                }
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

            // Check minimum value
            int itemValue = item.Template?.CreditsPrice ?? 0;
            if (itemValue < Configuration.BotMindConfig.MinItemValue.Value)
            {
                _curItemIndex++;
                return;
            }

            var moveResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                item,
                myInventory,
                _myContainers,
                InteractionsHandlerClass.EMoveItemOrder.PickUp,
                true);

            if (moveResult.Succeeded)
            {
                // Set flag BEFORE starting transaction to prevent race
                _moveInProgress = true;
                // Bug Fix: Track start time for emergency timeout
                _moveOperationStartTime = Time.time;

                myInventory.TryRunNetworkTransaction(moveResult, (result) =>
                {
                    // Healthcare Critical: Check _isStopped to prevent state modification after Stop()
                    if (_isStopped) return;

                    // Always advance on completion
                    _moveInProgress = false;
                    _curItemIndex++;

                    if (result.Succeed)
                    {
                        _takeItemsLeft--;
                    }
                    else
                    {
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name}] Container loot transaction failed: {result.Error}");
                    }
                });
            }
            else
            {
                _curItemIndex++;
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
            stringBuilder.AppendLine("LootContainerLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Items Left: {_takeItemsLeft}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
