using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;
using System.Text;
using Blackhorse311.BotMind.Configuration;
using Blackhorse311.BotMind.Interop;
using UnityEngine;

namespace Blackhorse311.BotMind.Modules.Questing
{
    /// <summary>
    /// BigBrain layer that handles bot questing/objective behavior.
    /// Activates when a bot has quest objectives to complete and is safe to pursue them.
    /// </summary>
    public class QuestingLayer : CustomLayer
    {
        // Third Review Fix: Added readonly modifier for immutability
        private readonly QuestManager _questManager;
        private float _lastQuestUpdateTime;
        private const float UPDATE_INTERVAL = 5f;

        // v1.5.0 Fix: Cooldown tracked internally — layer stays active during cooldown
        // to prevent EFT's default brain from walking bots back to spawn
        private float _lastObjectiveCompleteTime;
        private const float POST_OBJECTIVE_COOLDOWN = 3f;
        private bool _inCooldown;

        // Track current logic for completion checking
        private GoToLocationLogic _goToLogic;
        private ExploreAreaLogic _exploreLogic;
        private ExtractLogic _extractLogic;
        private FindItemLogic _findItemLogic;
        private PlaceItemLogic _placeItemLogic;

        public QuestingLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            _questManager = new QuestManager(botOwner);
        }

        public override string GetName()
        {
            return "BotMind_Questing";
        }

        public override bool IsActive()
        {
            // Third Review Fix: Added try-catch to prevent crashes in framework callback
            try
            {
                // Third Review Fix: Added BotOwner null check
                if (BotOwner?.Profile == null)
                {
                    return false;
                }

                // Check if questing is enabled
                if (!BotMindConfig.EnableQuesting.Value)
                {
                    return false;
                }

                // Check if this bot type should quest
                var role = BotOwner.Profile.Info?.Settings?.Role;
                bool isPMC = role == WildSpawnType.pmcUSEC || role == WildSpawnType.pmcBEAR;
                // Bug Fix: CursAssault was missing - these bots are registered in ScavBrains
                // but bypassed the ScavsDoQuests config check, always questing regardless
                bool isScav = role == WildSpawnType.assault || role == WildSpawnType.cursedAssault || role == WildSpawnType.marksman;

                if (isPMC && !BotMindConfig.PMCsDoQuests.Value)
                {
                    return false;
                }

                if (isScav && !BotMindConfig.ScavsDoQuests.Value)
                {
                    return false;
                }

                // Don't quest if bot is in combat — duration configurable via F12
                if (SAINInterop.IsBotInCombat(BotOwner, BotMindConfig.CombatAlertDuration.Value))
                {
                    return false;
                }

                // v1.5.0 Fix: Track cooldown internally but keep layer ACTIVE.
                // Returning false during cooldown lets EFT's default brain take over,
                // which walks bots back to spawn — the root cause of the "stuck" bug.
                _inCooldown = Time.time - _lastObjectiveCompleteTime < POST_OBJECTIVE_COOLDOWN;

                // Issue #1 Fix: Always update immediately when idle (prevents idle gaps).
                // Only throttle to 5s when we already have an active objective.
                if (!_inCooldown && (!_questManager.HasActiveObjective || Time.time - _lastQuestUpdateTime > UPDATE_INTERVAL))
                {
                    _lastQuestUpdateTime = Time.time;
                    _questManager.UpdateObjectives();
                }

                // Layer stays active during cooldown — GetNextAction handles the pause
                return _inCooldown || _questManager.HasActiveObjective;
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 90): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] QuestingLayer.IsActive error: {ex.Message}\n{ex.StackTrace}");
                return false; // Fail safe - deactivate layer on error
            }
        }

        public override Action GetNextAction()
        {
            // Seventh Review Fix (Issue 4): Add try-catch to framework callback for consistency
            try
            {
                // v1.5.0 Fix: During cooldown, explore locally instead of deactivating the layer.
                // This keeps the layer active (preventing EFT brain takeover) while giving
                // the bot a natural pause between objectives.
                if (_inCooldown)
                {
                    return new Action(typeof(ExploreAreaLogic), "Pausing between objectives");
                }

                var objective = _questManager.GetCurrentObjective();
                if (objective == null)
                {
                    return new Action(typeof(ExploreAreaLogic), "No objective");
                }

                // Create ActionData to pass objective information
                var actionData = new QuestingActionData
                {
                    Objective = objective,
                    Layer = this
                };

                switch (objective.Type)
                {
                    case QuestObjectiveType.GoToLocation:
                        return new Action(typeof(GoToLocationLogic), $"Going to {objective.Name}", actionData);
                    case QuestObjectiveType.FindItem:
                        return new Action(typeof(FindItemLogic), $"Finding {objective.Name}", actionData);
                    case QuestObjectiveType.PlaceItem:
                        return new Action(typeof(PlaceItemLogic), $"Placing {objective.Name}", actionData);
                    case QuestObjectiveType.Explore:
                    case QuestObjectiveType.Patrol:
                        return new Action(typeof(ExploreAreaLogic), $"Exploring {objective.Name}", actionData);
                    case QuestObjectiveType.Extract:
                        return new Action(typeof(ExtractLogic), "Heading to extraction", actionData);
                    default:
                        return new Action(typeof(ExploreAreaLogic), "Default explore", actionData);
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] QuestingLayer.GetNextAction error: {ex.Message}\n{ex.StackTrace}");
                return new Action(typeof(ExploreAreaLogic), "Error fallback");
            }
        }

        public override bool IsCurrentActionEnding()
        {
            // Healthcare Critical: Wrap framework callback in try-catch
            try
            {
                // v1.5.0 Fix: If cooldown just ended, end the pause action so
                // GetNextAction can pick the real next objective
                if (!_inCooldown && _exploreLogic != null && _questManager.HasActiveObjective)
                {
                    // Cooldown ended and we have a real objective — switch from pause explore
                    _exploreLogic = null;
                    return true;
                }

                // Check if any logic reports complete
                if (_goToLogic != null && _goToLogic.IsComplete)
                {
                    _questManager.MarkCurrentObjectiveComplete();
                    _goToLogic = null;
                    _lastObjectiveCompleteTime = Time.time;
                    return true;
                }
                if (_exploreLogic != null && _exploreLogic.IsComplete)
                {
                    _questManager.MarkCurrentObjectiveComplete();
                    _exploreLogic = null;
                    _lastObjectiveCompleteTime = Time.time;
                    return true;
                }
                if (_extractLogic != null && _extractLogic.IsComplete)
                {
                    _questManager.MarkCurrentObjectiveComplete();
                    _extractLogic = null;
                    _lastObjectiveCompleteTime = Time.time;
                    return true;
                }
                if (_findItemLogic != null && _findItemLogic.IsComplete)
                {
                    _questManager.MarkCurrentObjectiveComplete();
                    _findItemLogic = null;
                    _lastObjectiveCompleteTime = Time.time;
                    return true;
                }
                if (_placeItemLogic != null && _placeItemLogic.IsComplete)
                {
                    _questManager.MarkCurrentObjectiveComplete();
                    _placeItemLogic = null;
                    _lastObjectiveCompleteTime = Time.time;
                    return true;
                }

                return !_questManager.HasActiveObjective;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name}] QuestingLayer.IsCurrentActionEnding error: {ex.Message}\n{ex.StackTrace}");
                return true; // End action on error
            }
        }

        public void RegisterLogic(GoToLocationLogic logic)
        {
            _goToLogic = logic;
        }

        public void RegisterLogic(ExploreAreaLogic logic)
        {
            _exploreLogic = logic;
        }

        public void RegisterLogic(ExtractLogic logic)
        {
            _extractLogic = logic;
        }

        public void RegisterLogic(FindItemLogic logic)
        {
            _findItemLogic = logic;
        }

        public void RegisterLogic(PlaceItemLogic logic)
        {
            _placeItemLogic = logic;
        }

        public override void Start()
        {
            // Seventh Review Fix (Issue 141): Add try-catch to framework callback
            try
            {
                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] QuestingLayer started");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] QuestingLayer.Start error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void Stop()
        {
            // Seventh Review Fix (Issue 142): Add try-catch to framework callback
            try
            {
                // v1.4.0 Fix: Reset pose/speed so SAIN/vanilla AI doesn't inherit our values
                if (BotOwner != null)
                {
                    BotOwner.SetPose(1f);
                    BotOwner.SetTargetMoveSpeed(1f);
                }

                BotMindPlugin.Log?.LogDebug($"[{BotOwner?.name ?? "Unknown"}] QuestingLayer stopped");
                _goToLogic = null;
                _exploreLogic = null;
                _extractLogic = null;
                _findItemLogic = null;
                _placeItemLogic = null;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[{BotOwner?.name ?? "Unknown"}] QuestingLayer.Stop error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            // Healthcare Critical: Wrap framework callback in try-catch with null safety
            try
            {
                stringBuilder.AppendLine("BotMind Questing Layer");
                stringBuilder.AppendLine($"  Has Objective: {_questManager?.HasActiveObjective ?? false}");
                stringBuilder.AppendLine($"  In Cooldown: {_inCooldown}");
                float cooldownLeft = POST_OBJECTIVE_COOLDOWN - (Time.time - _lastObjectiveCompleteTime);
                if (cooldownLeft > 0f)
                {
                    stringBuilder.AppendLine($"  Cooldown Left: {cooldownLeft:F1}s");
                }
                var obj = _questManager?.GetCurrentObjective();
                if (obj != null)
                {
                    stringBuilder.AppendLine($"  Current: {obj.Type} - {obj.Name ?? "Unknown"}");
                }
            }
            catch (Exception ex)
            {
                stringBuilder.AppendLine($"  Error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Custom ActionData that passes quest objective information to logic classes.
    /// </summary>
    public class QuestingActionData : CustomLayer.ActionData
    {
        public QuestObjective Objective { get; set; }
        public QuestingLayer Layer { get; set; }
    }
}
