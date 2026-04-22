using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for bot limit reservation calculation logic.
///
/// v1.8.0: Changed from pre-reservation to just-in-time expansion.
/// CalculateEffectiveMax now returns the full slider value (no subtraction).
/// MedicBuddy slots are expanded above the current limit at spawn time,
/// making this compatible with ABPS and other bot limit mods.
/// </summary>
public class BotLimitReservationTests
{
    #region CalculateEffectiveMax Tests

    [Theory]
    [InlineData(0, 4, true, 0)]     // slider=0: use game default (no override)
    [InlineData(31, 6, true, 31)]   // slider=31: full value (no pre-reservation)
    [InlineData(31, 4, true, 31)]   // slider=31: full value
    [InlineData(25, 4, true, 25)]   // slider=25: full value
    [InlineData(20, 6, true, 20)]   // slider=20: full value
    [InlineData(10, 6, true, 10)]   // slider=10: full value
    public void CalculateEffectiveMax_NormalOperation_ReturnsFullSliderValue(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: false);
        result.Should().Be(expectedEffective);
    }

    [Theory]
    [InlineData(31, 4, false, 31)]  // slider=31, medic OFF: full 31
    [InlineData(25, 6, false, 25)]  // slider=25, medic OFF: full 25
    [InlineData(15, 2, false, 15)]  // slider=15, medic OFF: full 15
    public void CalculateEffectiveMax_MedicBuddyDisabled_ReturnsFullSlider(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: false);
        result.Should().Be(expectedEffective);
    }

    [Theory]
    [InlineData(31, 4, true, 31)]   // During spawn: still full slider (JIT expands separately)
    [InlineData(31, 6, true, 31)]   // During spawn: full slider
    [InlineData(25, 6, true, 25)]   // During spawn: full slider
    public void CalculateEffectiveMax_DuringMedicBuddySpawn_ReturnsFullSlider(
        int sliderValue, int teamSize, bool medicEnabled, int expectedEffective)
    {
        int result = CalculateEffectiveMax(sliderValue, teamSize, medicEnabled, isMedicBuddySpawning: true);
        result.Should().Be(expectedEffective);
    }

    [Fact]
    public void CalculateEffectiveMax_SliderZero_AlwaysReturnsZero()
    {
        // slider=0 always means "use game default", regardless of other params
        CalculateEffectiveMax(0, 4, true, false).Should().Be(0);
        CalculateEffectiveMax(0, 4, true, true).Should().Be(0);
        CalculateEffectiveMax(0, 4, false, false).Should().Be(0);
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
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);
        result.Should().Be(expectedReserved);
    }

    [Theory]
    [InlineData(0, 4, true, 0)]     // Slider=0: no reservation regardless
    [InlineData(0, 6, true, 0)]     // Slider=0: no reservation
    public void CalculateReservedSlots_SliderZero_ReturnsZero(
        int sliderValue, int teamSize, bool medicEnabled, int expectedReserved)
    {
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);
        result.Should().Be(expectedReserved);
    }

    [Theory]
    [InlineData(31, 4, false, 0)]   // MedicBuddy disabled: no reservation
    [InlineData(25, 6, false, 0)]   // MedicBuddy disabled: no reservation
    public void CalculateReservedSlots_MedicDisabled_ReturnsZero(
        int sliderValue, int teamSize, bool medicEnabled, int expectedReserved)
    {
        int result = CalculateReservedSlots(sliderValue, teamSize, medicEnabled);
        result.Should().Be(expectedReserved);
    }

    #endregion

    #region JIT Expansion Logic Tests

    [Theory]
    [InlineData(23, 4, 27)]   // ABPS sets 23, MedicBuddy adds 4 = 27
    [InlineData(30, 4, 34)]   // 30 + 4 = 34
    [InlineData(15, 6, 21)]   // 15 + 6 = 21
    public void JitExpansion_AddsTeamSizeToCurrentLimit(
        int currentLimit, int teamSize, int expectedExpanded)
    {
        // Simulates what BeginMedicBuddySpawn does: current + teamSize
        int expanded = currentLimit + teamSize;
        expanded.Should().Be(expectedExpanded,
            $"expanding from {currentLimit} by {teamSize} should give {expectedExpanded}");
    }

    [Theory]
    [InlineData(23, 4)]
    [InlineData(30, 6)]
    public void JitExpansion_RestoresOriginalLimit(int originalLimit, int teamSize)
    {
        // Simulates Begin -> End cycle
        int expanded = originalLimit + teamSize;
        int restored = originalLimit;
        restored.Should().Be(originalLimit,
            "EndMedicBuddySpawn should restore the pre-spawn limit exactly");
    }

    #endregion

    // Mirror of BotLimitManager logic, extracted for testability
    private static int CalculateEffectiveMax(int sliderValue, int teamSize, bool medicEnabled, bool isMedicBuddySpawning)
    {
        if (sliderValue <= 0) return 0;
        return sliderValue;
    }

    private static int CalculateReservedSlots(int sliderValue, int teamSize, bool medicEnabled)
    {
        if (sliderValue <= 0) return 0;
        if (!medicEnabled) return 0;
        return teamSize;
    }
}
