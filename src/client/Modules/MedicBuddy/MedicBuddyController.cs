using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        /// <summary>Active cooldown duration — set dynamically based on spawn outcome.</summary>
        private float _activeCooldown;
        // Fifth Review Fix (Issue 57): Add lock for thread-safe team list access
        private readonly object _teamLock = new object();
        private readonly List<BotOwner> _activeTeam = new List<BotOwner>();
        // Review 10 Fix: Added volatile for thread-safe visibility of _medicBot reads
        private volatile BotOwner _medicBot;
        /// <summary>Shared BotsGroup for the MedicBuddy team, created by CreateMedicBuddyGroup callback.</summary>
        private BotsGroup _medicBuddyGroup;
        private MedicBuddyState _state = MedicBuddyState.Idle;
        // Third Review Fix: Lock object for state machine thread safety
        private readonly object _stateLock = new object();
        private Player _player;
        private float _healingStartTime;
        private const float HEALING_DURATION = 15f;
        private float _nextHealTick;
        private float _retreatStartTime;
        private volatile int _pendingSpawns;
        // Issue 1 Fix: Track event subscription state to prevent memory leaks
        private volatile bool _isSubscribedToSpawner;

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
        /// <summary>Short cooldown for failed spawns or early team wipes (seconds).</summary>
        private const float SHORT_COOLDOWN = 15f;
        /// <summary>Time threshold: team killed within this period gets short cooldown (seconds).</summary>
        private const float EARLY_WIPE_THRESHOLD = 30f;
        /// <summary>Minimum time bots spend walking to the player before arrival can trigger (seconds).</summary>
        private const float MIN_APPROACH_TIME = 10f;
        /// <summary>Time bots spend setting up a defensive perimeter before healing begins (seconds).</summary>
        private const float PERIMETER_SETUP_TIME = 5f;
        /// <summary>Max time bots can spend trying to reach the player before being teleported (seconds).</summary>
        private const float MOVEMENT_TIMEOUT = 45f;
        /// <summary>Max distance a bot can drift from the rally point before being teleported back (meters).
        /// Prevents bots from following long NavMesh detours that take them far from the team.</summary>
        private const float MAX_LEASH_DISTANCE = 50f;
        /// <summary>Max vertical distance between spawn candidate and player (prevents wrong-floor spawns).</summary>
        private const float MAX_SPAWN_HEIGHT_DIFF = 3f;
        /// <summary>Radius of the circular spread pattern for bot spawn positions (meters).</summary>
        private const float BOT_SPREAD_RADIUS = 3f;
        /// <summary>Maximum search radius for NavMesh.SamplePosition when placing bots.</summary>
        private const float SPAWN_NAVMESH_SAMPLE_RADIUS = 5f;
        /// <summary>Timeout for the ActivateBot async call before aborting (seconds).</summary>
        private const float ACTIVATE_BOT_TIMEOUT = 30f;
        /// <summary>Duration of spawn invulnerability to survive until friendship registers (seconds).</summary>
        private const float SPAWN_INVULNERABILITY_DURATION = 10f;

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

        /// <summary>
        /// Next time to re-apply friendship for all team bots. SSIF continuously marks
        /// scav-type bots as enemies every frame, so we must continuously fight back.
        /// Runs every FRIENDSHIP_REFRESH_INTERVAL seconds while the team is active.
        /// </summary>
        private float _nextFriendshipRefreshTime;
        private const float FRIENDSHIP_REFRESH_INTERVAL = 0.5f;
        private float _defendingStartTime;
        /// <summary>Next time to run team health/hostility checks (throttled to 1/sec).</summary>
        private float _nextTeamCheckTime;
        /// <summary>When the team fully entered the game (spawn complete → MovingToPlayer).</summary>
        private float _teamDeployedTime;
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

        /// <summary>
        /// Client-side DTO matching the server's GenerateEscortRequest record.
        /// Used only for JSON serialization to the /botmind/generate-escort endpoint.
        /// </summary>
        private class EscortRequest
        {
            public int Count { get; set; }
            public string Side { get; set; }
            public string Difficulty { get; set; }
            public int PlayerLevel { get; set; }
            public string Location { get; set; }
            public string GameVersion { get; set; }
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

                // Check for summon input — manual modifier check so key order doesn't matter.
                // BepInEx KeyboardShortcut.IsDown() requires modifiers pressed before the main key,
                // which is unintuitive. This checks all keys are held simultaneously.
                if (IsSummonKeybindPressed())
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

        /// <summary>
        /// Check if the summon keybind is pressed, regardless of modifier key order.
        /// BepInEx KeyboardShortcut.IsDown() requires modifiers BEFORE the main key,
        /// which fails if the player presses F10 before Ctrl+Alt.
        /// </summary>
        private bool IsSummonKeybindPressed()
        {
            var shortcut = BotMindConfig.MedicBuddyKeybind.Value;
            KeyCode mainKey = shortcut.MainKey;
            if (mainKey == KeyCode.None) return false;

            // Main key must be pressed THIS frame (not held from previous)
            if (!Input.GetKeyDown(mainKey)) return false;

            // All modifiers must be held (any order)
            foreach (var modifier in shortcut.Modifiers)
            {
                if (!Input.GetKey(modifier)) return false;
            }
            return true;
        }

        private void TrySummonMedicBuddy()
        {
            bool verbose = BotMindConfig.VerboseLogging.Value;
            if (verbose)
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] TrySummon: state={CurrentState}, " +
                    $"cooldown={_activeCooldown}s, timeSince={(Time.time - _lastSummonTime):F1}s");

            // If currently spawning, tell player to wait (don't show misleading cooldown timer)
            if (CurrentState == MedicBuddyState.Spawning)
            {
                if (verbose) BotMindPlugin.Log?.LogInfo("[MedicBuddy] Rejected: already spawning");
                MedicBuddyNotifier.WarnSpawning();
                return;
            }

            // Check if on cooldown — _activeCooldown is set dynamically based on outcome
            float cooldown = _activeCooldown;
            if (Time.time - _lastSummonTime < cooldown)
            {
                float remaining = cooldown - (Time.time - _lastSummonTime);
                if (verbose) BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Rejected: cooldown {remaining:F0}s remaining");
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

            // Check if player's inventory can actually treat their current injuries.
            // Food, water, painkillers, and wrong-type items don't count.
            if (PlayerCanSelfHeal())
            {
                BotMindPlugin.Log?.LogInfo("Cannot summon MedicBuddy - player has matching medical supplies for all injuries");
                MedicBuddyNotifier.WarnHasMedicalSupplies();
                return;
            }

            // Check if team is already active - use thread-safe transition
            if (!TryTransitionState(MedicBuddyState.Idle, MedicBuddyState.Spawning))
            {
                BotMindPlugin.Log?.LogInfo("MedicBuddy team is already active");
                return;
            }

            // Begin spawning — cooldown starts now, duration set based on outcome
            SpawnMedicTeam();
            _lastSummonTime = Time.time;
            _activeCooldown = BotMindConfig.MedicBuddyCooldown.Value; // Full cooldown by default
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
                BotMindPlugin.Log?.LogWarning($"[MedicBuddy] CCP rejected: state={currentState}, pending={_pendingSpawns}");
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

        /// <summary>
        /// Requests server-generated PMC profiles for the escort team.
        /// Makes a synchronous HTTP POST to /botmind/generate-escort.
        /// Synchronous is fine: the server runs on localhost so round-trip is sub-50ms,
        /// and profile generation for 4 bots takes ~200ms.
        /// Returns null if the request fails (caller falls back to SpawnBotByTypeForce).
        /// </summary>
        private Profile[] RequestEscortProfiles(int count, EPlayerSide playerSide, BotDifficulty difficulty)
        {
            try
            {
                string side = playerSide == EPlayerSide.Bear ? "bear" : "usec";
                string diff = difficulty.ToString().ToLowerInvariant();
                int playerLevel = _player?.Profile?.Info?.Level ?? 40;
                string location = Singleton<GameWorld>.Instance?.LocationId ?? "";
                var request = new EscortRequest
                {
                    Count = count,
                    Side = side,
                    Difficulty = diff,
                    PlayerLevel = playerLevel,
                    Location = location,
                    GameVersion = ""
                };

                string requestJson = JsonConvert.SerializeObject(request);
                string responseJson = RequestHandler.PostJson("/botmind/generate-escort", requestJson);

                if (string.IsNullOrEmpty(responseJson))
                {
                    BotMindPlugin.Log?.LogWarning("[MedicBuddy] Empty response from escort endpoint");
                    return null;
                }

                // Log first 500 chars of response for debugging deserialization issues
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Server response (first 500): {responseJson.Substring(0, Math.Min(500, responseJson.Length))}");

                // Use EFT's JsonParserClass which has all the converters for Profile's nested
                // types (InfoClass, ProfileInfoClass, etc.). Newtonsoft.Json.JsonConvert fails
                // because Profile's constructors expect EFT-specific intermediate types.
                var profiles = JsonParserClass.ParseJsonTo<Profile[]>(responseJson);
                if (profiles == null || profiles.Length == 0)
                {
                    BotMindPlugin.Log?.LogWarning("[MedicBuddy] No profiles returned from escort endpoint");
                    return null;
                }

                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Received {profiles.Length} PMC profiles from server");
                return profiles;
            }
            catch (Exception ex)
            {
                // Log the full exception chain — TargetInvocationException wraps the real cause
                var inner = ex.InnerException;
                string details = inner != null
                    ? $"{ex.Message} -> {inner.GetType().Name}: {inner.Message}\n{inner.StackTrace}"
                    : $"{ex.Message}\n{ex.StackTrace}";
                BotMindPlugin.Log?.LogWarning($"[MedicBuddy] Failed to request escort profiles: {details}");
                return null;
            }
        }

        /// <summary>
        /// Fire-and-forget: async void is intentional because TrySummonMedicBuddy is called
        /// from Update() which cannot await. Errors are caught by the outer try-catch.
        /// Cooldown is set optimistically by the caller; on failure, UpdateSpawning applies SHORT_COOLDOWN.
        /// </summary>
        private void SpawnMedicTeam()
        {
          try
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
            _medicBuddyGroup = null;

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

            var spawner = botGame.BotsController?.BotSpawner;
            if (spawner == null)
            {
                BotMindPlugin.Log?.LogWarning("BotSpawner not available");
                SetState(MedicBuddyState.Idle);
                return;
            }

            BotDifficulty escortDifficulty = (BotDifficulty)BotMindConfig.MedicBuddyEscortDifficulty.Value;

            // Try server-generated PMC profiles first, fall back to legacy assault spawn.
            Profile[] profiles = RequestEscortProfiles(teamSize, _playerSide, escortDifficulty);

            if (profiles != null && profiles.Length > 0)
            {
                SpawnBotsFromProfiles(profiles, spawner, escortDifficulty);
            }
            else
            {
                FallbackToLegacySpawn(spawner, teamSize, escortDifficulty);
            }
          }
          catch (Exception ex)
          {
            // Outer safety net: catches anything thrown before the inner try blocks.
            BotMindPlugin.Log?.LogError($"[MedicBuddy] SpawnMedicTeam unhandled error: {ex.Message}\n{ex.StackTrace}");
            BotLimitManager.EndMedicBuddySpawn();
            UnsubscribeFromSpawner();
            SetState(MedicBuddyState.Idle);
          }
        }

        /// <summary>
        /// Spawns escort bots using server-generated PMC profiles via IBotCreator.ActivateBot.
        /// Each profile is wrapped in a BotCreationDataClass and activated individually.
        /// The existing OnBotCreated event handler captures each bot (method_11 fires the event).
        /// </summary>
        private void SpawnBotsFromProfiles(Profile[] profiles, BotSpawner spawner, BotDifficulty difficulty)
        {
            try
            {
                // Subscribe to OnBotCreated — method_11 fires this event after ActivateBot,
                // so our existing handler captures and configures each bot.
                SnapshotExistingBots(spawner);
                spawner.OnBotCreated += OnBotCreated;
                _isSubscribedToSpawner = true;

                BotLimitManager.BeginMedicBuddySpawn();

                // Find the closest BotZone and CorePoint for spawn registration
                BotZone zone = FindClosestZone(spawner, _spawnPosition);
                if (zone == null)
                {
                    zone = spawner.AllBotZones?.FirstOrDefault();
                }

                if (zone == null)
                {
                    BotMindPlugin.Log?.LogWarning("[MedicBuddy] No BotZone available for ActivateBot");
                    FallbackToLegacySpawn(spawner, profiles.Length, difficulty);
                    return;
                }

                var botCreator = spawner.BotCreator;
                var token = spawner.CancellationTokenSource.Token;
                bool verbose = BotMindConfig.VerboseLogging.Value;

                // Get CorePointId for spawn position registration (ABPS pattern)
                int corePointId = 0;
                try
                {
                    corePointId = Singleton<IBotGame>.Instance.BotsController
                        .CoversData.GetClosest(_spawnPosition).CorePointInGame.Id;
                }
                catch
                {
                    BotMindPlugin.Log?.LogDebug("[MedicBuddy] Could not get CorePointId, using 0");
                }

                foreach (var profile in profiles)
                {
                    if (token.IsCancellationRequested) break;

                    try
                    {
                        // CreateWithoutProfile skips server profile generation (which hangs for PMC types).
                        // We inject our pre-fetched server profile instead.
                        var profileData = new BotProfileDataClass(
                            EPlayerSide.Savage,  // ABPS pattern: always Savage for BotProfileDataClass
                            WildSpawnType.pmcUSEC,
                            difficulty,
                            0f,
                            null);

                        var creationData = BotCreationDataClass.CreateWithoutProfile(profileData);
                        creationData.AddProfile(profile);
                        creationData.AddPosition(_spawnPosition, corePointId);

                        spawner.InSpawnProcess++;

                        // The ActivateBot callback wraps method_11 which registers the bot with BotSpawner
                        // (adds to Bots, increments AllBotsCount, fires OnBotCreated, sets die callback).
                        var localData = creationData;
                        botCreator.ActivateBot(
                            creationData,
                            zone,
                            false,
                            new Func<BotOwner, BotZone, BotsGroup>(CreateMedicBuddyGroup),
                            new Action<BotOwner>(bot =>
                                spawner.method_11(bot, localData, null, false, new Stopwatch())),
                            token);

                        if (verbose)
                            BotMindPlugin.Log?.LogInfo($"[MedicBuddy] ActivateBot called for profile: {profile.Nickname}");
                    }
                    catch (Exception ex)
                    {
                        BotMindPlugin.Log?.LogWarning($"[MedicBuddy] ActivateBot failed for {profile.Nickname}: {ex.Message}");
                    }
                }

                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Requested spawn of {profiles.Length} PMC escort bots via ActivateBot");

                int variant = MedicBuddyNotifier.NotifyHelpRequested(_player.Side);
                MedicBuddyAudio.Play("summon_request", variant, _player.Side);
            }
            catch (OperationCanceledException)
            {
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] PMC spawn cancelled (raid ending)");
                BotLimitManager.EndMedicBuddySpawn();
                UnsubscribeFromSpawner();
                SetState(MedicBuddyState.Idle);
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[MedicBuddy] SpawnBotsFromProfiles failed: {ex.Message}\n{ex.StackTrace}");
                MedicBuddyNotifier.WarnSpawnFailed();
                BotLimitManager.EndMedicBuddySpawn();
                UnsubscribeFromSpawner();
                SetState(MedicBuddyState.Idle);
            }
        }

        /// <summary>
        /// Fallback spawn path using SpawnBotByTypeForce when server profiles are unavailable.
        /// Creates assault-type bots (scav appearance) instead of real PMCs.
        /// The OnBotCreated event handler captures and configures the bots.
        /// </summary>
        private void FallbackToLegacySpawn(BotSpawner spawner, int teamSize, BotDifficulty difficulty)
        {
            try
            {
                // Use the player's faction PMC type so escorts spawn as PMCs, not scavs.
                // This relies on the game's profile pool having pmcUSEC/pmcBEAR profiles
                // available (ABPS pre-loads these at raid start). Falls back to assault
                // if PMC spawn hangs or fails.
                var (spawnType, _) = GetSpawnTypeForSide(_playerSide);
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] SpawnBotByTypeForce: count={teamSize}, type={spawnType}, diff={difficulty}");

                // Only subscribe if not already subscribed (SpawnBotsFromProfiles may have already done it)
                if (!_isSubscribedToSpawner)
                {
                    SnapshotExistingBots(spawner);
                    spawner.OnBotCreated += OnBotCreated;
                    _isSubscribedToSpawner = true;

                    BotLimitManager.BeginMedicBuddySpawn();
                }

                _ = spawner.SpawnBotByTypeForce(teamSize, spawnType, difficulty, null);

                BotMindPlugin.Log?.LogInfo($"Requested spawn of {teamSize} MedicBuddy bots (type: {spawnType})");

                int variant = MedicBuddyNotifier.NotifyHelpRequested(_player.Side);
                MedicBuddyAudio.Play("summon_request", variant, _player.Side);
            }
            catch (OperationCanceledException)
            {
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Fallback spawn cancelled (raid ending)");
                BotLimitManager.EndMedicBuddySpawn();
                UnsubscribeFromSpawner();
                SetState(MedicBuddyState.Idle);
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[MedicBuddy] Fallback spawn failed: {ex.Message}\n{ex.StackTrace}");
                MedicBuddyNotifier.WarnSpawnFailed();
                BotLimitManager.EndMedicBuddySpawn();
                UnsubscribeFromSpawner();
                SetState(MedicBuddyState.Idle);
            }
        }

        /// <summary>
        /// Group callback for ActivateBot. Creates a shared BotsGroup for all MedicBuddy bots.
        /// First bot creates the group; subsequent bots join it.
        /// </summary>
        private BotsGroup CreateMedicBuddyGroup(BotOwner bot, BotZone zone)
        {
            try
            {
                // Thread-safe check-and-set: ActivateBot may invoke this callback
                // concurrently for multiple bots in the group.
                lock (_teamLock)
                {
                    if (_medicBuddyGroup != null)
                        return _medicBuddyGroup;

                    var botSpawner = Singleton<IBotGame>.Instance?.BotsController?.BotSpawner;
                    if (botSpawner == null)
                        throw new InvalidOperationException("BotSpawner unavailable during group creation");

                    var botsGroup = new BotsGroup(
                        zone, botSpawner.BotGame, bot, new List<BotOwner>(),
                        botSpawner.DeadBodiesController, botSpawner.AllPlayers, true);

                    botSpawner.Groups.Add(zone, bot.Profile.Info.Side, botsGroup, true);
                    _medicBuddyGroup = botsGroup;

                    BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Created BotsGroup for team (side={bot.Profile.Info.Side})");
                    return botsGroup;
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogError($"[MedicBuddy] CreateMedicBuddyGroup error: {ex.Message}\n{ex.StackTrace}");
                throw; // Let ActivateBot propagate to SpawnMedicTeam's catch for cleanup
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
            bool verbose = BotMindConfig.VerboseLogging.Value;
            if (verbose)
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] OnBotCreated fired: {bot?.name ?? "null"}, " +
                    $"role={bot?.Profile?.Info?.Settings?.Role}, id={bot?.GetInstanceID()}");

            // Fifth Review Fix (Issue 59): Use Interlocked for thread-safe pending spawns decrement
            // Sixth Review Fix (Issue 106): Read state with lock to prevent race condition
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }
            if (currentState != MedicBuddyState.Spawning || _pendingSpawns <= 0)
            {
                if (verbose)
                    BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Skipped {bot?.name}: " +
                        $"state={currentState}, pending={_pendingSpawns}");
                return;
            }

            // Bug Fix: Reject bots that existed before our spawn request.
            // Without this, regular PMC waves spawning concurrently consume our _pendingSpawns
            // slots, leaving actual team bots uncaptured.
            if (_preExistingBotIds.Contains(bot.GetInstanceID()))
            {
                if (verbose)
                    BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Skipped {bot?.name}: pre-existing bot (id={bot.GetInstanceID()})");
                return;
            }

            // Role filter removed: SpawnBotByTypeForce creates different types depending on
            // the map (assault on most maps, ruafMachinegunner on Ground Zero, etc.).
            // The pre-existing bot snapshot is the primary defense against capturing wrong bots.
            // Log the role for debugging but accept any bot that passed the snapshot check.
            var role = bot.Profile?.Info?.Settings?.Role;
            if (verbose)
                BotMindPlugin.Log?.LogInfo($"[MedicBuddy] Capturing {bot?.name}: role={role}");

            // Spawn invulnerability: set damage coefficient to 0 IMMEDIATELY so hostile AI
            // can't kill the bot before friendship is registered. SAIN-enhanced bots on open
            // maps (Woods) can detect and kill scav-type bots within milliseconds of spawn.
            // Restored after SPAWN_INVULNERABILITY_DURATION seconds via RestoreVulnerability().
            ApplySpawnInvulnerability(bot);

            // Fallback teleport: AddPosition in SpawnMedicTeam sets the spawn location,
            // but if the bot appeared elsewhere (zone override, spawn system quirk),
            // snap it to our calculated position as a safety net.
            if (_spawnPosition != Vector3.zero)
            {
                try
                {
                    int currentTeamCount;
                    lock (_teamLock)
                    {
                        currentTeamCount = _activeTeam.Count;
                    }
                    int totalTeamSize = BotMindConfig.MedicBuddyTeamSize.Value;
                    float spreadAngle = currentTeamCount * (360f / totalTeamSize);
                    Vector3 spreadDir = Quaternion.Euler(0f, spreadAngle, 0f) * Vector3.forward;
                    Vector3 targetPos = _spawnPosition + spreadDir * BOT_SPREAD_RADIUS;

                    if (NavMesh.SamplePosition(targetPos, out NavMeshHit hit, SPAWN_NAVMESH_SAMPLE_RADIUS, NavMesh.AllAreas) &&
                        Mathf.Abs(hit.position.y - _spawnPosition.y) <= MAX_SPAWN_HEIGHT_DIFF)
                    {
                        bot.Transform.position = hit.position;
                    }
                    else
                    {
                        bot.Transform.position = _spawnPosition;
                    }
                    BotMindPlugin.Log?.LogInfo($"[{bot.name}] Positioned at {bot.Transform.position}");
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug($"[{bot.name}] Could not position bot: {ex.Message}");
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

            // Trigger an immediate friendship refresh on the next Update frame.
            // SSIF will override our friendship, but the continuous refresh loop
            // will fight back every 0.5s from now on.
            _nextFriendshipRefreshTime = Time.time + 0.1f;

            int remaining = Interlocked.Decrement(ref _pendingSpawns);

            // Medic assignment: OnBotCreated runs on the main thread, so a simple null
            // check is sufficient — no concurrent access to _medicBot here.
            if (_medicBot == null)
            {
                _medicBot = bot;
                BotMindPlugin.Log?.LogInfo($"[{bot.name}] Assigned as MedicBuddy medic (pending={remaining})");
            }
            else
            {
                BotMindPlugin.Log?.LogInfo($"[{bot.name}] Assigned as MedicBuddy shooter (pending={remaining})");
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
                _teamDeployedTime = Time.time;

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
        /// Makes a bot invulnerable by setting its damage coefficient to 0.
        /// This prevents hostile AI from killing team bots in the milliseconds between
        /// spawn and friendship registration. Vulnerability is restored after a delay
        /// by scheduling RestoreVulnerability via Invoke.
        /// </summary>
        private void ApplySpawnInvulnerability(BotOwner bot)
        {
            try
            {
                var player = bot.GetPlayer;
                player?.ActiveHealthController?.SetDamageCoeff(0f);
                BotMindPlugin.Log?.LogInfo($"[{bot.name}] Spawn invulnerability applied (DamageCoeff=0)");

                // Schedule vulnerability restore. Use MonoBehaviour.Invoke for simplicity —
                // we store the bot reference in a closure via StartCoroutine alternative.
                // Since we can't pass args to Invoke, use a tracking dictionary.
                _invulnerableBots[bot.GetInstanceID()] = (bot, Time.time + SPAWN_INVULNERABILITY_DURATION);
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"[{bot.name}] Could not apply spawn invulnerability: {ex.Message}");
            }
        }

        /// <summary>Bots with active spawn invulnerability: (bot, expireTime).</summary>
        private readonly List<(BotOwner bot, float expireTime)> _invulnerableBots
            = new List<(BotOwner, float)>(8);

        /// <summary>
        /// Called from Update to restore vulnerability for bots whose protection has expired.
        /// Uses the bot's original DamageCoeff from its settings file.
        /// </summary>
        private void UpdateSpawnInvulnerability()
        {
            if (_invulnerableBots.Count == 0) return;

            float now = Time.time;
            for (int i = _invulnerableBots.Count - 1; i >= 0; i--)
            {
                var (bot, expireTime) = _invulnerableBots[i];
                if (now < expireTime) continue;

                _invulnerableBots.RemoveAt(i);
                try
                {
                    if (bot == null || bot.IsDead) continue;
                    // Restore the bot's normal damage coefficient from its settings
                    float normalCoeff = bot.Settings?.FileSettings?.Core?.DamageCoeff ?? 1f;
                    bot.GetPlayer?.ActiveHealthController?.SetDamageCoeff(normalCoeff);
                    BotMindPlugin.Log?.LogInfo($"[{bot.name}] Spawn invulnerability expired (DamageCoeff={normalCoeff})");
                }
                catch (Exception ex)
                {
                    BotMindPlugin.Log?.LogDebug($"Could not restore vulnerability: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Re-applies friendship for all team bots. Called on a short delay after
        /// OnBotCreated to override SameSideIsFriendly (SSIF) which marks scav-type
        /// bots as enemies after our initial friendship call.
        /// </summary>
        private void RefreshTeamFriendship()
        {
            List<BotOwner> teamSnapshot = GetTeamSnapshot();
            int refreshed = 0;
            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;
                MakeBotFriendlyToPlayer(bot);
                refreshed++;
            }

            // Also refresh inter-team friendship
            foreach (var bot in teamSnapshot)
            {
                if (bot == null || bot.IsDead) continue;
                MakeTeamBotsFriendly(bot);
            }

            BotMindPlugin.Log?.LogDebug($"[MedicBuddy] Refreshed friendship for {refreshed} team bots (post-SSIF)");
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
            foreach (var slotType in EQUIPMENT_GRID_SLOTS)
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
        /// <summary>Close-range distances for Pass 3 (very close behind player).</summary>
        private static readonly float[] SPAWN_DISTANCES_CLOSE = { 10f, 8f };
        /// <summary>Emergency distances for Pass 4 (any direction, ignoring FOV).</summary>
        private static readonly float[] SPAWN_DISTANCES_EMERGENCY = { 10f, 8f, 5f };
        /// <summary>Equipment grid slots to check when adding items to a bot's inventory.</summary>
        private static readonly EquipmentSlot[] EQUIPMENT_GRID_SLOTS = { EquipmentSlot.TacticalVest, EquipmentSlot.Backpack, EquipmentSlot.Pockets };
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

        /// <summary>
        /// Single source of truth for mapping player faction to bot spawn type.
        /// Used by both SpawnMedicTeam (to create profiles) and OnBotCreated (to filter).
        /// </summary>
        private static (WildSpawnType spawnType, EPlayerSide spawnSide) GetSpawnTypeForSide(EPlayerSide playerSide)
        {
            switch (playerSide)
            {
                case EPlayerSide.Usec: return (WildSpawnType.pmcUSEC, EPlayerSide.Usec);
                case EPlayerSide.Bear: return (WildSpawnType.pmcBEAR, EPlayerSide.Bear);
                default: return (WildSpawnType.assault, EPlayerSide.Savage);
            }
        }

        /// <summary>
        /// Returns the BotZone closest to <paramref name="position"/>, or null if none found.
        /// </summary>
        private static BotZone FindClosestZone(BotSpawner spawner, Vector3 position)
        {
            BotZone closest = null;
            float closestDist = float.MaxValue;
            var allZones = spawner?.AllBotZones;
            if (allZones == null) return null;

            foreach (var z in allZones)
            {
                if (z == null) continue;
                float dist = (z.transform.position - position).sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = z;
                }
            }
            return closest;
        }

        /// <summary>
        /// Gets zone names sorted by distance to the given position.
        /// Returns up to <paramref name="count"/> unique zones so each bot spawns
        /// in a different zone, avoiding ABPS's per-zone scav cap.
        /// The bot is teleported to _spawnPosition in OnBotCreated anyway,
        /// so the zone only needs to be valid, not optimal.
        /// </summary>
        private static List<string> GetZoneNamesSortedByDistance(BotSpawner spawner, Vector3 position, int count)
        {
            var result = new List<string>();
            try
            {
                var zones = spawner?.AllBotZones;
                if (zones == null)
                {
                    result.Add("");
                    return result;
                }

                // Build list of (name, distance) pairs
                var zoneDistances = new List<(string name, float dist)>();
                foreach (var zone in zones)
                {
                    if (zone == null || string.IsNullOrEmpty(zone.NameZone)) continue;
                    float dist = (zone.transform.position - position).sqrMagnitude;
                    zoneDistances.Add((zone.NameZone, dist));
                }

                // Sort by distance (closest first)
                zoneDistances.Sort((a, b) => a.dist.CompareTo(b.dist));

                // Take up to count unique zones
                for (int i = 0; i < zoneDistances.Count && result.Count < count; i++)
                {
                    result.Add(zoneDistances[i].name);
                }
            }
            catch
            {
                // Fallback
            }

            if (result.Count == 0)
                result.Add("");

            return result;
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
            foreach (float distance in SPAWN_DISTANCES_CLOSE)
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
                foreach (float distance in SPAWN_DISTANCES_EMERGENCY)
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
            // Review 10 Fix: Throttle team checks to 1/sec (was every frame with lambda allocations)
            if (Time.time >= _nextTeamCheckTime)
            {
                _nextTeamCheckTime = Time.time + 1f;
                CleanupDeadBots();
                CheckTeamHostility();
            }

            // Fifth Review Fix (Issue 55): Read state with lock to prevent race condition
            MedicBuddyState currentState;
            lock (_stateLock)
            {
                currentState = _state;
            }

            // Check and restore vulnerability for bots whose invulnerability expired
            UpdateSpawnInvulnerability();

            // Continuously re-apply friendship while team is active.
            // Scav-type bots need this because SSIF and EFT default AI mark them hostile.
            // PMC-type bots generally don't need it but it's harmless as a safety net.
            if (currentState != MedicBuddyState.Idle &&
                Time.time >= _nextFriendshipRefreshTime)
            {
                int teamCount;
                lock (_teamLock) { teamCount = _activeTeam.Count; }
                if (teamCount > 0)
                {
                    _nextFriendshipRefreshTime = Time.time + FRIENDSHIP_REFRESH_INTERVAL;
                    RefreshTeamFriendship();
                }
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

                // No bots left to promote - clean up with tiered cooldown
                if (currentState != MedicBuddyState.Idle)
                {
                    float timeSinceDeployed = Time.time - _teamDeployedTime;
                    if (timeSinceDeployed < EARLY_WIPE_THRESHOLD)
                    {
                        // Team wiped within 30s — short cooldown, they barely had a chance
                        _activeCooldown = SHORT_COOLDOWN;
                        _lastSummonTime = Time.time;
                        BotMindPlugin.Log?.LogInfo($"MedicBuddy team wiped early ({timeSinceDeployed:F0}s) — short cooldown");
                        MedicBuddyNotifier.WarnTeamWipedEarly();
                    }
                    else
                    {
                        // Team survived 30s+ — full cooldown applies
                        BotMindPlugin.Log?.LogInfo($"MedicBuddy team wiped after {timeSinceDeployed:F0}s — full cooldown");
                        MedicBuddyNotifier.WarnTeamWiped();
                    }

                    RestorePlayerStance();
                    BotLimitManager.OnMedicBuddyDespawned();
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
                        // Re-apply friendship instead of aborting. Scav-type bots get
                        // continuously marked hostile by SSIF and EFT default AI.
                        // The continuous refresh loop handles steady-state, but this
                        // catches hostility between refresh intervals.
                        MakeBotFriendlyToPlayer(bot);
                        if (BotMindConfig.VerboseLogging.Value)
                            BotMindPlugin.Log?.LogInfo(
                                $"[MedicBuddy] Fixed hostility for [{bot.name}]");
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
                    _teamDeployedTime = Time.time;
                }
                else
                {
                    // No bots spawned — apply short cooldown so player can retry quickly
                    _activeCooldown = SHORT_COOLDOWN;
                    _lastSummonTime = Time.time;
                    MedicBuddyNotifier.WarnSpawnFailed();
                    BotMindPlugin.Log?.LogWarning("MedicBuddy spawn failed — no bots entered the game. Short cooldown applied.");
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

                // Leash check: if a bot drifted too far (bad NavMesh path, stuck on geometry),
                // teleport them back immediately instead of waiting for MOVEMENT_TIMEOUT.
                // This prevents one lost bot from blocking the entire team for 45 seconds.
                if (distance > MAX_LEASH_DISTANCE)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[MedicBuddy] {bot.name} exceeded leash distance ({distance:F0}m > {MAX_LEASH_DISTANCE}m) - teleporting");
                    TeleportBotNearPlayer(bot);
                    continue;
                }

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

            // Leash check during defending: teleport back any bot that drifted too far
            // (e.g., chasing an enemy or following a bad NavMesh path)
            Vector3 rallyPos = RallyPoint;
            List<BotOwner> defendSnapshot = GetTeamSnapshot();
            foreach (var bot in defendSnapshot)
            {
                if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) continue;
                float dist = Vector3.Distance(bot.Position, rallyPos);
                if (dist > MAX_LEASH_DISTANCE)
                {
                    BotMindPlugin.Log?.LogWarning(
                        $"[MedicBuddy] {bot.name} exceeded leash during defend ({dist:F0}m) - teleporting");
                    TeleportBotNearPlayer(bot);
                }
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

            // v1.8.0: Trigger native bot-to-bot healing for stimulator animation.
            // This gives visual feedback (medic applies stimulator) while our ApplyHealing()
            // handles the actual incremental healing. If native system also calls RestoreFullHealth(),
            // our system will find nothing left to heal and complete early — no conflict.
            TryNativeHealAsk();

            // Voice line plays immediately on arrival
            BotMindPlugin.Log?.LogInfo("MedicBuddy starting healing - player forced prone, medic preparing");
            int variant = MedicBuddyNotifier.NotifyHealingStarted(_player.Side);
            MedicBuddyAudio.Play("healing_start", variant, _player.Side);
        }

        /// <summary>
        /// v1.8.0: Trigger EFT's native bot-to-bot healing system for stimulator animation.
        /// Falls back silently if the medic bot has no stimulators or the system isn't available.
        /// </summary>
        private void TryNativeHealAsk()
        {
            try
            {
                var healTarget = _medicBot?.HealAnotherTarget;
                if (healTarget == null)
                {
                    BotMindPlugin.Log?.LogDebug("[MedicBuddy] Native HealAnotherTarget not available — using custom healing only");
                    return;
                }

                healTarget.HealAsk(_player);
                BotMindPlugin.Log?.LogInfo("[MedicBuddy] Native HealAsk triggered for stimulator animation");
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"[MedicBuddy] Native HealAsk failed (expected if no meds): {ex.Message}");
            }
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
            if (elapsed > HEAL_PREP_DELAY + HEALING_DURATION + HEAL_COMPLETE_DELAY + 5f)
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
        /// Smart medical check: determines if the player's inventory contains items
        /// that can actually treat their current injuries. Food, water, stimulators,
        /// and painkillers do NOT count. A splint won't help a heavy bleed. Only blocks
        /// MedicBuddy if the player has a matching treatment for EVERY active condition.
        /// </summary>
        private bool PlayerCanSelfHeal()
        {
            try
            {
                var activeHC = _player?.ActiveHealthController;
                if (activeHC == null) return false; // can't check, allow summon

                // --- Phase 1: Detect player's current injuries ---
                bool hasHPDamage = false;
                bool hasLightBleed = false;
                bool hasHeavyBleed = false;
                bool hasFracture = false;
                bool hasDestroyedLimb = false;

                foreach (EBodyPart bodyPart in _bodyParts)
                {
                    if (bodyPart == EBodyPart.Common) continue;
                    try
                    {
                        var health = activeHC.GetBodyPartHealth(bodyPart, false);
                        if (health.Current <= 0f)
                            hasDestroyedLimb = true;
                        else if (health.Current < health.Maximum * 0.95f)
                            hasHPDamage = true;
                    }
                    catch { /* skip inaccessible parts */ }
                }

                // Check for active bleeding and fracture effects
                try
                {
                    var allEffects = activeHC.GetAllActiveEffects(EBodyPart.Common);
                    if (allEffects != null)
                    {
                        foreach (var effect in allEffects)
                        {
                            if (effect == null) continue;
                            string typeName = effect.Type?.Name;
                            if (typeName == null) continue;

                            if (typeName == "LightBleeding") hasLightBleed = true;
                            else if (typeName == "HeavyBleeding") hasHeavyBleed = true;
                            else if (typeName == "Fracture") hasFracture = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // GetAllActiveEffects may not accept Common — try per-part
                    BotMindPlugin.Log?.LogDebug($"GetAllActiveEffects(Common) failed, trying per-part: {ex.Message}");
                    foreach (EBodyPart bodyPart in _bodyParts)
                    {
                        if (bodyPart == EBodyPart.Common) continue;
                        try
                        {
                            var effects = activeHC.GetAllActiveEffects(bodyPart);
                            if (effects == null) continue;
                            foreach (var effect in effects)
                            {
                                if (effect == null) continue;
                                string typeName = effect.Type?.Name;
                                if (typeName == null) continue;

                                if (typeName == "LightBleeding") hasLightBleed = true;
                                else if (typeName == "HeavyBleeding") hasHeavyBleed = true;
                                else if (typeName == "Fracture") hasFracture = true;
                            }
                        }
                        catch { /* skip */ }
                    }
                }

                bool hasAnyInjury = hasHPDamage || hasLightBleed || hasHeavyBleed || hasFracture || hasDestroyedLimb;
                if (!hasAnyInjury) return true; // no injuries at all

                // --- Phase 2: Scan inventory for treatment capability ---
                bool canHealHP = false;
                bool canStopLightBleed = false;
                bool canStopHeavyBleed = false;
                bool canFixFracture = false;
                bool canRestoreLimb = false;

                var equipment = _player?.InventoryController?.Inventory?.Equipment;
                if (equipment == null) return false; // can't check, allow summon

                ScanContainerForTreatments(equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem as CompoundItem,
                    ref canHealHP, ref canStopLightBleed, ref canStopHeavyBleed, ref canFixFracture, ref canRestoreLimb);
                ScanContainerForTreatments(equipment.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem as CompoundItem,
                    ref canHealHP, ref canStopLightBleed, ref canStopHeavyBleed, ref canFixFracture, ref canRestoreLimb);
                ScanContainerForTreatments(equipment.GetSlot(EquipmentSlot.Pockets)?.ContainedItem as CompoundItem,
                    ref canHealHP, ref canStopLightBleed, ref canStopHeavyBleed, ref canFixFracture, ref canRestoreLimb);
                // Also check the secure container (Gamma, Epsilon, etc.)
                ScanContainerForTreatments(equipment.GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem as CompoundItem,
                    ref canHealHP, ref canStopLightBleed, ref canStopHeavyBleed, ref canFixFracture, ref canRestoreLimb);

                // --- Phase 3: Allow MedicBuddy if ANY injury lacks treatment ---
                if (hasHPDamage && !canHealHP) return false;
                if (hasLightBleed && !canStopLightBleed) return false;
                if (hasHeavyBleed && !canStopHeavyBleed) return false;
                if (hasFracture && !canFixFracture) return false;
                if (hasDestroyedLimb && !canRestoreLimb) return false;

                // All injuries have matching treatments — player can self-heal
                if (BotMindConfig.VerboseLogging.Value)
                {
                    BotMindPlugin.Log?.LogInfo("[MedicBuddy] Player can self-heal: " +
                        $"hp={hasHPDamage}→{canHealHP}, lightBleed={hasLightBleed}→{canStopLightBleed}, " +
                        $"heavyBleed={hasHeavyBleed}→{canStopHeavyBleed}, fracture={hasFracture}→{canFixFracture}, " +
                        $"destroyed={hasDestroyedLimb}→{canRestoreLimb}");
                }
                return true;
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Error checking player medical capability: {ex.Message}");
                return false; // Allow summon if we can't determine
            }
        }

        // Well-known Tarkov template IDs for medical item categorization.
        // These IDs are stable across versions. Grouped by treatment capability.

        /// <summary>Items that stop heavy bleeding (tourniquets, hemostatics, heavy medkits).</summary>
        private static readonly HashSet<string> HEAVY_BLEED_ITEMS = new HashSet<string>
        {
            "5e8488fa988a8b0f1d60e59f", // CALOK-B hemostatic
            "5e831507ea0a7c419c2f9bd6", // Esmarch tourniquet
            "60098b1705871a0e2c55a60c", // CAT tourniquet
            "5af0548586f7743a532b7e58", // Hemostat
        };

        /// <summary>Items that stop light bleeding (bandages, all medkits that treat bleeds).</summary>
        private static readonly HashSet<string> LIGHT_BLEED_ITEMS = new HashSet<string>
        {
            "544fb25a4bdc2dfb738b4567", // Army Bandage
            "5751a25924597722c463c472", // Aseptic Bandage
        };

        /// <summary>Items that fix fractures (splints).</summary>
        private static readonly HashSet<string> FRACTURE_ITEMS = new HashSet<string>
        {
            "544fb3364bdc2d34748b456a", // Aluminum Splint
            "5af0454c86f7746bf20992e8", // Immobilizing Splint
        };

        /// <summary>Surgical kits that restore destroyed limbs.</summary>
        private static readonly HashSet<string> SURGICAL_ITEMS = new HashSet<string>
        {
            "5d02778e86f774203e7dedbe", // CMS (Compact Medical Surgery Kit)
            "5d02797c86f774203f38e30a", // Surv12 Field Surgical Kit
        };

        /// <summary>Medkits with HP resource that also treat various effects.</summary>
        private static readonly HashSet<string> MEDKIT_ITEMS = new HashSet<string>
        {
            "590c678286f77426c9660122", // IFAK - HP + light bleed + heavy bleed + fracture
            "590c661e86f7741b993b9d17", // Car First Aid Kit - HP + light bleed
            "5d1b376e86f774252519444e", // AFAK - HP + light bleed + heavy bleed
            "590c657e86f77412b013051d", // Grizzly - HP + all effects + surgery
            "5755356824597772cb798962", // AI-2 - HP only (no effect treatment)
        };

        /// <summary>
        /// Scans a container for items that can treat specific conditions.
        /// Categorizes by: medkit (HP), bleed treatment, fracture treatment, surgical.
        /// Skips food, drink, and stimulators entirely.
        /// </summary>
        private static void ScanContainerForTreatments(
            CompoundItem container,
            ref bool canHealHP,
            ref bool canStopLightBleed,
            ref bool canStopHeavyBleed,
            ref bool canFixFracture,
            ref bool canRestoreLimb)
        {
            if (container == null) return;

            try
            {
                foreach (var item in container.GetAllItems())
                {
                    // Skip food, drink, and stimulators — they don't treat injuries
                    if (item is FoodDrinkItemClass) continue;
                    if (item is StimulatorItemClass) continue;
                    if (!(item is MedsItemClass)) continue;

                    string templateId = item.TemplateId;
                    if (string.IsNullOrEmpty(templateId)) continue;

                    // Medkits heal HP and some also treat effects
                    if (MEDKIT_ITEMS.Contains(templateId))
                    {
                        canHealHP = true;
                        // IFAK treats light+heavy bleed and fracture
                        if (templateId == "590c678286f77426c9660122") { canStopLightBleed = true; canStopHeavyBleed = true; canFixFracture = true; }
                        // Car medkit treats light bleed only
                        else if (templateId == "590c661e86f7741b993b9d17") { canStopLightBleed = true; }
                        // AFAK treats light+heavy bleed
                        else if (templateId == "5d1b376e86f774252519444e") { canStopLightBleed = true; canStopHeavyBleed = true; }
                        // Grizzly treats everything including surgery
                        else if (templateId == "590c657e86f77412b013051d") { canStopLightBleed = true; canStopHeavyBleed = true; canFixFracture = true; canRestoreLimb = true; }
                        // AI-2 only heals HP, no effect treatment
                        continue;
                    }

                    // Specific treatment items
                    if (HEAVY_BLEED_ITEMS.Contains(templateId)) { canStopHeavyBleed = true; continue; }
                    if (LIGHT_BLEED_ITEMS.Contains(templateId)) { canStopLightBleed = true; continue; }
                    if (FRACTURE_ITEMS.Contains(templateId)) { canFixFracture = true; continue; }
                    if (SURGICAL_ITEMS.Contains(templateId)) { canRestoreLimb = true; canFixFracture = true; continue; }

                    // Unknown MedsItemClass (mod items, etc.) — don't count as treatment
                    // This is conservative: if we don't recognize it, we don't block MedicBuddy
                }
            }
            catch (Exception ex)
            {
                BotMindPlugin.Log?.LogDebug($"Error scanning container for treatments: {ex.Message}");
            }
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
            _medicBuddyGroup = null;
            _rallyPoint = null;
            _nextFriendshipRefreshTime = 0f;
            _invulnerableBots.Clear();
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

            // Review 10 Fix: Restore player stance before cleanup to prevent prone lock on destroy
            RestorePlayerStance();

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
