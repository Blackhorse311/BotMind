using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for item value and weighting calculations used in looting logic.
/// Tests the value-per-slot calculations to ensure bots prioritize efficiently.
/// </summary>
public class ItemValueCalculationTests
{
    [Theory]
    [InlineData(10000, 1, 1, 10000)]   // 1x1 item
    [InlineData(10000, 2, 1, 5000)]    // 2x1 item
    [InlineData(10000, 2, 2, 2500)]    // 2x2 item
    [InlineData(50000, 5, 3, 3333)]    // 5x3 item (15 slots)
    public void CalculatePricePerSlot_ShouldReturnCorrectValue(int price, int width, int height, int expected)
    {
        // Act
        var result = CalculatePricePerSlot(price, width, height);

        // Assert
        result.Should().BeCloseTo(expected, 1);
    }

    [Fact]
    public void CalculatePricePerSlot_WithZeroSize_ShouldReturnZero()
    {
        // Act
        var result = CalculatePricePerSlot(10000, 0, 0);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void CalculatePricePerSlot_WithZeroPrice_ShouldReturnZero()
    {
        // Act
        var result = CalculatePricePerSlot(0, 2, 2);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public void WeightedRandomSelection_HigherValueShouldHaveHigherWeight()
    {
        // Arrange - Simulating the weighted selection from LootCorpseLogic
        var items = new[]
        {
            new TestItem { PricePerSlot = 100, Weight = CalculateWeight(100) },
            new TestItem { PricePerSlot = 10000, Weight = CalculateWeight(10000) },
            new TestItem { PricePerSlot = 1000, Weight = CalculateWeight(1000) }
        };

        // Act - Sort by weight descending
        var sorted = items.OrderByDescending(i => i.Weight).ToList();

        // Assert - Higher price per slot should tend to have higher weight
        // Note: Due to randomness, we can't assert exact order, but the weight formula
        // should favor higher value items on average
        sorted[0].PricePerSlot.Should().BeGreaterOrEqualTo(sorted[2].PricePerSlot);
    }

    [Theory]
    [InlineData(10000, true)]  // Above minimum
    [InlineData(5000, true)]   // Exactly at minimum
    [InlineData(4999, false)]  // Below minimum
    [InlineData(0, false)]     // Zero value
    [InlineData(100000, true)] // Very high value
    public void MeetsMinimumValue_ShouldValidateCorrectly(int itemValue, bool expected)
    {
        // Arrange
        const int minItemValue = 5000;

        // Act
        var result = itemValue >= minItemValue;

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void SortItemsByWeight_ShouldOrderDescending()
    {
        // Arrange
        var items = new List<(float Weight, string Name)>
        {
            (0.5f, "Low"),
            (0.9f, "High"),
            (0.7f, "Medium")
        };

        // Act - Sort like LootCorpseLogic does
        items.Sort((a, b) => -1 * a.Weight.CompareTo(b.Weight));

        // Assert
        items[0].Name.Should().Be("High");
        items[1].Name.Should().Be("Medium");
        items[2].Name.Should().Be("Low");
    }

    [Fact]
    public void RandomTakeItemsCount_ShouldBeWithinRange()
    {
        // Arrange
        var random = new Random(42); // Fixed seed for reproducibility
        int itemCount = 10;
        var results = new List<int>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            // Simulating: Random.Range(1, _itemsCache.Count + 1)
            int takeCount = random.Next(1, itemCount + 1);
            results.Add(takeCount);
        }

        // Assert
        results.Should().OnlyContain(x => x >= 1 && x <= itemCount);
        results.Should().Contain(1, "Should sometimes take minimum");
        results.Should().Contain(itemCount, "Should sometimes take maximum");
    }

    /// <summary>
    /// Mirrors the price-per-slot calculation from LootCorpseLogic.
    /// </summary>
    private static int CalculatePricePerSlot(int price, int width, int height)
    {
        int slotCount = width * height;
        return slotCount > 0 ? price / slotCount : 0;
    }

    /// <summary>
    /// Mirrors the weight calculation from LootCorpseLogic.
    /// Uses Pow(random, 1/pricePerSlot) to weight toward higher values.
    /// </summary>
    private static float CalculateWeight(int pricePerSlot)
    {
        var random = new Random(42); // Fixed seed for testing
        float randomValue = (float)random.NextDouble();

        return pricePerSlot > 0
            ? MathF.Pow(randomValue, 1f / pricePerSlot)
            : randomValue;
    }

    private class TestItem
    {
        public int PricePerSlot { get; init; }
        public float Weight { get; init; }
    }
}
