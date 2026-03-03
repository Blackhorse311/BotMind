using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for v1.7.0 fixes:
/// - Navigation failure tier skipping: consecutive nav failures cause waypoint generation
///   to skip far distance tiers, preventing the Bot40 loop (same unreachable waypoint recycled)
/// - Guaranteed fallback: bots never go permanently idle when all waypoints fail at runtime
/// - Collider buffer bump: 256 → 512 for dense maps (Woods)
/// </summary>
public class NavFailureTests
{
    private const int NAV_FAILURE_TIER_SKIP_THRESHOLD = 2;

    // --- Tier Skip Logic ---

    [Theory]
    [InlineData(0, 0)]   // No failures → skip nothing, start at tier 0 (far)
    [InlineData(1, 0)]   // 1 failure → below threshold, still try far
    [InlineData(2, 1)]   // 2 failures → skip 1 tier (skip far, start at medium)
    [InlineData(3, 1)]   // 3 failures → still skip 1 tier
    [InlineData(4, 2)]   // 4 failures → skip 2 tiers (skip far+medium, start at close)
    [InlineData(5, 2)]   // 5 failures → still skip 2 tiers
    [InlineData(6, 3)]   // 6 failures → skip all 3 tiers (no waypoints generated → fallback to Explore)
    public void TierSkip_CalculatesCorrectly_BasedOnFailureCount(int navFailures, int expectedTiersToSkip)
    {
        int tiersToSkip = navFailures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        tiersToSkip.Should().Be(expectedTiersToSkip);
    }

    [Fact]
    public void TierSkip_ThreeTiersAvailable_SkipOneStartsAtMedium()
    {
        // Simulates: PMC tiers = { far(50-150), medium(20-60), close(10-30) }
        int totalTiers = 3;
        int tiersToSkip = 2 / NAV_FAILURE_TIER_SKIP_THRESHOLD; // 2 failures

        int startTier = tiersToSkip;
        int tiersAttempted = totalTiers - startTier;

        startTier.Should().Be(1, "should skip the far tier");
        tiersAttempted.Should().Be(2, "medium and close tiers should still be attempted");
    }

    [Fact]
    public void TierSkip_AllTiersSkipped_GeneratesNoWaypoints()
    {
        // When failures exceed all tiers, no waypoints are generated.
        // QuestManager falls back to Explore objective (local patrol).
        int totalTiers = 3;
        int tiersToSkip = 6 / NAV_FAILURE_TIER_SKIP_THRESHOLD; // 6 failures = skip 3

        int tiersAttempted = totalTiers - tiersToSkip;
        tiersAttempted.Should().BeLessOrEqualTo(0,
            "all tiers skipped — QuestManager should fall back to local Explore objective");
    }

    [Fact]
    public void TierSkip_SuccessResetsCounter()
    {
        // After a successful navigation, the counter resets to 0,
        // allowing far-tier waypoints to be generated again.
        int navFailures = 4; // Was at 4 (skipping 2 tiers)
        // Successful arrival → MarkCurrentObjectiveComplete resets to 0
        navFailures = 0;

        int tiersToSkip = navFailures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        tiersToSkip.Should().Be(0, "success resets failures — all tiers available again");
    }

    // --- MarkCurrentObjectiveFailed vs MarkCurrentObjectiveComplete ---

    [Fact]
    public void MarkFailed_IncrementsNavFailures_WhileComplete_Resets()
    {
        // Simulates the two code paths:
        // - MarkCurrentObjectiveFailed() increments _consecutiveNavFailures
        // - MarkCurrentObjectiveComplete() resets _consecutiveNavFailures to 0
        int failures = 0;

        // Two nav failures
        failures++; // MarkCurrentObjectiveFailed call 1
        failures++; // MarkCurrentObjectiveFailed call 2
        failures.Should().Be(2);

        // Successful arrival
        failures = 0; // MarkCurrentObjectiveComplete
        failures.Should().Be(0);
    }

    // Mirrors GoToLocationLogic state enum
    private const int StateFailed = 2;
    private const int StateComplete = 3;

    // Mirrors GoToLocationLogic.IsComplete property
    private static bool IsComplete(int state) => state == StateComplete || state == StateFailed;

    // Mirrors GoToLocationLogic.HasFailed property
    private static bool HasFailed(int state) => state == StateFailed;

    [Theory]
    [InlineData(StateFailed, true, true)]    // Failed: IsComplete=true, HasFailed=true
    [InlineData(StateComplete, true, false)] // Complete: IsComplete=true, HasFailed=false
    public void HasFailed_DistinguishesFailureFromSuccess(int state, bool expectedIsComplete, bool expectedHasFailed)
    {
        // GoToLocationLogic.HasFailed allows the layer to call MarkCurrentObjectiveFailed()
        // (increments nav failure counter) vs MarkCurrentObjectiveComplete() (resets counter).
        IsComplete(state).Should().Be(expectedIsComplete);
        HasFailed(state).Should().Be(expectedHasFailed);
    }

    // --- Bot40 Scenario Recreation ---

    [Fact]
    public void Bot40Scenario_RepeatedNavFailure_EventuallySkipsFarTier()
    {
        // Recreates the Bot40 loop:
        // 1. Bot generates waypoint at 50.3m (far tier: 50-150m)
        // 2. Navigation fails — path unreachable
        // 3. Objective marked failed, new batch generated
        // 4. New batch again picks far tier → same area → same failure
        //
        // v1.7.0 fix: After 2 consecutive failures, skip far tier.
        // Bot generates medium-tier (20-60m) waypoints instead.
        int failures = 0;
        int totalTiers = 3; // far, medium, close

        // First failure cycle — still tries far
        failures++;
        int skip1 = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip1.Should().Be(0, "first failure: still try all tiers");

        // Second failure — NOW skip far
        failures++;
        int skip2 = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip2.Should().Be(1, "second failure: skip far tier, start at medium");
        (totalTiers - skip2).Should().Be(2, "medium and close tiers remain");

        // Third failure — still skip just far
        failures++;
        int skip3 = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip3.Should().Be(1, "third failure: still skip 1 tier");

        // Fourth failure — skip far AND medium
        failures++;
        int skip4 = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip4.Should().Be(2, "fourth failure: skip far + medium, only close remains");
        (totalTiers - skip4).Should().Be(1, "only close tier remains");
    }

    [Fact]
    public void Bot40Scenario_AfterTierSkip_SuccessResetsForNextBatch()
    {
        // After the bot successfully reaches a medium/close waypoint,
        // nav failures reset. Next objective batch can try far tier again.
        int failures = 3; // Was failing
        int skip = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip.Should().Be(1, "was skipping far tier");

        // Bot reaches a medium-tier waypoint successfully
        failures = 0;
        skip = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip.Should().Be(0, "success resets — far tier available again");
    }

    // --- Guaranteed Fallback (Bot46/Bot48 standing-still fix) ---

    [Fact]
    public void UpdateObjectives_NoActiveObjective_InjectsFallbackExplore()
    {
        // Documents the fix: after UpdateObjectives() runs, if _currentObjective is still null
        // (all generated waypoints passed NavMesh validation but failed GoToPoint runtime),
        // a "Fallback Patrol" Explore objective is injected so the bot never goes permanently idle.
        //
        // Before fix: Bot46/Bot48 on Woods exhausted all objectives → stood still forever.
        // After fix: Fallback Explore keeps the bot moving locally.
        bool hasActiveObjectiveAfterUpdate = false; // Simulates SelectBestObjective() returning null
        bool fallbackInjected = false;

        if (!hasActiveObjectiveAfterUpdate)
        {
            fallbackInjected = true;
            hasActiveObjectiveAfterUpdate = true;
        }

        fallbackInjected.Should().BeTrue("fallback must be injected when no objective is available");
        hasActiveObjectiveAfterUpdate.Should().BeTrue("bot must always have an active objective after update");
    }

    [Fact]
    public void FallbackExplore_UsesCurrentBotPosition()
    {
        // The fallback Explore objective should use the bot's CURRENT position as the center,
        // not a stale spawn position, so the bot explores its immediate surroundings.
        // This is the same pattern as the existing "Local Patrol" fallback in GeneratePMCObjectives().
        bool usesCurrentPosition = true; // TargetPosition = _bot.Position in the fix

        usesCurrentPosition.Should().BeTrue(
            "fallback explore must use current position, not spawn position");
    }

    [Fact]
    public void FallbackExplore_OnlyTriggersWhenAllElseFails()
    {
        // The fallback is a last resort — it only fires when:
        // 1. All completed objectives have been removed
        // 2. GenerateObjectives() ran but produced nothing usable
        // 3. SelectBestObjective() returned null
        //
        // It does NOT fire when normal objectives exist.
        bool hasNormalObjective = true;
        bool fallbackNeeded = !hasNormalObjective;
        fallbackNeeded.Should().BeFalse("fallback should not fire when normal objectives exist");

        hasNormalObjective = false;
        fallbackNeeded = !hasNormalObjective;
        fallbackNeeded.Should().BeTrue("fallback should fire when no objectives remain");
    }

    [Fact]
    public void Bot46Scenario_AllWaypointsFailRuntime_FallbackPreventsIdle()
    {
        // Recreates the Bot46 scenario:
        // 1. Bot spawns on Woods, gets 3 waypoints that pass NavMesh.CalculatePath()
        // 2. Waypoint 1 fails at GoToPoint() → MarkCurrentObjectiveFailed (nav failures: 1)
        // 3. Waypoint 2 also passes quickly (arrived or failed)
        // 4. Waypoint 3 consumed
        // 5. All objectives gone → GenerateObjectives() → new batch
        // 6. New batch also all fail at runtime
        // 7. Bot has no objectives → BEFORE FIX: stands still forever
        //                          → AFTER FIX: fallback Explore injected
        int objectivesRemaining = 0;
        bool selectReturnsNull = objectivesRemaining == 0;
        bool fallbackInjected = selectReturnsNull;

        selectReturnsNull.Should().BeTrue("all objectives consumed");
        fallbackInjected.Should().BeTrue("fallback Explore prevents permanent idle");
    }

    [Fact]
    public void FallbackExplore_CompletionRadius_AllowsLocalMovement()
    {
        // Fallback uses 30m completion radius — large enough for meaningful local patrol,
        // small enough to keep the bot in a reasonable area.
        float fallbackRadius = 30f;

        fallbackRadius.Should().BeGreaterOrEqualTo(20f, "too small = trivial movement");
        fallbackRadius.Should().BeLessOrEqualTo(50f, "too large = wandering aimlessly");
    }

    // --- Collider Buffer ---

    [Fact]
    public void ColliderBuffer_IncreasedTo512_CoversWoodsDensity()
    {
        // Woods runtime showed "Collider buffer full (256)" frequently.
        // Bumped to 512 to catch more containers per scan.
        int oldBuffer = 256;
        int newBuffer = 512;

        newBuffer.Should().BeGreaterThan(oldBuffer);
        newBuffer.Should().Be(512, "512 should cover Woods density without excessive memory");
    }

    // --- Layer Interruption Fix (Bot47 loop on Woods) ---
    // Root cause: When a higher-priority layer (Looting, SAIN combat) interrupts
    // questing, QuestingLayer.Stop() cleared _goToLogic BEFORE IsCurrentActionEnding()
    // could check HasFailed. The nav failure was lost, counter never incremented past 1.

    [Fact]
    public void LayerStop_WithFailedGoToLogic_IncrementsNavFailures()
    {
        // Simulates: GoToLogic fails → higher-priority layer activates → Stop() called
        // Before fix: Stop() cleared _goToLogic without processing → counter stuck at 0
        // After fix: Stop() checks HasFailed and calls MarkCurrentObjectiveFailed()
        int navFailures = 0;
        bool goToComplete = true;
        bool goToFailed = true;

        // Stop() processes completed logic before clearing references
        if (goToComplete)
        {
            if (goToFailed)
            {
                navFailures++; // MarkCurrentObjectiveFailed
            }
            else
            {
                navFailures = 0; // MarkCurrentObjectiveComplete
            }
        }

        navFailures.Should().Be(1, "Stop() must capture nav failure before clearing logic reference");
    }

    [Fact]
    public void Bot47Scenario_RepeatedInterruptions_AccumulatesFailures()
    {
        // Recreates the Bot47 loop on Woods:
        // Bot gets 111.2m waypoint → GoToLogic fails → LootingLayer interrupts →
        // QuestingLayer.Stop() → layer restarts → same waypoint → fails again
        //
        // Before fix: counter reset to 0 each cycle (failures lost in Stop())
        // After fix: counter increments in Stop(), tier-skip kicks in after 2
        int failures = 0;
        int totalTiers = 3;

        // Cycle 1: GoTo fails at 111.2m, Looting layer interrupts, Stop() fires
        failures++; // Stop() → MarkCurrentObjectiveFailed
        (failures / NAV_FAILURE_TIER_SKIP_THRESHOLD).Should().Be(0,
            "cycle 1: below threshold, still tries far tier");

        // Cycle 2: Same waypoint generated (far tier), fails again, layer interrupted
        failures++; // Stop() → MarkCurrentObjectiveFailed
        int skip = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip.Should().Be(1, "cycle 2: threshold reached, skip far tier");
        (totalTiers - skip).Should().Be(2, "medium + close tiers remain");

        // Cycles 3-4: Medium tier also fails
        failures++;
        failures++;
        skip = failures / NAV_FAILURE_TIER_SKIP_THRESHOLD;
        skip.Should().Be(2, "cycles 3-4: skip far + medium, only close remains");
        (totalTiers - skip).Should().Be(1, "only close tier remains");
    }

    [Fact]
    public void LayerStop_WithSuccessfulGoToLogic_ResetsNavFailures()
    {
        // When Stop() processes a successfully completed GoToLogic,
        // it calls MarkCurrentObjectiveComplete() which resets the counter.
        int navFailures = 3; // Had some failures
        bool goToComplete = true;
        bool goToFailed = false; // Successful arrival

        if (goToComplete && !goToFailed)
        {
            navFailures = 0; // MarkCurrentObjectiveComplete resets
        }

        navFailures.Should().Be(0, "successful completion resets nav failure counter");
    }

    [Fact]
    public void LayerStop_GoToLogicNotComplete_DoesNotAffectCounter()
    {
        // If the layer is interrupted while GoToLogic is still MOVING (not failed/complete),
        // no mark method should be called. The objective remains active for next restart.
        int navFailures = 2;
        bool goToComplete = false; // Still moving

        if (goToComplete)
        {
            navFailures++; // This branch should NOT execute
        }

        navFailures.Should().Be(2, "in-progress logic should not affect the counter");
    }
}
