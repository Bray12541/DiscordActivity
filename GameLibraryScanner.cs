using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace DiscordActivity;

internal sealed class GameLibraryScanner
{
    private static readonly string[] ExcludedExecutableParts =
    [
        "crash", "unins", "setup", "install", "report", "redist", "easyanticheat",
        "battleye", "bootstrap", "helper", "webhelper", "cefprocess", "launcherprereq",
        "unitycrashhandler", "vcredist", "dxsetup", "dotnet"
    ];

    public List<AppMapping> Scan()
    {
        var results = new List<AppMapping>();
        ScanSteam(results);
        ScanEpic(results);
        ScanXbox(results);
        ScanEa(results);
        ScanRegisteredGames(results);

        return results
            .Where(m => m.IsValid)
            .GroupBy(m => $"{m.ProcessName}|{m.WindowTitleContains}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.DisplayName)
            .ToList();
    }

    public static int MergeInto(ICollection<AppMapping> target, IEnumerable<AppMapping> discovered)
    {
        var added = 0;
        foreach (var item in discovered)
        {
            var existing = target.FirstOrDefault(m =>
                string.Equals(Path.GetFileNameWithoutExtension(m.ProcessName), item.ProcessName,
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(m.WindowTitleContains, item.WindowTitleContains,
                    StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                target.Add(item);
                added++;
            }
            else
            {
                existing.ExecutablePath = item.ExecutablePath;
                if (string.IsNullOrWhiteSpace(existing.ArtworkUrl))
                    existing.ArtworkUrl = item.ArtworkUrl;
                if (string.IsNullOrWhiteSpace(existing.LargeImageText))
                    existing.LargeImageText = item.LargeImageText;
                if (string.IsNullOrWhiteSpace(existing.Source) || existing.Source == "Manual")
                    existing.Source = item.Source;
                if (item.ContinueWhenUnfocused)
                    existing.ContinueWhenUnfocused = true;
            }

            foreach (var sameGame in target.Where(m =>
                         string.Equals(CleanName(m.DisplayName), CleanName(item.DisplayName),
                             StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(sameGame.ArtworkUrl))
                    sameGame.ArtworkUrl = item.ArtworkUrl;
                if (string.IsNullOrWhiteSpace(sameGame.LargeImageText))
                    sameGame.LargeImageText = item.LargeImageText;
            }
        }
        return added;
    }

    public static int RemoveMissingAutoMappings(ICollection<AppMapping> target)
    {
        var removable = target.Where(mapping =>
                IsAutomaticSource(mapping.Source)
                && !string.IsNullOrWhiteSpace(mapping.ExecutablePath)
                && !File.Exists(mapping.ExecutablePath))
            .ToList();
        foreach (var mapping in removable) target.Remove(mapping);
        return removable.Count;
    }

    private static bool IsAutomaticSource(string source) =>
        source is "Steam" or "Epic Games" or "Xbox" or "EA" or "Installed programs";

    private static void ScanSteam(ICollection<AppMapping> results)
    {
        foreach (var library in FindSteamLibraries())
        {
            var steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;

            foreach (var manifest in SafeFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
            {
                var text = SafeRead(manifest);
                var name = MatchVdf(text, "name");
                var installDir = MatchVdf(text, "installdir");
                var appId = Path.GetFileNameWithoutExtension(manifest).Replace("appmanifest_", "");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir)
                    || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Driver Booster", StringComparison.OrdinalIgnoreCase))
                    continue;

                var gamePath = Path.Combine(steamApps, "common", installDir);
                var executable = SelectGameExecutable(gamePath, installDir);
                if (executable is null) continue;

                results.Add(CreateMapping(name, executable, "Steam",
                    $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg"));
            }
        }
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamPath = (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")
            ?.GetValue("SteamPath") as string)?.Replace('/', '\\');
        if (!string.IsNullOrWhiteSpace(steamPath)) roots.Add(steamPath);
        roots.Add(@"C:\Program Files (x86)\Steam");

        foreach (var root in roots.ToList())
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            var text = SafeRead(vdf);
            foreach (Match match in Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"",
                         RegexOptions.IgnoreCase))
                roots.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return roots.Where(Directory.Exists);
    }

    private static void ScanEpic(ICollection<AppMapping> results)
    {
        const string manifestDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        foreach (var manifest in SafeFiles(manifestDirectory, "*.item", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                var name = GetString(root, "DisplayName");
                var location = GetString(root, "InstallLocation");
                var launchExecutable = GetString(root, "LaunchExecutable");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location)
                    || !Directory.Exists(location)) continue;

                var executable = SelectGameExecutable(location, name);
                if (executable is null && !string.IsNullOrWhiteSpace(launchExecutable))
                {
                    var candidate = Path.Combine(location, launchExecutable.Replace('/', '\\'));
                    if (File.Exists(candidate)) executable = candidate;
                }
                if (executable is not null)
                    results.Add(CreateMapping(name, executable, "Epic Games"));
            }
            catch
            {
                // Ignore incomplete or changing launcher manifests.
            }
        }
    }

    private static void ScanXbox(ICollection<AppMapping> results)
    {
        foreach (var root in DriveInfo.GetDrives()
                     .Where(d => d.IsReady)
                     .Select(d => Path.Combine(d.RootDirectory.FullName, "XboxGames"))
                     .Where(Directory.Exists))
        {
            foreach (var configPath in SafeFiles(root, "MicrosoftGame.Config", SearchOption.AllDirectories))
            {
                try
                {
                    var document = XDocument.Load(configPath);
                    var executableName = document.Descendants()
                        .Attributes("Executable")
                        .Select(a => a.Value)
                        .FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(executableName)) continue;
                    var basePath = Path.GetDirectoryName(configPath)!;
                    var executable = Path.Combine(basePath, executableName.Replace('/', '\\'));
                    if (!File.Exists(executable)) continue;
                    var name = new DirectoryInfo(basePath).Parent?.Name ?? Path.GetFileNameWithoutExtension(executable);
                    results.Add(CreateMapping(name, executable, "Xbox"));
                }
                catch
                {
                    // Ignore inaccessible package metadata.
                }
            }
        }
    }

    private static void ScanEa(ICollection<AppMapping> results)
    {
        foreach (var root in new[] { @"C:\Program Files\EA Games", @"C:\Program Files (x86)\Origin Games" })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var gameDirectory in SafeDirectories(root))
            {
                var executable = SelectGameExecutable(gameDirectory, Path.GetFileName(gameDirectory));
                if (executable is not null)
                    results.Add(CreateMapping(Path.GetFileName(gameDirectory), executable, "EA"));
            }
        }
    }

    private static void ScanRegisteredGames(ICollection<AppMapping> results)
    {
        var registryPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };
        foreach (var path in registryPaths)
        {
            using var root = Registry.LocalMachine.OpenSubKey(path);
            if (root is null) continue;
            foreach (var subkeyName in root.GetSubKeyNames())
            {
                using var key = root.OpenSubKey(subkeyName);
                var publisher = key?.GetValue("Publisher") as string ?? "";
                if (!IsGamePublisher(publisher)) continue;
                var name = key?.GetValue("DisplayName") as string ?? "";
                var normalizedName = Normalize(name);
                if (normalizedName.Length > 5 && results.Any(existing =>
                    Normalize(existing.DisplayName).Contains(normalizedName)
                    || normalizedName.Contains(Normalize(existing.DisplayName))))
                    continue;
                var installLocation = key?.GetValue("InstallLocation") as string ?? "";
                var displayIcon = (key?.GetValue("DisplayIcon") as string ?? "").Trim('"').Split(',')[0];
                if (name.Contains("Launcher", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("SDK", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("EA app", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Ubisoft Connect", StringComparison.OrdinalIgnoreCase))
                    continue;
                string? executable = File.Exists(displayIcon)
                                     && string.Equals(Path.GetExtension(displayIcon), ".exe",
                                         StringComparison.OrdinalIgnoreCase)
                    ? displayIcon
                    : null;
                if (executable is null && Directory.Exists(installLocation))
                    executable = SelectGameExecutable(installLocation, name);
                if (executable is not null)
                    results.Add(CreateMapping(name, executable, "Installed programs"));
            }
        }
    }

    private static bool IsGamePublisher(string publisher) =>
        Regex.IsMatch(publisher,
            "Electronic Arts|Ubisoft|Battlestate|Rockstar|Mojang|Microsoft Game Studios|Gaijin|Blizzard",
            RegexOptions.IgnoreCase);

    private static AppMapping CreateMapping(string name, string executable, string source, string artworkUrl = "") =>
        new()
        {
            ProcessName = Path.GetFileNameWithoutExtension(executable),
            DisplayName = CleanName(name),
            Details = DefaultDetails(name),
            ExecutablePath = executable,
            Source = source,
            ArtworkUrl = artworkUrl,
            LargeImageText = CleanName(name),
            ContinueWhenUnfocused = true,
            TrackStatistics = true
        };

    private static string? SelectGameExecutable(string directory, string gameName)
    {
        if (!Directory.Exists(directory)) return null;
        var normalizedGame = Normalize(gameName);
        return SafeFiles(directory, "*.exe", SearchOption.AllDirectories)
            .Where(path => !ExcludedExecutableParts.Any(part =>
                path.Contains(part, StringComparison.OrdinalIgnoreCase)))
            .Select(path => new
            {
                Path = path,
                Score = ScoreExecutable(path, directory, normalizedGame)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => SafeLength(x.Path))
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private static int ScoreExecutable(string path, string root, string normalizedGame)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var normalizedExe = Normalize(name);
        var score = 0;
        if (normalizedExe == normalizedGame) score += 160;
        else if (normalizedExe.Contains(normalizedGame) || normalizedGame.Contains(normalizedExe)) score += 100;
        if (name.Contains("client", StringComparison.OrdinalIgnoreCase)) score += 70;
        if (name.Contains("shipping", StringComparison.OrdinalIgnoreCase)) score += 35;
        if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase)
            || name.Contains("server", StringComparison.OrdinalIgnoreCase)
            || name.Contains("editor", StringComparison.OrdinalIgnoreCase)) score -= 60;
        var relative = Path.GetRelativePath(root, path);
        score -= relative.Count(c => c is '\\' or '/') * 8;
        if (SafeLength(path) > 5 * 1024 * 1024) score += 10;
        return score;
    }

    private static IEnumerable<string> SafeFiles(string directory, string pattern, SearchOption option)
    {
        try { return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern, option).ToList() : []; }
        catch { return []; }
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try { return Directory.EnumerateDirectories(directory).ToList(); }
        catch { return []; }
    }

    private static string SafeRead(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
        catch { return ""; }
    }

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string MatchVdf(string text, string key) =>
        Regex.Match(text, $"\"{Regex.Escape(key)}\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)
            .Groups[1].Value;

    private static string GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");

    private static string CleanName(string value) =>
        value.Replace("®", "").Replace("™", "").Trim();

    private static string DefaultDetails(string name) => $"Playing {CleanName(name)}";
}
