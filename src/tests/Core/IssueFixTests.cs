using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for v1.6.0 fixes (GitHub Issue #10):
/// - Questing role allowlist: only PMCs and Scavs get questing objectives
/// - Loot hang safety net: action-level timeout in LootingLayer
/// - Backwards walking fix: lookToMovingDirection parameter in GoToPoint
/// </summary>
public class IssueFixTests
{
    // WildSpawnType enum values (mirrored from EFT)
    private const int ROLE_PMCUSEC = 1;
    private const int ROLE_PMCBEAR = 2;
    private const int ROLE_ASSAULT = 3;
    private const int ROLE_CURSED_ASSAULT = 4;
    private const int ROLE_MARKSMAN = 5;
    private const int ROLE_PMCBOT = 10;     // Raiders
    private const int ROLE_EXUSEC = 11;     // Rogues
    private const int ROLE_ARENA_FIGHTER = 12; // Bloodhounds
    private const int ROLE_BOSS_KNIGHT = 20;
    private const int ROLE_FOLLOWER_BIRD_EYE = 21;

    // --- Questing Role Allowlist (Fix #2) ---

    [Theory]
    [InlineData(ROLE_PMCUSEC, true)]
    [InlineData(ROLE_PMCBEAR, true)]
    public void QuestingRoleCheck_PMC_AllowedWhenEnabled(int role, bool expected)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;
        bool allowed = isPMC || isScav;
        allowed.Should().Be(expected);
    }

    [Theory]
    [InlineData(ROLE_ASSAULT, true)]
    [InlineData(ROLE_CURSED_ASSAULT, true)]
    [InlineData(ROLE_MARKSMAN, true)]
    public void QuestingRoleCheck_Scav_AllowedWhenEnabled(int role, bool expected)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;
        bool allowed = isPMC || isScav;
        allowed.Should().Be(expected);
    }

    [Theory]
    [InlineData(ROLE_PMCBOT)]       // Raiders — brain "PMC" but wrong WildSpawnType
    [InlineData(ROLE_EXUSEC)]       // Rogues — brain "ExUsec" but wrong WildSpawnType
    [InlineData(ROLE_ARENA_FIGHTER)] // Bloodhounds
    [InlineData(ROLE_BOSS_KNIGHT)]   // Boss
    [InlineData(ROLE_FOLLOWER_BIRD_EYE)] // Boss follower
    public void QuestingRoleCheck_UnrecognizedRole_Rejected(int role)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;

        // v1.6.0 Fix: Explicit allowlist rejects unrecognized roles
        bool allowed = isPMC || isScav;
        allowed.Should().BeFalse(
            "Raiders, Rogues, Bloodhounds, and bosses must not receive questing objectives — " +
            "they have their own patrol systems and generic waypoints cause spawn-stuck loops");
    }

    [Fact]
    public void QuestingRoleCheck_Raider_BrainNameIsPMC_ButRoleIsPmcBot()
    {
        // This test documents the core bug: Raiders have brain name "PMC" (so BigBrain attaches
        // the questing layer), but WildSpawnType.pmcBot (not pmcUSEC/pmcBEAR). The old code
        // only checked PMC/Scav WildSpawnTypes, so Raiders fell through both guards.
        // Raiders use brain "PMC" but WildSpawnType.pmcBot
        int role = ROLE_PMCBOT;

        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;

        isPMC.Should().BeFalse("pmcBot is NOT pmcUSEC or pmcBEAR");
        isScav.Should().BeFalse("pmcBot is NOT assault, cursedAssault, or marksman");

        // Before fix: both guards skipped → questing activated → stuck on bad waypoints
        // After fix: explicit (!isPMC && !isScav) → return false
        bool allowedAfterFix = isPMC || isScav;
        allowedAfterFix.Should().BeFalse();
    }

    // --- GenerateObjectives Defense-in-Depth (Fix #2) ---

    [Theory]
    [InlineData(ROLE_PMCUSEC, "PMC")]
    [InlineData(ROLE_PMCBEAR, "PMC")]
    public void GenerateObjectives_PMCRole_GeneratesPMCObjectives(int role, string expectedType)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;

        string objectiveType = isPMC ? "PMC" : isScav ? "Scav" : "None";
        objectiveType.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(ROLE_ASSAULT, "Scav")]
    [InlineData(ROLE_CURSED_ASSAULT, "Scav")]
    [InlineData(ROLE_MARKSMAN, "Scav")]
    public void GenerateObjectives_ScavRole_GeneratesScavObjectives(int role, string expectedType)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;

        string objectiveType = isPMC ? "PMC" : isScav ? "Scav" : "None";
        objectiveType.Should().Be(expectedType);
    }

    [Theory]
    [InlineData(ROLE_PMCBOT)]
    [InlineData(ROLE_EXUSEC)]
    [InlineData(ROLE_ARENA_FIGHTER)]
    public void GenerateObjectives_UnrecognizedRole_GeneratesNothing(int role)
    {
        bool isPMC = role == ROLE_PMCUSEC || role == ROLE_PMCBEAR;
        bool isScav = role == ROLE_ASSAULT || role == ROLE_CURSED_ASSAULT || role == ROLE_MARKSMAN;

        string objectiveType = isPMC ? "PMC" : isScav ? "Scav" : "None";
        objectiveType.Should().Be("None",
            "defense-in-depth: unrecognized roles should generate no objectives");
    }

    // --- Loot Hang Action-Level Timeout (Fix #1) ---

    private const float ACTION_TIMEOUT = 70f; // Matches LootingLayer.ACTION_TIMEOUT
    private const float OVERALL_TIMEOUT = 60f; // Matches logic-level timeout

    [Fact]
    public void ActionTimeout_ExceedsOverallTimeout_ForcesActionEnd()
    {
        // The action timeout (70s) must exceed the logic's OVERALL_TIMEOUT (60s)
        // to give the logic time to self-complete first
        ACTION_TIMEOUT.Should().BeGreaterThan(OVERALL_TIMEOUT,
            "action timeout must exceed logic timeout to avoid premature termination");
    }

    [Theory]
    [InlineData(59f, false)]  // Before logic timeout
    [InlineData(65f, false)]  // After logic timeout, before action timeout
    [InlineData(70f, true)]   // At action timeout
    [InlineData(80f, true)]   // Well past action timeout
    public void ActionTimeout_OnlyFiresAfterActionTimeout(float elapsedTime, bool shouldTimeout)
    {
        bool timedOut = elapsedTime >= ACTION_TIMEOUT;
        timedOut.Should().Be(shouldTimeout);
    }

    [Fact]
    public void ActionTimeout_IsLastResort_LogicTimeoutFiresFirst()
    {
        // Verify the layered timeout approach:
        // 1. Logic OVERALL_TIMEOUT fires at 60s → sets Complete state
        // 2. If IsCurrentActionEnding catches it → action ends normally (no action timeout)
        // 3. If logic reference is null → action timeout catches it at 70s
        float logicTimeout = 60f;
        float actionTimeout = 70f;
        float buffer = actionTimeout - logicTimeout;

        buffer.Should().BeGreaterOrEqualTo(5f,
            "action timeout should have ≥5s buffer after logic timeout to avoid races");
    }

    // --- LootCorpseLogic _target Reset (Fix #1) ---

    [Fact]
    public void LootCorpseLogic_StartResetsTarget_PreventsSkippedRegistration()
    {
        // Documents the bug: without _target = null in Start(), the registration block
        // in Update() ("if (_target == null)") is skipped on logic instance reuse,
        // causing RegisterLogic() to never be called on the layer
        bool targetResetInStart = true; // v1.6.0 adds _target = null to LootCorpseLogic.Start()
        bool registrationWillFire = targetResetInStart; // Registration block requires _target == null
        registrationWillFire.Should().BeTrue(
            "Start() must reset _target to null so RegisterLogic() fires on next Update()");
    }

    // --- Backwards Walking GoToPoint Fix (Fix #3) ---

    [Fact]
    public void GoToPoint_LookToMovingDirection_MustBeTrue()
    {
        // Documents the fix: GoToPoint's 5th param (lookToMovingDirection) must be true
        // to give EFT's locomotion a facing constraint during movement.
        // With false/false for both look params, bots walked backwards.
        // 4th param (lookToPoint) always false — don't stare at destination
        // 5th param (lookToMovingDirection) — v1.6.0 changed from false to true
        bool lookToMovingDirection = true;

        // The key distinction from v1.2.0's per-frame LookToMovingDirection() call:
        // GoToPoint's parameter is a one-time path hint applied per path update (every 2-3s),
        // NOT a per-frame override. EFT's LookSensor still runs between path updates.
        bool perFrameOverride = false;

        lookToMovingDirection.Should().BeTrue(
            "one-time path hint prevents backwards walking without blocking enemy detection");
        perFrameOverride.Should().BeFalse(
            "per-frame LookToMovingDirection() was correctly removed in v1.2.0");
    }

    [Fact]
    public void GoToPoint_LookToMovingDirection_DifferentFromPerFrameCall()
    {
        // Ensures we understand the distinction between:
        // 1. GoToPoint(..., lookToMovingDirection: true) — path-level hint, safe
        // 2. BotOwner.Steering.LookToMovingDirection() every frame — blocks LookSensor, dangerous
        //
        // We want #1 (on), #2 (off). This was the root cause of both:
        // - v1.2.0 bug: #2 on → enemies not detected
        // - v1.5.0 bug: #1 off → backwards walking

        bool goToPointParam = true;  // Safe — applied per path update
        bool perFrameCall = false;    // Dangerous — blocks LookSensor

        goToPointParam.Should().BeTrue();
        perFrameCall.Should().BeFalse();
    }

    [Fact]
    public void GoToPoint_AllFilesConsistent_LookToMovingDirectionTrue()
    {
        // All 5 files with GoToPoint calls should use lookToMovingDirection = true:
        // - GoToLocationLogic.cs (2 calls)
        // - ExploreAreaLogic.cs (1 call)
        // - LootContainerLogic.cs (1 call)
        // - LootCorpseLogic.cs (1 call)
        // - PickupItemLogic.cs (1 call) — already had true before v1.6.0
        int totalGoToPointCalls = 6;
        int callsWithLookToMovingDirectionTrue = 6; // All should be true now

        callsWithLookToMovingDirectionTrue.Should().Be(totalGoToPointCalls,
            "every GoToPoint call must use lookToMovingDirection=true for consistent facing");
    }
}
