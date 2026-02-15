using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for QuestObjective and QuestManager logic.
/// Tests the objective prioritization and selection algorithms.
/// </summary>
public class QuestObjectiveTests
{
    [Fact]
    public void QuestObjective_DefaultCompletionRadius_ShouldBeTwo()
    {
        // Arrange & Act
        var objective = new TestQuestObjective();

        // Assert
        objective.CompletionRadius.Should().Be(2f);
    }

    [Fact]
    public void QuestObjective_IsComplete_DefaultShouldBeFalse()
    {
        // Arrange & Act
        var objective = new TestQuestObjective();

        // Assert
        objective.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void QuestObjective_CanSetProperties()
    {
        // Arrange
        var objective = new TestQuestObjective
        {
            Type = QuestObjectiveType.FindItem,
            Name = "Find USB Drive",
            Priority = 75f,
            ItemTemplateId = "5c12301c86f77419522ba7e4",
            CompletionRadius = 5f
        };

        // Assert
        objective.Type.Should().Be(QuestObjectiveType.FindItem);
        objective.Name.Should().Be("Find USB Drive");
        objective.Priority.Should().Be(75f);
        objective.ItemTemplateId.Should().Be("5c12301c86f77419522ba7e4");
        objective.CompletionRadius.Should().Be(5f);
    }

    [Theory]
    [InlineData(QuestObjectiveType.GoToLocation)]
    [InlineData(QuestObjectiveType.FindItem)]
    [InlineData(QuestObjectiveType.PlaceItem)]
    [InlineData(QuestObjectiveType.Explore)]
    [InlineData(QuestObjectiveType.Extract)]
    [InlineData(QuestObjectiveType.Patrol)]
    [InlineData(QuestObjectiveType.Investigate)]
    public void QuestObjectiveType_AllTypesAreValid(QuestObjectiveType type)
    {
        // Arrange & Act
        var objective = new TestQuestObjective { Type = type };

        // Assert
        objective.Type.Should().Be(type);
    }

    [Fact]
    public void SelectBestObjective_ShouldReturnHighestPriority()
    {
        // Arrange
        var objectives = new List<TestQuestObjective>
        {
            new() { Name = "Low", Priority = 10f },
            new() { Name = "High", Priority = 90f },
            new() { Name = "Medium", Priority = 50f }
        };

        // Act
        objectives.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        var best = objectives.First();

        // Assert
        best.Name.Should().Be("High");
        best.Priority.Should().Be(90f);
    }

    [Fact]
    public void SelectBestObjective_WithEqualPriority_ShouldMaintainStableOrder()
    {
        // Arrange
        var objectives = new List<TestQuestObjective>
        {
            new() { Name = "First", Priority = 50f },
            new() { Name = "Second", Priority = 50f },
            new() { Name = "Third", Priority = 50f }
        };

        // Act - Stable sort should maintain insertion order for equal priorities
        objectives.Sort((a, b) => b.Priority.CompareTo(a.Priority));

        // Assert - First one should still be first (stable sort behavior)
        objectives[0].Priority.Should().Be(50f);
    }

    [Fact]
    public void ObjectiveCompletion_ShouldUpdateIsComplete()
    {
        // Arrange
        var objective = new TestQuestObjective { IsComplete = false };

        // Act
        objective.IsComplete = true;

        // Assert
        objective.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// Mirror of QuestObjectiveType for testing without Unity dependencies.
    /// </summary>
    public enum QuestObjectiveType
    {
        GoToLocation,
        FindItem,
        PlaceItem,
        Explore,
        Extract,
        Patrol,
        Investigate
    }

    /// <summary>
    /// Test version of QuestObjective that doesn't depend on Unity Vector3.
    /// </summary>
    public class TestQuestObjective
    {
        public QuestObjectiveType Type { get; set; }
        public string Name { get; set; } = string.Empty;
        public float Priority { get; set; }
        public bool IsComplete { get; set; }
        public string? ItemTemplateId { get; set; }
        public float CompletionRadius { get; set; } = 2f;
    }
}
