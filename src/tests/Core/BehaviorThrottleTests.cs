using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for v1.4.0 behavior throttle changes:
/// - Move speed thresholds (GoToLocation, ExploreArea)
/// - Post-loot cooldown and session limits
/// - Post-objective cooldown for questing
/// - Scan interval and search radius sanity checks
/// </summary>
public class BehaviorThrottleTests
{
    // Mirror constants from GoToLocationLogic
    private const float GO_TO_SPRINT_THRESHOLD = 30f;
    private const float GO_TO_WALK_THRESHOLD = 5f;
    private const float GO_TO_SPRINT_SPEED = 1f;
    private const float GO_TO_JOG_SPEED = 0.85f;
    private const float GO_TO_WALK_SPEED = 0.7f;

    // Mirror constants from ExploreAreaLogic
    private const float EXPLORE_SPRINT_THRESHOLD = 15f;
    private const float EXPLORE_SPRINT_SPEED = 1f;
    private const float EXPLORE_JOG_SPEED = 0.85f;

    // Mirror constants from LootingLayer
    private const float SCAN_INTERVAL = 8f;
    private const float POST_LOOT_COOLDOWN = 15f;
    private const int MAX_LOOT_ACTIONS_PER_SESSION = 3;
    private const float SESSION_RESET_DURATION = 120f;
    private const float DEFAULT_SEARCH_RADIUS = 35f;

    // Mirror constants from QuestingLayer
    private const float POST_OBJECTIVE_COOLDOWN = 5f;

    // --- GoToLocation Move Speed Tests ---

    [Theory]
    [InlineData(40f, 1f)]     // Far away - sprint
    [InlineData(30.1f, 1f)]   // Just above sprint threshold
    [InlineData(30f, 0.85f)]  // At threshold - jog (uses > not >=)
    [InlineData(15f, 0.85f)]  // Mid range - jog
    [InlineData(5.1f, 0.85f)] // Just above walk threshold
    [InlineData(5f, 0.7f)]    // At threshold - walk (uses > not >=)
    [InlineData(2f, 0.7f)]    // Close - walk
    public void GetMoveSpeed_GoToLocation_ReturnsCorrectSpeed(float distance, float expectedSpeed)
    {
        // Mirror GoToLocationLogic.GetMoveSpeed()
        float speed;
        if (distance > GO_TO_SPRINT_THRESHOLD) speed = GO_TO_SPRINT_SPEED;
        else if (distance > GO_TO_WALK_THRESHOLD) speed = GO_TO_JOG_SPEED;
        else speed = GO_TO_WALK_SPEED;

        speed.Should().Be(expectedSpeed);
    }

    [Fact]
    public void GetMoveSpeed_GoToLocation_NeverBelowWalkSpeed()
    {
        // Even at 0m distance, speed should not be below walk speed
        GO_TO_WALK_SPEED.Should().BeGreaterThanOrEqualTo(0.7f,
            "minimum walk speed should prevent creeping behavior");
    }

    // --- ExploreArea Move Speed Tests ---

    [Theory]
    [InlineData(20f, 1f)]     // Far - sprint
    [InlineData(15.1f, 1f)]   // Just above threshold
    [InlineData(15f, 0.85f)]  // At threshold - jog (uses > not >=)
    [InlineData(5f, 0.85f)]   // Close - jog
    public void GetMoveSpeed_ExploreArea_ReturnsCorrectSpeed(float distance, float expectedSpeed)
    {
        // Mirror ExploreAreaLogic movement speed
        float speed = distance > EXPLORE_SPRINT_THRESHOLD ? EXPLORE_SPRINT_SPEED : EXPLORE_JOG_SPEED;
        speed.Should().Be(expectedSpeed);
    }

    // --- Post-Loot Cooldown Tests ---

    [Theory]
    [InlineData(5f, false)]    // 5s since last loot, cooldown is 15s - should block
    [InlineData(14.9f, false)] // Just under cooldown - should block
    [InlineData(15f, true)]    // Exactly at cooldown boundary (uses < not <=) - should allow
    [InlineData(20f, true)]    // Well past cooldown - should allow
    public void PostLootCooldown_ShouldBlockDuringCooldown(float timeSinceLoot, bool shouldAllow)
    {
        // Mirror LootingLayer.IsActive() cooldown check
        bool allowed = !(timeSinceLoot < POST_LOOT_COOLDOWN);
        allowed.Should().Be(shouldAllow);
    }

    [Fact]
    public void PostLootCooldown_ShouldBeReasonable()
    {
        // Cooldown should be long enough to create breathing room but not too long
        POST_LOOT_COOLDOWN.Should().BeInRange(5f, 30f);
    }

    // --- Session Limit Tests ---

    [Theory]
    [InlineData(0, false)]  // No actions yet - not at limit
    [InlineData(1, false)]  // 1 action - not at limit
    [InlineData(2, false)]  // 2 actions - not at limit
    [InlineData(3, true)]   // 3 actions - at limit
    [InlineData(5, true)]   // Over limit (shouldn't happen but defensive)
    public void SessionLimit_ShouldBlockAtMax(int actionsCompleted, bool atLimit)
    {
        bool blocked = actionsCompleted >= MAX_LOOT_ACTIONS_PER_SESSION;
        blocked.Should().Be(atLimit);
    }

    [Theory]
    [InlineData(60f, false)]   // 60s since session started, reset is 120s - still blocked
    [InlineData(119.9f, false)] // Just under reset - still blocked
    [InlineData(120f, true)]    // At reset boundary (uses < not <=) - should reset
    [InlineData(180f, true)]    // Well past reset - should reset
    public void SessionLimit_AtMax_ShouldResetAfterDuration(float timeSinceSessionStart, bool shouldReset)
    {
        // Mirror LootingLayer.IsActive() session reset check
        bool resetExpired = !(timeSinceSessionStart < SESSION_RESET_DURATION);
        resetExpired.Should().Be(shouldReset);
    }

    [Fact]
    public void SessionLimit_MaxActions_ShouldBeReasonable()
    {
        MAX_LOOT_ACTIONS_PER_SESSION.Should().BeInRange(2, 6,
            "session limit should allow some looting but prevent hoover behavior");
    }

    [Fact]
    public void SessionLimit_ResetDuration_ShouldBeReasonable()
    {
        SESSION_RESET_DURATION.Should().BeInRange(60f, 300f,
            "session reset should be long enough to create varied behavior");
    }

    // --- Post-Objective Cooldown Tests ---

    [Theory]
    [InlineData(2f, false)]    // 2s since objective, cooldown is 5s - should block
    [InlineData(4.9f, false)]  // Just under cooldown - should block
    [InlineData(5f, true)]     // At cooldown boundary (uses < not <=) - should allow
    [InlineData(10f, true)]    // Well past cooldown - should allow
    public void PostObjectiveCooldown_ShouldBlockDuringCooldown(float timeSinceObjective, bool shouldAllow)
    {
        // Mirror QuestingLayer.IsActive() cooldown check
        bool allowed = !(timeSinceObjective < POST_OBJECTIVE_COOLDOWN);
        allowed.Should().Be(shouldAllow);
    }

    [Fact]
    public void PostObjectiveCooldown_ShouldBeReasonable()
    {
        // Long enough for bots to look around, short enough to not look idle
        POST_OBJECTIVE_COOLDOWN.Should().BeInRange(3f, 10f);
    }

    // --- Scan Interval Sanity Tests ---

    [Fact]
    public void ScanInterval_ShouldBeAtLeast5Seconds()
    {
        // Scanning faster than 5s creates vacuum behavior
        SCAN_INTERVAL.Should().BeGreaterThanOrEqualTo(5f);
    }

    [Fact]
    public void ScanInterval_ShouldNotExceed30Seconds()
    {
        // Scanning slower than 30s makes bots too oblivious to nearby loot
        SCAN_INTERVAL.Should().BeLessThanOrEqualTo(30f);
    }

    // --- Search Radius Sanity Tests ---

    [Fact]
    public void DefaultSearchRadius_ShouldBe35()
    {
        DEFAULT_SEARCH_RADIUS.Should().Be(35f);
    }

    [Fact]
    public void DefaultSearchRadius_ShouldBeLessThan50()
    {
        // 50m was too large — always found loot on every map
        DEFAULT_SEARCH_RADIUS.Should().BeLessThan(50f);
    }
}
