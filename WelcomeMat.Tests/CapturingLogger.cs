using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace WelcomeMat.Tests;

public class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        this.Entries.Add(new CapturedLogEntry
        {
            Level = logLevel,
            Message = formatter(state, exception),
            Exception = exception,
        });
    }

    public string AllText() =>
        string.Join(
            "\n",
            this.Entries.Select(e => e.Exception is null ? e.Message : $"{e.Message} {e.Exception}"));

    private class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

public record CapturedLogEntry
{
    public required LogLevel Level { get; init; }

    public string Message { get; init; } = string.Empty;

    public Exception? Exception { get; init; }
}
