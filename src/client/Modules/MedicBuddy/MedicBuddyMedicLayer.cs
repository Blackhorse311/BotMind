using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// BigBrain layer for the MedicBuddy medic bot.
    /// Handles navigation to player, healing, and retreating.
    /// </summary>
    public class MedicBuddyMedicLayer : CustomLayer
    {
        private MedicBuddyController _controller;
        private MedicState _medicState = MedicState.Idle;
        private MoveToPatientLogic _moveLogic;
        private HealPatientLogic _healLogic;
        private FollowTeamLogic _retreatLogic;

        // Issue 18 Fix: Extract magic number to named constant
        // Sixth Review Fix (Issue 108): Made public for shared access by HealPatientLogic
        public const float HEAL_RANGE = 4f; // Distance at which medic starts healing

        private enum MedicState
        {
            Idle,
            MovingToPlayer,
            Healing,
            Retreating
        }

        public MedicBuddyMedicLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "MedicBuddy_Medic";
        }

        public override bool IsActive()
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                _controller = MedicBuddyController.Instance;
                if (_controller == null) return false;

                // Check if this bot is the medic in the active team
                if (!_controller.IsBotInTeam(BotOwner)) return false;
                if (!_controller.IsMedic(BotOwner)) return false;

                // Active when controller is in appropriate state
                var controllerState = _controller.CurrentState;
                return controllerState == MedicBuddyController.MedicBuddyState.MovingToPlayer ||
                       controllerState == MedicBuddyController.MedicBuddyState.Defending ||
                       controllerState == MedicBuddyController.MedicBuddyState.Healing ||
                       controllerState == MedicBuddyController.MedicBuddyState.Retreating;
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 95): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] MedicBuddyMedicLayer.IsActive error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public override Action GetNextAction()
        {
            // Bug Fix: Added try-catch for consistency with all other BigBrain framework callbacks
            try
            {
                UpdateMedicState();

                switch (_medicState)
                {
                    case MedicState.MovingToPlayer:
                        return new Action(typeof(MoveToPatientLogic), "Moving to patient");
                    case MedicState.Healing:
                        return new Action(typeof(HealPatientLogic), "Healing patient");
                    case MedicState.Retreating:
                        return new Action(typeof(FollowTeamLogic), "Retreating");
                    default:
                        return new Action(typeof(MoveToPatientLogic), "Moving to patient");
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer.GetNextAction error: {ex.Message}\n{ex.StackTrace}");
                return new Action(typeof(MoveToPatientLogic), "Error fallback");
            }
        }

        private void UpdateMedicState()
        {
            if (_controller == null) return;

            var controllerState = _controller.CurrentState;

            switch (controllerState)
            {
                case MedicBuddyController.MedicBuddyState.MovingToPlayer:
                    _medicState = MedicState.MovingToPlayer;
                    break;

                case MedicBuddyController.MedicBuddyState.Defending:
                case MedicBuddyController.MedicBuddyState.Healing:
                    // Check if arrived at player
                    if (_moveLogic != null && _moveLogic.HasArrived)
                    {
                        _medicState = MedicState.Healing;
                    }
                    else
                    {
                        var player = _controller.TargetPlayer;
                        if (player != null)
                        {
                            float dist = Vector3.Distance(BotOwner.Position, player.Position);
                            _medicState = dist <= HEAL_RANGE ? MedicState.Healing : MedicState.MovingToPlayer;
                        }
                        else
                        {
                            _medicState = MedicState.MovingToPlayer;
                        }
                    }
                    break;

                case MedicBuddyController.MedicBuddyState.Retreating:
                case MedicBuddyController.MedicBuddyState.Despawning:
                    _medicState = MedicState.Retreating;
                    break;

                default:
                    _medicState = MedicState.Idle;
                    break;
            }
        }

        public override bool IsCurrentActionEnding()
        {
            // Bug Fix: Added try-catch for consistency with all other BigBrain framework callbacks
            try
            {
                if (_controller == null) return true;

                UpdateMedicState();

                // Check if current action matches current state
                if (CurrentAction?.Type == typeof(MoveToPatientLogic) && _medicState != MedicState.MovingToPlayer)
                    return true;
                if (CurrentAction?.Type == typeof(HealPatientLogic) && _medicState != MedicState.Healing)
                    return true;
                if (CurrentAction?.Type == typeof(FollowTeamLogic) && _medicState != MedicState.Retreating)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer.IsCurrentActionEnding error: {ex.Message}\n{ex.StackTrace}");
                return true; // End action on error
            }
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 147): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer started");
                _medicState = MedicState.MovingToPlayer;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 148): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer stopped");
                _medicState = MedicState.Idle;
                _moveLogic = null;
                _healLogic = null;
                _retreatLogic = null;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyMedicLayer.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void RegisterLogic(CustomLogic logic)
        {
            if (logic is MoveToPatientLogic move)
                _moveLogic = move;
            else if (logic is HealPatientLogic heal)
                _healLogic = heal;
            else if (logic is FollowTeamLogic retreat)
                _retreatLogic = retreat;
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var controllerState = _controller?.CurrentState.ToString() ?? "None";
            stringBuilder.AppendLine("MedicBuddy Medic Layer");
            stringBuilder.AppendLine($"  Medic State: {_medicState}");
            stringBuilder.AppendLine($"  Controller: {controllerState}");
        }
    }
}
