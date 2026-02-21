using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Logic for exploring an area to discover points of interest.
    /// Generates random patrol points within an area and visits them.
    /// </summary>
    public class ExploreAreaLogic : CustomLogic
    {
        private enum State
        {
            SelectingWaypoint,
            MovingToWaypoint,
            LookingAround,
            Complete,
            // Sixth Review Fix (Issue 105): Add Failed state for proper error handling
            Failed
        }

        private State _currentState = State.SelectingWaypoint;
        private QuestObjective _objective;
        private Vector3 _centerPosition;
        private float _exploreRadius;
        private float _startTime;
        private float _nextMoveTime;
        private float _lookEndTime;
        private float _exploreDuration;
        private Vector3 _currentWaypoint;
        private int _waypointsVisited;
        private int _maxWaypoints;

        private const float WAYPOINT_ARRIVAL_DISTANCE = 3f;
        private const float LOOK_DURATION = 2f;
        private const float MOVE_UPDATE_INTERVAL = 2f;
        private const float DEFAULT_EXPLORE_RADIUS = 30f;
        private const float DEFAULT_EXPLORE_DURATION = 120f; // 2 minutes

        // Issue 12 Fix: Cached NavMeshPath to avoid allocations in hot path
        private readonly NavMeshPath _cachedNavPath = new NavMeshPath();

        public ExploreAreaLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Healthcare-grade: Wrap framework callback in try-catch
            try
            {
                _currentState = State.SelectingWaypoint;
                _startTime = Time.time;
                _waypointsVisited = 0;
                _maxWaypoints = Random.Range(3, 7);
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] ExploreAreaLogic started (max waypoints: {_maxWaypoints})");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] ExploreAreaLogic.Start error: {ex.Message}\n{ex.StackTrace}");
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
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] ExploreAreaLogic stopped");
                _cachedNavPath.ClearCorners();
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] ExploreAreaLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
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
                    _centerPosition = _objective?.TargetPosition ?? BotOwner.Position;
                    _exploreRadius = _objective?.CompletionRadius > 0 ? _objective.CompletionRadius : DEFAULT_EXPLORE_RADIUS;
                    _exploreDuration = DEFAULT_EXPLORE_DURATION;
                    questingData.Layer?.RegisterLogic(this);
                }

                // If no objective, use bot's current position as center
                if (_objective == null)
                {
                    _centerPosition = BotOwner.Position;
                    _exploreRadius = DEFAULT_EXPLORE_RADIUS;
                    _exploreDuration = DEFAULT_EXPLORE_DURATION;
                }

                // Check if exploration time exceeded
                if (Time.time - _startTime > _exploreDuration)
                {
                    _currentState = State.Complete;
                    return;
                }

                // Check if visited enough waypoints
                if (_waypointsVisited >= _maxWaypoints)
                {
                    _currentState = State.Complete;
                    return;
                }

                switch (_currentState)
                {
                    case State.SelectingWaypoint:
                        SelectNextWaypoint();
                        break;
                    case State.MovingToWaypoint:
                        UpdateMovingToWaypoint();
                        break;
                    case State.LookingAround:
                        UpdateLookingAround();
                        break;
                    case State.Complete:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 93): Include stack trace in error log
                // Sixth Review Fix (Issue 105): Set to Failed state instead of Complete
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] ExploreAreaLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed; // Fail safe
            }
        }

        private void SelectNextWaypoint()
        {
            // Try to find a valid random point within the explore radius
            for (int attempts = 0; attempts < 10; attempts++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * _exploreRadius;
                Vector3 candidatePoint = _centerPosition + new Vector3(randomOffset.x, 0f, randomOffset.y);

                // Check if point is on NavMesh
                if (NavMesh.SamplePosition(candidatePoint, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    // Issue 12 Fix: Use cached NavMeshPath to avoid allocation
                    _cachedNavPath.ClearCorners();
                    if (NavMesh.CalculatePath(BotOwner.Position, hit.position, NavMesh.AllAreas, _cachedNavPath) &&
                        _cachedNavPath.status == NavMeshPathStatus.PathComplete)
                    {
                        _currentWaypoint = hit.position;
                        _currentState = State.MovingToWaypoint;
                        _nextMoveTime = 0f;
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Selected waypoint {_waypointsVisited + 1}: {_currentWaypoint}");
                        return;
                    }
                }
            }

            // Couldn't find valid point - complete exploration
            BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Could not find valid waypoint - completing exploration");
            _currentState = State.Complete;
        }

        private void UpdateMovingToWaypoint()
        {
            float distance = Vector3.Distance(BotOwner.Position, _currentWaypoint);

            // Check if arrived
            if (distance <= WAYPOINT_ARRIVAL_DISTANCE)
            {
                _waypointsVisited++;
                _lookEndTime = Time.time + LOOK_DURATION;
                _currentState = State.LookingAround;
                BotOwner.SetPose(0.5f); // Semi-crouch while looking
                return;
            }

            // Movement
            BotOwner.SetPose(1f);
            // v1.4.0 Fix: Increased speeds — 0.6f made bots creep during exploration
            BotOwner.SetTargetMoveSpeed(distance > 15f ? 1f : 0.85f);
            // Issue #1 Fix: Removed LookToMovingDirection() — overrides natural head-scanning

            if (Time.time >= _nextMoveTime)
            {
                _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;

                var pathResult = BotOwner.GoToPoint(_currentWaypoint, true, -1f, false, false, true, false, false);
                if (pathResult != NavMeshPathStatus.PathComplete)
                {
                    // Failed to path - select new waypoint
                    _currentState = State.SelectingWaypoint;
                }
            }
        }

        private void UpdateLookingAround()
        {
            // Scan the environment while paused
            if (Time.time >= _lookEndTime)
            {
                _currentState = State.SelectingWaypoint;
                return;
            }

            // Slowly rotate while looking
            float lookProgress = 1f - ((_lookEndTime - Time.time) / LOOK_DURATION);
            float angle = lookProgress * 180f; // Look 180 degrees during pause
            Vector3 lookDirection = Quaternion.Euler(0f, angle, 0f) * BotOwner.LookDirection;
            BotOwner.Steering.LookToDirection(lookDirection);
        }

        // Sixth Review Fix (Issue 105): Include Failed state in completion check
        public bool IsComplete => _currentState == State.Complete || _currentState == State.Failed;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("ExploreAreaLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Waypoints: {_waypointsVisited}/{_maxWaypoints}");
            stringBuilder.AppendLine($"  Center: {_centerPosition}");
            stringBuilder.AppendLine($"  Radius: {_exploreRadius:F0}m");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
