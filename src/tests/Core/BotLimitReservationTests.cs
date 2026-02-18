using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for bot limit reservation calculation logic.
/// Tests the pure math extracted from BotLimitManager to validate
/// slot reservation across all edge cases without game dependencies.
/// </summary>
public class BotLimitReservationTests
{
    #region CalculateEffectiveMax Tests

    [Theory]
    [InlineData(0, 4, true, 0)]     // slider=0: use game default (no override)
    [InlineData(31, 6, true, 25)]   // slider=31, team=6, medic ON: 25 regular
    [InlineData(31, 4, true, 27)]   // slider=31, team=4, medic ON: 27 regular
    [InlineData(31, 2, true, 29)]   // slider=31, team=2, medic ON: 29 regular
    [InlineData(25, 4, true, 21)]   // slider=25, team=4, medic ON: 21 regular
    [InlineData(20, 6, true, 14)]   // slider=20, team=6, medic ON: 14 regular
    [InlineData(10, 6, true, 4)]    // slider=10, team=6: only 4 regular
    public void CalculateEffectiveMax_NormalOperation_ReturnsSliderMinusReserved(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        // Act
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: false);

        // Assert
        result.Should().Be(expectedEffective);
    }

    [Theory]
    [InlineData(31, 4, false, 31)]  // slider=31, medic OFF: full 31 (no reservation)
    [InlineData(25, 6, false, 25)]  // slider=25, medic OFF: full 25
    [InlineData(15, 2, false, 15)]  // slider=15, medic OFF: full 15
    public void CalculateEffectiveMax_MedicBuddyDisabled_ReturnsFullSlider(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        // Act
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: false);

        // Assert
        result.Should().Be(expectedEffective);
    }

    [Theory]
    [InlineData(6, 6, true, 1)]     // slider=6, team=6: clamped to minimum 1
    [InlineData(2, 6, true, 1)]     // slider=2, team=6: clamped to minimum 1
    [InlineData(1, 4, true, 1)]     // slider=1, team=4: clamped to minimum 1
    public void CalculateEffectiveMax_SliderLessThanTeamSize_ClampsToMinimumOne(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        // Act
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: false);

        // Assert
        result.Should().Be(expectedEffective);
    }

    [Theory]
    [InlineData(31, 4, true, 31)]   // During spawn: full slider value
    [InlineData(31, 6, true, 31)]   // During spawn: full slider value
    [InlineData(25, 6, true, 25)]   // During spawn: full slider value
    public void CalculateEffectiveMax_DuringMedicBuddySpawn_ReturnsFullSlider(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        // Act
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: true);

        // Assert
        result.Should().Be(expectedEffective);
    }

    [Fact]
    public void CalculateEffectiveMax_SliderZeroDuringSpawn_StillReturnsZero()
    {
        // slider=0 always means "use game default", even during spawn
        int result = CalculateEffectiveMax(0, 4, true, isMedicBuddySpawning: true);
        result.Should().Be(0);
    }

    #endregion

    #region CalculateReservedSlots Tests

    [Theory]
    [InlineData(31, 4, true, 4)]    // Normal case: reserves team size
    [InlineData(31, 6, true, 6)]    // Larger team: reserves 6
    [InlineData(31, 2, true, 2)]    // Smaller team: reserves 2
    [InlineData(25, 4, true, 4)]    // Different slider: still reserves team size
    public void CalculateReservedSlots_MedicEnabled_ReturnsTeamSize(
        int sliderValue, int teamSize, bool medicEnabled, int expectedReserved)
    {
        // Act
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);

        // Assert
        result.Should().Be(expectedReserved);
    }

    [Theory]
    [InlineData(0, 4, true, 0)]     // Slider=0: no reservation regardless
    [InlineData(0, 6, true, 0)]     // Slider=0: no reservation
    public void CalculateReservedSlots_SliderZero_ReturnsZero(
        int sliderValue, int teamSize, bool medicEnabled, int expectedReserved)
    {
        // Act
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);

        // Assert
        result.Should().Be(expectedReserved);
    }

    [Theory]
    [InlineData(31, 4, false, 0)]   // MedicBuddy disabled: no reservation
    [InlineData(25, 6, false, 0)]   // MedicBuddy disabled: no reservation
    public void CalculateReservedSlots_MedicDisabled_ReturnsZero(
        int sliderValue, int teamSize, bool medicEnabled, int expectedReserved)
    {
        // Act
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);

        // Assert
        result.Should().Be(expectedReserved);
    }

    #endregion

    #region Calculation Consistency Tests

    [Theory]
    [InlineData(31, 6, true)]
    [InlineData(31, 4, true)]
    [InlineData(25, 4, true)]
    public void EffectiveMax_PlusReserved_EqualsSlider(int sliderValue, int teamSize, bool medicEnabled)
    {
        // Effective + Reserved should equal the slider value
        int effective = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, false);
        int reserved = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);

        (effective + reserved).Should().Be(sliderValue);
    }

    [Theory]
    [InlineData(2, 6, true)]
    [InlineData(1, 4, true)]
    public void EffectiveMax_WhenClamped_PlusReserved_MayExceedSlider(int sliderValue, int teamSize, bool medicEnabled)
    {
        // When clamped, effective(1) + reserved(teamSize) > slider — this is expected
        // because we guarantee at least 1 regular bot
        int effective = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, false);
        effective.Should().Be(1);
    }

    #endregion

    // Mirror of BotLimitManager logic, extracted for testability
    private static int CalculateEffectiveMax(int sliderValue, int teamSize, bool medicEnabled, bool isMedicBuddySpawning)
    {
        if (sliderValue <= 0) return 0;
        if (isMedicBuddySpawning) return sliderValue;
        int reserved = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);
        return System.Math.Max(1, sliderValue - reserved);
    }

    private static int CalculateReservedSlots(int sliderValue, int teamSize, bool medicEnabled)
    {
        if (sliderValue <= 0) return 0;
        if (!medicEnabled) return 0;
        return teamSize;
    }
}
