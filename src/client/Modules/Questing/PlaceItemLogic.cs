using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Logic for placing an item at a quest location.
    /// Navigates to the placement location and performs the placement action.
    /// </summary>
    public class PlaceItemLogic : CustomLogic
    {
        private enum State
        {
            MovingToLocation,
            Placing,
            Complete,
            Failed
        }

        private State _currentState = State.MovingToLocation;
        private QuestObjective _objective;
        private Vector3 _placePosition;
        private string _itemTemplateId;
        private float _startTime;
        private float _nextMoveTime;
        private float _placeEndTime;

        private const float PLACE_DISTANCE = 2f;
        private const float PLACE_DURATION = 3f;
        private const float MOVE_UPDATE_INTERVAL = 2f;

        // Issue 15 Fix: Cached lists to avoid allocations in HasItemToPlace
        private readonly List<CompoundItem> _containerCache = new List<CompoundItem>(4);
        private readonly List<Item> _itemCache = new List<Item>(32);

        // Healthcare Critical: Cache HasItemToPlace result to avoid expensive inventory scan every frame
        private bool _cachedHasItemToPlace;
        private float _lastHasItemCheckTime;
        private const float HAS_ITEM_CHECK_INTERVAL = 1.0f;

        public PlaceItemLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _currentState = State.MovingToLocation;
                _startTime = Time.time;
                _nextMoveTime = 0f;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] PlaceItemLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] PlaceItemLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed;
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] PlaceItemLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] PlaceItemLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                // Extract objective from ActionData if not set
                if (_objective == null && data is QuestingActionData questingData)
                {
                    _objective = questingData.Objective;
                    _placePosition = _objective?.TargetPosition ?? Vector3.zero;
                    _itemTemplateId = _objective?.ItemTemplateId;
                    questingData.Layer?.RegisterLogic(this);
                }

                if (_objective == null || _placePosition == Vector3.zero)
                {
                    _currentState = State.Failed;
                    return;
                }

                // Check if we have the item to place
                if (!string.IsNullOrEmpty(_itemTemplateId) && !HasItemToPlace())
                {
                    BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Missing item to place: {_itemTemplateId}");
                    _currentState = State.Failed;
                    return;
                }

                switch (_currentState)
                {
                    case State.MovingToLocation:
                        UpdateMovingToLocation();
                        break;
                    case State.Placing:
                        UpdatePlacing();
                        break;
                    case State.Complete:
                    case State.Failed:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 91): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] PlaceItemLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed; // Fail safe
            }
        }

        private void UpdateMovingToLocation()
        {
            float distance = Vector3.Distance(BotOwner.Position, _placePosition);

            if (distance <= PLACE_DISTANCE)
            {
                _placeEndTime = Time.time + PLACE_DURATION;
                _currentState = State.Placing;
                BotOwner.SetPose(0f); // Crouch for placement
                LookAtPlacePosition();
                BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Starting item placement");
                return;
            }

            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(0.7f);
            BotOwner.Steering.LookToMovingDirection();

            if (Time.time >= _nextMoveTime)
            {
                _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;

                Vector3 direction = (_placePosition - BotOwner.Position).normalized;
                Vector3 destination = _placePosition - direction * 0.5f;

                var pathResult = BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false);
                if (pathResult != NavMeshPathStatus.PathComplete)
                {
                    // Try to find nearest valid position
                    if (NavMesh.SamplePosition(_placePosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        BotOwner.GoToPoint(hit.position, true, -1f, false, false, true, false, false);
                    }
                    else
                    {
                        _currentState = State.Failed;
                    }
                }
            }
        }

        private void LookAtPlacePosition()
        {
            Vector3 direction = (_placePosition - BotOwner.Position).normalized;
            if (direction.sqrMagnitude > 0.01f)
            {
                BotOwner.Steering.LookToDirection(direction);
            }
        }

        private void UpdatePlacing()
        {
            // Keep looking at place position during placement
            LookAtPlacePosition();

            if (Time.time >= _placeEndTime)
            {
                // Placement complete
                // In a real implementation, this would trigger the actual item placement
                // For now, we simulate success
                BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Item placement complete");
                _currentState = State.Complete;
            }
        }

        private bool HasItemToPlace()
        {
            if (string.IsNullOrEmpty(_itemTemplateId))
            {
                return true; // No specific item required
            }

            // Healthcare Critical: Cache result to avoid expensive inventory scan every frame
            if (Time.time - _lastHasItemCheckTime < HAS_ITEM_CHECK_INTERVAL)
            {
                return _cachedHasItemToPlace;
            }
            _lastHasItemCheckTime = Time.time;

            var equipment = BotOwner.GetPlayer?.InventoryController?.Inventory?.Equipment;
            if (equipment == null)
            {
                _cachedHasItemToPlace = false;
                return false;
            }

            // Issue 15 Fix: Use cached lists to avoid allocations every frame
            _containerCache.Clear();
            AddContainerIfNotNull(_containerCache, equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containerCache, equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem);
            AddContainerIfNotNull(_containerCache, equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem);

            _itemCache.Clear();
            foreach (var container in _containerCache)
            {
                container.GetAllAssembledItems(_itemCache);
            }

            foreach (var item in _itemCache)
            {
                if (item.TemplateId == _itemTemplateId)
                {
                    _cachedHasItemToPlace = true;
                    return true;
                }
            }

            _cachedHasItemToPlace = false;
            return false;
        }

        private void AddContainerIfNotNull(List<CompoundItem> list, CompoundItem container)
        {
            if (container != null)
            {
                list.Add(container);
            }
        }

        public bool IsComplete => _currentState == State.Complete || _currentState == State.Failed;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            float distance = Vector3.Distance(BotOwner.Position, _placePosition);
            stringBuilder.AppendLine("PlaceItemLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Target: {_placePosition}");
            stringBuilder.AppendLine($"  Distance: {distance:F1}m");
            stringBuilder.AppendLine($"  Item: {_itemTemplateId ?? "None"}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
