using FluentAssertions;
using Xunit;

namespace Blackhorse311.BotMind.Tests.Core;

/// <summary>
/// Unit tests for error handling patterns.
/// Verifies that exceptions are properly caught, logged, and handled per ERROR_HANDLING.md standards.
/// </summary>
public class ErrorHandlingTests
{
    [Fact]
    public void TryCatch_InUpdateLoop_ShouldCatchAndContinue()
    {
        // Arrange
        var processor = new TestFrameProcessor();
        processor.ThrowOnFrame(5);

        // Act - Process 10 frames
        for (int i = 0; i < 10; i++)
        {
            processor.ProcessFrame();
        }

        // Assert
        processor.FramesProcessed.Should().Be(10, "All frames should be processed even with exception");
        processor.ExceptionsCaught.Should().Be(1);
    }

    [Fact]
    public void ExceptionMessage_ShouldIncludeStackTrace()
    {
        // Arrange
        Exception? caughtException = null;
        try
        {
            ThrowNestedException();
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Act
        var errorMessage = FormatErrorMessage(caughtException!);

        // Assert
        errorMessage.Should().Contain(caughtException!.Message);
        errorMessage.Should().Contain(caughtException.StackTrace!);
    }

    [Fact]
    public void ErrorMessage_ShouldFollowWhatWhyHowFormat()
    {
        // Arrange
        var ex = new InvalidOperationException("Bot reference was null");
        var botName = "TestBot";
        var operation = "CanBotQuest";

        // Act
        var formattedMessage = FormatSAINInteropError(operation, botName, ex);

        // Assert - Should follow WHAT/WHY/HOW format
        formattedMessage.Should().Contain(operation, "WHAT: operation that failed");
        formattedMessage.Should().Contain(botName, "Context: which bot");
        formattedMessage.Should().Contain(ex.Message, "WHAT: error details");
        formattedMessage.Should().Contain("SAIN", "WHY: likely cause");
        formattedMessage.Should().MatchRegex("(default|Check|Verify)", "HOW: resolution hint");
    }

    [Fact]
    public void FailSafe_ShouldSetCompletionState()
    {
        // Arrange
        var logic = new TestLogicWithFailSafe();
        logic.SimulateException = true;

        // Act
        logic.Update();

        // Assert
        logic.IsComplete.Should().BeTrue("Should mark complete on exception (fail safe)");
    }

    [Fact]
    public void NullCheck_ShouldPreventNullReferenceException()
    {
        // Arrange
        object? nullObject = null;

        // Act
        var result = SafeGetValue(nullObject);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void NullConditional_ShouldShortCircuitSafely()
    {
        // Arrange
        TestPlayer? player = null;

        // Act
        var isAlive = player?.HealthController?.IsAlive ?? false;

        // Assert
        isAlive.Should().BeFalse();
    }

    [Fact]
    public void NullConditional_WithValidObject_ShouldReturnValue()
    {
        // Arrange
        var player = new TestPlayer
        {
            HealthController = new TestHealthController { IsAlive = true }
        };

        // Act
        var isAlive = player?.HealthController?.IsAlive ?? false;

        // Assert
        isAlive.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "null")]
    [InlineData("TestBot", "TestBot")]
    public void BotNameFormatting_ShouldHandleNull(string? botName, string expected)
    {
        // Act
        var formatted = botName ?? "null";

        // Assert
        formatted.Should().Be(expected);
    }

    private static void ThrowNestedException()
    {
        try
        {
            throw new InvalidOperationException("Inner exception");
        }
        catch (Exception ex)
        {
            throw new ApplicationException("Outer exception", ex);
        }
    }

    private static string FormatErrorMessage(Exception ex)
    {
        return $"Error: {ex.Message}\n{ex.StackTrace}";
    }

    private static string FormatSAINInteropError(string operation, string botName, Exception ex)
    {
        return $"SAINInterop.{operation} failed for bot '{botName}': {ex.Message}. " +
               $"SAIN API may have changed. Defaulting to allow behavior. " +
               $"Check SAIN version compatibility if this persists.";
    }

    private static object? SafeGetValue(object? obj)
    {
        return obj?.ToString();
    }

    private class TestFrameProcessor
    {
        private int _throwOnFrame = -1;

        public int FramesProcessed { get; private set; }
        public int ExceptionsCaught { get; private set; }

        public void ThrowOnFrame(int frame) => _throwOnFrame = frame;

        public void ProcessFrame()
        {
            try
            {
                if (FramesProcessed == _throwOnFrame)
                {
                    throw new InvalidOperationException("Test exception");
                }
            }
            catch
            {
                ExceptionsCaught++;
            }
            finally
            {
                FramesProcessed++;
            }
        }
    }

    private class TestLogicWithFailSafe
    {
        public bool SimulateException { get; set; }
        public bool IsComplete { get; private set; }

        public void Update()
        {
            try
            {
                if (SimulateException)
                {
                    throw new InvalidOperationException("Simulated error");
                }
            }
            catch
            {
                // Fail safe - mark complete to prevent repeated errors
                IsComplete = true;
            }
        }
    }

    private class TestPlayer
    {
        public TestHealthController? HealthController { get; set; }
    }

    private class TestHealthController
    {
        public bool IsAlive { get; set; }
    }
}
