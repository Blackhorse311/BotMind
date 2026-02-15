using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Blackhorse311.BotMind.Interop;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// Logic for navigating to an extraction point and extracting.
    /// Integrates with SAIN's extraction system when available.
    /// </summary>
    public class ExtractLogic : CustomLogic
    {
        private enum State
        {
            AssigningExfil,
            MovingToExfil,
            Extracting,
            Complete,
            Failed
        }

        private State _currentState = State.AssigningExfil;
        private QuestObjective _objective;
        private float _startTime;
        private float _nextMoveTime;
        private bool _exfilAssigned;
        private int _assignAttempts;

        private const int MAX_ASSIGN_ATTEMPTS = 5;
        private const float ASSIGN_RETRY_INTERVAL = 3f;
        private const float MOVE_UPDATE_INTERVAL = 2f;
        /// <summary>Healthcare Critical: Rate limit SAIN extraction calls to avoid spam.</summary>
        private const float EXTRACT_CHECK_INTERVAL = 1.0f;
        /// <summary>Bug Fix: Overall timeout to prevent infinite extraction loop if SAIN never extracts the bot.</summary>
        private const float EXTRACTION_TIMEOUT = 300f; // 5 minutes

        // Fifth Review Fix (Issue 60): Cache extracted bots list to avoid allocation every frame
        private readonly List<string> _extractedBotsCache = new List<string>();

        // Healthcare Critical: Track last extraction call time to avoid spamming SAIN
        private float _lastExtractCallTime;

        public ExtractLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 143): Add try-catch to framework callback
            try
            {
                _currentState = State.AssigningExfil;
                _startTime = Time.time;
                _exfilAssigned = false;
                _assignAttempts = 0;
                _nextMoveTime = 0f;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] ExtractLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] ExtractLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed;
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 144): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] ExtractLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] ExtractLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                // Extract objective from ActionData if available
                if (_objective == null && data is QuestingActionData questingData)
                {
                    _objective = questingData.Objective;
                    questingData.Layer?.RegisterLogic(this);
                }

                switch (_currentState)
                {
                    case State.AssigningExfil:
                        UpdateAssigningExfil();
                        break;
                    case State.MovingToExfil:
                        UpdateMovingToExfil();
                        break;
                    case State.Extracting:
                        UpdateExtracting();
                        break;
                    case State.Complete:
                    case State.Failed:
                        break;
                }
            }
            catch (Exception ex)
            {
                // Fifth Review Fix: Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] ExtractLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _currentState = State.Failed; // Fail safe
            }
        }

        private void UpdateAssigningExfil()
        {
            // Try to use SAIN's extraction system
            if (SAINInterop.TrySetExfilForBot(BotOwner))
            {
                _exfilAssigned = true;
                _currentState = State.MovingToExfil;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] SAIN assigned extraction point");
                return;
            }

            _assignAttempts++;
            if (_assignAttempts >= MAX_ASSIGN_ATTEMPTS)
            {
                // SAIN extraction failed - try to trigger extract behavior anyway
                BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Could not assign exfil after {MAX_ASSIGN_ATTEMPTS} attempts");

                // If we have an objective with a target position, use that
                if (_objective != null && _objective.TargetPosition != Vector3.zero)
                {
                    _currentState = State.MovingToExfil;
                }
                else
                {
                    _currentState = State.Failed;
                }
                return;
            }

            // Wait and retry
            _nextMoveTime = Time.time + ASSIGN_RETRY_INTERVAL;
        }

        private void UpdateMovingToExfil()
        {
            // Bug Fix: Overall timeout to prevent infinite extraction loop.
            // If SAIN never confirms extraction (path blocked, SAIN bug, etc.),
            // the bot would loop forever without this check.
            if (Time.time - _startTime > EXTRACTION_TIMEOUT)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{BotOwner?.name ?? "Unknown"}] Extraction timed out after {EXTRACTION_TIMEOUT}s - marking as failed");
                _currentState = State.Failed;
                return;
            }

            // Let SAIN handle the extraction if it was assigned
            if (_exfilAssigned)
            {
                // Healthcare Critical: Rate limit SAIN extraction calls to avoid spam
                if (Time.time - _lastExtractCallTime >= EXTRACT_CHECK_INTERVAL)
                {
                    _lastExtractCallTime = Time.time;

                    // Trigger SAIN's extract behavior
                    SAINInterop.TryExtractBot(BotOwner);

                    // Fifth Review Fix (Issue 60): Reuse cached list to avoid allocation every frame
                    _extractedBotsCache.Clear();
                    SAINInterop.GetExtractedBots(_extractedBotsCache);
                    if (_extractedBotsCache.Contains(BotOwner.ProfileId))
                    {
                        _currentState = State.Complete;
                        BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Successfully extracted via SAIN");
                    }
                }
                return;
            }

            // Manual extraction if SAIN didn't assign (use objective position)
            if (_objective != null && _objective.TargetPosition != Vector3.zero)
            {
                float distance = Vector3.Distance(BotOwner.Position, _objective.TargetPosition);

                if (distance < 5f)
                {
                    _currentState = State.Extracting;
                    return;
                }

                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(1f);
                BotOwner.Steering.LookToMovingDirection();

                if (Time.time >= _nextMoveTime)
                {
                    _nextMoveTime = Time.time + MOVE_UPDATE_INTERVAL;
                    BotOwner.GoToPoint(_objective.TargetPosition, true, -1f, false, false, true, false, false);
                }
            }
        }

        private void UpdateExtracting()
        {
            // In a real implementation, this would trigger the extraction timer
            // For now, mark as complete after reaching the extract point
            _currentState = State.Complete;
            BotMindPlugin.Log?.LogDebug($"[{BotOwner.name}] Reached extraction point");
        }

        public bool IsComplete => _currentState == State.Complete || _currentState == State.Failed;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine("ExtractLogic");
            stringBuilder.AppendLine($"  State: {_currentState}");
            stringBuilder.AppendLine($"  Exfil Assigned: {_exfilAssigned}");
            stringBuilder.AppendLine($"  Assign Attempts: {_assignAttempts}");
            stringBuilder.AppendLine($"  Duration: {Time.time - _startTime:F1}s");
        }
    }
}
