using Microsoft.Extensions.Logging;

namespace SampleApp.Infrastructure;

/// <summary>Console formatting for the scenarios.</summary>
public static class Output
{
    /// <summary>Gets or sets a value indicating whether EFCatalyst log output is muted (while a database is being reset).</summary>
    public static bool Muted { get; set; }

    public static void Title(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"== {title}");
        Console.ResetColor();
    }

    public static void Step(string text) => Console.WriteLine($"-- {text}");

    public static void Line(string text) => Console.WriteLine($"   {text}");

    public static void Error(Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"   {exception.GetType().Name}: {exception.Message}");
        Console.ResetColor();
    }
}

/// <summary>Prints EFCatalyst's own warnings (slow commands, integrity violations, deadlock retries).</summary>
public sealed class EFCatalystConsoleLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        categoryName.StartsWith("AwadyLab.EFCatalyst", StringComparison.Ordinal) ? new Logger() : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && !Output.Muted;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"   [log {eventId.Id}] {formatter(state, exception).ReplaceLineEndings(" | ")}");
                Console.ResetColor();
            }
        }
    }
}
