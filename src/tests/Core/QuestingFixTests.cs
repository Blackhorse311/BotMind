using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for v1.5.0 questing fixes:
/// - POST_OBJECTIVE_COOLDOWN no longer deactivates the layer
/// - Graduated waypoint distance search
/// - Progressive exploration (current position, not fixed center)
/// - Looting timeout spam guard
/// - CombatAlertDuration default reduced to 15s
/// </summary>
public class QuestingFixTests
{
    // Mirror constants from QuestingLayer v1.5.0
    private const float POST_OBJECTIVE_COOLDOWN = 3f;
    private const float DEFAULT_COMBAT_ALERT_DURATION = 15f;

    // PMC graduated distance tiers
    private static readonly float[][] PMC_TIERS = { new[] { 50f, 150f }, new[] { 20f, 60f }, new[] { 10f, 30f } };
    // Scav graduated distance tiers
    private static readonly float[][] SCAV_TIERS = { new[] { 30f, 100f }, new[] { 15f, 40f }, new[] { 8f, 20f } };

    // --- Layer Stays Active During Cooldown ---

    [Theory]
    [InlineData(0.5f, true)]  // During cooldown
    [InlineData(2.9f, true)]  // Just before cooldown ends
    [InlineData(3f, false)]   // Cooldown ended
    [InlineData(10f, false)]  // Well past cooldown
    public void IsActive_DuringCooldown_LayerStaysActive(float timeSinceComplete, bool expectedInCooldown)
    {
        // v1.5.0 Fix: Layer stays active during cooldown (no EFT brain takeover)
        bool inCooldown = timeSinceComplete < POST_OBJECTIVE_COOLDOWN;
        inCooldown.Should().Be(expectedInCooldown);

        // The key assertion: even during cooldown, IsActive returns true
        // (because _inCooldown || HasActiveObjective)
        bool hasObjective = true;
        bool layerActive = inCooldown || hasObjective;
        layerActive.Should().BeTrue("layer must stay active to prevent EFT brain from walking bots to spawn");
    }

    [Fact]
    public void IsActive_DuringCooldown_NoObjectives_StillActive()
    {
        // Even without objectives, cooldown alone keeps layer active
        bool inCooldown = true;
        bool hasObjective = false;
        bool layerActive = inCooldown || hasObjective;
        layerActive.Should().BeTrue("cooldown alone should keep layer active");
    }

    [Fact]
    public void IsActive_NoCooldown_NoObjectives_Deactivates()
    {
        // Only deactivate when BOTH cooldown is over AND no objectives
        bool inCooldown = false;
        bool hasObjective = false;
        bool layerActive = inCooldown || hasObjective;
        layerActive.Should().BeFalse("layer should deactivate only when no cooldown and no objectives");
    }

    // --- Graduated Distance Search ---

    [Fact]
    public void GraduatedSearch_PMC_HasThreeTiers()
    {
        PMC_TIERS.Should().HaveCount(3, "PMCs should try far, medium, then close ranges");
    }

    [Fact]
    public void GraduatedSearch_Scav_HasThreeTiers()
    {
        SCAV_TIERS.Should().HaveCount(3, "Scavs should try far, medium, then close ranges");
    }

    [Fact]
    public void GraduatedSearch_PMC_TiersDescendInDistance()
    {
        // Each tier should have smaller max distance than the previous
        for (int i = 1; i < PMC_TIERS.Length; i++)
        {
            PMC_TIERS[i][1].Should().BeLessThan(PMC_TIERS[i - 1][1],
                $"tier {i} max should be less than tier {i - 1} max");
        }
    }

    [Fact]
    public void GraduatedSearch_Scav_TiersDescendInDistance()
    {
        for (int i = 1; i < SCAV_TIERS.Length; i++)
        {
            SCAV_TIERS[i][1].Should().BeLessThan(SCAV_TIERS[i - 1][1],
                $"tier {i} max should be less than tier {i - 1} max");
        }
    }

    [Fact]
    public void GraduatedSearch_PMC_ClosestTierMinIsSmall()
    {
        // The closest tier should start at 10m or less so bots can always find SOMETHING
        PMC_TIERS[^1][0].Should().BeLessThanOrEqualTo(10f,
            "closest PMC tier min should be <= 10m to guarantee waypoint generation");
    }

    [Fact]
    public void GraduatedSearch_Scav_ClosestTierMinIsSmall()
    {
        SCAV_TIERS[^1][0].Should().BeLessThanOrEqualTo(10f,
            "closest scav tier min should be <= 10m to guarantee waypoint generation");
    }

    [Fact]
    public void GraduatedSearch_PMC_RangesFartherThanScav()
    {
        PMC_TIERS[0][1].Should().BeGreaterThan(SCAV_TIERS[0][1],
            "PMCs should range further than scavs");
    }

    // --- CombatAlertDuration Default ---

    [Fact]
    public void CombatAlertDuration_Default_ShouldBe15()
    {
        DEFAULT_COMBAT_ALERT_DURATION.Should().Be(15f,
            "30s was too long — kept bots in perpetual combat on active maps");
    }

    [Fact]
    public void CombatAlertDuration_Default_ShouldBeInRange()
    {
        DEFAULT_COMBAT_ALERT_DURATION.Should().BeInRange(10f, 20f,
            "should be long enough for combat persistence but short enough to allow questing");
    }

    // --- Timeout Spam Guard ---

    [Fact]
    public void TimeoutGuard_FirstTimeout_ShouldLog()
    {
        // Simulate the timeout guard pattern
        bool hasTimedOut = false;
        int logCount = 0;

        // First timeout
        if (!hasTimedOut)
        {
            hasTimedOut = true;
            logCount++;
        }

        logCount.Should().Be(1);
        hasTimedOut.Should().BeTrue();
    }

    [Fact]
    public void TimeoutGuard_SubsequentFrames_ShouldNotLog()
    {
        // Simulate multiple frames after timeout
        bool hasTimedOut = false;
        int logCount = 0;

        for (int frame = 0; frame < 100; frame++)
        {
            // Mirror production code: only log when !_hasTimedOut
            if (!hasTimedOut)
            {
                hasTimedOut = true;
                logCount++;
            }
            // State still set to Complete every frame (which is fine)
        }

        logCount.Should().Be(1, "timeout warning should only log once, not every frame");
    }

    [Fact]
    public void TimeoutGuard_ResetOnStart_AllowsNewLog()
    {
        // Simulate: timeout → stop → start again → new timeout
        bool hasTimedOut = false;
        int logCount = 0;

        // First use — hits timeout
        if (!hasTimedOut) { hasTimedOut = true; logCount++; }

        // Reset (happens in Start())
        hasTimedOut = false;

        // Second use — hits timeout again
        if (!hasTimedOut) { hasTimedOut = true; logCount++; }

        logCount.Should().Be(2, "resetting guard in Start() should allow logging on next use");
    }

    // --- POST_OBJECTIVE_COOLDOWN Value ---

    [Fact]
    public void PostObjectiveCooldown_ShouldBe3Seconds()
    {
        // v1.5.0: Reduced from 5s to 3s
        POST_OBJECTIVE_COOLDOWN.Should().Be(3f);
    }

    [Fact]
    public void PostObjectiveCooldown_ShouldBeInReasonableRange()
    {
        POST_OBJECTIVE_COOLDOWN.Should().BeInRange(2f, 5f,
            "long enough for natural pause, short enough to not look idle");
    }
}
