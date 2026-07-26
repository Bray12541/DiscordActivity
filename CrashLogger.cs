using System.Text;

namespace DiscordActivity;

internal static class CrashLogger
{
    private static readonly object Sync = new();
    private static string _logDirectory = "";
    private static string _crashMarkerPath = "";

    public static string LogDirectory => _logDirectory;

    public static void Initialize(string dataDirectory)
    {
        _logDirectory = Path.Combine(dataDirectory, "Logs");
        _crashMarkerPath = Path.Combine(dataDirectory, "last-crash.txt");
        Directory.CreateDirectory(_logDirectory);
        PruneOldLogs();
        LogInfo($"Discord Activity {Application.ProductVersion} starting on " +
                $"{Environment.OSVersion} ({Environment.Version})");
    }

    public static void LogInfo(string message) => Write("INFO", message, null);
    public static void LogWarning(string message, Exception? exception = null) =>
        Write("WARN", message, exception);
    public static void LogError(string message, Exception exception) =>
        Write("ERROR", message, exception);

    public static void RecordCrash(string context, Exception exception)
    {
        Write("FATAL", context, exception);
        try
        {
            var marker = $"""
                          Discord Activity ended unexpectedly.
                          Time: {DateTimeOffset.Now:O}
                          Context: {context}
                          Error: {exception.GetType().Name}: {exception.Message}
                          Log: {CurrentLogPath()}
                          """;
            File.WriteAllText(_crashMarkerPath, marker);
        }
        catch
        {
            // Crash handling must never throw another exception.
        }
    }

    public static string? ConsumePreviousCrash()
    {
        try
        {
            if (!File.Exists(_crashMarkerPath)) return null;
            var message = File.ReadAllText(_crashMarkerPath);
            File.Delete(_crashMarkerPath);
            return message;
        }
        catch
        {
            return "The previous session ended unexpectedly. Open the Logs folder for details.";
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        if (string.IsNullOrWhiteSpace(_logDirectory)) return;
        try
        {
            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" [").Append(level).Append("] ")
                .AppendLine(message);
            if (exception is not null) builder.AppendLine(exception.ToString());
            lock (Sync)
                File.AppendAllText(CurrentLogPath(), builder.ToString());
        }
        catch
        {
            // Logging failures are intentionally non-fatal.
        }
    }

    private static string CurrentLogPath() =>
        Path.Combine(_logDirectory, $"discord-activity-{DateTime.Now:yyyy-MM-dd}.log");

    private static void PruneOldLogs()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "discord-activity-*.log")
                         .Select(path => new FileInfo(path))
                         .Where(file => file.LastWriteTime < cutoff))
                file.Delete();
        }
        catch
        {
            // Retention cleanup is best effort.
        }
    }
}
