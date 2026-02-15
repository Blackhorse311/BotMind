using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for timer-based cleanup patterns used in LootFinder.
/// Tests that periodic cleanup reduces per-frame overhead.
/// </summary>
public class TimerCleanupTests
{
    private const float CleanupInterval = 5f;

    [Fact]
    public void PeriodicCleanup_ShouldNotRunBeforeInterval()
    {
        // Arrange
        var cleaner = new TestPeriodicCleaner(CleanupInterval);

        // Act
        cleaner.TryCleanup(currentTime: 2f);
        cleaner.TryCleanup(currentTime: 4f);

        // Assert
        cleaner.CleanupCount.Should().Be(0, "Cleanup should not run before interval elapses");
    }

    [Fact]
    public void PeriodicCleanup_ShouldRunAfterInterval()
    {
        // Arrange
        var cleaner = new TestPeriodicCleaner(CleanupInterval);

        // Act
        cleaner.TryCleanup(currentTime: 5.1f);

        // Assert
        cleaner.CleanupCount.Should().Be(1);
    }

    [Fact]
    public void PeriodicCleanup_ShouldUpdateLastCleanupTime()
    {
        // Arrange
        var cleaner = new TestPeriodicCleaner(CleanupInterval);

        // Act
        cleaner.TryCleanup(currentTime: 6f);
        cleaner.TryCleanup(currentTime: 8f);  // Too soon after first cleanup
        cleaner.TryCleanup(currentTime: 11f); // After interval from first

        // Assert
        cleaner.CleanupCount.Should().Be(2);
    }

    [Fact]
    public void PeriodicCleanup_MultipleIntervalsElapsed_ShouldOnlyCleanupOnce()
    {
        // Arrange
        var cleaner = new TestPeriodicCleaner(CleanupInterval);

        // Act - Jump far ahead (e.g., game was paused)
        cleaner.TryCleanup(currentTime: 100f);

        // Assert - Should still only clean up once
        cleaner.CleanupCount.Should().Be(1);
    }

    [Fact]
    public void CleanupLogic_ShouldRemoveStaleTargets()
    {
        // Arrange
        var targets = new List<object?>
        {
            new object(),
            null,
            new object(),
            null,
            null
        };

        // Act
        targets.RemoveAll(t => t == null);

        // Assert
        targets.Should().HaveCount(2);
        targets.Should().NotContainNulls();
    }

    [Fact]
    public void PerFrameVsTimerBased_TimerShouldReduceCalls()
    {
        // Arrange
        int perFrameCleanups = 0;
        int timerBasedCleanups = 0;
        const int frameCount = 1000;
        const float deltaTime = 0.016f; // ~60 FPS

        var cleaner = new TestPeriodicCleaner(CleanupInterval);

        // Act - Simulate 1000 frames
        for (int i = 0; i < frameCount; i++)
        {
            // Per-frame approach (old)
            perFrameCleanups++;

            // Timer-based approach (new)
            if (cleaner.TryCleanup(i * deltaTime))
            {
                timerBasedCleanups++;
            }
        }

        // Assert
        perFrameCleanups.Should().Be(1000);
        timerBasedCleanups.Should().BeLessThan(10, "Timer-based should run far fewer times");
    }

    [Theory]
    [InlineData(1f, 0f, false)]    // Too soon
    [InlineData(5f, 0f, true)]     // Exactly at interval
    [InlineData(6f, 0f, true)]     // After interval
    [InlineData(10f, 6f, false)]   // Too soon after last cleanup at 6
    [InlineData(12f, 6f, true)]    // After interval from last cleanup at 6
    public void TryCleanup_ShouldRespectInterval(float currentTime, float lastCleanupTime, bool expected)
    {
        // Arrange
        var cleaner = new TestPeriodicCleaner(CleanupInterval);
        if (lastCleanupTime > 0)
        {
            // Simulate a previous cleanup
            cleaner.TryCleanup(lastCleanupTime);
            cleaner.ResetCount();
        }

        // Act
        var result = cleaner.TryCleanup(currentTime);

        // Assert
        result.Should().Be(expected);
    }

    /// <summary>
    /// Test implementation of periodic cleanup pattern.
    /// Mirrors the LootFinder's timer-based cleanup logic.
    /// </summary>
    private class TestPeriodicCleaner
    {
        private readonly float _interval;
        private float _lastCleanupTime;
        private int _cleanupCount;

        public TestPeriodicCleaner(float interval)
        {
            _interval = interval;
            _lastCleanupTime = 0f;
        }

        public int CleanupCount => _cleanupCount;

        public bool TryCleanup(float currentTime)
        {
            if (currentTime - _lastCleanupTime < _interval)
            {
                return false;
            }

            _lastCleanupTime = currentTime;
            _cleanupCount++;
            return true;
        }

        public void ResetCount()
        {
            _cleanupCount = 0;
        }
    }
}
