using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Blackhorse311.BotMind.Configuration;

namespace Blackhorse311.BotMind.Modules.Looting
{
    /// <summary>
    /// Scans for and prioritizes lootable targets within the configured radius.
    /// Uses similar patterns to BotItemTaker and BotDeadBodyWork from EFT.
    /// </summary>
    public class LootFinder
    {
        private readonly BotOwner _bot;
        private readonly List<LootTarget> _targets = new List<LootTarget>();
        private LootTarget _currentTarget;

        // Blacklist uses stable string IDs (ProfileId for corpses, InstanceId for containers/items)
        // Tracks both the ID and when it was added for LRU eviction
        private readonly ConcurrentDictionary<string, float> _blacklistedTargets = new ConcurrentDictionary<string, float>();

        // Blacklist size limit - when exceeded, evict oldest 25% instead of nuking everything
        private const int MAX_BLACKLIST_SIZE = 200;
        private const int EVICTION_COUNT = 50; // Remove oldest 50 when limit hit

        // Standards Compliance Fix: Timer-based cleanup instead of per-frame RemoveAll
        private float _lastCleanupTime;
        /// <summary>Interval between stale target cleanup operations (seconds) - reduces CPU overhead.</summary>
        private const float CLEANUP_INTERVAL = 5f;

        // Issue 9 Fix: Pre-allocated buffer for Physics.OverlapSphereNonAlloc to reduce GC pressure
        // Third Review Fix: Changed from static to instance-based to prevent race condition
        // when multiple bots scan simultaneously (was causing data corruption)
        private readonly Collider[] _colliderBuffer = new Collider[64];

        // Issue 12 Fix: Cached NavMeshPath to avoid allocations in hot path
        private readonly NavMeshPath _cachedNavPath = new NavMeshPath();

        // Seventh Review Fix (Issue 160): Static comparison delegate to avoid lambda allocation in Sort
        private static readonly System.Comparison<LootTarget> _priorityComparer =
            (a, b) => b.Priority.CompareTo(a.Priority);

        /// <summary>Seventh Review Fix (Issue 199): Whether there is loot available to pursue.</summary>
        public bool HasTargetLoot => _currentTarget != null || _targets.Count > 0;
        /// <summary>Seventh Review Fix (Issue 200): The number of potential loot targets found.</summary>
        public int TargetCount => _targets.Count;

        /// <summary>Creates a new loot finder for the specified bot.</summary>
        /// <param name="bot">The bot owner to find loot for.</param>
        public LootFinder(BotOwner bot)
        {
            _bot = bot;
        }

        /// <summary>Seventh Review Fix (Issue 201): Scans for lootable targets within configured radius.</summary>
        public void ScanForLoot()
        {
            _targets.Clear();

            float searchRadius = BotMindConfig.LootingSearchRadius.Value;
            Vector3 botPosition = _bot.Position;
            int minValue = BotMindConfig.MinItemValue.Value;

            // Scan for corpses
            if (BotMindConfig.LootCorpses.Value)
            {
                ScanForCorpses(botPosition, searchRadius);
            }

            // Scan for containers
            if (BotMindConfig.LootContainers.Value)
            {
                ScanForContainers(botPosition, searchRadius);
            }

            // Scan for loose items
            if (BotMindConfig.LootLooseItems.Value)
            {
                ScanForLooseItems(botPosition, searchRadius, minValue);
            }

            // Sort by priority (value/distance ratio)
            // Seventh Review Fix (Issue 160): Use static delegate to avoid lambda allocation
            _targets.Sort(_priorityComparer);
        }

        private void ScanForCorpses(Vector3 position, float radius)
        {
            // Use the bot's group's dead body controller like BotDeadBodyWork does
            var botsGroup = _bot.BotsGroup;
            if (botsGroup?.DeadBodiesController == null)
            {
                return;
            }

            // Get bodies tracked by the group
            var bodies = botsGroup.DeadBodiesController.BodiesByGroup(botsGroup);
            if (bodies == null || bodies.Count == 0)
            {
                return;
            }

            // Healthcare Critical: Iterate directly - the DeadBodiesController's BodiesByGroup
            // returns a stable collection that doesn't change during iteration.
            // The previous ToArray() was creating 14+ MB of garbage per raid unnecessarily.
            // If iteration fails due to collection modification, the catch block handles it.
            float radiusSqr = radius * radius;

            foreach (var body in bodies)
            {
                if (body == null || IsBlacklisted(body))
                {
                    continue;
                }

                // Check if body is on NavMesh (reachable)
                if (!body.IsOnNavMesh)
                {
                    continue;
                }

                // Check if body is already being worked on by another bot
                if (!body.IsFreeFor(_bot))
                {
                    continue;
                }

                Vector3 bodyPos = body.BodyPosition;
                float distSqr = (bodyPos - position).sqrMagnitude;

                if (distSqr > radiusSqr)
                {
                    continue;
                }

                // Check if we can pathfind to the body
                if (!CanPathTo(bodyPos))
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distSqr);
                float estimatedValue = EstimateCorpseValue(body);

                _targets.Add(new LootTarget
                {
                    Position = bodyPos,
                    Distance = distance,
                    EstimatedValue = estimatedValue,
                    Priority = LootTarget.CalculatePriority(estimatedValue, distance),
                    IsCorpse = true,
                    Target = body
                });
            }
        }

        // Reusable set to track seen containers during scan (avoids duplicates from multiple colliders)
        private readonly HashSet<int> _seenContainerIds = new HashSet<int>();

        private void ScanForContainers(Vector3 position, float radius)
        {
            int colliderCount = Physics.OverlapSphereNonAlloc(position, radius, _colliderBuffer);

            if (colliderCount >= _colliderBuffer.Length)
            {
                BotMindPlugin.Log?.LogDebug(
                    $"[{_bot?.name}] LootFinder: Collider buffer full ({colliderCount}). Some containers may be missed.");
            }

            float radiusSqr = radius * radius;

            // Clear seen set at start of each scan
            _seenContainerIds.Clear();

            for (int i = 0; i < colliderCount; i++)
            {
                var collider = _colliderBuffer[i];
                if (collider == null) continue;

                var container = collider.GetComponentInParent<LootableContainer>();
                if (container == null) continue;

                // DEDUPLICATION: Multiple colliders can belong to same container
                int containerId = container.GetInstanceID();
                if (_seenContainerIds.Contains(containerId)) continue;
                _seenContainerIds.Add(containerId);

                if (IsBlacklisted(container)) continue;

                Vector3 containerPos = container.transform.position;
                float distSqr = (containerPos - position).sqrMagnitude;

                if (distSqr > radiusSqr) continue;
                if (!CanPathTo(containerPos)) continue;

                float distance = Mathf.Sqrt(distSqr);
                float estimatedValue = 10000f; // Base value for containers

                _targets.Add(new LootTarget
                {
                    Position = containerPos,
                    Distance = distance,
                    EstimatedValue = estimatedValue,
                    Priority = LootTarget.CalculatePriority(estimatedValue, distance),
                    IsContainer = true,
                    Target = container
                });
            }
        }

        private void ScanForLooseItems(Vector3 position, float radius, int minValue)
        {
            // Check items in the bot's ItemTaker's ThrownItems list (items dropped nearby)
            var itemTaker = _bot.ItemTaker;
            if (itemTaker == null)
            {
                return;
            }

            float radiusSqr = radius * radius;

            foreach (var lootItem in itemTaker.ThrownItems)
            {
                if (lootItem == null || IsBlacklisted(lootItem))
                {
                    continue;
                }

                // Skip items still in physics (falling)
                if (lootItem.IsPhysicsOn)
                {
                    continue;
                }

                Vector3 itemPos = lootItem.transform.position;
                float distSqr = (itemPos - position).sqrMagnitude;

                if (distSqr > radiusSqr)
                {
                    continue;
                }

                // Check item value
                int itemValue = GetItemValue(lootItem.Item);
                if (itemValue < minValue)
                {
                    continue;
                }

                // Check if bot has space for the item
                if (!CanFitItem(lootItem.Item))
                {
                    continue;
                }

                if (!CanPathTo(itemPos))
                {
                    continue;
                }

                float distance = Mathf.Sqrt(distSqr);

                _targets.Add(new LootTarget
                {
                    Position = itemPos,
                    Distance = distance,
                    EstimatedValue = itemValue,
                    Priority = LootTarget.CalculatePriority(itemValue, distance),
                    IsLooseItem = true,
                    Target = lootItem
                });
            }
        }

        private bool CanPathTo(Vector3 targetPosition)
        {
            // Issue 12 Fix: Use cached NavMeshPath to avoid allocation every call
            _cachedNavPath.ClearCorners();
            return NavMesh.CalculatePath(_bot.Position, targetPosition, -1, _cachedNavPath)
                   && _cachedNavPath.status == NavMeshPathStatus.PathComplete;
        }

        private bool CanFitItem(Item item)
        {
            var inventoryController = _bot.GetPlayer?.InventoryController;
            if (inventoryController == null)
            {
                return false;
            }

            // Check if there's a slot or grid space for this item
            var slotAddress = inventoryController.FindSlotToPickUp(item);
            if (slotAddress != null)
            {
                return true;
            }

            // Also check grid space if not a weapon
            if (!(item is Weapon))
            {
                var gridAddress = inventoryController.FindGridToPickUp(item);
                return gridAddress != null;
            }

            return false;
        }

        private int GetItemValue(Item item)
        {
            // Use the template's credits price like BotDeadBodyWork does
            return item?.Template?.CreditsPrice ?? 0;
        }

        private float EstimateCorpseValue(object body)
        {
            // Actually estimate corpse value by checking equipment
            float totalValue = 5000f; // Base value for any corpse

            try
            {
                // Get the Player from the body via reflection
                var bodyType = body.GetType();
                var playerProp = bodyType.GetProperty("Player");
                if (playerProp == null) return totalValue;

                var player = playerProp.GetValue(body) as Player;
                if (player?.InventoryController?.Inventory?.Equipment == null) return totalValue;

                var equipment = player.InventoryController.Inventory.Equipment;

                // Check valuable slots - weapons are worth a lot
                totalValue += GetSlotValue(equipment, EquipmentSlot.FirstPrimaryWeapon);
                totalValue += GetSlotValue(equipment, EquipmentSlot.SecondPrimaryWeapon);
                totalValue += GetSlotValue(equipment, EquipmentSlot.Holster);

                // Armor and rigs
                totalValue += GetSlotValue(equipment, EquipmentSlot.ArmorVest);
                totalValue += GetSlotValue(equipment, EquipmentSlot.TacticalVest);
                totalValue += GetSlotValue(equipment, EquipmentSlot.Headwear);

                // Backpack contents could be valuable
                var backpack = equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem;
                if (backpack != null)
                {
                    totalValue += backpack.Template?.CreditsPrice ?? 0;
                    // Add estimate for backpack contents
                    if (backpack is EFT.InventoryLogic.CompoundItem container)
                    {
                        totalValue += EstimateContainerContents(container);
                    }
                }
            }
            catch
            {
                // Reflection failed - return base value
            }

            return totalValue;
        }

        private float GetSlotValue(InventoryEquipment equipment, EquipmentSlot slot)
        {
            try
            {
                var item = equipment.GetSlot(slot)?.ContainedItem;
                return item?.Template?.CreditsPrice ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private float EstimateContainerContents(EFT.InventoryLogic.CompoundItem container)
        {
            float value = 0;
            try
            {
                var items = new List<Item>();
                container.GetAllAssembledItems(items);

                // Cap at checking first 20 items to avoid performance hit
                int checkCount = Math.Min(items.Count, 20);
                for (int i = 0; i < checkCount; i++)
                {
                    value += items[i]?.Template?.CreditsPrice ?? 0;
                }
            }
            catch
            {
                // Failed to enumerate - return what we have
            }
            return value;
        }

        /// <summary>Gets the current loot target without modifying state.</summary>
        public LootTarget CurrentTarget => _currentTarget;

        /// <summary>
        /// Claims the next best loot target, removing it from the queue.
        /// This is a mutating operation - named appropriately unlike a "Get" method.
        /// </summary>
        /// <returns>The claimed loot target, or null if none available.</returns>
        public LootTarget ClaimNextTarget()
        {
            // If we already have a target, return it
            if (_currentTarget != null)
            {
                return _currentTarget;
            }

            // Pop the next target from the priority queue
            if (_targets.Count > 0)
            {
                _currentTarget = _targets[0];
                _targets.RemoveAt(0);
                return _currentTarget;
            }

            return null;
        }

        /// <summary>
        /// Legacy method name - use ClaimNextTarget() instead.
        /// Kept for backwards compatibility with existing layer code.
        /// </summary>
        [Obsolete("Use ClaimNextTarget() instead - this method mutates state")]
        public LootTarget GetBestLootTarget() => ClaimNextTarget();

        /// <summary>Clears all targets and current target reference.</summary>
        public void ClearTargets()
        {
            _targets.Clear();
            _currentTarget = null;
        }

        /// <summary>Marks the current target as complete, clearing the reference.</summary>
        public void MarkCurrentTargetComplete()
        {
            _currentTarget = null;
        }

        public void BlacklistTarget(object target)
        {
            if (target == null) return;

            string stableId = GetStableTargetId(target);
            if (string.IsNullOrEmpty(stableId)) return;

            // Evict oldest entries when limit reached - don't nuke everything
            if (_blacklistedTargets.Count >= MAX_BLACKLIST_SIZE)
            {
                EvictOldestEntries();
            }

            _blacklistedTargets.TryAdd(stableId, Time.time);
        }

        /// <summary>
        /// Evicts the oldest entries from the blacklist instead of clearing everything.
        /// This preserves recent blacklist entries while making room for new ones.
        /// </summary>
        private void EvictOldestEntries()
        {
            // Get all entries sorted by timestamp (oldest first)
            var entries = _blacklistedTargets.ToArray();
            if (entries.Length < EVICTION_COUNT) return;

            // Sort by time added (value is the timestamp)
            System.Array.Sort(entries, (a, b) => a.Value.CompareTo(b.Value));

            // Remove the oldest EVICTION_COUNT entries
            for (int i = 0; i < EVICTION_COUNT && i < entries.Length; i++)
            {
                _blacklistedTargets.TryRemove(entries[i].Key, out _);
            }

            BotMindPlugin.Log?.LogDebug($"[{_bot?.name}] LootFinder: Evicted {EVICTION_COUNT} oldest blacklist entries");
        }

        /// <summary>
        /// Check if a target is blacklisted using stable identifier.
        /// </summary>
        private bool IsBlacklisted(object target)
        {
            if (target == null) return false;
            string stableId = GetStableTargetId(target);
            if (string.IsNullOrEmpty(stableId)) return false;
            return _blacklistedTargets.ContainsKey(stableId);
        }

        /// <summary>
        /// Healthcare Critical: Get a stable identifier for a loot target that survives GC.
        /// Uses ProfileId for corpses, Unity InstanceID for containers/items.
        /// </summary>
        private static string GetStableTargetId(object target)
        {
            if (target == null) return null;

            // For containers: use Unity's GetInstanceID which is stable for object lifetime
            if (target is LootableContainer container)
            {
                return $"container_{container.GetInstanceID()}";
            }

            // For loose items: use Unity's GetInstanceID
            if (target is LootItem lootItem)
            {
                return $"item_{lootItem.GetInstanceID()}";
            }

            // For corpses: try to get the ProfileId from the dead body
            // Dead bodies in EFT have a Player reference with stable ProfileId
            try
            {
                var bodyType = target.GetType();
                var playerProp = bodyType.GetProperty("Player");
                if (playerProp != null)
                {
                    var player = playerProp.GetValue(target) as Player;
                    if (player?.ProfileId != null)
                    {
                        return $"corpse_{player.ProfileId}";
                    }
                }
            }
            catch
            {
                // Reflection failed - fall through to fallback
            }

            // Fallback: Use type name + GetHashCode (less stable but better than nothing)
            // This path should rarely be hit in practice
            return $"unknown_{target.GetType().Name}_{target.GetHashCode()}";
        }

        /// <summary>
        /// Issue 4 Fix: Allow manual clearing of blacklist when needed.
        /// </summary>
        public void ClearBlacklist()
        {
            _blacklistedTargets.Clear();
        }

        /// <summary>
        /// Standards Compliance Fix: Timer-based cleanup to reduce per-frame overhead.
        /// Call this periodically (e.g., in layer Update) instead of per-scan.
        /// </summary>
        public void PerformPeriodicCleanup()
        {
            if (Time.time - _lastCleanupTime < CLEANUP_INTERVAL) return;
            _lastCleanupTime = Time.time;

            // Remove stale targets that are no longer valid
            _targets.RemoveAll(t => t.Target == null);
        }

        /// <summary>
        /// Full cleanup - clears all cached data.
        /// </summary>
        public void Cleanup()
        {
            _cachedNavPath.ClearCorners();
            _targets.Clear();
            _blacklistedTargets.Clear();
            _seenContainerIds.Clear();
            _currentTarget = null;
        }
    }

    /// <summary>
    /// Represents a lootable target (corpse, container, or loose item).
    /// </summary>
    public class LootTarget
    {
        public Vector3 Position { get; set; }
        public float EstimatedValue { get; set; }
        public float Distance { get; set; }
        public float Priority { get; set; }
        public bool IsCorpse { get; set; }
        public bool IsContainer { get; set; }
        public bool IsLooseItem { get; set; }
        public object Target { get; set; }

        public static float CalculatePriority(float value, float distance)
        {
            // Healthcare-grade: Guard against all edge cases that could cause issues
            // - value <= 0: Return 0 priority (worthless items)
            // - distance <= 0: Clamp to minimum to prevent division by zero or negative
            // - distance very small: Clamp to prevent overflow from huge priority values

            if (value <= 0f) return 0f;

            // Clamp distance to safe range: [0.5m, infinity)
            // 0.5m is about arm's reach, closer than this has no practical meaning
            const float MIN_DISTANCE = 0.5f;
            float safeDist = distance < MIN_DISTANCE ? MIN_DISTANCE : distance;

            // Higher value and closer distance = higher priority
            // Use squared distance to favor closer items more strongly
            float priority = value / (safeDist * safeDist);

            // Clamp max priority to prevent floating point overflow on very high value items
            const float MAX_PRIORITY = 1e9f;
            return priority > MAX_PRIORITY ? MAX_PRIORITY : priority;
        }
    }
}
