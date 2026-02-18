using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Logic for searching an area to find a specific item.
    /// Combines movement to search locations with container checking.
    /// </summary>
    public class FindItemLogic : CustomLogic
    {
        private enum State
        {
            SelectingSearchLocation,
            MovingToLocation,
            SearchingContainers,
            ItemFound,
            Complete,
            Failed
        }

        private State _currentState = State.SelectingSearchLocation;
        private QuestObjective _objective;
        private string _targetItemTemplateId;
        private Vector3 _searchCenter;
        private float _searchRadius;
        private float _startTime;
        private float _nextMoveTime;
        private float _searchEndTime;
        private Vector3 _currentSearchLocation;
        private int _locationsSearched;
        private int _maxSearchLocations;

        // Standards Compliance Fix: Use HashSet for O(1) Contains() instead of O(n) List
        private readonly HashSet<LootableContainer> _nearbyContainers = new HashSet<LootableContainer>();

        /// <summary>Radius around bot position to scan for containers (meters).</summary>
        private const float SEARCH_RADIUS = 5f;
        /// <summary>Time spent "searching" at each location before moving on (seconds).</summary>
        private const float SEARCH_DURATION_PER_LOCATION = 3f;
        /// <summary>Interval between pathfinding updates while moving (seconds).</summary>
        private const float MOVE_UPDATE_INTERVAL = 2f;
        /// <summary>Default search area radius when objective doesn't specify one (meters).</summary>
        private const float DEFAULT_AREA_RADIUS = 50f;

        // Issue 9 Fix: Pre-allocated buffer for Physics.OverlapSphereNonAlloc
        // Third Review Fix: Changed from static to instance-based to prevent race conditions
        private readonly Collider[] _colliderBuffer = new Collider[32];

        // Issue 12 Fix: Cached NavMeshPath to avoid allocations in hot path
        private readonly NavMeshPath _cachedNavPath = new NavMeshPath();

        // Issue 15 Fix: Cached lists to avoid allocations in HasTargetItem
        private readonly List<CompoundItem> _containerCache = new List<CompoundItem>(4);
        private readonly List<Item> _itemCache = new List<Item>(32);

        // Healthcare Critical: Cache HasTargetItem result to avoid GetAllAssembledItems() every frame
        // GetAllAssembledItems is O(n) over all items and creates garbage - catastrophic in Update loop
        private bool _cachedHasTargetItem;
        private float _lastHasTargetItemCheckTime;
        /// <summary>Interval between inventory checks for target item (seconds).</summary>
        private const float HAS_TARGET_ITEM_CHECK_INTERVAL = 1.0f;

        public FindItemLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _currentState = State.SelectingSearchLocation;
                _startTime = Time.time;
                _locationsSearched = 0;
                _maxSearchLocations = Random.Range(3, 6);
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] FindItemLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] FindItemLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed;
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] FindItemLogic stopped");
                _nearbyContainers.Clear();
                _containerCache.Clear();
                _itemCache.Clear();
                _cachedNavPath.ClearCorners();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] FindItemLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
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
                    _targetItemTemplateId = _objective?.ItemTemplateId;
                    _searchCenter = _objective?.TargetPosition ?? BotOwner.Position;
                    _searchRadius = _objective?.CompletionRadius > 0 ? _objective.CompletionRadius : DEFAULT_AREA_RADIUS;
                    questingData.Layer?.RegisterLogic(this);
                }

                // Check if we already have the item
                if (HasTargetItem())
                {
                    _currentState = State.ItemFound;
                }

                // Check if searched too many locations
                if (_locationsSearched >= _maxSearchLocations)
                {
                    _currentState = State.Complete;
                    return;
                }

                switch (_currentState)
                {
                    case State.SelectingSearchLocation:
                        SelectNextSearchLocation();
                        break;
                    case State.MovingToLocation:
                        UpdateMovingToLocation();
                        break;
                    case State.SearchingContainers:
                        UpdateSearchingContainers();
                        break;
                    case State.ItemFound:
                        _currentState = State.Complete;
                        break;
                    case State.Complete:
                    case State.Failed:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Standards Compliance Fix: Include stack trace per ERROR_HANDLING.md
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] FindItemLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                // Sixth Review Fix (Issue 104): Set to Failed state instead of Complete for consistency
                _currentState = State.Failed; // Fail safe
            }
        }

        private void SelectNextSearchLocation()
        {
            // Try to find a valid search location
            for (int attempts = 0; attempts < 10; attempts++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * _searchRadius;
                Vector3 candidatePoint = _searchCenter + new Vector3(randomOffset.x, 0f, randomOffset.y);

                if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    // Issue 12 Fix: Use cached NavMeshPath to avoid allocation
                    _cachedNavPath.ClearCorners();
                    if (NavMesh.CalculatePath(BotOwner.Position, hit.position, NavMesh.AllAreas, _cachedNavPath) &&
                        _cachedNavPath.status == NavMeshPathStatus.PathComplete)
                    {
                        _currentSearchLocation = hit.position;
                        _currentState = State.MovingToLocation;
                        _nextMoveTime = 0f;
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Selected search location: {_currentSearchLocation}");
                        return;
                    }
                }
            }

            // Couldn't find valid location
            _currentState = State.Complete;
        }

        private void UpdateMovingToLocation()
        {
            float distance = Vector3.Distance(BotOwner.Position, _currentSearchLocation);

            if (distance <= SEARCH_RADIUS)
            {
                _locationsSearched++;
                _searchEndTime = Time.time + SEARCH_DURATION_PER_LOCATION;
                ScanForContainers();
                _currentState = State.SearchingContainers;
                BotOwner.SetPose(0.5f);
                return;
            }

            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(0.7f);
            // LookToMovingDirection removed — blocks EFT's LookSensor from detecting enemies

            if (Time.time >= _nextMoveTime)
            {
                _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;
                BotOwner.GoToPoint(_currentSearchLocation, true, -1f, false, false, true, false, false);
            }
        }

        private void ScanForContainers()
        {
            _nearbyContainers.Clear();

            // Issue 9 Fix: Use OverlapSphereNonAlloc with pre-allocated buffer
            int colliderCount = Physics.OverlapSphereNonAlloc(BotOwner.Position, SEARCH_RADIUS, _colliderBuffer);
            for (int i = 0; i < colliderCount; i++)
            {
                var collider = _colliderBuffer[i];
                if (collider == null) continue;

                var container = collider.GetComponentInParent<LootableContainer>();
                if (container != null)
                {
                    // HashSet.Add returns false if already present - O(1) operation
                    _nearbyContainers.Add(container);
                }
            }

            BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Found {_nearbyContainers.Count} containers to search");
        }

        private void UpdateSearchingContainers()
        {
            // Check if we found the item
            if (HasTargetItem())
            {
                _currentState = State.ItemFound;
                return;
            }

            // Check if search time expired
            if (Time.time >= _searchEndTime)
            {
                _currentState = State.SelectingSearchLocation;
                return;
            }

            // Look around while "searching"
            float progress = 1f - ((_searchEndTime - Time.time) / SEARCH_DURATION_PER_LOCATION);
            float angle = progress * 360f;
            Vector3 lookDirection = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            BotOwner.Steering.LookToDirection(lookDirection);
        }

        private bool HasTargetItem()
        {
            if (string.IsNullOrEmpty(_targetItemTemplateId))
            {
                return false;
            }

            // Healthcare Critical: Cache result to avoid GetAllAssembledItems() every frame
            // GetAllAssembledItems is expensive O(n) operation that shouldn't run at 60fps
            if (Time.time - _lastHasTargetItemCheckTime < HAS_TARGET_ITEM_CHECK_INTERVAL)
            {
                return _cachedHasTargetItem;
            }
            _lastHasTargetItemCheckTime = Time.time;

            var equipment = BotOwner.GetPlayer?.InventoryController?.Inventory?.Equipment;
            if (equipment == null)
            {
                _cachedHasTargetItem = false;
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
                if (item.TemplateId == _targetItemTemplateId)
                {
                    _cachedHasTargetItem = true;
                    return true;
                }
            }

            _cachedHasTargetItem = false;
            return false;
        }

        private void AddContainerIfNotNull(List<CompoundItem> list, CompoundItem container)
        {
            if (container != null)
            {
                list.Add(container);
            }
        }

        public bool IsComplete => _currentState == State.Complete || _currentState == State.Failed || _currentState == State.ItemFound;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("FindItemLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Locations: {_locationsSearched}/{_maxSearchLocations}");
            stringBuilder.AppendLine($"  Target Item: {_targetItemTemplateId ?? "None"}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
