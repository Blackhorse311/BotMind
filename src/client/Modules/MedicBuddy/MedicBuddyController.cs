using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AI;
using Blackhorse311.BotMind.Configuration;

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
        private KeyCode _summonKey = KeyCode.F10;
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

        /// <summary>Interval between healing ticks when treating the player (seconds).</summary>
        private const float HEAL_TICK_INTERVAL = 1f;
        /// <summary>Amount of health restored per body part per tick.</summary>
        private const float HEAL_AMOUNT_PER_TICK = 15f;
        /// <summary>Distance from player at which bots are considered "arrived" (meters).</summary>
        private const float ARRIVAL_DISTANCE = 8f;
        /// <summary>Distance bots must retreat from player before despawning (meters).</summary>
        private const float RETREAT_DISTANCE = 50f;
        /// <summary>Maximum time to wait for bots to spawn before timing out (seconds).</summary>
        private const float SPAWN_TIMEOUT = 30f;

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
            ParseKeybind();
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

                // Check for summon input
                if (UnityEngine.Input.GetKeyDown(_summonKey))
                {
                    TrySummonMedicBuddy();
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

        private void ParseKeybind()
        {
            string keybindStr = BotMindConfig.MedicBuddyKeybind.Value;
            if (Enum.TryParse<KeyCode>(keybindStr, true, out KeyCode parsed))
            {
                _summonKey = parsed;
            }
        }

        private void TrySummonMedicBuddy()
        {
            // Check if on cooldown
            float cooldown = BotMindConfig.MedicBuddyCooldown.Value;
            if (Time.time - _lastSummonTime < cooldown)
            {
                float remaining = cooldown - (Time.time - _lastSummonTime);
                BotMindPlugin.Log?.LogInfo($"MedicBuddy on cooldown: {remaining:F0}s remaining");
                return;
            }

            // Check if PMC-only restriction applies
            if (BotMindConfig.MedicBuddyPMCOnly.Value && _player != null)
            {
                if (_player.Side != EPlayerSide.Bear && _player.Side != EPlayerSide.Usec)
                {
                    BotMindPlugin.Log?.LogInfo("MedicBuddy only available to PMC players");
                    return;
                }
            }

            // Check if player is alive
            if (_player == null || !_player.HealthController.IsAlive)
            {
                BotMindPlugin.Log?.LogInfo("Cannot summon MedicBuddy - player is dead");
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

                    for (int i = 0; i < teamSize; i++)
                    {
                        spawner.SpawnBotByTypeForce(1, spawnType, BotDifficulty.normal, new BotSpawnParams());
                    }

                    BotMindPlugin.Log?.LogInfo($"Requested spawn of {teamSize} MedicBuddy bots (type: {spawnType})");
                }
                catch (Exception ex)
                {
                    // Sixth Review Fix (Issue 100): Include stack trace in error log
                    BotMindPlugin.Log?.LogError($"Failed to spawn MedicBuddy team: {ex.Message}\n{ex.StackTrace}");
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
                    // Find valid NavMesh position near the spawn point (spread bots slightly)
                    int currentTeamCount;
                    lock (_teamLock)
                    {
                        currentTeamCount = _activeTeam.Count;
                    }
                    Vector3 offset = new Vector3(currentTeamCount * 1.5f, 0f, 0f);
                    Vector3 targetPos = _spawnPosition + offset;

                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, 10f, NavMesh.AllAreas))
                    {
                        bot.Transform.position = hit.position;
                    }
                    else
                    {
                        bot.Transform.position = _spawnPosition;
                    }
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

            // Make the bot friendly to the player.
            // PMC bots are hostile to PMC players by default in EFT/SPT.
            // We must add the player to the bot's group ally list so the bot
            // won't target the player, and remove any existing enemy entry.
            MakeBotFriendlyToPlayer(bot);

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

            // Check if all bots spawned
            if (remaining <= 0)
            {
                // Issue 1 Fix: Use centralized unsubscribe method
                UnsubscribeFromSpawner();

                int teamCount;
                lock (_teamLock)
                {
                    teamCount = _activeTeam.Count;
                }
                BotMindPlugin.Log?.LogInfo($"MedicBuddy team complete: {teamCount} bots");
                SetState(MedicBuddyState.MovingToPlayer);
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

        /// <summary>Angles to try when looking for a spawn position, in order of preference.
        /// Behind player first (180), then diagonals, then sides.</summary>
        private static readonly float[] SPAWN_ANGLES = { 180f, 150f, 210f, 120f, 240f, 90f, 270f };
        /// <summary>Distances to try for spawn position, closest first.</summary>
        private static readonly float[] SPAWN_DISTANCES = { 15f, 25f, 35f, 50f };

        private Vector3 CalculateSpawnPosition()
        {
            if (_player == null)
            {
                return Vector3.zero;
            }

            Vector3 playerPos = _player.Position;
            Vector3 playerForward = _player.Transform.forward;

            // Try each angle/distance combination. Prefer behind the player at shorter distances.
            foreach (float distance in SPAWN_DISTANCES)
            {
                foreach (float angle in SPAWN_ANGLES)
                {
                    Vector3 direction = Quaternion.Euler(0f, angle, 0f) * playerForward;
                    Vector3 candidatePos = playerPos + direction * distance + Vector3.up * 0.5f;

                    if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 15f, NavMesh.AllAreas))
                    {
                        _cachedNavPath.ClearCorners();
                        if (NavMesh.CalculatePath(hit.position, playerPos, NavMesh.AllAreas, _cachedNavPath) &&
                            _cachedNavPath.status == NavMeshPathStatus.PathComplete)
                        {
                            BotMindPlugin.Log?.LogDebug(
                                $"MedicBuddy spawn position found: angle={angle}, distance={distance}, pos={hit.position}");
                            return hit.position;
                        }
                    }
                }
            }

            // Last resort: try a very close position (10m) in any direction with partial path allowed
            for (float angle = 0f; angle < 360f; angle += 45f)
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * playerForward;
                Vector3 candidatePos = playerPos + direction * 10f;

                if (NavMesh.SamplePosition(candidatePos, out NavMeshHit hit, 20f, NavMesh.AllAreas))
                {
                    BotMindPlugin.Log?.LogDebug(
                        $"MedicBuddy spawn position found (fallback): angle={angle}, distance=10, pos={hit.position}");
                    return hit.position;
                }
            }

            return Vector3.zero;
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
                _activeTeam.RemoveAll(bot => bot == null || bot.IsDead);
            }

            // Issue 2 Fix: Corrected null check order - check null first with OR
            // Fifth Review Fix (Issue 87): Simplified redundant null check logic
            if (_medicBot != null && _medicBot.IsDead)
            {
                _medicBot = null;
            }

            if (_medicBot == null)
            {
                // Bug Fix: Don't trigger retreat during Spawning state.
                // More bots may still be incoming; their OnBotCreated callbacks would be
                // rejected because state is no longer Spawning, leaving orphaned bots.
                var currentState = CurrentState;
                if (currentState == MedicBuddyState.Spawning)
                {
                    return;
                }

                int teamCount;
                lock (_teamLock)
                {
                    teamCount = _activeTeam.Count;
                }

                if (teamCount > 0)
                {
                    BotMindPlugin.Log?.LogInfo("Medic died - aborting MedicBuddy mission");
                    SetState(MedicBuddyState.Retreating);
                    _retreatStartTime = Time.time;
                }
                else if (currentState != MedicBuddyState.Idle)
                {
                    SetState(MedicBuddyState.Idle);
                }
            }
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

            // Check if team has reached the player
            bool allArrived = true;
            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;

                float distance = Vector3.Distance(bot.Position, _player.Position);
                if (distance > ARRIVAL_DISTANCE)
                {
                    allArrived = false;

                    // Direct bot toward player
                    bot.GoToPoint(_player.Position, true, -1f, false, false, true, false, false);
                }
            }

            if (allArrived && teamSnapshot.Count > 0)
            {
                SetState(MedicBuddyState.Defending);
                StartHealing();
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

            // Shooters maintain positions - handled by their layers
            // Check if we should start healing
            if (_medicBot != null && !_medicBot.IsDead)
            {
                float distToPlayer = Vector3.Distance(_medicBot.Position, _player.Position);
                if (distToPlayer <= ARRIVAL_DISTANCE)
                {
                    StartHealing();
                }
            }
        }

        private void StartHealing()
        {
            SetState(MedicBuddyState.Healing);
            _healingStartTime = Time.time;
            _nextHealTick = Time.time;
            BotMindPlugin.Log?.LogInfo("MedicBuddy starting healing");
        }

        private void UpdateHealing()
        {
            // Third Review Fix: Added null check for HealthController
            if (_player == null || _player.HealthController == null || !_player.HealthController.IsAlive)
            {
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Check if healing duration exceeded
            if (Time.time - _healingStartTime > _healingDuration)
            {
                BotMindPlugin.Log?.LogInfo("MedicBuddy healing complete");
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Check if player is fully healed
            if (IsPlayerFullyHealed())
            {
                BotMindPlugin.Log?.LogInfo("Player fully healed - MedicBuddy retreating");
                SetState(MedicBuddyState.Retreating);
                _retreatStartTime = Time.time;
                return;
            }

            // Apply healing ticks
            if (Time.time >= _nextHealTick)
            {
                _nextHealTick = Time.time + HEAL_TICK_INTERVAL;
                ApplyHealing();
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

        private void ApplyHealing()
        {
            if (_player?.HealthController == null) return;

            // Heal each body part that needs it
            var damageInfo = new DamageInfoStruct
            {
                DamageType = EDamageType.Medicine
            };

            // Using cached array to avoid allocation every tick
            foreach (EBodyPart bodyPart in _bodyParts)
            {
                if (bodyPart == EBodyPart.Common) continue;

                try
                {
                    var health = _player.HealthController.GetBodyPartHealth(bodyPart, false);
                    if (health.Current < health.Maximum)
                    {
                        float healAmount = Mathf.Min(HEAL_AMOUNT_PER_TICK, health.Maximum - health.Current);

                        // Use reflection or direct call if available
                        if (_player.HealthController is ActiveHealthController activeHC)
                        {
                            activeHC.ChangeHealth(bodyPart, healAmount, damageInfo);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Seventh Review Fix (Issue 153): Include stack trace in debug log
                    BotMindPlugin.Log?.LogDebug($"Could not heal {bodyPart}: {ex.Message}\n{ex.StackTrace}");
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
                SetState(MedicBuddyState.Despawning);
            }
        }

        private void DespawnTeam()
        {
            BotMindPlugin.Log?.LogInfo("Despawning MedicBuddy team");

            // Fifth Review Fix (Issue 57): Thread-safe team list access
            // Seventh Review Fix (Issue 163): Use cached snapshot to avoid allocation
            List<BotOwner> teamSnapshot = GetTeamSnapshot();
            lock (_teamLock)
            {
                _activeTeam.Clear();
            }

            foreach (var bot in teamSnapshot)
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

            if (bot == _medicBot)
            {
                // Medic stays near player
                return _player.Position;
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

            // Distribute shooters in a circle around player
            float angle = (360f / Mathf.Max(1, shooterCount)) * shooterIndex;
            float radius = 6f;

            Vector3 offset = Quaternion.Euler(0, angle, 0) * Vector3.forward * radius;
            Vector3 defensePos = _player.Position + offset;

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
