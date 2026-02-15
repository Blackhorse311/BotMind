using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for MedicBuddyController state machine logic.
/// Tests state transitions and validation without Unity dependencies.
/// </summary>
public class MedicBuddyStateTests
{
    public enum MedicBuddyState
    {
        Idle,
        Spawning,
        MovingToPlayer,
        Defending,
        Healing,
        Retreating,
        Despawning
    }

    [Fact]
    public void InitialState_ShouldBeIdle()
    {
        // Arrange
        var stateMachine = new TestStateMachine();

        // Assert
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Idle);
    }

    [Fact]
    public void TryTransitionState_FromIdle_ToSpawning_ShouldSucceed()
    {
        // Arrange
        var stateMachine = new TestStateMachine();

        // Act
        var result = stateMachine.TryTransition(MedicBuddyState.Idle, MedicBuddyState.Spawning);

        // Assert
        result.Should().BeTrue();
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Spawning);
    }

    [Fact]
    public void TryTransitionState_WithWrongExpectedState_ShouldFail()
    {
        // Arrange
        var stateMachine = new TestStateMachine();

        // Act - Try to transition from Spawning when actually Idle
        var result = stateMachine.TryTransition(MedicBuddyState.Spawning, MedicBuddyState.MovingToPlayer);

        // Assert
        result.Should().BeFalse();
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Idle);
    }

    [Theory]
    [InlineData(MedicBuddyState.Idle, MedicBuddyState.Spawning)]
    [InlineData(MedicBuddyState.Spawning, MedicBuddyState.MovingToPlayer)]
    [InlineData(MedicBuddyState.MovingToPlayer, MedicBuddyState.Defending)]
    [InlineData(MedicBuddyState.Defending, MedicBuddyState.Healing)]
    [InlineData(MedicBuddyState.Healing, MedicBuddyState.Retreating)]
    [InlineData(MedicBuddyState.Retreating, MedicBuddyState.Despawning)]
    [InlineData(MedicBuddyState.Despawning, MedicBuddyState.Idle)]
    public void ValidStateTransitions_ShouldSucceed(MedicBuddyState from, MedicBuddyState to)
    {
        // Arrange
        var stateMachine = new TestStateMachine();
        stateMachine.SetState(from);

        // Act
        var result = stateMachine.TryTransition(from, to);

        // Assert
        result.Should().BeTrue();
        stateMachine.CurrentState.Should().Be(to);
    }

    [Fact]
    public void SetState_ShouldDirectlyChangeState()
    {
        // Arrange
        var stateMachine = new TestStateMachine();

        // Act
        stateMachine.SetState(MedicBuddyState.Healing);

        // Assert
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Healing);
    }

    [Fact]
    public void StateMachine_ShouldBeThreadSafe()
    {
        // Arrange
        var stateMachine = new TestStateMachine();
        var successCount = 0;
        var failCount = 0;

        // Act - Multiple threads try to transition from Idle
        Parallel.For(0, 100, _ =>
        {
            if (stateMachine.TryTransition(MedicBuddyState.Idle, MedicBuddyState.Spawning))
            {
                Interlocked.Increment(ref successCount);
            }
            else
            {
                Interlocked.Increment(ref failCount);
            }
        });

        // Assert - Only one should succeed
        successCount.Should().Be(1, "Only one thread should succeed in the race condition");
        failCount.Should().Be(99);
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Spawning);
    }

    [Fact]
    public void AbortMission_ShouldTransitionToRetreating()
    {
        // Arrange
        var stateMachine = new TestStateMachine();
        stateMachine.SetState(MedicBuddyState.Defending);

        // Act - Simulate medic death causing abort
        stateMachine.SetState(MedicBuddyState.Retreating);

        // Assert
        stateMachine.CurrentState.Should().Be(MedicBuddyState.Retreating);
    }

    /// <summary>
    /// Test implementation of thread-safe state machine.
    /// Mirrors the MedicBuddyController's state management logic.
    /// </summary>
    private class TestStateMachine
    {
        private MedicBuddyState _state = MedicBuddyState.Idle;
        private readonly object _lock = new();

        public MedicBuddyState CurrentState
        {
            get
            {
                lock (_lock)
                {
                    return _state;
                }
            }
        }

        public bool TryTransition(MedicBuddyState expected, MedicBuddyState newState)
        {
            lock (_lock)
            {
                if (_state != expected)
                    return false;
                _state = newState;
                return true;
            }
        }

        public void SetState(MedicBuddyState newState)
        {
            lock (_lock)
            {
                _state = newState;
            }
        }
    }
}
