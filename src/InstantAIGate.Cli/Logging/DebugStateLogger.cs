// InstantAIGate.Cli/Logging/DebugStateLogger.cs
using InstantAIGate.Cli.State;
using Microsoft.Extensions.Logging;

namespace InstantAIGate.Cli.Logging;

/// <summary>
/// Custom logger that only outputs to console when debug mode is enabled.
/// </summary>
public class DebugStateLogger : ILogger
{
    private readonly DebugState _debugState;
    private readonly string _categoryName;

    public DebugStateLogger(DebugState debugState, string categoryName)
    {
        _debugState = debugState;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        // Suppress all logs if debug mode is off
        return _debugState.IsEnabled;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        string message = formatter(state, exception);

        // Log level determines the console color of the message
        var color = logLevel switch
        {
            LogLevel.Error or LogLevel.Critical => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            _ => ConsoleColor.DarkGray
        };

        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = color;

        // Print the log
        Console.WriteLine($"[{logLevel}] {_categoryName}: {message}");

        if (exception != null)
        {
            Console.WriteLine(exception.ToString());
        }

        Console.ForegroundColor = previousColor;
    }
}