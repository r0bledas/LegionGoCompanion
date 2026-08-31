using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using System.Diagnostics;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace HandheldCompanion.Shared;

public static class LogManager
{
    private static ILogger logger;

    // Trace.Trace* calls String.Format internally, so any literal '{' or '}' in a
    // plain message string (e.g. an exception message) causes a FormatException when
    // there are no format arguments.  Escape the braces before forwarding in that case.
    private static string EscapeForTrace(string message)
        => message.Replace("{", "{{").Replace("}", "}}");

    private static string TraceMessage(string message, object[] args)
        => args.Length == 0 ? EscapeForTrace(message) : message;

    public static void Initialize(string name)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile($"{name}.json")
            .Build();

        var serilogLogger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        logger = new SerilogLoggerFactory(serilogLogger).CreateLogger(name);

#if DEBUG
        // Redirect standard input, output, and error to the new console
        StreamWriter standardOutput = new StreamWriter(Console.OpenStandardOutput())
        {
            AutoFlush = true
        };
        Console.SetOut(standardOutput);

        StreamReader standardInput = new StreamReader(Console.OpenStandardInput());
        Console.SetIn(standardInput);
#endif
    }

    public static void LogInformation(string message, params object[] args)
    {
        Trace.TraceInformation(TraceMessage(message, args), args);
        logger.LogInformation(message, args);
    }

    public static void LogWarning(string message, params object[] args)
    {
        Trace.TraceWarning(TraceMessage(message, args), args);
        logger.LogWarning(message, args);
    }

    public static void LogCritical(string message, params object[] args)
    {
        Trace.TraceError(TraceMessage(message, args), args);
        logger.LogCritical(message, args);
    }

    public static void LogDebug(string message, params object[] args)
    {
        Trace.TraceInformation(TraceMessage(message, args), args);
        logger.LogDebug(message, args);
    }

    public static void LogError(string message, params object[] args)
    {
        Trace.TraceError(TraceMessage(message, args), args);
        logger.LogError(message, args);
    }

    public static void LogTrace(string message, params object[] args)
    {
        // Trace.TraceInformation(TraceMessage(message, args), args);
        logger.LogTrace(message, args);
    }
}