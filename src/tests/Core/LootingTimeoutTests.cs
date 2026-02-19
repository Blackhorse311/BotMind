using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for looting timeout and stuck detection constants.
/// Validates that timeout values are sensible and stuck detection thresholds are reasonable.
/// These test the algorithm logic extracted from the looting state machines.
/// </summary>
public class LootingTimeoutTests
{
    // Mirror constants from production code
    private const float CONTAINER_OVERALL_TIMEOUT = 60f;
    private const float CORPSE_OVERALL_TIMEOUT = 60f;
    private const float PICKUP_OVERALL_TIMEOUT = 45f;
    private const float MOVE_OPERATION_TIMEOUT = 30f;
    private const int MAX_NO_PROGRESS = 5;
    private const float PROGRESS_THRESHOLD = 0.3f;
    private const int COLLIDER_BUFFER_SIZE = 256;

    // --- Overall Timeout Tests ---

    [Fact]
    public void OverallTimeout_Container_ShouldBeLongerThanMoveTimeout()
    {
        // The overall timeout must be longer than the individual move operation timeout
        // so bots can at least attempt one full loot cycle before the master timeout fires
        CONTAINER_OVERALL_TIMEOUT.Should().BeGreaterThan(MOVE_OPERATION_TIMEOUT);
    }

    [Fact]
    public void OverallTimeout_Corpse_ShouldBeLongerThanMoveTimeout()
    {
        CORPSE_OVERALL_TIMEOUT.Should().BeGreaterThan(MOVE_OPERATION_TIMEOUT);
    }

    [Fact]
    public void OverallTimeout_Pickup_ShouldBeLongerThanMoveTimeout()
    {
        PICKUP_OVERALL_TIMEOUT.Should().BeGreaterThan(MOVE_OPERATION_TIMEOUT);
    }

    [Fact]
    public void OverallTimeout_ShouldNotExceedTwoMinutes()
    {
        // Bots spending more than 2 minutes on a single loot target is unreasonable
        CONTAINER_OVERALL_TIMEOUT.Should().BeLessThanOrEqualTo(120f);
        CORPSE_OVERALL_TIMEOUT.Should().BeLessThanOrEqualTo(120f);
        PICKUP_OVERALL_TIMEOUT.Should().BeLessThanOrEqualTo(120f);
    }

    [Theory]
    [InlineData(0f, 60f, false)]    // Just started
    [InlineData(30f, 60f, false)]   // Midway
    [InlineData(59.9f, 60f, false)] // Just under timeout
    [InlineData(60.1f, 60f, true)]   // Just over timeout (uses > not >=)
    [InlineData(90f, 60f, true)]    // Well past timeout
    public void ShouldTimeout_GivenElapsedTime(float elapsed, float timeout, bool expected)
    {
        // Mirror the production timeout check: Time.time - _startTime > OVERALL_TIMEOUT
        bool shouldTimeout = elapsed > timeout;
        shouldTimeout.Should().Be(expected);
    }

    // --- Stuck Detection Tests ---

    [Theory]
    [InlineData(10.0f, 9.5f, true)]   // Moved 0.5m closer (> 0.3 threshold) - progress
    [InlineData(10.0f, 9.8f, false)]  // Moved 0.2m closer (< 0.3 threshold) - no progress
    [InlineData(10.0f, 10.0f, false)] // Didn't move at all - no progress
    [InlineData(10.0f, 10.5f, false)] // Moved further away - no progress
    public void StuckDetection_ShouldDetectProgress(float lastDist, float currentDist, bool madeProgress)
    {
        // Mirror production logic: dist < _lastMoveDistance - 0.3f
        bool progress = currentDist < lastDist - PROGRESS_THRESHOLD;
        progress.Should().Be(madeProgress);
    }

    [Fact]
    public void StuckDetection_ShouldAbortAfterMaxNoProgress()
    {
        // Simulate consecutive no-progress checks
        int noProgressCount = 0;
        float lastDistance = 15.0f;
        bool aborted = false;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            // Simulate bot not moving (stuck against a wall)
            float currentDistance = lastDistance - 0.1f; // Only moving 0.1m, under 0.3m threshold

            if (currentDistance < lastDistance - PROGRESS_THRESHOLD)
            {
                noProgressCount = 0;
            }
            else
            {
                noProgressCount++;
                if (noProgressCount >= MAX_NO_PROGRESS)
                {
                    aborted = true;
                    break;
                }
            }
            lastDistance = currentDistance;
        }

        aborted.Should().BeTrue();
        noProgressCount.Should().Be(MAX_NO_PROGRESS);
    }

    [Fact]
    public void StuckDetection_IntermittentProgress_ShouldNotAbort()
    {
        // Simulate bot making progress every few attempts (not stuck)
        int noProgressCount = 0;
        float lastDistance = 20.0f;
        bool aborted = false;

        // Pattern: no progress, no progress, progress, no progress, no progress, progress...
        float[] distances = { 19.9f, 19.8f, 19.0f, 18.9f, 18.8f, 18.0f, 17.9f, 17.8f, 17.0f };

        foreach (float currentDistance in distances)
        {
            if (currentDistance < lastDistance - PROGRESS_THRESHOLD)
            {
                noProgressCount = 0;
            }
            else
            {
                noProgressCount++;
                if (noProgressCount >= MAX_NO_PROGRESS)
                {
                    aborted = true;
                    break;
                }
            }
            lastDistance = currentDistance;
        }

        aborted.Should().BeFalse("Bot was making progress intermittently");
    }

    // --- Collider Buffer Tests ---

    [Fact]
    public void ColliderBuffer_ShouldBeAtLeast128()
    {
        // 64 was too small for mods like "Lots of Loot Redux" that add many containers
        COLLIDER_BUFFER_SIZE.Should().BeGreaterThanOrEqualTo(128);
    }

    [Fact]
    public void ColliderBuffer_ShouldNotExceed1024()
    {
        // Too large wastes memory per bot (each bot has its own buffer)
        COLLIDER_BUFFER_SIZE.Should().BeLessThanOrEqualTo(1024);
    }

    // --- Combat Fallback Tests ---

    [Fact]
    public void CombatCheck_TimeSinceEnemy_BelowThreshold_ShouldBeInCombat()
    {
        // Mirror IsBotInCombat logic
        float timeSinceEnemy = 5f;
        float safeCombatDelay = 10f;

        bool inCombat = timeSinceEnemy < safeCombatDelay;
        inCombat.Should().BeTrue();
    }

    [Fact]
    public void CombatCheck_TimeSinceEnemy_AboveThreshold_ShouldNotBeInCombat()
    {
        float timeSinceEnemy = 15f;
        float safeCombatDelay = 10f;

        bool inCombat = timeSinceEnemy < safeCombatDelay;
        inCombat.Should().BeFalse();
    }

    [Fact]
    public void CombatCheck_NoSAIN_WithGoalEnemy_ShouldBeInCombat()
    {
        // Mirror the non-SAIN fallback: if no SAIN and GoalEnemy != null, should be in combat
        bool sainLoaded = false;
        float timeSinceEnemy = float.MaxValue; // SAIN not loaded returns MaxValue
        bool hasGoalEnemy = true;

        bool inCombat = timeSinceEnemy < 10f;
        if (!inCombat && !sainLoaded)
        {
            inCombat = hasGoalEnemy;
        }

        inCombat.Should().BeTrue("Non-SAIN fallback should detect combat via GoalEnemy");
    }

    [Fact]
    public void CombatCheck_NoSAIN_NoEnemies_ShouldNotBeInCombat()
    {
        bool sainLoaded = false;
        float timeSinceEnemy = float.MaxValue;
        bool hasGoalEnemy = false;
        bool isUnderFire = false;

        bool inCombat = timeSinceEnemy < 10f;
        if (!inCombat && !sainLoaded)
        {
            inCombat = hasGoalEnemy || isUnderFire;
        }

        inCombat.Should().BeFalse("No enemies and no SAIN should not be in combat");
    }

    [Fact]
    public void CombatCheck_NoSAIN_UnderFire_ShouldBeInCombat()
    {
        bool sainLoaded = false;
        float timeSinceEnemy = float.MaxValue;
        bool hasGoalEnemy = false;
        bool isUnderFire = true;

        bool inCombat = timeSinceEnemy < 10f;
        if (!inCombat && !sainLoaded)
        {
            inCombat = hasGoalEnemy || isUnderFire;
        }

        inCombat.Should().BeTrue("Non-SAIN fallback should detect combat via IsUnderFire");
    }
}
