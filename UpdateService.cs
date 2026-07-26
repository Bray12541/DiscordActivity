using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DiscordActivity;

internal sealed class UpdateService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _dataDirectory;

    public UpdateService(string dataDirectory)
    {
        _dataDirectory = dataDirectory;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordActivity/2.1");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Task<UpdateCheckResult> CheckAsync(string githubRepository, string manifestUrl,
        CancellationToken cancellationToken = default) =>
        !string.IsNullOrWhiteSpace(githubRepository)
            ? CheckGitHubAsync(githubRepository, cancellationToken)
            : CheckManifestAsync(manifestUrl, cancellationToken);

    public async Task<UpdateCheckResult> CheckManifestAsync(string manifestUrl,
        CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return new UpdateCheckResult(false, current, null, null,
                "Set an HTTPS update-manifest URL before checking for updates.");

        var json = await _http.GetStringAsync(uri, cancellationToken);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidDataException("The update manifest is invalid.");
        if (!Version.TryParse(manifest.Version, out var latest))
            throw new InvalidDataException("The update manifest has an invalid version.");
        var available = latest > current;
        return new UpdateCheckResult(available, current, latest, manifest,
            available ? $"Version {latest} is available." : $"Version {current} is up to date.");
    }

    public async Task<UpdateCheckResult> CheckGitHubAsync(string repository,
        CancellationToken cancellationToken = default)
    {
        var current = CurrentVersion();
        var parts = repository.Trim().Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(part => string.IsNullOrWhiteSpace(part)))
            return new UpdateCheckResult(false, current, null, null,
                "Enter the GitHub repository as owner/name.");

        var apiUrl = $"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/" +
                     $"{Uri.EscapeDataString(parts[1])}/releases/latest";
        var json = await _http.GetStringAsync(apiUrl, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = GetJsonString(root, "tag_name").TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var latest))
            throw new InvalidDataException("The latest GitHub release tag must look like v2.1.0.");

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The GitHub release does not contain assets.");
        JsonElement? executableAsset = null;
        JsonElement? checksumAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = GetJsonString(asset, "name");
            if (name.Equals("DiscordActivity.exe", StringComparison.OrdinalIgnoreCase))
                executableAsset = asset.Clone();
            if (name.Equals("DiscordActivity.sha256", StringComparison.OrdinalIgnoreCase))
                checksumAsset = asset.Clone();
        }
        if (executableAsset is null)
            throw new InvalidDataException(
                "The latest GitHub release must include an asset named DiscordActivity.exe.");

        var downloadUrl = GetJsonString(executableAsset.Value, "browser_download_url");
        var digest = GetJsonString(executableAsset.Value, "digest");
        var checksum = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
            ? digest["sha256:".Length..]
            : "";
        if (string.IsNullOrWhiteSpace(checksum) && checksumAsset is not null)
        {
            var checksumUrl = GetJsonString(checksumAsset.Value, "browser_download_url");
            var checksumText = await _http.GetStringAsync(checksumUrl, cancellationToken);
            checksum = checksumText.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }
        if (string.IsNullOrWhiteSpace(checksum))
            throw new InvalidDataException(
                "The release asset needs a GitHub SHA-256 digest or DiscordActivity.sha256 asset.");

        var manifest = new UpdateManifest
        {
            Version = latest.ToString(),
            DownloadUrl = downloadUrl,
            Sha256 = checksum,
            ReleaseNotes = GetJsonString(root, "body")
        };
        var available = latest > current;
        return new UpdateCheckResult(available, current, latest, manifest,
            available
                ? $"Version {latest} is available on GitHub."
                : $"Version {current} is up to date.");
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("The update download URL must use HTTPS.");
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
            throw new InvalidDataException("The update manifest must include a SHA-256 checksum.");

        var updateDirectory = Path.Combine(_dataDirectory, "Updates");
        Directory.CreateDirectory(updateDirectory);
        var destination = Path.Combine(updateDirectory, $"DiscordActivity-{manifest.Version}.exe");
        await using (var output = File.Create(destination))
        await using (var input = await _http.GetStreamAsync(uri, cancellationToken))
            await input.CopyToAsync(output, cancellationToken);

        await using var file = File.OpenRead(destination);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!hash.Equals(manifest.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destination);
            throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
        }
        return destination;
    }

    public void ApplyOnExit(string downloadedExecutable)
    {
        var target = Application.ExecutablePath;
        var scriptPath = Path.Combine(_dataDirectory, "apply-update.ps1");
        static string Quote(string value) => value.Replace("'", "''");
        var script = $"""
                      $target = '{Quote(target)}'
                      $source = '{Quote(downloadedExecutable)}'
                      Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue
                      Copy-Item -LiteralPath $source -Destination $target -Force
                      Start-Process -FilePath $target
                      Remove-Item -LiteralPath $source -Force -ErrorAction SilentlyContinue
                      Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue
                      """;
        File.WriteAllText(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string GetJsonString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static Version CurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
