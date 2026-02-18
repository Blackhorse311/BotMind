using Comfort.Common;
using EFT;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Manages quest objectives for a bot, determining what the bot should be working towards.
    /// </summary>
    public class QuestManager
    {
        private readonly BotOwner _bot;
        private readonly List<QuestObjective> _objectives = new List<QuestObjective>();
        private QuestObjective _currentObjective;
        private float _raidStartTime;

        // Issue 18 Fix: Extract magic numbers to named constants
        private const float EXTRACT_PRIORITY_RAID_TIME = 1800f; // 30 minutes - when to prioritize extraction
        /// <summary>Healthcare Critical: Maximum objectives to prevent unbounded memory growth.</summary>
        private const int MAX_OBJECTIVES = 50;

        // Seventh Review Fix (Issue 168): Static comparison delegate to avoid lambda allocation in Sort
        private static readonly System.Comparison<QuestObjective> _priorityComparer =
            (a, b) => b.Priority.CompareTo(a.Priority);

        // Cached NavMeshPath to avoid allocation per GetRandomNavMeshPoint call
        private readonly NavMeshPath _cachedNavPath = new NavMeshPath();

        /// <summary>Seventh Review Fix (Issue 194): Whether this bot has an active objective to pursue.</summary>
        public bool HasActiveObjective => _currentObjective != null;
        /// <summary>Seventh Review Fix (Issue 195): Whether the current objective has been marked complete.</summary>
        public bool IsCurrentObjectiveComplete { get; private set; }

        /// <summary>Creates a new quest manager for the specified bot.</summary>
        /// <param name="bot">The bot owner to manage quests for.</param>
        public QuestManager(BotOwner bot)
        {
            _bot = bot;
            // Get ACTUAL raid start time, not bot spawn time
            // Bots can spawn mid-raid; using Time.time would make them think raid just started
            _raidStartTime = GetRaidStartTime();
        }

        /// <summary>
        /// Gets the raid start time as a Time.time value.
        /// Uses SPT's RaidTimeUtil via reflection when available, otherwise falls back to current time.
        /// Note: Fallback means mid-raid spawned bots treat their spawn time as raid start.
        /// </summary>
        private static float GetRaidStartTime()
        {
            try
            {
                // Try SPT's RaidTimeUtil.GetElapsedRaidSeconds() via reflection
                var raidTimeUtilType = System.Type.GetType(
                    "SPT.SinglePlayer.Utils.InRaid.RaidTimeUtil, spt-singleplayer", false);
                if (raidTimeUtilType != null)
                {
                    var method = raidTimeUtilType.GetMethod("GetElapsedRaidSeconds",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (method != null)
                    {
                        float elapsed = (float)method.Invoke(null, null);
                        return Time.time - elapsed;
                    }
                }
            }
            catch
            {
                // Reflection failed - fall back to current time
            }
            return Time.time;
        }

        /// <summary>Updates quest objectives - clears completed ones and generates new ones if needed.</summary>
        public void UpdateObjectives()
        {
            // Healthcare-grade: Use efficient reverse iteration to remove completed objectives
            // This avoids the O(n) allocation that RemoveAll creates with its predicate delegate
            for (int i = _objectives.Count - 1; i >= 0; i--)
            {
                if (_objectives[i].IsComplete)
                {
                    _objectives.RemoveAt(i);
                }
            }

            // If no objectives, generate new ones based on bot type and map
            if (_objectives.Count == 0)
            {
                GenerateObjectives();
            }

            // Select best objective to pursue
            if (_currentObjective == null || _currentObjective.IsComplete)
            {
                _currentObjective = SelectBestObjective();
                IsCurrentObjectiveComplete = false;
            }
        }

        private void GenerateObjectives()
        {
            var role = _bot.Profile?.Info?.Settings?.Role;
            bool isPMC = role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR;

            if (isPMC)
            {
                GeneratePMCObjectives();
            }
            else
            {
                GenerateScavObjectives();
            }
        }

        private void GeneratePMCObjectives()
        {
            // Generate 2-4 exploration waypoints with path reachability validation
            int waypointCount = UnityEngine.Random.Range(2, 5);
            int successCount = 0;
            for (int i = 0; i < waypointCount; i++)
            {
                Vector3 randomPoint = GetRandomNavMeshPoint(_bot.Position, 50f, 150f);
                if (randomPoint != Vector3.zero)
                {
                    successCount++;
                    _objectives.Add(new QuestObjective
                    {
                        Type = QuestObjectiveType.GoToLocation,
                        Name = $"Waypoint {i + 1}",
                        TargetPosition = randomPoint,
                        CompletionRadius = 5f,
                        Priority = 50f - (i * 5f)
                    });
                }
            }

            // Fallback: if no reachable waypoints found, explore locally instead of standing idle
            if (successCount == 0)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{_bot?.name}] No reachable waypoints generated — falling back to local exploration");
                _objectives.Add(new QuestObjective
                {
                    Type = QuestObjectiveType.Explore,
                    Name = "Local Patrol",
                    TargetPosition = _bot.Position,
                    CompletionRadius = 40f,
                    Priority = 45f
                });
            }

            // Extract objective — SAIN handles actual extraction point assignment.
            // TargetPosition is unused when SAIN assigns the exfil.
            _objectives.Add(new QuestObjective
            {
                Type = QuestObjectiveType.Extract,
                Name = "Extract",
                TargetPosition = Vector3.zero,
                Priority = 10f
            });
        }

        /// <summary>
        /// Gets a random point on NavMesh within min/max range that has a valid path from origin.
        /// Returns Vector3.zero on failure.
        /// </summary>
        private Vector3 GetRandomNavMeshPoint(Vector3 origin, float minRange, float maxRange)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float distance = UnityEngine.Random.Range(minRange, maxRange);
                float angle = UnityEngine.Random.Range(0f, 360f);

                Vector3 direction = Quaternion.Euler(0, angle, 0) * Vector3.forward;
                Vector3 targetPoint = origin + direction * distance;

                // Tolerance reduced from 20f to 5f to avoid snapping to wrong floors/buildings
                if (NavMesh.SamplePosition(targetPoint, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    // Validate a complete path exists — a valid NavMesh position may be on
                    // a disconnected island, different floor, or across an unbridgeable gap
                    _cachedNavPath.ClearCorners();
                    if (NavMesh.CalculatePath(origin, hit.position, NavMesh.AllAreas, _cachedNavPath)
                        && _cachedNavPath.status == NavMeshPathStatus.PathComplete)
                    {
                        return hit.position;
                    }
                }
            }
            return Vector3.zero;
        }

        private void GenerateScavObjectives()
        {
            int patrolPoints = UnityEngine.Random.Range(2, 4);
            int successCount = 0;
            for (int i = 0; i < patrolPoints; i++)
            {
                Vector3 patrolPoint = GetRandomNavMeshPoint(_bot.Position, 30f, 100f);
                if (patrolPoint != Vector3.zero)
                {
                    successCount++;
                    _objectives.Add(new QuestObjective
                    {
                        Type = QuestObjectiveType.GoToLocation,
                        Name = $"Patrol Point {i + 1}",
                        TargetPosition = patrolPoint,
                        CompletionRadius = 3f,
                        Priority = 40f - (i * 3f)
                    });
                }
            }

            // Fallback: if no reachable patrol points found, explore locally
            if (successCount == 0)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{_bot?.name}] No reachable patrol points generated — falling back to local exploration");
                _objectives.Add(new QuestObjective
                {
                    Type = QuestObjectiveType.Explore,
                    Name = "Local Patrol",
                    TargetPosition = _bot.Position,
                    CompletionRadius = 30f,
                    Priority = 40f
                });
            }
        }

        private QuestObjective SelectBestObjective()
        {
            if (_objectives.Count == 0)
            {
                return null;
            }

            // Sort by priority (higher = more important)
            // Seventh Review Fix (Issue 168): Use static delegate to avoid lambda allocation
            _objectives.Sort(_priorityComparer);

            // Check time remaining - prioritize extraction if running low
            float raidTime = Time.time - _raidStartTime;
            if (raidTime > EXTRACT_PRIORITY_RAID_TIME)
            {
                var extractObj = _objectives.Find(o => o.Type == QuestObjectiveType.Extract);
                if (extractObj != null)
                {
                    return extractObj;
                }
            }

            return _objectives[0];
        }

        /// <summary>Seventh Review Fix (Issue 196): Gets the current objective being pursued.</summary>
        /// <returns>The current quest objective, or null if none.</returns>
        public QuestObjective GetCurrentObjective()
        {
            return _currentObjective;
        }

        /// <summary>Seventh Review Fix (Issue 197): Marks the current objective as complete.</summary>
        public void MarkCurrentObjectiveComplete()
        {
            if (_currentObjective != null)
            {
                _currentObjective.IsComplete = true;
                IsCurrentObjectiveComplete = true;
                _currentObjective = null;
            }
            // Issue #1 Fix: Immediately select next objective so HasActiveObjective stays true.
            // Without this, the layer deactivates for up to 5 seconds between objectives,
            // causing bots to stand idle after reaching each waypoint.
            UpdateObjectives();
        }

        /// <summary>Seventh Review Fix (Issue 198): Adds a new objective to the quest list.</summary>
        /// <param name="objective">The objective to add.</param>
        public void AddObjective(QuestObjective objective)
        {
            // Healthcare Critical: Prevent unbounded memory growth
            if (_objectives.Count >= MAX_OBJECTIVES)
            {
                BotMindPlugin.Log?.LogDebug($"[{_bot?.name}] QuestManager: Max objectives ({MAX_OBJECTIVES}) reached, ignoring new objective");
                return;
            }
            _objectives.Add(objective);
        }
    }

    public enum QuestObjectiveType
    {
        GoToLocation,
        FindItem,
        PlaceItem,
        Explore,
        Extract,
        Patrol,
        Investigate
    }

    public class QuestObjective
    {
        public QuestObjectiveType Type { get; set; }
        public string Name { get; set; }
        public Vector3 TargetPosition { get; set; }
        public float Priority { get; set; }
        public bool IsComplete { get; set; }
        public string ItemTemplateId { get; set; }
        public float CompletionRadius { get; set; } = 2f;
    }
}
