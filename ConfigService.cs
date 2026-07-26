using System.Text.Json;
using Microsoft.Win32;

namespace DiscordActivity;

internal sealed class ConfigService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DiscordActivity";
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string ConfigDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DiscordActivity");

    public string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions) ?? new AppConfig();
                if (!json.Contains("\"SchemaVersion\"", StringComparison.Ordinal))
                    ApplyV2Migration(config);
                return config;
            }
        }
        catch (Exception)
        {
            // A malformed config should not prevent the settings window from opening.
        }

        return new AppConfig();
    }

    private static void ApplyV2Migration(AppConfig config)
    {
        var knownBackgroundGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FlightSimulator", "aces-min-cpu", "Rust", "FallGuys_client_game", "javaw",
            "Minecraft.Windows", "client", "client_tc", "SoTGame", "SeaOfThieves"
        };
        foreach (var mapping in config.Mappings)
        {
            mapping.TrackStatistics = true;
            if (knownBackgroundGames.Contains(Path.GetFileNameWithoutExtension(mapping.ProcessName)))
                mapping.ContinueWhenUnfocused = true;
        }
        config.SchemaVersion = 2;
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, _jsonOptions));
        SetStartup(config.StartWithWindows);
    }

    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null) return;

        if (enabled)
        {
            key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\" --minimized");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}
