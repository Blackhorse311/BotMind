using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for LootTarget priority calculation logic.
/// These tests verify the value/distance weighting algorithm.
/// </summary>
public class LootTargetTests
{
    [Theory]
    [InlineData(10000f, 10f, 100f)]      // 10000 / (10*10) = 100
    [InlineData(50000f, 5f, 2000f)]      // 50000 / (5*5) = 2000
    [InlineData(1000f, 0.5f, 4000f)]     // 1000 / (0.5*0.5) = 4000 (at MIN_DISTANCE)
    [InlineData(1000f, 0.3f, 4000f)]     // Distance < 0.5 clamped to 0.5, same as above
    public void CalculatePriority_ShouldReturnExpectedValue(float value, float distance, float expected)
    {
        // Act
        var result = CalculatePriority(value, distance);

        // Assert
        result.Should().BeApproximately(expected, 0.01f);
    }

    [Fact]
    public void CalculatePriority_HigherValue_ShouldHaveHigherPriority()
    {
        // Arrange
        float sameDistance = 10f;
        float lowValue = 1000f;
        float highValue = 50000f;

        // Act
        var lowPriority = CalculatePriority(lowValue, sameDistance);
        var highPriority = CalculatePriority(highValue, sameDistance);

        // Assert
        highPriority.Should().BeGreaterThan(lowPriority);
    }

    [Fact]
    public void CalculatePriority_CloserDistance_ShouldHaveHigherPriority()
    {
        // Arrange
        float sameValue = 10000f;
        float farDistance = 50f;
        float closeDistance = 5f;

        // Act
        var farPriority = CalculatePriority(sameValue, farDistance);
        var closePriority = CalculatePriority(sameValue, closeDistance);

        // Assert
        closePriority.Should().BeGreaterThan(farPriority);
    }

    [Fact]
    public void CalculatePriority_ZeroValue_ShouldReturnZero()
    {
        // Act
        var result = CalculatePriority(0f, 10f);

        // Assert
        result.Should().Be(0f);
    }

    [Fact]
    public void CalculatePriority_VerySmallDistance_ShouldClampToHalf()
    {
        // Arrange - Testing that distances < 0.5 are clamped to prevent extreme values
        // Production code uses MIN_DISTANCE = 0.5f per Fifth Review Fix (Issue 76)
        float value = 1000f;

        // Act
        var result1 = CalculatePriority(value, 0.1f);  // Clamped to 0.5
        var result2 = CalculatePriority(value, 0.3f);  // Clamped to 0.5
        var result3 = CalculatePriority(value, 0.5f);  // At MIN_DISTANCE

        // Assert - All should be same since distance is clamped to 0.5
        result1.Should().Be(result2);
        result2.Should().Be(result3);
        result1.Should().Be(4000f); // 1000 / (0.5 * 0.5) = 4000
    }

    /// <summary>
    /// Mirror of LootTarget.CalculatePriority for testing.
    /// This allows us to test the algorithm without Unity dependencies.
    /// Must match production code in LootFinder.cs (MIN_DISTANCE = 0.5f).
    /// </summary>
    private static float CalculatePriority(float value, float distance)
    {
        // Higher value and closer distance = higher priority
        // Normalize: value in rubles, distance in meters
        // Fifth Review Fix (Issue 76): Production uses 0.5f, not 1f
        const float MIN_DISTANCE = 0.5f;
        if (distance < MIN_DISTANCE) distance = MIN_DISTANCE;
        return value / (distance * distance);
    }
}
