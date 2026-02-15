using FluentAssertions;
using System.Collections.Concurrent;
using System.Threading;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for blacklist logic used in LootFinder.
/// Tests thread-safety and size limiting behavior.
/// </summary>
public class BlacklistTests
{
    private const int MaxBlacklistSize = 200;

    [Fact]
    public void Blacklist_ShouldBeEmptyInitially()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);

        // Assert
        blacklist.Count.Should().Be(0);
    }

    [Fact]
    public void AddToBlacklist_ShouldIncreaseCount()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);
        var target = new object();

        // Act
        blacklist.Add(target);

        // Assert
        blacklist.Count.Should().Be(1);
        blacklist.Contains(target).Should().BeTrue();
    }

    [Fact]
    public void AddDuplicate_ShouldNotIncreaseCount()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);
        var target = new object();

        // Act
        blacklist.Add(target);
        blacklist.Add(target);

        // Assert
        blacklist.Count.Should().Be(1);
    }

    [Fact]
    public void Blacklist_ShouldClearWhenMaxSizeExceeded()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);

        // Act - Fill to max
        for (int i = 0; i < MaxBlacklistSize; i++)
        {
            blacklist.Add(new object());
        }

        // Assert - Should be at max
        blacklist.Count.Should().Be(MaxBlacklistSize);

        // Act - Add one more
        blacklist.Add(new object());

        // Assert - Should have cleared and added just the new one
        blacklist.Count.Should().Be(1);
    }

    [Fact]
    public void Contains_WithNullTarget_ShouldReturnFalse()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);

        // Act
        var result = blacklist.Contains(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Clear_ShouldRemoveAllEntries()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);
        for (int i = 0; i < 50; i++)
        {
            blacklist.Add(new object());
        }

        // Act
        blacklist.Clear();

        // Assert
        blacklist.Count.Should().Be(0);
    }

    [Fact]
    public void Blacklist_ShouldBeThreadSafe()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);
        var objectsToAdd = Enumerable.Range(0, 100).Select(_ => new object()).ToList();

        // Act - Add from multiple threads
        Parallel.ForEach(objectsToAdd, obj => blacklist.Add(obj));

        // Assert - All should be added (no exceptions, count is correct)
        blacklist.Count.Should().Be(100);
    }

    [Fact]
    public void ConcurrentAddAndCheck_ShouldNotThrow()
    {
        // Arrange
        var blacklist = new TestBlacklist(MaxBlacklistSize);
        var objects = Enumerable.Range(0, 50).Select(_ => new object()).ToList();

        // Act - Concurrent adds and checks
        var exception = Record.Exception(() =>
        {
            Parallel.Invoke(
                () => { foreach (var obj in objects) blacklist.Add(obj); },
                () => { foreach (var obj in objects) blacklist.Contains(obj); },
                () => { foreach (var obj in objects) blacklist.Add(new object()); }
            );
        });

        // Assert
        exception.Should().BeNull();
    }

    /// <summary>
    /// Test implementation of thread-safe blacklist.
    /// Mirrors the LootFinder's ConcurrentDictionary-based blacklist.
    ///
    /// IMPORTANT: Uses stable string identifiers (not GetHashCode()) to match production code.
    /// In production, LootFinder uses ProfileId for corpses and InstanceID for containers/items.
    /// For testing, we use ConditionalWeakTable to maintain object->ID mappings without
    /// preventing garbage collection.
    /// </summary>
    private class TestBlacklist
    {
        private readonly ConcurrentDictionary<string, byte> _items = new();
        private readonly int _maxSize;
        private int _idCounter;

        // Use ConditionalWeakTable to track object->ID mappings without preventing GC
        // This ensures the same object always gets the same ID
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, StableId> _objectIds = new();

        public TestBlacklist(int maxSize)
        {
            _maxSize = maxSize;
        }

        public int Count => _items.Count;

        /// <summary>
        /// Adds an object to the blacklist using a stable identifier.
        /// In production, this would use ProfileId for corpses, InstanceID for containers/items.
        /// For testing, we assign a unique stable ID per object instance.
        /// </summary>
        public void Add(object target)
        {
            if (target == null) return;

            if (_items.Count >= _maxSize)
            {
                _items.Clear();
            }

            // Use stable identifier like production code does
            string stableId = GetStableId(target);
            _items.TryAdd(stableId, 0);
        }

        public bool Contains(object? target)
        {
            if (target == null) return false;
            string stableId = GetStableId(target);
            return _items.ContainsKey(stableId);
        }

        public void Clear()
        {
            _items.Clear();
        }

        /// <summary>
        /// Gets a stable identifier for an object.
        /// In production this uses ProfileId/InstanceID. For tests we use ConditionalWeakTable
        /// to assign and track stable IDs per object instance.
        /// </summary>
        private string GetStableId(object target)
        {
            // GetOrCreateValue ensures the same object always gets the same ID
            var stableId = _objectIds.GetOrCreateValue(target);
            if (stableId.Id == null)
            {
                stableId.Id = $"test_{Interlocked.Increment(ref _idCounter)}";
            }
            return stableId.Id;
        }

        /// <summary>Helper class to store stable ID in ConditionalWeakTable.</summary>
        private class StableId
        {
            public string? Id { get; set; }
        }
    }
}
