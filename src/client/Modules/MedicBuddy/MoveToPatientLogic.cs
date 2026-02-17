using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Logic for the medic bot to navigate to the player.
    /// </summary>
    public class MoveToPatientLogic : CustomLogic
    {
        private float _startTime;
        private float _nextMoveTime;
        private bool _arrived;

        private const float MOVE_UPDATE_INTERVAL = 1.5f;
        private const float ARRIVAL_DISTANCE = 3f;

        public MoveToPatientLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _startTime = Time.time;
                _nextMoveTime = 0f;
                _arrived = false;
                BotMindPlugin.Log?.LogInfo($"[{BotOwner?.name ?? "Unknown"}] MoveToPatientLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MoveToPatientLogic.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] MoveToPatientLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MoveToPatientLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
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

                // Navigate to rally point (CCP) if set, otherwise to the player
                Vector3 targetPos = controller.RallyPoint;
                float distanceToTarget = Vector3.Distance(BotOwner.Position, targetPos);

                // Check if arrived
                if (distanceToTarget <= ARRIVAL_DISTANCE)
                {
                    _arrived = true;
                    BotOwner.SetPose(0.5f); // Semi-crouch when arrived
                    BotOwner.Steering.LookToPoint(targetPos + Vector3.up * 1.5f);
                    return;
                }

                _arrived = false;

                // Movement settings - sprint when far away
                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(distanceToTarget > 20f ? 1f : 0.7f);
                BotOwner.Steering.LookToMovingDirection();

                // Update path periodically
                if (Time.time >= _nextMoveTime)
                {
                    _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;

                    // Calculate destination (rally point or player position)
                    Vector3 destination = targetPos;

                    // Try to find NavMesh-valid position
                    if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        destination = hit.position;
                    }

                    BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false);
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 97): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] MoveToPatientLogic.Update error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public bool HasArrived => _arrived;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var controller = MedicBuddyController.Instance;
            var targetPos = controller?.RallyPoint ?? Vector3.zero;
            float distance = Vector3.Distance(BotOwner.Position, targetPos);

            stringBuilder.AppendLine("MoveToPatientLogic");
            stringBuilder.AppendLine($"  Arrived: {_arrived}");
            stringBuilder.AppendLine($"  Distance: {distance:F1}m");
            stringBuilder.AppendLine($"  Target: {targetPos}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
