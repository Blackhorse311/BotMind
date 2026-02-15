using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for reflection caching pattern used in LootCorpseLogic.
/// Verifies that the cache works correctly for performance optimization.
/// </summary>
public class ReflectionCacheTests
{
    [Fact]
    public void PropertyCache_ShouldCachePropertyInfo()
    {
        // Arrange
        var cache = new TestPropertyCache();
        var testObject = new TestClass { Player = "TestPlayer" };

        // Act
        var prop1 = cache.GetPlayerProperty(testObject.GetType());
        var prop2 = cache.GetPlayerProperty(testObject.GetType());

        // Assert
        prop1.Should().NotBeNull();
        prop2.Should().NotBeNull();
        prop1.Should().BeSameAs(prop2, "Property should be cached");
    }

    [Fact]
    public void PropertyCache_ShouldReturnNullForMissingProperty()
    {
        // Arrange
        var cache = new TestPropertyCache();
        var testObject = new ClassWithoutPlayer();

        // Act
        var prop = cache.GetPlayerProperty(testObject.GetType());

        // Assert
        prop.Should().BeNull();
    }

    [Fact]
    public void PropertyCache_ShouldCacheMultipleTypes()
    {
        // Arrange
        var cache = new TestPropertyCache();
        var type1 = typeof(TestClass);
        var type2 = typeof(AnotherTestClass);

        // Act
        var prop1 = cache.GetPlayerProperty(type1);
        var prop2 = cache.GetPlayerProperty(type2);

        // Assert
        prop1.Should().NotBeNull();
        prop2.Should().NotBeNull();
        prop1.Should().NotBeSameAs(prop2, "Different types have different PropertyInfo");
    }

    [Fact]
    public void GetValue_ShouldReturnCorrectValue()
    {
        // Arrange
        var cache = new TestPropertyCache();
        var testObject = new TestClass { Player = "ExpectedValue" };
        var prop = cache.GetPlayerProperty(testObject.GetType());

        // Act
        var value = prop!.GetValue(testObject);

        // Assert
        value.Should().Be("ExpectedValue");
    }

    [Fact]
    public void PropertyCache_ShouldBeThreadSafe()
    {
        // Arrange
        var cache = new TestPropertyCache();
        var types = new[] { typeof(TestClass), typeof(AnotherTestClass) };

        // Act - Concurrent access
        Parallel.For(0, 1000, i =>
        {
            var type = types[i % 2];
            var prop = cache.GetPlayerProperty(type);
            prop.Should().NotBeNull();
        });

        // Assert - Cache should still work correctly
        cache.CacheSize.Should().Be(2);
    }

    [Fact]
    public void CacheMiss_ShouldOnlyCallReflectionOnce()
    {
        // Arrange
        var cache = new CountingPropertyCache();
        var type = typeof(TestClass);

        // Act
        cache.GetPlayerProperty(type);
        cache.GetPlayerProperty(type);
        cache.GetPlayerProperty(type);

        // Assert
        cache.ReflectionCallCount.Should().Be(1, "Reflection should only be called on cache miss");
    }

    /// <summary>
    /// Test implementation of reflection cache pattern.
    /// Mirrors the LootCorpseLogic's cached PropertyInfo lookup.
    /// </summary>
    private class TestPropertyCache
    {
        private readonly Dictionary<Type, PropertyInfo?> _cache = new();
        private readonly object _lock = new();

        public int CacheSize => _cache.Count;

        public PropertyInfo? GetPlayerProperty(Type type)
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(type, out var prop))
                {
                    prop = type.GetProperty("Player");
                    _cache[type] = prop;
                }
                return prop;
            }
        }
    }

    private class CountingPropertyCache
    {
        private readonly Dictionary<Type, PropertyInfo?> _cache = new();
        private readonly object _lock = new();
        private int _reflectionCallCount;

        public int ReflectionCallCount => _reflectionCallCount;

        public PropertyInfo? GetPlayerProperty(Type type)
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(type, out var prop))
                {
                    Interlocked.Increment(ref _reflectionCallCount);
                    prop = type.GetProperty("Player");
                    _cache[type] = prop;
                }
                return prop;
            }
        }
    }

    private class TestClass
    {
        public string? Player { get; set; }
    }

    private class AnotherTestClass
    {
        public string? Player { get; set; }
    }

    private class ClassWithoutPlayer
    {
        public string? Name { get; set; }
    }
}
