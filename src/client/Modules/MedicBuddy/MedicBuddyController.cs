using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Blackhorse311.BotMind.Configuration;
using Blackhorse311.BotMind.Patches;

namespace Blackhorse311.BotMind.Modules.MedicBuddy
{
    /// <summary>
    /// Controls the MedicBuddy feature - spawning and managing the medical team.
    /// Provides on-demand medical assistance to the player via a team of bots.
    /// </summary>
    public class MedicBuddyController : MonoBehaviour
    {
        // Issue 7 Fix: Added volatile for thread safety on singleton access
        // Fifth Review Fix (Issue 58): Use volatile OR Interlocked, not both - keeping volatile only
        private static volatile MedicBuddyController _instance;
        public static MedicBuddyController Instance => _instance;

        private float _lastSummonTime = float.MinValue;
        // Fifth Review Fix (Issue 57): Add lock for thread-safe team list access
        private readonly object _teamLock = new object();
        private readonly List<BotOwner> _activeTeam = new List<BotOwner>();
        // Use Interlocked for _medicBot - don't need volatile when using Interlocked
        // (Interlocked provides full memory barrier)
        private BotOwner _medicBot;
        private MedicBuddyState _state = MedicBuddyState.Idle;
        // Third Review Fix: Lock object for state machine thread safety
        private readonly object _stateLock = new object();
        private Player _player;
        private float _healingStartTime;
        private readonly float _healingDuration = 15f;
        private float _nextHealTick;
        private float _retreatStartTime;
        private int _pendingSpawns;
        // Issue 1 Fix: Track event subscription state to prevent memory leaks
        private bool _isSubscribedToSpawner;

        // Bug Fix: Store spawn position and player side for use in OnBotCreated callback
        private Vector3 _spawnPosition;
        private EPlayerSide _playerSide;

        // Casualty Collection Point: fixed rally position set by pressing the rally key.
        // When set, all bots navigate to this point instead of tracking the moving player.
        private Vector3? _rallyPoint;

        // Bug Fix: Track bots that existed before our spawn request to avoid capturing them
        private HashSet<int> _preExistingBotIds = new HashSet<int>();

        // Standards Compliance Fix: Cache NavMeshPath to avoid allocation in GetDefensePosition
        private readonly NavMeshPath _cachedNavPath = new NavMeshPath();

        // Standards Compliance Fix: Cache Enum.GetValues() result to avoid allocation every frame
        private static readonly EBodyPart[] _bodyParts = (EBodyPart[])Enum.GetValues(typeof(EBodyPart));

        // Healthcare Critical: Use a REUSABLE snapshot list for GetTeamSnapshot
        // Since Unity MonoBehaviour callbacks run on a single thread, we can safely reuse
        // a cached list instead of allocating new ones (which was causing 180+ allocs/sec).
        // The key insight: aliasing bugs only happen if we RETURN the cached list to callers
        // who then modify it or hold references. By copying into a reusable buffer that we
        // control, we avoid both GC pressure AND aliasing issues.
        private readonly List<BotOwner> _teamSnapshotBuffer = new List<BotOwner>(8);

        /// <summary>Time the medic spends "preparing gear" before healing begins (seconds).</summary>
        private const float HEAL_PREP_DELAY = 10f;
        /// <summary>Delay after healing completes before voice line and retreat (seconds).</summary>
        private const float HEAL_COMPLETE_DELAY = 3f;
        /// <summary>Interval between healing ticks when treating the player (seconds).</summary>
        private const float HEAL_TICK_INTERVAL = 1f;
        /// <summary>Amount of health restored per body part per tick.</summary>
        private const float HEAL_AMOUNT_PER_TICK = 25f;
        /// <summary>Health penalty for restored destroyed limbs (1.0 = full max HP, 0.5 = half max).</summary>
        private const float SURGERY_HEALTH_PENALTY = 1.0f;
        /// <summary>Distance from player at which bots are considered "arrived" (meters).</summary>
        private const float ARRIVAL_DISTANCE = 8f;
        /// <summary>Distance bots must retreat from player before despawning (meters).</summary>
        private const float RETREAT_DISTANCE = 50f;
        /// <summary>Maximum time to wait for bots to spawn before timing out (seconds).</summary>
        private const float SPAWN_TIMEOUT = 30f;
        /// <summary>Minimum time bots spend walking to the player before arrival can trigger (seconds).</summary>
        private const float MIN_APPROACH_TIME = 10f;
        /// <summary>Time bots spend setting up a defensive perimeter before healing begins (seconds).</summary>
        private const float PERIMETER_SETUP_TIME = 5f;
        /// <summary>Max time bots can spend trying to reach the player before being teleported (seconds).</summary>
        private const float MOVEMENT_TIMEOUT = 45f;
        /// <summary>Max vertical distance between spawn candidate and player (prevents wrong-floor spawns).</summary>
        private const float MAX_SPAWN_HEIGHT_DIFF = 3f;

        // Medical item template IDs - well-known Tarkov item IDs that are stable across versions.
        // Medic bots carry better gear (IFAK, CMS) while shooters carry basic first aid (AI-2, bandage).
        // Players can loot these from team bot corpses if the bots are killed.
        private static readonly string[] MEDIC_ITEM_TEMPLATES =
        {
            "590c678286f77426c9660122", // IFAK (Car First Aid Kit) - 300 HP
            "5d02778e86f774203e7dedbe", // CMS (Compact Medical Surgery Kit)
            "544fb25a4bdc2dfb738b4567", // Army Bandage
            "544fb3364bdc2d34748b456a", // Aluminum Splint
        };

        private static readonly string[] SHOOTER_ITEM_TEMPLATES =
        {
            "590c678286f77426c9660122", // IFAK (Car First Aid Kit) - 300 HP
            "544fb25a4bdc2dfb738b4567", // Army Bandage
            "544fb37f4bdc2dee738b4567", // Analgin Painkillers
        };

        private float _movingStartTime;
        private float _defendingStartTime;
        private bool _effectsCleared;
        private bool _playerWasProne;
        private bool _healingApplied;
        private float _healingCompleteTime;

        public enum MedicBuddyState
        {
            Idle,
            Spawning,
            MovingToPlayer,
            Defending,
            Healing,
            Retreating,
            Despawning
        }

        // Team tracking
        // Fifth Review Fix (Issue 57): Thread-safe team list access
        public bool IsBotInTeam(BotOwner bot)
        {
            lock (_teamLock)
            {
                return _activeTeam.Contains(bot);
            }
        }
        public bool IsMedic(BotOwner bot) => bot == _medicBot;
        public MedicBuddyState CurrentState { get { lock (_stateLock) { return _state; } } }
        public Player TargetPlayer => _player;

        /// <summary>
        /// The position bots should navigate to. Returns the Casualty Collection Point
        /// if one has been set (Y-key), otherwise returns the player's current position.
        /// Used by logic classes (MoveToPatientLogic, HealPatientLogic, DefendPerimeterLogic)
        /// as the central rally/defend point.
        /// </summary>
        public Vector3 RallyPoint => _rallyPoint ?? (_player != null ? _player.Position : Vector3.zero);

        /// <summary>
        /// Third Review Fix: Thread-safe state transition helper.
        /// Returns true if transition was successful.
        /// </summary>
        private bool TryTransitionState(MedicBuddyState expectedCurrent, MedicBuddyState newState)
        {
            lock (_stateLock)
            {
                if (_state != expectedCurrent) return false;
                _state = newState;
                return true;
            }
        }

        /// <summary>
        /// Third Review Fix: Thread-safe state setter.
        /// </summary>
        private void SetState(MedicBuddyState newState)
        {
            lock (_stateLock)
            {
                _state = newState;
            }
        }

        /// <summary>
        /// Healthcare Critical Fix: Get a thread-safe snapshot of the team.
        /// Uses a REUSABLE buffer to avoid GC allocation on every call.
        /// Safe because Unity MonoBehaviour callbacks are single-threaded.
        ///
        /// IMPORTANT: Callers must NOT store references to this list or modify it.
        /// The list is only valid until the next call to GetTeamSnapshot.
        /// </summary>
        private List<BotOwner> GetTeamSnapshot()
        {
            // Clear and refill the reusable buffer - avoids allocation
            _teamSnapshotBuffer.Clear();
            lock (_teamLock)
            {
                _teamSnapshotBuffer.AddRange(_activeTeam);
            }
            return _teamSnapshotBuffer;
        }

        /// <summary>
        /// Safely checks if the player is alive and their Transform is still accessible.
        /// After death in EFT, the Player object remains non-null but its underlying
        /// Unity Transform may be destroyed, causing NRE on .Position access.
        /// </summary>
        private bool IsPlayerAccessible()
        {
            try
            {
                return _player != null
                    && _player.HealthController != null
                    && _player.HealthController.IsAlive
                    && _player.Transform != null;
            }
            catch
            {
                return false;
            }
        }

        public void Awake()
        {
            _instance = this;
        }

        public void Update()
        {
            // Issue 6 Fix: Wrap Update in try-catch to prevent breaking Unity callback loop
            try
            {
                if (!BotMindConfig.EnableMedicBuddy.Value)
                {
                    return;
                }

                // Check for summon input (Ctrl+Alt+F10 key combo via BepInEx KeyboardShortcut)
                if (BotMindConfig.MedicBuddyKeybind.Value.IsDown())
                {
                    TrySummonMedicBuddy();
                }

                // Check for rally point input (Y key by default)
                if (BotMindConfig.MedicBuddyRallyKeybind.Value.IsDown())
                {
                    TrySetRallyPoint();
                }

                // Update state machine
                UpdateStateMachine();
            }
            catch (Exception ex)
            {
                // Sixth Review Fix (Issue 94): Include stack trace in error log
                BotMindPlugin.Log?.LogError($"MedicBuddyController.Update failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void TrySummonMedicBuddy()
        {
            // Check if on cooldown
            float cooldown = BotMindConfig.MedicBuddyCooldown.Value;
            if (Time.time - _lastSummonTime < cooldown)
            {
                float remaining = cooldown - (Time.time - _lastSummonTime);
                MedicBuddyNotifier.WarnCooldown(remaining);
                return;
            }

            // Check if PMC-only restriction applies
            if (BotMindConfig.MedicBuddyPMCOnly.Value && _player != null)
            {
                if (_player.Side != EPlayerSide.Bear && _player.Side != EPlayerSide.Usec)
                {
                    BotMindPlugin.Log?.LogInfo("MedicBuddy only available to PMC players");
                    MedicBuddyNotifier.WarnPMCOnly();
                    return;
                }
            }

            // Check if player is alive
            if (_player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
            {
                BotMindPlugin.Log?.LogInfo("Cannot summon MedicBuddy - player is dead");
                return;
            }

            // Check if player is actually injured
            if (IsPlayerFullyHealed())
            {
                BotMindPlugin.Log?.LogInfo("Cannot summon MedicBuddy - player is not injured");
                MedicBuddyNotifier.WarnNotInjured();
                return;
            }

            // Check if player has medical items to heal themselves
            if (PlayerHasMedicalItems())
            {
                BotMindPlugin.Log?.LogInfo("Cannot summon MedicBuddy - player has medical supplies");
                MedicBuddyNotifier.WarnHasMedicalSupplies();
                return;
            }

            // Check if team is already active - use thread-safe transition
            if (!TryTransitionState(MedicBuddyState.Idle, MedicBuddyState.Spawning))
            {
                BotMindPlugin.Log?.LogInfo("MedicBuddy team is already active");
                return;
            }

            // Begin spawning
            _lastSummonTime = Time.time;
            SpawnMedicTeam();
        }

        /// <summary>
        /// Sets a Casualty Collection Point at the player's current position.
        /// All team bots will converge on this fixed point instead of chasing the moving player.
        /// Only works when a MedicBuddy team is actively deployed.
        /// </summary>
        private void TrySetRallyPoint()
        {
            var currentState = CurrentState;
            if (currentState == MedicBuddyState.Idle || currentState == MedicBuddyState.Spawning)
            {
                MedicBuddyNotifier.WarnNoActiveTeam();
                return;
            }

            // Don't allow rally point changes during retreat/despawn - team is leaving
            if (currentState == MedicBuddyState.Retreating || currentState == MedicBuddyState.Despawning)
            {
                return;
            }

            if (!IsPlayerAccessible())
            {
                return;
            }

            _rallyPoint = _player.Position;
            BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Casualty Collection Point set at {_rallyPoint.Value}");
            MedicBuddyNotifier.NotifyRallyPointSet();
        }

        private void SpawnMedicTeam()
        {
            BotMindPlugin.Log?.LogInfo("Spawning MedicBuddy team...");

            int teamSize = BotMindConfig.MedicBuddyTeamSize.Value;
            // Fifth Review Fix (Issue 59): Use Interlocked.Exchange for thread-safe assignment
            Interlocked.Exchange(ref _pendingSpawns, teamSize);
            lock (_teamLock)
            {
                _activeTeam.Clear();
            }
            _medicBot = null;

            // Calculate spawn position (behind player, out of sight)
            // Bug Fix: Store as field so OnBotCreated can teleport bots to this position
            _spawnPosition = CalculateSpawnPosition();

            if (_spawnPosition == Vector3.zero)
            {
                BotMindPlugin.Log?.LogWarning("Could not find valid spawn position for MedicBuddy team");
                SetState(MedicBuddyState.Idle);
                return;
            }

            // Bug Fix: Store player side so OnBotCreated can make bots friendly
            _playerSide = _player.Side;

            // Hook into bot spawner to register our bots when they spawn
            var botGame = Singleton<IBotGame>.Instance;
            if (botGame == null)
            {
                BotMindPlugin.Log?.LogWarning("BotGame not available - cannot spawn MedicBuddy team");
                SetState(MedicBuddyState.Idle);
                return;
            }

            // Subscribe to bot creation events
            var spawner = botGame.BotsController?.BotSpawner;
            if (spawner != null)
            {
                // Issue 1 Fix: Track subscription state and use try-finally for cleanup
                try
                {
                    // Bug Fix: Snapshot all existing bot IDs BEFORE subscribing to OnBotCreated.
                    // This prevents capturing bots that were already spawning/queued by the game
                    // (e.g., regular PMC waves) which would consume our _pendingSpawns slots.
                    SnapshotExistingBots(spawner);

                    spawner.OnBotCreated += OnBotCreated;
                    _isSubscribedToSpawner = true;

                    // Bug Fix: Spawn bots as the same WildSpawnType that matches the player's side.
                    // Using WildSpawnType.assault (Scav) for a PMC player spawns HOSTILE bots.
                    // PMC-side bots must use pmcUSEC or pmcBEAR to be friendly to the player.
                    WildSpawnType spawnType;
                    if (_playerSide == EPlayerSide.Usec)
                        spawnType = WildSpawnType.pmcUSEC;
                    else if (_playerSide == EPlayerSide.Bear)
                        spawnType = WildSpawnType.pmcBEAR;
                    else
                        spawnType = WildSpawnType.assault; // Scav player gets Scav allies

                    // Temporarily raise bot cap to allow MedicBuddy bots through
                    BotLimitManager.BeginMedicBuddySpawn();

                    BotDifficulty escortDifficulty = (BotDifficulty)BotMindConfig.MedicBuddyEscortDifficulty.Value;
                    for (int i = 0; i < teamSize; i++)
                    {
                        spawner.SpawnBotByTypeForce(1, spawnType, escortDifficulty, new BotSpawnParams());
                    }

                    BotMindPlugin.Log?.LogInfo($"Requested spawn of {teamSize} MedicBuddy bots (type: {spawnType})");

                    // Notify player that help has been requested
                    int variant = MedicBuddyNotifier.NotifyHelpRequested(_player.Side);
                    MedicBuddyAudio.Play("summon_request", variant, _player.Side);
                }
                catch (Exception ex)
                {
                    // Sixth Review Fix (Issue 100): Include stack trace in error log
                    BotMindPlugin.Log?.LogError($"Failed to spawn MedicBuddy team: {ex.Message}\n{ex.StackTrace}");
                    MedicBuddyNotifier.WarnSpawnFailed();
                    BotLimitManager.EndMedicBuddySpawn();
                    UnsubscribeFromSpawner();
                    SetState(MedicBuddyState.Idle);
                }
            }
            else
            {
                BotMindPlugin.Log?.LogWarning("BotSpawner not available");
                SetState(MedicBuddyState.Idle);
            }
        }

        /// <summary>
        /// Issue 1 Fix: Centralized method to safely unsubscribe from spawner events.
        /// </summary>
        private void UnsubscribeFromSpawner()
        {
            if (!_isSubscribedToSpawner) return;

            try
            {
                var spawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
                if (spawner != null)
                {
                    spawner.OnBotCreated -= OnBotCreated;
                }
            }
            catch (Exception ex)
            {
                // Seventh Review Fix (Issue 151): Include stack trace in debug log
                BotMindPlugin.Log?.LogDebug($"Error unsubscribing from spawner: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                _isSubscribedToSpawner = false;
            }
        }

        /// <summary>
        /// Snapshots all existing bot instance IDs before we subscribe to OnBotCreated.
        /// Any bot whose ID is in this set was NOT spawned by our request.
        /// </summary>
        private void SnapshotExistingBots(BotSpawner spawner)
        {
            // Allocate a new set instead of Clear+reuse. OnBotCreated reads _preExistingBotIds
            // on a potentially different callback timing, so reusing the same set risks a race
            // where Contains() reads while Clear() is modifying the set.
            var newSet = new HashSet<int>();
            try
            {
                var existingBots = spawner.Bots?.BotOwners;
                if (existingBots != null)
                {
                    foreach (var bot in existingBots)
                    {
                        if (bot != null)
                        {
                            newSet.Add(bot.GetInstanceID());
                        }
                    }
                }
                BotMindPlugin.Log?.LogDebug($"Snapshotted {newSet.Count} pre-existing bots");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Error snapshotting existing bots: {ex.Message}");
            }
            _preExistingBotIds = newSet;
        }

        private void OnBotCreated(BotOwner bot)
        {
            // Fifth Review Fix (Issue 59): Use Interlocked for thread-safe pending spawns decrement
            // Sixth Review Fix (Issue 106): Read state with lock to prevent race condition
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }
            if (currentState != MedicBuddyState.Spawning || _pendingSpawns <= 0)
            {
                return;
            }

            // Bug Fix: Reject bots that existed before our spawn request.
            // Without this, regular PMC waves spawning concurrently consume our _pendingSpawns
            // slots, leaving actual team bots uncaptured.
            if (_preExistingBotIds.Contains(bot.GetInstanceID()))
            {
                return;
            }

            // Bug Fix: Filter to only capture bots matching our spawn request type.
            // Without this, ANY bot spawning during the window (enemy scavs, bosses, etc.)
            // would be claimed as a team member.
            var role = bot.Profile?.Info?.Settings?.Role;
            bool isExpectedType;
            if (_playerSide == EPlayerSide.Usec)
                isExpectedType = role == WildSpawnType.pmcUSEC;
            else if (_playerSide == EPlayerSide.Bear)
                isExpectedType = role == WildSpawnType.pmcBEAR;
            else
                isExpectedType = role == WildSpawnType.assault;

            if (!isExpectedType)
            {
                return;
            }

            // Bug Fix: Teleport bot to calculated spawn position behind the player.
            // SpawnBotByTypeForce uses default spawn points, so bots appear at random map locations.
            if (_spawnPosition != Vector3.zero)
            {
                try
                {
                    // Spread bots around the spawn point using angles instead of linear offset
                    int currentTeamCount;
                    lock (_teamLock)
                    {
                        currentTeamCount = _activeTeam.Count;
                    }
                    float spreadAngle = currentTeamCount * 90f;
                    Vector3 spreadDir = Quaternion.Euler(0f, spreadAngle, 0f) * Vector3.forward;
                    Vector3 targetPos = _spawnPosition + spreadDir * 3f;

                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 5f, NavMesh.AllAreas) &&
                        Mathf.Abs(hit.position.y - _spawnPosition.y) <= MAX_SPAWN_HEIGHT_DIFF)
                    {
                        bot.Transform.position = hit.position;
                    }
                    else
                    {
                        bot.Transform.position = _spawnPosition;
                    }
                    BotMindPlugin.Log?.LogInfo($"[{bot.name}] Teleported to spawn at {bot.Transform.position}");
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug($"[{bot.name}] Could not teleport to spawn position: {ex.Message}");
                }
            }

            // Fifth Review Fix (Issue 57): Thread-safe team list access
            lock (_teamLock)
            {
                _activeTeam.Add(bot);
            }

            // Make the bot friendly to the player and to all existing team members.
            // PMC bots are hostile to PMC players by default in EFT/SPT.
            // Without inter-team friendship, bots in different BotsGroups attack each other.
            MakeBotFriendlyToPlayer(bot);
            MakeTeamBotsFriendly(bot);

            int remaining = Interlocked.Decrement(ref _pendingSpawns);

            // Seventh Review Fix (Issue 3): Thread-safe medic assignment using Interlocked.CompareExchange
            // This prevents race condition where multiple bots spawning simultaneously could both
            // see _medicBot as null and both try to become the medic
            BotOwner previousMedic = Interlocked.CompareExchange(ref _medicBot, bot, null);
            if (previousMedic == null)
            {
                // This thread successfully assigned the medic (was null, now this bot)
                BotMindPlugin.Log?.LogDebug($"[{bot.name}] Assigned as MedicBuddy medic");
            }
            else
            {
                BotMindPlugin.Log?.LogDebug($"[{bot.name}] Assigned as MedicBuddy shooter");
            }

            // Equip the bot with medical items so the player can loot their corpse
            EquipBotWithMedicalGear(bot);

            // Check if all bots spawned
            if (remaining <= 0)
            {
                // Issue 1 Fix: Use centralized unsubscribe method
                UnsubscribeFromSpawner();
                BotLimitManager.EndMedicBuddySpawn();

                int teamCount;
                lock (_teamLock)
                {
                    teamCount = _activeTeam.Count;
                }
                BotMindPlugin.Log?.LogInfo($"MedicBuddy team complete: {teamCount} bots");

                // Final pass: ensure all team members have complete cross-friendship.
                // Earlier MakeTeamBotsFriendly calls only linked each new bot to those already
                // captured, so the first bot has no allies yet. This pass fills the gaps.
                FinalizeTeamFriendship();

                SetState(MedicBuddyState.MovingToPlayer);
                _movingStartTime = Time.time;

                // Notify player that team is en route and carries medical supplies
                int variant = MedicBuddyNotifier.NotifyHelpEnRoute(_player.Side);
                MedicBuddyAudio.Play("team_enroute", variant, _player.Side);
                MedicBuddyNotifier.NotifyTeamCarriesMeds();
            }
        }

        /// <summary>
        /// Makes a spawned bot treat the player as an ally instead of an enemy.
        /// Without this, PMC bots are hostile to the PMC player by default.
        /// Uses BotsGroup.AddAlly() to prevent future enemy detection from re-adding
        /// the player, and RemoveEnemy() to clear any existing hostile status.
        /// </summary>
        private void MakeBotFriendlyToPlayer(BotOwner bot)
        {
            if (_player == null || bot == null) return;

            try
            {
                var group = bot.BotsGroup;
                if (group == null)
                {
                    BotMindPlugin.Log?.LogWarning($"[{bot.name}] BotsGroup is null - cannot set friendly status");
                    return;
                }

                // Add the player as an ally to the bot's group.
                // This is checked by BotsGroup.AddEnemy() before adding, so the bot's AI
                // won't re-add the player as an enemy even when it detects them.
                group.AddAlly(_player);

                // Remove any existing enemy entry for the player
                IPlayer playerAsIPlayer = _player;
                if (group.IsEnemy(playerAsIPlayer))
                {
                    group.RemoveEnemy(playerAsIPlayer);
                }

                BotMindPlugin.Log?.LogDebug($"[{bot.name}] Set as friendly to player");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{bot.name}] Failed to set friendly status: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Makes a newly captured team bot friendly to all existing team members (and vice versa).
        /// Each PMC bot spawns in its own BotsGroup, so without this they treat each other as enemies.
        /// Called in OnBotCreated for each bot as it joins the team.
        /// </summary>
        private void MakeTeamBotsFriendly(BotOwner newBot)
        {
            if (newBot?.BotsGroup == null) return;

            List<BotOwner> existingMembers;
            lock (_teamLock)
            {
                existingMembers = new List<BotOwner>(_activeTeam);
            }

            foreach (var existingBot in existingMembers)
            {
                if (existingBot == null || existingBot.IsDead || existingBot == newBot) continue;
                if (existingBot.BotsGroup == null) continue;

                try
                {
                    // Add new bot as ally in existing bot's group
                    IPlayer newBotPlayer = newBot.GetPlayer;
                    if (newBotPlayer != null)
                    {
                        existingBot.BotsGroup.AddAlly(newBot.GetPlayer);
                        if (existingBot.BotsGroup.IsEnemy(newBotPlayer))
                        {
                            existingBot.BotsGroup.RemoveEnemy(newBotPlayer);
                        }
                    }

                    // Add existing bot as ally in new bot's group
                    IPlayer existingBotPlayer = existingBot.GetPlayer;
                    if (existingBotPlayer != null)
                    {
                        newBot.BotsGroup.AddAlly(existingBot.GetPlayer);
                        if (newBot.BotsGroup.IsEnemy(existingBotPlayer))
                        {
                            newBot.BotsGroup.RemoveEnemy(existingBotPlayer);
                        }
                    }
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug(
                        $"Error setting inter-team friendship between [{newBot.name}] and [{existingBot.name}]: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Final pass to ensure complete friendship between all team members.
        /// Called once after all bots have spawned.
        /// </summary>
        private void FinalizeTeamFriendship()
        {
            List<BotOwner> allMembers;
            lock (_teamLock)
            {
                allMembers = new List<BotOwner>(_activeTeam);
            }

            for (int i = 0; i < allMembers.Count; i++)
            {
                var botA = allMembers[i];
                if (botA == null || botA.IsDead || botA.BotsGroup == null) continue;

                for (int j = i + 1; j < allMembers.Count; j++)
                {
                    var botB = allMembers[j];
                    if (botB == null || botB.IsDead || botB.BotsGroup == null) continue;

                    try
                    {
                        IPlayer playerA = botA.GetPlayer;
                        IPlayer playerB = botB.GetPlayer;
                        if (playerA == null || playerB == null) continue;

                        botA.BotsGroup.AddAlly(botB.GetPlayer);
                        botB.BotsGroup.AddAlly(botA.GetPlayer);

                        if (botA.BotsGroup.IsEnemy(playerB))
                            botA.BotsGroup.RemoveEnemy(playerB);
                        if (botB.BotsGroup.IsEnemy(playerA))
                            botB.BotsGroup.RemoveEnemy(playerA);
                    }
                    catch (Exception ex)
                    {
                        BotMindPlugin.Log?.LogDebug(
                            $"Error finalizing friendship [{botA.name}] <-> [{botB.name}]: {ex.Message}");
                    }
                }
            }

            BotMindPlugin.Log?.LogDebug($"Finalized inter-team friendship for {allMembers.Count} bots");
        }

        /// <summary>
        /// Equips a team bot with medical items so the player can loot their corpse if they die.
        /// Medic bots get higher-tier gear (IFAK, CMS), shooters get basic items (AI-2, bandage).
        /// Items are created via ItemFactoryClass and placed into the bot's vest/pockets/backpack.
        /// </summary>
        private void EquipBotWithMedicalGear(BotOwner bot)
        {
            try
            {
                var player = bot.GetPlayer;
                if (player == null) return;

                var inventoryController = player.InventoryController;
                if (inventoryController == null) return;

                var itemFactory = Singleton<ItemFactoryClass>.Instance;
                if (itemFactory == null)
                {
                    BotMindPlugin.Log?.LogWarning($"[{bot.name}] ItemFactoryClass not available - cannot equip medical gear");
                    return;
                }

                bool isMedic = bot == _medicBot;
                string[] templateIds = isMedic ? MEDIC_ITEM_TEMPLATES : SHOOTER_ITEM_TEMPLATES;
                int addedCount = 0;

                foreach (var templateId in templateIds)
                {
                    try
                    {
                        Item item = itemFactory.CreateItem(MongoID.Generate(), templateId, null);
                        if (item == null)
                        {
                            BotMindPlugin.Log?.LogWarning($"[{bot.name}] Failed to create item {templateId}");
                            continue;
                        }

                        if (TryAddItemToEquipmentGrid(bot, inventoryController, item))
                        {
                            addedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        BotMindPlugin.Log?.LogWarning($"[{bot.name}] Could not add medical item {templateId}: {ex.Message}");
                    }
                }

                string role = isMedic ? "medic" : "shooter";
                if (addedCount > 0)
                {
                    BotMindPlugin.Log?.LogInfo($"[{bot.name}] Equipped {addedCount} medical items ({role})");
                }
                else
                {
                    BotMindPlugin.Log?.LogWarning($"[{bot.name}] Failed to equip any medical items ({role}) - {templateIds.Length} attempted");
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning($"[{bot.name ?? "Unknown"}] Failed to equip medical gear: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds an orphan item (no parent) directly to a bot's equipment grid.
        /// Iterates vest, backpack, and pockets to find free space, bypassing
        /// FindGridToPickUp() which throws on parentless items.
        /// </summary>
        private static bool TryAddItemToEquipmentGrid(BotOwner bot, InventoryController inventoryController, Item item)
        {
            var equipment = inventoryController.Inventory.Equipment;
            var slotsToCheck = new[] { EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.Pockets };

            foreach (var slotType in slotsToCheck)
            {
                var slot = equipment.GetSlot(slotType);
                if (slot.ContainedItem == null) continue;

                var container = slot.ContainedItem as SearchableItemItemClass;
                if (container?.Grids == null) continue;

                foreach (var grid in container.Grids)
                {
                    var location = grid.FindFreeSpace(item);
                    if (location == null) continue;

                    var result = grid.AddItemWithoutRestrictions(item, location);
                    if (result.Succeeded)
                    {
                        BotMindPlugin.Log?.LogDebug($"[{bot.name}] Added {item.ShortName} to {slotType}");
                        return true;
                    }
                }
            }

            BotMindPlugin.Log?.LogWarning($"[{bot.name}] No grid space for {item.ShortName}");
            return false;
        }

        /// <summary>Angles to try when looking for a spawn position, in order of preference.
        /// Behind player first (180), then rear diagonals, then sides. Never directly in front (0).
        /// This keeps the spawn out of the player's field of view.</summary>
        private static readonly float[] SPAWN_ANGLES_BEHIND = { 180f, 160f, 200f, 140f, 220f };
        private static readonly float[] SPAWN_ANGLES_WIDE = { 120f, 240f, 100f, 260f, 90f, 270f };
        /// <summary>Distances to try for spawn position. Keep close to the player's known-safe area
        /// to avoid minefields and out-of-bounds zones on map edges.</summary>
        private static readonly float[] SPAWN_DISTANCES = { 20f, 15f, 25f, 12f };
        /// <summary>Approximate half-angle of player's field of view (degrees).</summary>
        private const float PLAYER_FOV_HALF = 63f;

        /// <summary>
        /// Checks whether a candidate position is outside the player's field of view.
        /// </summary>
        private bool IsOutOfPlayerView(Vector3 candidatePos)
        {
            // Zero Y before normalizing so the angle is purely horizontal.
            // Normalizing first then zeroing Y skews the result for height differences.
            Vector3 toCandidate = candidatePos - _player.Position;
            toCandidate.y = 0f;
            Vector3 forward = _player.Transform.forward;
            forward.y = 0f;
            float angle = Vector3.Angle(forward, toCandidate);
            return angle > PLAYER_FOV_HALF;
        }

        private Vector3 CalculateSpawnPosition()
        {
            if (_player == null)
            {
                return Vector3.zero;
            }

            Vector3 playerPos = _player.Position;
            Vector3 playerForward = _player.Transform.forward;

            // Pass 1: Behind the player at distance, out of FOV, with complete NavMesh path
            foreach (float distance in SPAWN_DISTANCES)
            {
                foreach (float angle in SPAWN_ANGLES_BEHIND)
                {
                    Vector3 candidate = TrySpawnCandidate(playerPos, playerForward, angle, distance, true);
                    if (candidate != Vector3.zero) return candidate;
                }
            }

            // Pass 2: Wider angles (still preferring out of view) at distance
            foreach (float distance in SPAWN_DISTANCES)
            {
                foreach (float angle in SPAWN_ANGLES_WIDE)
                {
                    Vector3 candidate = TrySpawnCandidate(playerPos, playerForward, angle, distance, true);
                    if (candidate != Vector3.zero) return candidate;
                }
            }

            // Pass 3: Very close behind player (8-10m), out of FOV
            foreach (float distance in new float[] { 10f, 8f })
            {
                foreach (float angle in SPAWN_ANGLES_BEHIND)
                {
                    Vector3 candidate = TrySpawnCandidate(playerPos, playerForward, angle, distance, true);
                    if (candidate != Vector3.zero) return candidate;
                }
            }

            // Pass 4: Any direction with path, ignoring FOV (player may be in a corner)
            for (float angle = 0f; angle < 360f; angle += 30f)
            {
                foreach (float distance in new float[] { 10f, 8f, 5f })
                {
                    Vector3 candidate = TrySpawnCandidate(playerPos, playerForward, angle, distance, false);
                    if (candidate != Vector3.zero)
                    {
                        BotMindPlugin.Log?.LogInfo(
                            $"MedicBuddy spawn: in-view fallback angle={angle}, dist={distance}");
                        return candidate;
                    }
                }
            }

            BotMindPlugin.Log?.LogWarning("MedicBuddy could not find any valid spawn position");
            return Vector3.zero;
        }

        /// <summary>
        /// Tests a single spawn candidate position. Returns the valid NavMesh position or Vector3.zero.
        /// </summary>
        private Vector3 TrySpawnCandidate(Vector3 playerPos, Vector3 playerForward, float angle, float distance, bool requireOutOfView)
        {
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * playerForward;
            Vector3 candidatePos = playerPos + direction * distance + Vector3.up * 0.5f;

            if (!NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                return Vector3.zero;

            // Reject positions on a different floor
            if (Mathf.Abs(hit.position.y - playerPos.y) > MAX_SPAWN_HEIGHT_DIFF)
                return Vector3.zero;

            // Reject positions in the player's field of view (if required)
            if (requireOutOfView && !IsOutOfPlayerView(hit.position))
                return Vector3.zero;

            // Require a complete walkable path to the player
            _cachedNavPath.ClearCorners();
            if (!NavMesh.CalculatePath(hit.position, playerPos, NavMesh.AllAreas, _cachedNavPath) ||
                _cachedNavPath.status != NavMeshPathStatus.PathComplete)
                return Vector3.zero;

            BotMindPlugin.Log?.LogInfo(
                $"MedicBuddy spawn position: angle={angle}, dist={distance}, pos={hit.position}, playerPos={playerPos}");
            return hit.position;
        }

        private void UpdateStateMachine()
        {
            // Clean up dead bots from the team
            CleanupDeadBots();

            // Detect if another mod (e.g., SameSideIsFriendly teamkill) flipped our bots hostile
            CheckTeamHostility();

            // Fifth Review Fix (Issue 55): Read state with lock to prevent race condition
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }

            switch (currentState)
            {
                case MedicBuddyState.Idle:
                    break;

                case MedicBuddyState.Spawning:
                    UpdateSpawning();
                    break;

                case MedicBuddyState.MovingToPlayer:
                    UpdateMovingToPlayer();
                    break;

                case MedicBuddyState.Defending:
                    UpdateDefending();
                    break;

                case MedicBuddyState.Healing:
                    UpdateHealing();
                    break;

                case MedicBuddyState.Retreating:
                    UpdateRetreating();
                    break;

                case MedicBuddyState.Despawning:
                    DespawnTeam();
                    break;
            }
        }

        private void CleanupDeadBots()
        {
            // Fifth Review Fix (Issue 57): Thread-safe team list access
            lock (_teamLock)
            {
                _activeTeam.RemoveAll(bot =>
                {
                    if (bot == null) return true;
                    if (bot.IsDead)
                    {
                        BotMindPlugin.Log?.LogInfo($"[{bot.name}] MedicBuddy team member died at {bot.Position}");
                        return true;
                    }
                    return false;
                });
            }

            // Issue 2 Fix: Corrected null check order - check null first with OR
            // Fifth Review Fix (Issue 87): Simplified redundant null check logic
            if (_medicBot != null && _medicBot.IsDead)
            {
                _medicBot = null;
            }

            if (_medicBot == null)
            {
                // Don't trigger promotion/abort during Spawning (more bots incoming),
                // Retreating, or Despawning (already shutting down)
                var currentState = CurrentState;
                if (currentState == MedicBuddyState.Spawning
                    || currentState == MedicBuddyState.Retreating
                    || currentState == MedicBuddyState.Despawning)
                {
                    return;
                }

                // Try to promote a surviving shooter to medic
                if (TryPromoteMedic())
                {
                    return;
                }

                // No bots left to promote - clean up
                if (currentState != MedicBuddyState.Idle)
                {
                    BotMindPlugin.Log?.LogInfo("All MedicBuddy team members lost");
                    SetState(MedicBuddyState.Idle);
                }
            }
        }

        /// <summary>
        /// Promotes a surviving shooter to the medic role when the original medic dies.
        /// Returns true if a promotion occurred (mission continues), false if no bots remain.
        /// </summary>
        private bool TryPromoteMedic()
        {
            lock (_teamLock)
            {
                if (_activeTeam.Count == 0) return false;

                // Pick the first living team member as the new medic
                foreach (var bot in _activeTeam)
                {
                    if (bot != null && !bot.IsDead)
                    {
                        _medicBot = bot;
                        BotMindPlugin.Log?.LogInfo($"[{bot.name}] Promoted to medic (previous medic KIA)");
                        MedicBuddyNotifier.NotifyMedicPromoted();

                        // Reset effects-cleared flag so the new medic will
                        // clear effects and heal fresh when healing starts
                        _effectsCleared = false;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if any team bot has become hostile to the player.
        /// This can happen when another mod (e.g., SameSideIsFriendly's teamkill mechanic)
        /// flips bot allegiance. If detected, immediately despawns the team to prevent
        /// MedicBuddy bots from attacking the player.
        /// </summary>
        private void CheckTeamHostility()
        {
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }

            // Only relevant during active mission states
            if (currentState == MedicBuddyState.Idle
                || currentState == MedicBuddyState.Spawning
                || currentState == MedicBuddyState.Despawning)
            {
                return;
            }

            if (_player == null) return;

            List<BotOwner> teamSnapshot = GetTeamSnapshot();
            IPlayer playerAsIPlayer = _player;

            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;

                try
                {
                    if (bot.BotsGroup != null && bot.BotsGroup.IsEnemy(playerAsIPlayer))
                    {
                        BotMindPlugin.Log?.LogWarning(
                            $"MedicBuddy bot [{bot.name}] became hostile to player - aborting mission");
                        SetState(MedicBuddyState.Despawning);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug(
                        $"Error checking bot hostility: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        private void UpdateSpawning()
        {
            // Check for spawn timeout
            if (Time.time - _lastSummonTime > SPAWN_TIMEOUT)
            {
                BotMindPlugin.Log?.LogWarning("MedicBuddy spawn timed out");

                // Issue 1 Fix: Use centralized unsubscribe method
                UnsubscribeFromSpawner();
                BotLimitManager.EndMedicBuddySpawn();

                // Sixth Review Fix (Issue 107): Thread-safe team count access
                int teamCount;
                lock (_teamLock)
                {
                    teamCount = _activeTeam.Count;
                }
                if (teamCount > 0)
                {
                    // Partial team spawned - proceed anyway
                    SetState(MedicBuddyState.MovingToPlayer);
                    _movingStartTime = Time.time;
                }
                else
                {
                    SetState(MedicBuddyState.Idle);
                }
            }
        }

        private void UpdateMovingToPlayer()
        {
            // Third Review Fix: Added null check for HealthController
            if (_player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
            {
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Fifth Review Fix (Issue 57): Thread-safe team list access with snapshot
            // Seventh Review Fix (Issue 163): Use cached snapshot to avoid allocation
            List<BotOwner> teamSnapshot = GetTeamSnapshot();

            float elapsed = Time.time - _movingStartTime;
            bool movementTimedOut = elapsed >= MOVEMENT_TIMEOUT;

            // Use rally point (CCP) if set, otherwise track player position
            Vector3 targetPos = RallyPoint;

            // Check if team has reached the rally point / player
            bool allArrived = true;
            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;

                // Bug Fix: Skip bots whose Mover isn't activated yet to prevent GoToPoint NRE
                if (bot.BotState != EBotState.Active)
                {
                    allArrived = false;
                    continue;
                }

                float distance = Vector3.Distance(bot.Position, targetPos);
                if (distance > ARRIVAL_DISTANCE)
                {
                    // If movement timed out, teleport stuck bots to a NavMesh position near the target
                    if (movementTimedOut)
                    {
                        TeleportBotNearPlayer(bot);
                    }
                    else
                    {
                        allArrived = false;
                        // Set bots to sprint to the target
                        bot.SetPose(1f);
                        bot.SetTargetMoveSpeed(1f);
                        bot.GoToPoint(targetPos, true, -1f, false, false, true, false, false);
                    }
                }
            }

            // Enforce minimum approach time so notifications don't stack up instantly
            if ((allArrived || movementTimedOut) && teamSnapshot.Count > 0 && elapsed >= MIN_APPROACH_TIME)
            {
                if (movementTimedOut)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"MedicBuddy movement timed out after {MOVEMENT_TIMEOUT}s - teleported stuck bots");
                }

                // Notify player that team has arrived
                int variant = MedicBuddyNotifier.NotifyHelpArrived(_player.Side);
                MedicBuddyAudio.Play("team_arrived", variant, _player.Side);

                SetState(MedicBuddyState.Defending);
                _defendingStartTime = Time.time;
            }
        }

        /// <summary>
        /// Teleports a stuck bot to a valid NavMesh position near the player.
        /// Used as a last resort when bots can't path to the player (e.g., different floor).
        /// </summary>
        private void TeleportBotNearPlayer(BotOwner bot)
        {
            try
            {
                Vector3 playerPos = _player.Position;
                Vector3 playerForward = _player.Transform.forward;

                // Try positions around the player at close range
                for (float angle = 0f; angle < 360f; angle += 45f)
                {
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * playerForward;
                    Vector3 candidate = playerPos + direction * 3f;

                    if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                    {
                        if (Mathf.Abs(hit.position.y - playerPos.y) <= MAX_SPAWN_HEIGHT_DIFF)
                        {
                            bot.Transform.position = hit.position;
                            BotMindPlugin.Log?.LogInfo(
                                $"[{bot.name}] Teleported to {hit.position} (was stuck)");
                            return;
                        }
                    }
                }

                // Absolute fallback: teleport directly behind the player
                Vector3 behindPlayer = playerPos - playerForward * 2f;
                if (NavMesh.SamplePosition(behindPlayer, out NavMeshHit fallbackHit, 3f, NavMesh.AllAreas))
                {
                    bot.Transform.position = fallbackHit.position;
                    BotMindPlugin.Log?.LogInfo(
                        $"[{bot.name}] Teleported to fallback position {fallbackHit.position}");
                }
                else
                {
                    // No valid NavMesh at all - place at player position directly
                    bot.Transform.position = playerPos;
                    BotMindPlugin.Log?.LogWarning(
                        $"[{bot.name}] No NavMesh near player - placed at player position");
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning(
                    $"[{bot.name}] Teleport failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void UpdateDefending()
        {
            if (!IsPlayerAccessible())
            {
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Wait for perimeter setup before medic starts healing
            if (Time.time - _defendingStartTime < PERIMETER_SETUP_TIME)
            {
                return;
            }

            // Shooters maintain positions - handled by their layers
            // Check if medic has reached the rally point / player to start healing
            if (_medicBot != null && !_medicBot.IsDead)
            {
                Vector3 targetPos = RallyPoint;
                float distToTarget = Vector3.Distance(_medicBot.Position, targetPos);
                if (distToTarget <= ARRIVAL_DISTANCE)
                {
                    StartHealing();
                }
            }
        }

        private void StartHealing()
        {
            SetState(MedicBuddyState.Healing);
            _healingStartTime = Time.time;
            _nextHealTick = 0f;
            _effectsCleared = false;
            _healingApplied = false;
            _healingCompleteTime = 0f;

            // Force player prone immediately when medic arrives
            _playerWasProne = _player.IsInPronePose;
            try { _player.MovementContext.IsInPronePose = true; }
            catch (Exception ex) { BotMindPlugin.Log?.LogWarning($"[MedicBuddy] Could not force prone: {ex.Message}"); }

            // Voice line plays immediately on arrival
            BotMindPlugin.Log?.LogInfo("MedicBuddy starting healing - player forced prone, medic preparing");
            int variant = MedicBuddyNotifier.NotifyHealingStarted(_player.Side);
            MedicBuddyAudio.Play("healing_start", variant, _player.Side);
        }

        private void UpdateHealing()
        {
            // Abort if player died during treatment
            if (_player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
            {
                RestorePlayerStance();
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            float elapsed = Time.time - _healingStartTime;

            // Phase 1: Medic preparation (0 to HEAL_PREP_DELAY seconds)
            // Medic is crouching and looking at the player (handled by HealPatientLogic)
            if (!_healingApplied && elapsed < HEAL_PREP_DELAY)
            {
                return;
            }

            // Phase 2: Apply healing (after prep delay)
            if (!_healingApplied)
            {
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Medic applying treatment");
                _nextHealTick = Time.time;
                _healingApplied = true;
            }

            // Phase 3: Waiting for completion voice line + retreat
            if (_healingCompleteTime > 0f)
            {
                if (Time.time - _healingCompleteTime >= HEAL_COMPLETE_DELAY)
                {
                    RestorePlayerStance();
                    BotMindPlugin.Log?.LogInfo("[MedicBuddy] Treatment complete - retreating");
                    int variant = MedicBuddyNotifier.NotifyHealingComplete(_player.Side);
                    MedicBuddyAudio.Play("healing_complete", variant, _player.Side);
                    SetState(MedicBuddyState.Retreating);
                    _retreatStartTime = Time.time;
                }
                return;
            }

            // Safety: overall duration cap (prep + healing + buffer)
            if (elapsed > HEAL_PREP_DELAY + _healingDuration + HEAL_COMPLETE_DELAY + 5f)
            {
                RestorePlayerStance();
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Healing timed out - retreating");
                int variant = MedicBuddyNotifier.NotifyHealingComplete(_player.Side);
                MedicBuddyAudio.Play("healing_complete", variant, _player.Side);
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Check if player is fully healed - start the completion delay
            if (IsPlayerFullyHealed())
            {
                _healingCompleteTime = Time.time;
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Player fully healed - waiting before retreat");
                return;
            }

            // Apply healing ticks
            if (Time.time >= _nextHealTick)
            {
                _nextHealTick = Time.time + HEAL_TICK_INTERVAL;
                ApplyHealing();
            }
        }

        private void RestorePlayerStance()
        {
            try
            {
                if (_player != null && _player.MovementContext != null)
                    _player.MovementContext.IsInPronePose = _playerWasProne;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning($"[MedicBuddy] Could not restore stance: {ex.Message}");
            }
        }

        private bool IsPlayerFullyHealed()
        {
            if (_player?.HealthController == null) return true;

            // Check all body parts - using cached array to avoid allocation
            foreach (EBodyPart bodyPart in _bodyParts)
            {
                if (bodyPart == EBodyPart.Common) continue;

                try
                {
                    var health = _player.HealthController.GetBodyPartHealth(bodyPart, false);
                    if (health.Current < health.Maximum * 0.95f)
                    {
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    // Third Review Fix: Log why body part was skipped instead of silent catch
                    // Seventh Review Fix (Issue 152): Include stack trace in debug log
                    BotMindPlugin.Log?.LogDebug($"Skipping body part {bodyPart}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if the player has medical items in their inventory that could be used to self-heal.
        /// Scans backpack, tactical vest, and pockets for any MedsItemClass items
        /// (medkits, bandages, splints, stimulators, etc.).
        /// </summary>
        private bool PlayerHasMedicalItems()
        {
            try
            {
                var equipment = _player?.InventoryController?.Inventory?.Equipment;
                if (equipment == null) return false;

                // Check the three main item containers
                if (ContainerHasMeds(equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem))
                    return true;
                if (ContainerHasMeds(equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem))
                    return true;
                if (ContainerHasMeds(equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem))
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Error checking player medical items: {ex.Message}");
                return false; // Allow summon if we can't check
            }
        }

        /// <summary>
        /// Checks if a container has any usable medical items.
        /// Uses GetAllItems() to scan all nested items including inside pouches.
        /// Note: EFT auto-destroys medkits when HP reaches 0, so any MedsItemClass
        /// present in inventory is guaranteed to have remaining uses.
        /// </summary>
        private static bool ContainerHasMeds(CompoundItem container)
        {
            if (container == null) return false;

            try
            {
                foreach (var item in container.GetAllItems())
                {
                    if (item is MedsItemClass)
                        return true;
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Error scanning container for meds: {ex.Message}");
            }

            return false;
        }

        private void ApplyHealing()
        {
            var activeHC = _player?.ActiveHealthController;
            if (activeHC == null)
            {
                BotMindPlugin.Log?.LogDebug("[MedicBuddy] ActiveHealthController is null - cannot heal");
                return;
            }

            // Phase 1: On the first tick, remove all negative effects and restore destroyed limbs.
            // This must happen before HP healing so bleeds/fractures don't drain HP back down.
            if (!_effectsCleared)
            {
                _effectsCleared = true;
                ClearNegativeEffects(activeHC);
                RestoreDestroyedLimbs(activeHC);
            }

            // Phase 2: Heal HP on every tick
            var damageInfo = new DamageInfoStruct
            {
                DamageType = EDamageType.Medicine
            };

            foreach (EBodyPart bodyPart in _bodyParts)
            {
                if (bodyPart == EBodyPart.Common) continue;

                try
                {
                    var health = activeHC.GetBodyPartHealth(bodyPart, false);
                    if (health.Current < health.Maximum)
                    {
                        float healAmount = Mathf.Min(HEAL_AMOUNT_PER_TICK, health.Maximum - health.Current);
                        activeHC.ChangeHealth(bodyPart, healAmount, damageInfo);
                    }
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug($"Could not heal {bodyPart}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Removes all negative status effects (bleeding, fractures, pain, contusion, etc.)
        /// from all body parts. Uses EBodyPart.Common to clear effects globally.
        /// </summary>
        private void ClearNegativeEffects(ActiveHealthController activeHC)
        {
            try
            {
                activeHC.RemoveNegativeEffects(EBodyPart.Common);
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Cleared all negative effects (bleeding, fractures, etc.)");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogWarning($"[MedicBuddy] Failed to remove negative effects: {ex.Message}\n{ex.StackTrace}");

                // Fallback: try per-body-part removal
                foreach (EBodyPart bodyPart in _bodyParts)
                {
                    if (bodyPart == EBodyPart.Common) continue;
                    try
                    {
                        activeHC.RemoveNegativeEffects(bodyPart);
                    }
                    catch (Exception innerEx)
                    {
                        BotMindPlugin.Log?.LogDebug($"[MedicBuddy] Could not clear effects on {bodyPart}: {innerEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Restores destroyed limbs (0 HP body parts) via surgery.
        /// Head and Chest cannot be restored (death occurs when they hit 0).
        /// Restored limbs start at 1 HP and will be healed by subsequent ChangeHealth ticks.
        /// </summary>
        private void RestoreDestroyedLimbs(ActiveHealthController activeHC)
        {
            foreach (EBodyPart bodyPart in _bodyParts)
            {
                // Skip Common, Head, and Chest (Head/Chest destruction is fatal)
                if (bodyPart == EBodyPart.Common || bodyPart == EBodyPart.Head || bodyPart == EBodyPart.Chest)
                    continue;

                try
                {
                    var health = activeHC.GetBodyPartHealth(bodyPart, false);
                    if (health.Current <= 0f)
                    {
                        bool restored = activeHC.RestoreBodyPart(bodyPart, SURGERY_HEALTH_PENALTY);
                        if (restored)
                        {
                            BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Restored destroyed {bodyPart}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug($"[MedicBuddy] Could not restore {bodyPart}: {ex.Message}");
                }
            }
        }

        /// <summary>Fifth Review Fix (Issue 81): Extract magic number to constant.</summary>
        private const float RETREAT_TIMEOUT = 30f;

        private void UpdateRetreating()
        {
            // If the player is dead or inaccessible, skip retreat and despawn immediately
            if (!IsPlayerAccessible())
            {
                SetState(MedicBuddyState.Despawning);
                return;
            }

            // Fifth Review Fix (Issue 57): Thread-safe team list access with snapshot
            // Seventh Review Fix (Issue 163): Use cached snapshot to avoid allocation
            List<BotOwner> teamSnapshot = GetTeamSnapshot();

            // Check if team has retreated far enough
            bool allRetreated = true;
            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;

                // Bug Fix: Skip bots whose Mover isn't activated yet to prevent GoToPoint NRE
                if (bot.BotState != EBotState.Active)
                {
                    allRetreated = false;
                    continue;
                }

                float distance = Vector3.Distance(bot.Position, _player.Position);
                if (distance < RETREAT_DISTANCE)
                {
                    allRetreated = false;

                    Vector3 retreatDir = (bot.Position - _player.Position).normalized;
                    Vector3 retreatPos = bot.Position + retreatDir * 20f;

                    if (NavMesh.SamplePosition(retreatPos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                    {
                        bot.GoToPoint(hit.position, true, -1f, false, false, true, false, false);
                    }
                }
            }

            // Fifth Review Fix (Issue 81): Use named constant instead of magic number
            if (allRetreated || Time.time - _retreatStartTime > RETREAT_TIMEOUT)
            {
                // Notify player that team is exfilling
                int variant = MedicBuddyNotifier.NotifyTeamExfilling(_player.Side);
                MedicBuddyAudio.Play("team_exfil", variant, _player.Side);

                SetState(MedicBuddyState.Despawning);
            }
        }

        private void DespawnTeam()
        {
            BotMindPlugin.Log?.LogInfo("Despawning MedicBuddy team");
            BotLimitManager.OnMedicBuddyDespawned();

            // Use a dedicated local copy instead of GetTeamSnapshot's shared buffer.
            // RemoveFromMap() may trigger callbacks that re-enter UpdateStateMachine,
            // which would call GetTeamSnapshot and overwrite the buffer during iteration.
            List<BotOwner> teamCopy;
            lock (_teamLock)
            {
                teamCopy = new List<BotOwner>(_activeTeam);
                _activeTeam.Clear();
            }

            foreach (var bot in teamCopy)
            {
                if (bot != null && !bot.IsDead)
                {
                    try
                    {
                        // Remove bot from the game
                        bot.LeaveData?.RemoveFromMap();
                    }
                    catch (Exception ex)
                    {
                        // Seventh Review Fix (Issue 154): Include stack trace in debug log
                        BotMindPlugin.Log?.LogDebug($"Error despawning bot: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }

            _medicBot = null;
            _rallyPoint = null;
            SetState(MedicBuddyState.Idle);
        }

        public void SetPlayer(Player player)
        {
            _player = player;
        }

        /// <summary>
        /// Get the position a bot should defend (for shooters).
        /// </summary>
        public Vector3 GetDefensePosition(BotOwner bot)
        {
            // Fifth Review Fix (Issue 57): Thread-safe team check
            bool inTeam;
            lock (_teamLock)
            {
                inTeam = _activeTeam.Contains(bot);
            }
            if (!IsPlayerAccessible() || !inTeam) return bot.Position;

            // Use rally point (CCP) as center if set, otherwise player position
            Vector3 center = RallyPoint;

            if (bot == _medicBot)
            {
                // Medic stays near the rally point / player
                return center;
            }

            // Bug Fix: Calculate shooter index independently of list position.
            // The medic can be at any index in _activeTeam (depends on spawn order),
            // so using (botIndex - 1) gave wrong angles when medic wasn't at index 0.
            int shooterIndex = 0;
            int shooterCount = 0;
            lock (_teamLock)
            {
                foreach (var member in _activeTeam)
                {
                    if (member == _medicBot) continue;
                    if (member == bot) shooterIndex = shooterCount;
                    shooterCount++;
                }
            }

            // Distribute shooters in a circle around rally point / player
            float angle = (360f / Mathf.Max(1, shooterCount)) * shooterIndex;
            float radius = 6f;

            Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
            Vector3 defensePos = center + offset;

            if (NavMesh.SamplePosition(defensePos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                return hit.position;
            }

            return defensePos;
        }

        public void OnDestroy()
        {
            // Fifth Review Fix (Issue 58): Use volatile pattern only, not Interlocked
            // Simple volatile assignment is sufficient for Unity's main-thread destruction
            if (_instance != this)
            {
                // Another instance was set, or already null - just cleanup spawner
                UnsubscribeFromSpawner();
                return;
            }

            _instance = null;

            // Issue 1 Fix: Use centralized unsubscribe method
            UnsubscribeFromSpawner();

            // Fifth Review Fix (Issue 55): Read state with lock for thread safety
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }

            // Clean up any active team
            if (currentState != MedicBuddyState.Idle)
            {
                DespawnTeam();
            }
        }
    }
}
