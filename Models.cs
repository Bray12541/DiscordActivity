using System.Text.Json.Serialization;

namespace DiscordActivity;

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 2;
    public string ClientId { get; set; } = "";
    public int PollIntervalSeconds { get; set; } = 3;
    public bool StartWithWindows { get; set; }
    public bool ShowUnmappedApplications { get; set; }
    public bool AutoRescanLibraries { get; set; } = true;
    public bool RemoveUninstalledGames { get; set; } = true;
    public bool IdleDetectionEnabled { get; set; } = true;
    public int IdleTimeoutMinutes { get; set; } = 10;
    public bool DarkMode { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public string GitHubRepository { get; set; } = "";
    public string UpdateManifestUrl { get; set; } = "";
    public List<string> ExcludedProcesses { get; set; } =
    [
        "DiscordActivity", "Discord", "DiscordCanary", "chrome", "msedge",
        "firefox", "1Password", "KeePass", "KeePassXC", "explorer"
    ];
    public List<AppMapping> Mappings { get; set; } =
    [
        new() { ProcessName = "LegendsOfIdleon", DisplayName = "Legends of Idleon MMO", Details = "Grinding and collecting loot" },
        new() { ProcessName = "fsx", DisplayName = "Microsoft Flight Simulator X", Details = "Taking to the skies" },
        new() { ProcessName = "FlightSimulator", DisplayName = "Microsoft Flight Simulator", Details = "Taking to the skies" },
        new() { ProcessName = "Sand", DisplayName = "SAND", Details = "Exploring the rusted wastes" },
        new() { ProcessName = "Overwatch", DisplayName = "Overwatch 2", Details = "Fighting for the objective" },
        new() { ProcessName = "aces", DisplayName = "War Thunder", Details = "Heading into battle" },
        new() { ProcessName = "aces-min-cpu", DisplayName = "War Thunder", Details = "Heading into battle" },
        new() { ProcessName = "RustClient", DisplayName = "Rust", Details = "Trying to survive" },
        new() { ProcessName = "Rust", DisplayName = "Rust", Details = "Trying to survive" },
        new() { ProcessName = "GeoGuessr", DisplayName = "GeoGuessr", Details = "Guessing where in the world" },
        new() { ProcessName = "FishingPlanet", DisplayName = "Fishing Planet", Details = "Casting a line" },
        new() { ProcessName = "VRChat", DisplayName = "VRChat", Details = "Exploring virtual worlds" },
        new() { ProcessName = "left4dead", DisplayName = "Left 4 Dead", Details = "Surviving the outbreak" },
        new() { ProcessName = "left4dead2", DisplayName = "Left 4 Dead 2", Details = "Surviving the outbreak" },
        new() { ProcessName = "SR2_pc", DisplayName = "Saints Row 2", Details = "Running the streets of Stilwater" },
        new() { ProcessName = "FallGuys_client_game", DisplayName = "Fall Guys", Details = "Racing for the crown" },
        new() { ProcessName = "FallGuys_client", DisplayName = "Fall Guys", Details = "Racing for the crown" },
        new() { ProcessName = "GTA5", DisplayName = "Grand Theft Auto V", Details = "Causing trouble in Los Santos" },
        new() { ProcessName = "FortniteClient-Win64-Shipping", DisplayName = "Fortnite", Details = "Going for a Victory Royale" },
        new() { ProcessName = "javaw", WindowTitleContains = "Minecraft", DisplayName = "Minecraft: Java Edition", Details = "Exploring the world" },
        new() { ProcessName = "Minecraft.Windows", DisplayName = "Minecraft for Windows", Details = "Exploring the world" },
        new() { ProcessName = "PlantsVsZombies", DisplayName = "Plants vs. Zombies", Details = "Defending the lawn" },
        new() { ProcessName = "client", WindowTitleContains = "Ultima", DisplayName = "Ultima Online", Details = "Adventuring in Britannia" },
        new() { ProcessName = "client_tc", WindowTitleContains = "Ultima", DisplayName = "Ultima Online", Details = "Adventuring in Britannia" },
        new() { ProcessName = "SoTGame", DisplayName = "Sea of Thieves", Details = "Sailing the high seas" },
        new() { ProcessName = "SeaOfThieves", DisplayName = "Sea of Thieves", Details = "Sailing the high seas" },
        new() { ProcessName = "Code", DisplayName = "Visual Studio Code", Details = "Writing code" },
        new() { ProcessName = "devenv", DisplayName = "Visual Studio", Details = "Writing code" },
        new() { ProcessName = "Photoshop", DisplayName = "Adobe Photoshop", Details = "Creating artwork" },
        new() { ProcessName = "blender", DisplayName = "Blender", Details = "Creating in 3D" },
        new() { ProcessName = "Unity", DisplayName = "Unity", Details = "Building a game" },
        new() { ProcessName = "UnrealEditor", DisplayName = "Unreal Engine", Details = "Building a game" },
        new() { ProcessName = "steam", DisplayName = "Steam", Details = "Browsing games" },
        new() { ProcessName = "Spotify", DisplayName = "Spotify", Details = "Listening to music" }
    ];
}

public sealed class AppMapping
{
    public string ProcessName { get; set; } = "";
    public string WindowTitleContains { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Details { get; set; } = "";
    public string State { get; set; } = "";
    public string LargeImageKey { get; set; } = "";
    public string LargeImageText { get; set; } = "";
    public string ButtonLabel { get; set; } = "";
    public string ButtonUrl { get; set; } = "";
    public string ArtworkUrl { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public bool ContinueWhenUnfocused { get; set; }
    public bool TrackStatistics { get; set; } = true;

    [JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(ProcessName);
}

public sealed record DetectedActivity(string ProcessName, AppMapping Mapping);

public sealed record ForegroundApp(string ProcessName, string WindowTitle, string ExecutablePath, string DisplayName);

public sealed class GameStatistic
{
    public string Key { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long TotalSeconds { get; set; }
    public long RunningSeconds { get; set; }
    public int Sessions { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }
}

public sealed class StatisticsFile
{
    public List<GameStatistic> Games { get; set; } = [];
}

public sealed class BackupBundle
{
    public int FormatVersion { get; set; } = 1;
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public AppConfig Config { get; set; } = new();
    public StatisticsFile Statistics { get; set; } = new();
}

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
}

public sealed record UpdateCheckResult(bool UpdateAvailable, Version CurrentVersion,
    Version? LatestVersion, UpdateManifest? Manifest, string Message);
