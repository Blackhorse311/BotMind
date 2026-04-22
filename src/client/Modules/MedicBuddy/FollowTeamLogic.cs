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
    /// Logic for bots to retreat away from the player after the mission is complete.
    /// </summary>
    public class FollowTeamLogic : CustomLogic
    {
        private float _startTime;
        private float _nextMoveTime;
        private Vector3 _retreatTarget;
        private bool _retreatComplete;

        private const float MOVE_UPDATE_INTERVAL = 2f;
        private const float RETREAT_DISTANCE = 50f;
        /// <summary>Minimum squared magnitude to consider a vector valid (prevents NaN from normalizing zero vector).</summary>
        private const float MIN_DIRECTION_SQR_MAGNITUDE = 0.01f;

        public FollowTeamLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _startTime = Time.time;
                _nextMoveTime = 0f;
                _retreatComplete = false;
                _retreatTarget = Vector3.zero;

                CalculateRetreatTarget();
                // v1.8.0: Voice line when retreating
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.GetBack, true);
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] FollowTeamLogic started - retreating to {_retreatTarget}");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] FollowTeamLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _retreatComplete = true; // Fail safe
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] FollowTeamLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] FollowTeamLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void CalculateRetreatTarget()
        {
            var controller = MedicBuddyController.Instance;
            var player = controller?.TargetPlayer;

            if (player == null)
            {
                // No player - just move away from current position
                // Seventh Review Fix (Issue 8): Protect against zero vector from Random.insideUnitSphere
                Vector3 randomDir = Random.insideUnitSphere;
                if (randomDir.sqrMagnitude < MIN_DIRECTION_SQR_MAGNITUDE)
                {
                    randomDir = Vector3.forward; // Fallback to forward if random is zero
                }
                _retreatTarget = BotOwner.Position + randomDir.normalized * RETREAT_DISTANCE;
                _retreatTarget.y = BotOwner.Position.y;
            }
            else
            {
                // Calculate direction away from player
                Vector3 awayDir = (BotOwner.Position - player.Position);
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude < MIN_DIRECTION_SQR_MAGNITUDE)
                    awayDir = Vector3.forward;
                else
                    awayDir = awayDir.normalized;

                _retreatTarget = BotOwner.Position + awayDir * RETREAT_DISTANCE;
            }

            // Validate with NavMesh — fall back to current position if no valid point nearby
            // Review 10 Fix: Reduced from 20f to 5f — large radius snaps to wrong floors/buildings
            if (NavMesh.SamplePosition(_retreatTarget, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                _retreatTarget = hit.position;
            }
            else
            {
                _retreatTarget = BotOwner.Position;
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                var controller = MedicBuddyController.Instance;
                var player = controller?.TargetPlayer;

                // Check if far enough from player
                float distanceFromPlayer = player != null
                    ? Vector3.Distance(BotOwner.Position, player.Position)
                    : RETREAT_DISTANCE;

                if (distanceFromPlayer >= RETREAT_DISTANCE)
                {
                    _retreatComplete = true;
                    BotOwner.SetPose(0.5f);
                    return;
                }

                // Movement settings - move quickly
                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(1f);

                // Update path periodically
                if (Time.time >= _nextMoveTime)
                {
                    _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;

                    // Recalculate retreat target if needed
                    float distanceToTarget = Vector3.Distance(BotOwner.Position, _retreatTarget);
                    if (distanceToTarget < 10f)
                    {
                        CalculateRetreatTarget();
                    }

                    BotOwner.GoToPoint(_retreatTarget, true, -1f, false, true, true, false, false);
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 98): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] FollowTeamLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _retreatComplete = true; // Fail safe
            }
        }

        public bool IsRetreatComplete => _retreatComplete;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var player = MedicBuddyController.Instance?.TargetPlayer;
            float distanceFromPlayer = player != null
                ? Vector3.Distance(BotOwner.Position, player.Position)
                : 0f;

            stringBuilder.AppendLine("FollowTeamLogic (Retreat)");
            stringBuilder.AppendLine($"  Complete: {_retreatComplete}");
            stringBuilder.AppendLine($"  Distance from player: {distanceFromPlayer:F1}m");
            stringBuilder.AppendLine($"  Target: {_retreatTarget}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
