using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// BigBrain layer for MedicBuddy shooter bots.
    /// Handles defensive positioning around the player while the medic heals.
    /// </summary>
    public class MedicBuddyShooterLayer : CustomLayer
    {
        private MedicBuddyController _controller;
        private ShooterState _shooterState = ShooterState.Idle;
        private DefendPerimeterLogic _defendLogic;
        private FollowTeamLogic _retreatLogic;

        private enum ShooterState
        {
            Idle,
            MovingToPosition,
            Defending,
            Retreating
        }

        public MedicBuddyShooterLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "MedicBuddy_Shooter";
        }

        public override bool IsActive()
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                _controller = MedicBuddyController.Instance;
                if (_controller == null) return false;

                // Check if this bot is in the team but NOT the medic
                if (!_controller.IsBotInTeam(BotOwner)) return false;
                if (_controller.IsMedic(BotOwner)) return false;

                // Active when controller is in appropriate state
                var controllerState = _controller.CurrentState;
                return controllerState == MedicBuddyController.MedicBuddyState.MovingToPlayer ||
                       controllerState == MedicBuddyController.MedicBuddyState.Defending ||
                       controllerState == MedicBuddyController.MedicBuddyState.Healing ||
                       controllerState == MedicBuddyController.MedicBuddyState.Retreating;
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 96): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] MedicBuddyShooterLayer.IsActive error: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        public override Action GetNextAction()
        {
            // Bug Fix: Added try-catch for consistency with all other BigBrain framework callbacks
            try
            {
                UpdateShooterState();

                switch (_shooterState)
                {
                    case ShooterState.Retreating:
                        return new Action(typeof(FollowTeamLogic), "Retreating");
                    case ShooterState.MovingToPosition:
                    case ShooterState.Defending:
                    default:
                        return new Action(typeof(DefendPerimeterLogic), "Defending perimeter");
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer.GetNextAction error: {ex.Message}\n{ex.StackTrace}");
                return new Action(typeof(DefendPerimeterLogic), "Error fallback");
            }
        }

        private void UpdateShooterState()
        {
            if (_controller == null) return;

            var controllerState = _controller.CurrentState;

            switch (controllerState)
            {
                case MedicBuddyController.MedicBuddyState.MovingToPlayer:
                    _shooterState = ShooterState.MovingToPosition;
                    break;

                case MedicBuddyController.MedicBuddyState.Defending:
                case MedicBuddyController.MedicBuddyState.Healing:
                    _shooterState = ShooterState.Defending;
                    break;

                case MedicBuddyController.MedicBuddyState.Retreating:
                case MedicBuddyController.MedicBuddyState.Despawning:
                    _shooterState = ShooterState.Retreating;
                    break;

                default:
                    _shooterState = ShooterState.Idle;
                    break;
            }
        }

        public override bool IsCurrentActionEnding()
        {
            // Bug Fix: Added try-catch for consistency with all other BigBrain framework callbacks
            try
            {
                if (_controller == null) return true;

                UpdateShooterState();

                // Check if current action matches current state
                if (CurrentAction?.Type == typeof(DefendPerimeterLogic) && _shooterState == ShooterState.Retreating)
                    return true;
                if (CurrentAction?.Type == typeof(FollowTeamLogic) && _shooterState != ShooterState.Retreating)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer.IsCurrentActionEnding error: {ex.Message}\n{ex.StackTrace}");
                return true; // End action on error
            }
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 145): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer started");
                _shooterState = ShooterState.MovingToPosition;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 146): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer stopped");
                _shooterState = ShooterState.Idle;
                _defendLogic = null;
                _retreatLogic = null;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] MedicBuddyShooterLayer.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void RegisterLogic(CustomLogic logic)
        {
            if (logic is DefendPerimeterLogic defend)
                _defendLogic = defend;
            else if (logic is FollowTeamLogic retreat)
                _retreatLogic = retreat;
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var controllerState = _controller?.CurrentState.ToString() ?? "None";
            var defensePos = _controller?.GetDefensePosition(BotOwner) ?? Vector3.zero;

            stringBuilder.AppendLine("MedicBuddy Shooter Layer");
            stringBuilder.AppendLine($"  Shooter State: {_shooterState}");
            stringBuilder.AppendLine($"  Controller: {controllerState}");
            stringBuilder.AppendLine($"  Defense Pos: {defensePos}");
        }
    }
}
