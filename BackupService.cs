using System.Text.Json;

namespace DiscordActivity;

internal static class BackupService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Export(string path, AppConfig config, StatisticsFile statistics)
    {
        var bundle = new BackupBundle
        {
            Config = config,
            Statistics = statistics
        };
        File.WriteAllText(path, JsonSerializer.Serialize(bundle, Options));
    }

    public static BackupBundle Import(string path)
    {
        var bundle = JsonSerializer.Deserialize<BackupBundle>(File.ReadAllText(path), Options)
                     ?? throw new InvalidDataException("The backup file is empty.");
        if (bundle.FormatVersion != 1)
            throw new InvalidDataException($"Unsupported backup format version {bundle.FormatVersion}.");
        return bundle;
    }
}
