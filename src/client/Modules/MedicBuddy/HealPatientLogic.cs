using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Logic for the medic bot to heal the player.
    /// The actual healing is done by MedicBuddyController - this logic
    /// just manages the bot's behavior while healing is in progress.
    /// </summary>
    public class HealPatientLogic : CustomLogic
    {
        private float _startTime;
        private bool _healingComplete;

        public HealPatientLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 139): Add try-catch to framework callback
            try
            {
                _startTime = Time.time;
                _healingComplete = false;
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] HealPatientLogic started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] HealPatientLogic.Start error: {ex.Message}\n{ex.StackTrace}");
                _healingComplete = true;
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 140): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] HealPatientLogic stopped");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] HealPatientLogic.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Update(CustomLayer.ActionData data)
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                var controller = MedicBuddyController.Instance;
                if (controller == null)
                {
                    _healingComplete = true;
                    return;
                }

                var player = controller.TargetPlayer;
                // Third Review Fix: Added null check for HealthController
                if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                {
                    _healingComplete = true;
                    return;
                }

                // Check if controller has moved to retreat state
                if (controller.CurrentState == MedicBuddyController.MedicBuddyState.Retreating ||
                    controller.CurrentState == MedicBuddyController.MedicBuddyState.Despawning)
                {
                    _healingComplete = true;
                    return;
                }

                // Crouch while healing
                BotOwner.SetPose(0f);

                // Look at the player
                BotOwner.Steering.LookToPoint(player.Position + Vector3.up * 1f);

                // Stay near player
                // Sixth Review Fix (Issue 108): Use shared constant from MedicBuddyMedicLayer to avoid duplication
                float distanceToPlayer = Vector3.Distance(BotOwner.Position, player.Position);
                if (distanceToPlayer > MedicBuddyMedicLayer.HEAL_RANGE)
                {
                    // Move closer if drifted away
                    BotOwner.GoToPoint(player.Position, true, -1f, false, false, true, false, false);
                }
            }
            catch (Exception ex)
            {
                // Fifth Review Fix: Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] HealPatientLogic.Update error: {ex.Message}\n{ex.StackTrace}");
                _healingComplete = true; // Fail safe
            }
        }

        public bool IsHealingComplete => _healingComplete;

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var controller = MedicBuddyController.Instance;
            string state = controller?.CurrentState.ToString() ?? "Unknown";
            float elapsed = Time.time - _startTime;

            stringBuilder.AppendLine("HealPatientLogic");
            stringBuilder.AppendLine($"  Controller State: {state}");
            stringBuilder.AppendLine($"  Complete: {_healingComplete}");
            stringBuilder.AppendLine($"  Duration: {elapsed:F1}s");
        }
    }
}
