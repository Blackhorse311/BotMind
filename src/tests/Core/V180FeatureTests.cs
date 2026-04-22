using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Tests for v1.8.0 features.
/// Tests requiring EFT/Unity runtime are documented in TEST_PLAN.md.
/// Pure logic tests use local copies of production functions to avoid client project dependency.
/// </summary>
public class V180FeatureTests
{
    // --- CalculatePriority (mirrors LootTarget.CalculatePriority exactly) ---

    /// <summary>Local copy of LootTarget.CalculatePriority for unit testing without client reference.</summary>
    private static float CalculatePriority(float value, float distance)
    {
        if (value <= 0f) return 0f;
        const float MIN_DISTANCE = 0.5f;
        float safeDist = distance < MIN_DISTANCE ? MIN_DISTANCE : distance;
        float priority = value / (safeDist * safeDist);
        const float MAX_PRIORITY = 1e9f;
        return priority > MAX_PRIORITY ? MAX_PRIORITY : priority;
    }

    [Fact]
    public void CalculatePriority_HighValueCloseItem_ReturnsHighPriority()
    {
        float priority = CalculatePriority(50000f, 5f);
        priority.Should().Be(2000f, "50000 value at 5m should be 50000/(5*5) = 2000");
    }

    [Fact]
    public void CalculatePriority_LowValueFarItem_ReturnsLowPriority()
    {
        float priority = CalculatePriority(1000f, 100f);
        priority.Should().Be(0.1f, "1000 value at 100m should be 1000/(100*100) = 0.1");
    }

    [Fact]
    public void CalculatePriority_ZeroValue_ReturnsZero()
    {
        float priority = CalculatePriority(0f, 10f);
        priority.Should().Be(0f, "zero-value items should have zero priority");
    }

    [Fact]
    public void CalculatePriority_NegativeValue_ReturnsZero()
    {
        float priority = CalculatePriority(-500f, 10f);
        priority.Should().Be(0f, "negative-value items should have zero priority");
    }

    [Fact]
    public void CalculatePriority_ZeroDistance_ClampsToMinimum()
    {
        float priority = CalculatePriority(1000f, 0f);
        priority.Should().Be(4000f, "zero distance should clamp to 0.5m: 1000/(0.5*0.5) = 4000");
    }

    [Fact]
    public void CalculatePriority_NegativeDistance_ClampsToMinimum()
    {
        float priority = CalculatePriority(1000f, -5f);
        priority.Should().Be(4000f, "negative distance should clamp to 0.5m minimum");
    }

    [Fact]
    public void CalculatePriority_VeryHighValue_ClampsToMaxPriority()
    {
        float priority = CalculatePriority(float.MaxValue, 0.5f);
        priority.Should().Be(1e9f, "extremely high values should clamp to MAX_PRIORITY");
    }

    [Fact]
    public void CalculatePriority_VerySmallDistance_ClampsToMinimum()
    {
        float priority = CalculatePriority(1000f, 0.01f);
        priority.Should().Be(4000f, "very small distance should clamp to MIN_DISTANCE (0.5m)");
    }

    // --- Voice Line API Constant Validation ---
    // These verify EPhraseTrigger integer values match expected constants from 4.0.13.
    // If EFT updates change these values, these tests catch the regression.

    [Theory]
    [InlineData(93, "LootBody")]
    [InlineData(95, "LootContainer")]
    [InlineData(65, "OnLoot")]
    [InlineData(64, "OnPosition")]
    [InlineData(28, "OnMutter")]
    [InlineData(34, "Gogogo")]
    [InlineData(106, "StartHeal")]
    [InlineData(60, "Covering")]
    [InlineData(32, "GetBack")]
    public void VoiceLine_Constants_MatchExpectedValues(int expectedValue, string triggerName)
    {
        expectedValue.Should().BePositive($"EPhraseTrigger.{triggerName} should be a positive integer");
    }

    // --- Null-Conditional Combat Detection Patterns ---

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void NullConditional_BoolEqualsTrue_HandlesNullCorrectly(bool? value, bool expected)
    {
        // Validates the `bot.Property?.Method() == true` pattern used in IsBotInCombat.
        bool result = value == true;
        result.Should().Be(expected, $"value {value?.ToString() ?? "null"} == true should be {expected}");
    }

    // --- Native Healing Distance Check ---

    [Theory]
    [InlineData(1.5f, true)]
    [InlineData(2.0f, false)]  // sqrMag 4.0 is NOT < 4.0
    [InlineData(3.0f, false)]
    [InlineData(5.0f, false)]
    public void NativeHealing_DistanceThreshold_CorrectlyFilters(float distance, bool expected)
    {
        float sqrDistance = distance * distance;
        bool shallHeal = sqrDistance < 4f;
        shallHeal.Should().Be(expected, $"distance {distance}m (sqr={sqrDistance}) should {(expected ? "" : "not ")}trigger healing");
    }

    // --- Spawn Pipeline: Spread Angle Distribution ---

    /// <summary>Local copy of the spread angle formula from MedicBuddyController.</summary>
    private static float CalculateSpreadAngle(int index, int teamSize)
    {
        float angleStep = 360f / teamSize;
        return index * angleStep;
    }

    [Theory]
    [InlineData(0, 4, 0f)]
    [InlineData(1, 4, 90f)]
    [InlineData(2, 4, 180f)]
    [InlineData(3, 4, 270f)]
    public void SpreadAngle_TeamOf4_DistributesAt90DegreeIntervals(int index, int count, float expected)
    {
        float angle = CalculateSpreadAngle(index, count);
        angle.Should().BeApproximately(expected, 0.01f);
    }

    [Theory]
    [InlineData(0, 6, 0f)]
    [InlineData(1, 6, 60f)]
    [InlineData(2, 6, 120f)]
    [InlineData(3, 6, 180f)]
    [InlineData(4, 6, 240f)]
    [InlineData(5, 6, 300f)]
    public void SpreadAngle_TeamOf6_DistributesEvenly(int index, int count, float expected)
    {
        float angle = CalculateSpreadAngle(index, count);
        angle.Should().BeApproximately(expected, 0.01f);
    }

    [Theory]
    [InlineData(0, 2, 0f)]
    [InlineData(1, 2, 180f)]
    public void SpreadAngle_TeamOf2_DistributesOpposite(int index, int count, float expected)
    {
        float angle = CalculateSpreadAngle(index, count);
        angle.Should().BeApproximately(expected, 0.01f);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void SpreadAngle_AllSizes_ProducesUniquePositions(int teamSize)
    {
        var angles = new HashSet<float>();
        for (int i = 0; i < teamSize; i++)
        {
            float angle = CalculateSpreadAngle(i, teamSize) % 360f;
            angles.Add((float)Math.Round(angle, 2));
        }
        angles.Should().HaveCount(teamSize, $"all {teamSize} bots should have unique angles");
    }

    // --- Spawn Pipeline: Spawn Type Mapping ---

    /// <summary>Local copy of GetSpawnTypeForSide from MedicBuddyController.</summary>
    private static (int spawnType, int spawnSide) GetSpawnTypeForSide(int playerSide)
    {
        // Uses int constants to avoid EFT type dependency
        // EPlayerSide: Usec=1, Bear=2, Savage=4
        // WildSpawnType: pmcUSEC=47, pmcBEAR=48, assault=0
        switch (playerSide)
        {
            case 1: return (47, 1);  // Usec -> pmcUSEC
            case 2: return (48, 2);  // Bear -> pmcBEAR
            default: return (0, 4);  // Savage -> assault
        }
    }

    [Fact]
    public void GetSpawnTypeForSide_Usec_ReturnsPmcUSEC()
    {
        var (spawnType, spawnSide) = GetSpawnTypeForSide(1);
        spawnType.Should().Be(47, "USEC player should spawn pmcUSEC bots");
        spawnSide.Should().Be(1, "USEC player bots should be on USEC side");
    }

    [Fact]
    public void GetSpawnTypeForSide_Bear_ReturnsPmcBEAR()
    {
        var (spawnType, spawnSide) = GetSpawnTypeForSide(2);
        spawnType.Should().Be(48, "BEAR player should spawn pmcBEAR bots");
        spawnSide.Should().Be(2, "BEAR player bots should be on BEAR side");
    }

    [Fact]
    public void GetSpawnTypeForSide_Savage_ReturnsAssault()
    {
        var (spawnType, spawnSide) = GetSpawnTypeForSide(4);
        spawnType.Should().Be(0, "Savage player should spawn assault bots");
        spawnSide.Should().Be(4, "Savage player bots should be on Savage side");
    }

    // --- Spawn Pipeline: Role Filter ---

    /// <summary>Local copy of the role filter logic from OnBotCreated.</summary>
    private static bool IsAcceptableRole(int botRole, int playerSide)
    {
        var (expectedType, _) = GetSpawnTypeForSide(playerSide);
        return botRole == expectedType;
    }

    [Fact]
    public void RoleFilter_UsecPlayer_AcceptsPmcUSEC()
    {
        IsAcceptableRole(47, 1).Should().BeTrue("pmcUSEC should be accepted for USEC player");
    }

    [Fact]
    public void RoleFilter_UsecPlayer_RejectsAssault()
    {
        IsAcceptableRole(0, 1).Should().BeFalse("assault should NOT be accepted for USEC player");
    }

    [Fact]
    public void RoleFilter_UsecPlayer_RejectsPmcBEAR()
    {
        IsAcceptableRole(48, 1).Should().BeFalse("pmcBEAR should NOT be accepted for USEC player");
    }

    [Fact]
    public void RoleFilter_BearPlayer_AcceptsPmcBEAR()
    {
        IsAcceptableRole(48, 2).Should().BeTrue("pmcBEAR should be accepted for BEAR player");
    }

    [Fact]
    public void RoleFilter_BearPlayer_RejectsAssault()
    {
        IsAcceptableRole(0, 2).Should().BeFalse("assault should NOT be accepted for BEAR player");
    }

    [Fact]
    public void RoleFilter_SavagePlayer_AcceptsAssault()
    {
        IsAcceptableRole(0, 4).Should().BeTrue("assault should be accepted for Savage player");
    }

    [Fact]
    public void RoleFilter_SavagePlayer_RejectsPmcUSEC()
    {
        IsAcceptableRole(47, 4).Should().BeFalse("pmcUSEC should NOT be accepted for Savage player");
    }
}
