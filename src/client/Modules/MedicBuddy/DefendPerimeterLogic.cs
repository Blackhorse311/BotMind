using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Logic for shooter bots to hold a defensive position around the player.
    /// </summary>
    public class DefendPerimeterLogic : CustomLogic
    {
        private float _startTime;
        private float _nextMoveTime;
        private float _nextLookTime;
        private Vector3 _assignedPosition;
        private float _lookAngle;
        private bool _atPosition;

        private const float MOVE_UPDATE_INTERVAL = 2f;
        private const float LOOK_UPDATE_INTERVAL = 3f;
        private const float POSITION_THRESHOLD = 2f;
        /// <summary>Minimum squared magnitude to consider a direction vector valid (prevents NaN from normalizing zero).</summary>
        private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.01f;

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
                _atPosition = false;
                _assignedPosition = Vector3.zero;

                // Get assigned defense position from controller
                var controller = MedicBuddyController.Instance;
                if (controller != null)
                {
                    _assignedPosition = controller.GetDefensePosition(BotOwner);
                }

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

                // Update assigned position periodically (player may move)
                if (Time.time >= _nextMoveTime)
                {
                    _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;
                    _assignedPosition = controller.GetDefensePosition(BotOwner);
                }

                float distanceToPosition = Vector3.Distance(BotOwner.Position, _assignedPosition);

                // Check if at assigned position
                if (distanceToPosition <= POSITION_THRESHOLD)
                {
                    _atPosition = true;

                    // Hold position - semi-crouch for stability
                    BotOwner.SetPose(0.7f);

                    // Face outward from player
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
                    BotOwner.Steering.LookToMovingDirection();

                    // Issue 16 Fix: Only attempt movement if NavMesh position is valid
                    // Skip movement this frame if no valid NavMesh position found
                    if (NavMesh.SamplePosition(_assignedPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        BotOwner.GoToPoint(hit.position, true, -1f, false, false, true, false, false);
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

        private void UpdateLookDirection(Player player)
        {
            // Face outward from player (watch for threats)
            Vector3 outwardDir = (BotOwner.Position - player.Position).normalized;

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
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
