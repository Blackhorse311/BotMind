using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Logic for navigating a bot to a specific location.
    /// Uses BotOwner.GoToPoint() for pathfinding similar to looting navigation.
    /// </summary>
    public class GoToLocationLogic : CustomLogic
    {
        private enum State
        {
            Moving,
            Arrived,
            Failed,
            Complete
        }

        private State _currentState = State.Moving;
        private QuestObjective _objective;
        private Vector3 _targetPosition;
        private float _completionRadius;
        private float _startTime;
        private float _nextMoveTime;
        private float _stuckCheckTime;
        private Vector3 _lastPosition;
        private int _stuckCount;
        private int _pathFailCount;

        private const float MOVE_UPDATE_INTERVAL = 2f;
        private const float STUCK_CHECK_INTERVAL = 5f;
        private const float STUCK_THRESHOLD = 0.5f;
        private const int MAX_STUCK_COUNT = 3;
        private const int MAX_PATH_FAIL_COUNT = 2;

        public GoToLocationLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _currentState = State.Moving;
                _startTime = Time.time;
                _nextMoveTime = 0f;
                _stuckCheckTime = Time.time + STUCK_CHECK_INTERVAL;
                _lastPosition = BotOwner.Position;
                _stuckCount = 0;
                _pathFailCount = 0;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] GoToLocationLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] GoToLocationLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed;
            }
        }

        public override void Stop()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                // v1.4.0 Fix: Reset pose/speed to defaults
                if (BotOwner != null)
                {
                    BotOwner.SetPose(1f);
                    BotOwner.SetTargetMoveSpeed(1f);
                }
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] GoToLocationLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] GoToLocationLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
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
                    _targetPosition = _objective.TargetPosition;
                    _completionRadius = _objective.CompletionRadius;
                    questingData.Layer?.RegisterLogic(this);
                }

                if (_objective == null)
                {
                    _currentState = State.Failed;
                    return;
                }

                switch (_currentState)
                {
                    case State.Moving:
                        UpdateMoving();
                        break;
                    case State.Arrived:
                        // Brief pause at destination
                        _currentState = State.Complete;
                        break;
                    case State.Failed:
                    case State.Complete:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 92): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] GoToLocationLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed; // Fail safe
            }
        }

        private void UpdateMoving()
        {
            // Check if we've arrived
            float distanceToTarget = Vector3.Distance(BotOwner.Position, _targetPosition);
            if (distanceToTarget <= _completionRadius)
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Arrived at destination");
                _currentState = State.Arrived;
                return;
            }

            // Check if stuck
            if (Time.time >= _stuckCheckTime)
            {
                _stuckCheckTime = Time.time + STUCK_CHECK_INTERVAL;
                float movedDistance = Vector3.Distance(BotOwner.Position, _lastPosition);

                if (movedDistance < STUCK_THRESHOLD)
                {
                    _stuckCount++;
                    BotMindPlugin.Log?.LogWarning($"[{BotOwner.name}] Stuck check failed ({_stuckCount}/{MAX_STUCK_COUNT}) at {distanceToTarget:F1}m from target");

                    if (_stuckCount >= MAX_STUCK_COUNT)
                    {
                        BotMindPlugin.Log?.LogWarning($"[{BotOwner.name}] Navigation failed - stuck at {distanceToTarget:F1}m from target");
                        _currentState = State.Failed;
                        return;
                    }
                }
                else
                {
                    _stuckCount = 0;
                }
                _lastPosition = BotOwner.Position;
            }

            // Set movement parameters
            BotOwner.SetPose(1f); // Standing
            BotOwner.SetTargetMoveSpeed(GetMoveSpeed(distanceToTarget));
            // Issue #1 Fix: Removed BotOwner.Steering.LookToMovingDirection() — it was called
            // every frame, overriding EFT's natural head-scanning that feeds LookSensor.
            // Without it, bots never "see" nearby enemies and SAIN combat never activates.
            // GoToPoint() already handles movement-direction facing.

            // Update path periodically
            if (Time.time >= _nextMoveTime)
            {
                _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;

                // Calculate destination (stop short of exact point)
                Vector3 direction = (_targetPosition - BotOwner.Position).normalized;
                Vector3 destination = _targetPosition - direction * 0.5f;

                var pathResult = BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false);

                if (pathResult != NavMeshPathStatus.PathComplete)
                {
                    // Try to find nearest valid NavMesh position
                    if (NavMesh.SamplePosition(_targetPosition, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        destination = hit.position;
                        pathResult = BotOwner.GoToPoint(destination, true, -1f, false, false, true, false, false);
                    }

                    if (pathResult != NavMeshPathStatus.PathComplete)
                    {
                        _pathFailCount++;
                        BotMindPlugin.Log?.LogWarning(
                            $"[{BotOwner.name}] Path to target failed ({_pathFailCount}/{MAX_PATH_FAIL_COUNT}) at {distanceToTarget:F1}m");
                        if (_pathFailCount >= MAX_PATH_FAIL_COUNT)
                        {
                            BotMindPlugin.Log?.LogWarning(
                                $"[{BotOwner.name}] Navigation failed — target unreachable at {distanceToTarget:F1}m");
                            _currentState = State.Failed;
                        }
                    }
                    else
                    {
                        _pathFailCount = 0;
                    }
                }
                else
                {
                    _pathFailCount = 0;
                }
            }
        }

        private float GetMoveSpeed(float distance)
        {
            // v1.4.0 Fix: Increased speeds — old values (0.5/0.7) made bots creep
            if (distance > 30f) return 1f;      // Sprint
            if (distance > 5f) return 0.85f;    // Jog
            return 0.7f;                        // Walk (final approach only)
        }

        public void SetTarget(Vector3 position, float completionRadius = 2f)
        {
            _targetPosition = position;
            _completionRadius = completionRadius;
        }

        public bool IsComplete => _currentState == State.Complete || _currentState == State.Failed;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            float distance = Vector3.Distance(BotOwner.Position, _targetPosition);
            stringBuilder.AppendLine("GoToLocationLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Target: {_targetPosition}");
            stringBuilder.AppendLine($"  Distance: {distance:F1}m");
            stringBuilder.AppendLine($"  Stuck Count: {_stuckCount}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
