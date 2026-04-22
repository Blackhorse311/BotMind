using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Logic for shooter bots to hold a defensive position around the player
    /// and actively scan for nearby threats. When enemies are detected, the bot
    /// faces toward the nearest threat and registers them via BotsGroup.CheckAndAddEnemy(),
    /// which lets EFT's built-in combat AI handle engagement automatically.
    /// </summary>
    public class DefendPerimeterLogic : CustomLogic
    {
        private float _startTime;
        private float _nextMoveTime;
        private float _nextLookTime;
        private float _nextScanTime;
        private Vector3 _assignedPosition;
        private float _lookAngle;
        private bool _atPosition;
        private Player _nearestThreat;

        private const float MOVE_UPDATE_INTERVAL = 2f;
        private const float LOOK_UPDATE_INTERVAL = 3f;
        private const float SCAN_INTERVAL = 1f;
        private const float THREAT_DETECTION_RADIUS = 80f;
        private const int MAX_TRACKED_THREATS = 4;
        private const float POSITION_THRESHOLD = 2f;
        /// <summary>Minimum squared magnitude to consider a direction vector valid (prevents NaN from normalizing zero).</summary>
        private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.01f;

        // Reusable list for scan results — avoids allocation each scan cycle
        private readonly List<(Player player, float distance)> _threatCandidates = new List<(Player, float)>();
        // Static comparer to avoid delegate allocation on sort
        private static readonly Comparison<(Player player, float distance)> _threatComparer =
            (a, b) => a.distance.CompareTo(b.distance);

        public DefendPerimeterLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _startTime = Time.time;
                _nextMoveTime = 0f;
                _nextLookTime = 0f;
                _nextScanTime = 0f;
                _atPosition = false;
                _assignedPosition = Vector3.zero;
                _nearestThreat = null;

                // Get assigned defense position from controller
                var controller = MedicBuddyController.Instance;
                if (controller != null)
                {
                    _assignedPosition = controller.GetDefensePosition(BotOwner);

                    // v1.8.0: Try to snap to actual cover near assigned position
                    _assignedPosition = TryFindCoverPosition(_assignedPosition);
                }

                // v1.8.0: Voice line when taking defensive position
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Covering, true);
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] DefendPerimeterLogic started at position {_assignedPosition}");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] DefendPerimeterLogic.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _nearestThreat = null;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] DefendPerimeterLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] DefendPerimeterLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                var controller = MedicBuddyController.Instance;
                if (controller == null) return;

                var player = controller.TargetPlayer;
                // Third Review Fix: Added null check for HealthController
                if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                {
                    return;
                }

                // Scan for threats periodically
                if (Time.time >= _nextScanTime)
                {
                    _nextScanTime = Time.time + SCAN_INTERVAL;
                    ScanForThreats(controller, player);
                }

                // Update assigned position periodically (player may move)
                if (Time.time >= _nextMoveTime)
                {
                    _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;
                    _assignedPosition = controller.GetDefensePosition(BotOwner);

                    // v1.8.0: Try to snap to actual cover near assigned position
                    _assignedPosition = TryFindCoverPosition(_assignedPosition);
                }

                float distanceToPosition = Vector3.Distance(BotOwner.Position, _assignedPosition);

                // Check if at assigned position
                if (distanceToPosition <= POSITION_THRESHOLD)
                {
                    _atPosition = true;

                    // Hold position - semi-crouch for stability
                    BotOwner.SetPose(0.7f);

                    // Face toward threats or outward from player
                    if (Time.time >= _nextLookTime)
                    {
                        _nextLookTime = Time.time + LOOK_UPDATE_INTERVAL;
                        UpdateLookDirection(player);
                    }
                }
                else
                {
                    _atPosition = false;

                    // Move to assigned position
                    BotOwner.SetPose(1f);
                    BotOwner.SetTargetMoveSpeed(distanceToPosition > 10f ? 0.8f : 0.5f);

                    // Issue 16 Fix: Only attempt movement if NavMesh position is valid
                    if (NavMesh.SamplePosition(_assignedPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        BotOwner.GoToPoint(hit.position, true, -1f, false, true, true, false, false);
                    }
                    // else: skip this frame, position will be recalculated next interval
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 99): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] DefendPerimeterLogic.Update error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Scans AllAlivePlayersList for nearby hostiles, registers them with the bot's
        /// awareness system, and tracks the nearest threat for look direction.
        /// EFT's combat AI handles engagement once enemies are registered.
        /// </summary>
        private void ScanForThreats(MedicBuddyController controller, Player patient)
        {
            _threatCandidates.Clear();
            _nearestThreat = null;

            var gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld == null) return;

            var allPlayers = gameWorld.AllAlivePlayersList;
            if (allPlayers == null) return;

            Vector3 botPos = BotOwner.Position;

            for (int i = 0; i < allPlayers.Count; i++)
            {
                var candidate = allPlayers[i];

                // Skip null, dead, self, the patient, and team members
                if (candidate == null) continue;
                if (!candidate.HealthController.IsAlive) continue;
                if (candidate == patient) continue;
                if (candidate.IsAI && candidate.AIData?.BotOwner != null && controller.IsBotInTeam(candidate.AIData.BotOwner)) continue;

                // Review 10 Fix: Distance check BEFORE enemy registration to avoid
                // registering every player on the map as an enemy every scan cycle
                float distance = Vector3.Distance(botPos, candidate.Position);
                if (distance > THREAT_DETECTION_RADIUS) continue;

                // Skip allies — only register and track actual enemies
                // CheckAndAddEnemy registers the player as a threat if EFT's faction logic
                // considers them hostile. Returns false for allies (idempotent, safe to call repeatedly).
                if (BotOwner.BotsGroup != null)
                {
                    if (!BotOwner.BotsGroup.IsEnemy(candidate) &&
                        !BotOwner.BotsGroup.CheckAndAddEnemy(candidate))
                    {
                        continue;
                    }
                }

                _threatCandidates.Add((candidate, distance));
            }

            if (_threatCandidates.Count == 0) return;

            // Sort by distance (closest first)
            _threatCandidates.Sort(_threatComparer);

            // Track nearest for look direction
            _nearestThreat = _threatCandidates[0].player;
        }

        /// <summary>
        /// v1.8.0: Try to find a real cover position (wall, object) near the assigned position
        /// using EFT's native cover graph. Falls back to the original position if no cover found.
        /// </summary>
        private Vector3 TryFindCoverPosition(Vector3 assignedPos)
        {
            try
            {
                var coverPoint = BotOwner.Covers?.FindHidePoint(assignedPos, 0f);
                if (coverPoint != null)
                {
                    return coverPoint.Position;
                }
            }
            catch
            {
                // Cover system may not be initialized — fall back silently
            }
            return assignedPos;
        }

        private void UpdateLookDirection(Player player)
        {
            // If we have a nearby threat, face toward it
            if (_nearestThreat != null && _nearestThreat.HealthController != null && _nearestThreat.HealthController.IsAlive)
            {
                Vector3 dirToThreat = _nearestThreat.Position - BotOwner.Position;
                if (dirToThreat.sqrMagnitude >= MIN_DIRECTION_SQR_MAGNITUDE)
                {
                    BotOwner.Steering.LookToDirection(dirToThreat.normalized);
                    return;
                }
            }

            // No threat — face outward from rally point/player (watch for threats)
            var controller = MedicBuddyController.Instance;
            Vector3 center = controller?.RallyPoint ?? player.Position;
            Vector3 outwardDir = (BotOwner.Position - center).normalized;

            // Seventh Review Fix (Issue 13): Use named constant instead of magic number
            if (outwardDir.sqrMagnitude < MIN_DIRECTION_SQR_MAGNITUDE)
            {
                outwardDir = Vector3.forward;
            }

            // Add some variation to look angle
            _lookAngle += Random.Range(-30f, 30f);
            _lookAngle = Mathf.Clamp(_lookAngle, -45f, 45f);

            Vector3 lookDir = Quaternion.Euler(0, _lookAngle, 0) * outwardDir;
            BotOwner.Steering.LookToDirection(lookDir);
        }

        public bool IsAtPosition => _atPosition;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            float distanceToPosition = Vector3.Distance(BotOwner.Position, _assignedPosition);

            stringBuilder.AppendLine("DefendPerimeterLogic");
            stringBuilder.AppendLine($"  At Position: {_atPosition}");
            stringBuilder.AppendLine($"  Distance: {distanceToPosition:F1}m");
            stringBuilder.AppendLine($"  Target: {_assignedPosition}");
            stringBuilder.AppendLine($"  Nearest Threat: {_nearestThreat?.name ?? "None"}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
