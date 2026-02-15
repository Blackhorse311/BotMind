using Microsoft.Extensions.Logging;

namespace Blackhorse311.BotMind.Tests.TestHelpers;

/// <summary>
/// Simple logger for testing that captures log messages.
/// </summary>
public class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _logEntries = new();

    public IReadOnlyList<LogEntry> LogEntries => _logEntries;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _logEntries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    public void Clear() => _logEntries.Clear();

    public bool HasLogLevel(LogLevel level) => _logEntries.Any(e => e.Level == level);

    public bool HasMessage(string substring) =>
        _logEntries.Any(e => e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));
}

public record LogEntry(LogLevel Level, string Message, Exception? Exception);
