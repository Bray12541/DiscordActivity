using System.Diagnostics;

namespace DiscordActivity;

internal sealed class ActivityMonitor : IDisposable
{
    private readonly DiscordRpcClient _rpc = new();
    private readonly StatisticsService _statistics;
    private readonly Action<string> _statusChanged;
    private CancellationTokenSource? _cts;
    private AppConfig _config;
    private string? _lastActivityKey;

    public bool IsPaused { get; private set; }
    public bool IsIdle { get; private set; }
    public string CurrentActivityName { get; private set; } = "None";
    public DateTimeOffset? CurrentSessionStarted { get; private set; }
    public string LastDiagnostic { get; private set; } = "Not connected";

    public TimeSpan CurrentSessionElapsed =>
        CurrentSessionStarted is null ? TimeSpan.Zero : DateTimeOffset.UtcNow - CurrentSessionStarted.Value;

    public ActivityMonitor(AppConfig config, StatisticsService statistics, Action<string> statusChanged)
    {
        _config = config;
        _statistics = statistics;
        _statusChanged = statusChanged;
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        _lastActivityKey = null;
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _ = MonitorLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public async Task SetPausedAsync(bool paused)
    {
        IsPaused = paused;
        _lastActivityKey = null;
        if (paused)
        {
            CurrentActivityName = "Paused";
            CurrentSessionStarted = null;
            try
            {
                if (_rpc.IsConnected)
                    await _rpc.ClearActivityAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                LastDiagnostic = ex.Message;
            }
            ReportStatus("Presence paused");
        }
        else
        {
            ReportStatus("Presence resumed");
        }
    }

    public void Refresh() => _lastActivityKey = null;

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var active = DetectForegroundActivity();
                var running = DetectRunningActivities();
                IsIdle = _config.IdleDetectionEnabled
                         && IdleDetector.GetIdleTime() >= TimeSpan.FromMinutes(
                             Math.Clamp(_config.IdleTimeoutMinutes, 1, 1440));

                if (IsPaused || IsIdle)
                    _statistics.Tick(null, [], DateTimeOffset.UtcNow);
                else
                    _statistics.Tick(active?.Mapping, running.Select(r => r.Mapping).ToList(),
                        DateTimeOffset.UtcNow);

                var detected = IsPaused || IsIdle ? null : active ?? ChooseBackgroundActivity(running);
                if (IsPaused)
                {
                    await ClearIfNeededAsync(cancellationToken);
                    ReportStatus("Presence paused");
                }
                else if (IsIdle)
                {
                    await ClearIfNeededAsync(cancellationToken);
                    ReportStatus($"Idle — presence cleared after {_config.IdleTimeoutMinutes} minutes");
                }
                else if (string.IsNullOrWhiteSpace(_config.ClientId))
                {
                    UpdateCurrentActivity(detected);
                    ReportStatus("Setup required: enter a Discord Application ID");
                }
                else
                {
                    await _rpc.ConnectAsync(_config.ClientId.Trim(), cancellationToken);
                    LastDiagnostic = "Discord RPC connected and responding";

                    if (detected is null)
                    {
                        await ClearIfNeededAsync(cancellationToken);
                        ReportStatus("Connected — no mapped app is active");
                    }
                    else
                    {
                        var key = BuildActivityKey(detected);
                        if (!string.Equals(key, _lastActivityKey, StringComparison.Ordinal))
                        {
                            await _rpc.SetActivityAsync(detected, cancellationToken);
                            _lastActivityKey = key;
                            LastDiagnostic = $"Discord accepted the activity for {detected.Mapping.DisplayName}";
                        }
                        UpdateCurrentActivity(detected);
                        var mode = active is null ? "running in background" : "active";
                        ReportStatus($"Showing: {detected.Mapping.DisplayName} ({mode})");
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _lastActivityKey = null;
                LastDiagnostic = ex.Message;
                CrashLogger.LogWarning("Activity monitor or Discord RPC update failed.", ex);
                ReportStatus($"Discord diagnostic: {ex.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_config.PollIntervalSeconds, 1, 60)),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ClearIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_lastActivityKey is not null && _rpc.IsConnected)
            await _rpc.ClearActivityAsync(cancellationToken);
        _lastActivityKey = null;
        CurrentActivityName = IsPaused ? "Paused" : IsIdle ? "Idle" : "None";
        CurrentSessionStarted = null;
    }

    private DetectedActivity? DetectForegroundActivity()
    {
        var foreground = ForegroundAppDetector.GetCurrent();
        if (foreground is null || IsExcluded(foreground.ProcessName)) return null;

        var mapping = _config.Mappings.FirstOrDefault(m =>
            MatchesProcess(m, foreground.ProcessName)
            && (string.IsNullOrWhiteSpace(m.WindowTitleContains)
                || foreground.WindowTitle.Contains(m.WindowTitleContains.Trim(),
                    StringComparison.OrdinalIgnoreCase)));

        if (mapping is not null)
            return new DetectedActivity(foreground.ProcessName, NormalizeMapping(mapping, foreground.ProcessName));

        if (_config.ShowUnmappedApplications)
        {
            return new DetectedActivity(foreground.ProcessName, new AppMapping
            {
                ProcessName = foreground.ProcessName,
                DisplayName = foreground.DisplayName,
                Details = $"Using {foreground.DisplayName}",
                ExecutablePath = foreground.ExecutablePath,
                Source = "Automatic",
                TrackStatistics = true
            });
        }
        return null;
    }

    private List<DetectedActivity> DetectRunningActivities()
    {
        var results = new List<DetectedActivity>();
        foreach (var mapping in _config.Mappings.Where(m =>
                     m.IsValid && (m.TrackStatistics || m.ContinueWhenUnfocused)))
        {
            var processName = Path.GetFileNameWithoutExtension(mapping.ProcessName.Trim());
            if (IsExcluded(processName)) continue;
            try
            {
                var processes = Process.GetProcessesByName(processName);
                var matching = processes.FirstOrDefault(process =>
                    string.IsNullOrWhiteSpace(mapping.WindowTitleContains)
                    || process.MainWindowTitle.Contains(mapping.WindowTitleContains,
                        StringComparison.OrdinalIgnoreCase));
                foreach (var process in processes) process.Dispose();
                if (matching is not null)
                    results.Add(new DetectedActivity(processName, NormalizeMapping(mapping, processName)));
            }
            catch
            {
                // A protected or exiting process is ignored until the next poll.
            }
        }
        return results;
    }

    private DetectedActivity? ChooseBackgroundActivity(IReadOnlyCollection<DetectedActivity> running)
    {
        var eligible = running.Where(activity => activity.Mapping.ContinueWhenUnfocused).ToList();
        if (eligible.Count == 0) return null;
        return eligible.FirstOrDefault(activity =>
                   string.Equals(activity.Mapping.DisplayName, CurrentActivityName,
                       StringComparison.OrdinalIgnoreCase))
               ?? eligible.First();
    }

    private bool IsExcluded(string processName) =>
        _config.ExcludedProcesses.Any(excluded =>
            string.Equals(Path.GetFileNameWithoutExtension(excluded.Trim()), processName,
                StringComparison.OrdinalIgnoreCase));

    private static bool MatchesProcess(AppMapping mapping, string processName) =>
        mapping.IsValid && string.Equals(Path.GetFileNameWithoutExtension(mapping.ProcessName.Trim()),
            processName, StringComparison.OrdinalIgnoreCase);

    private void UpdateCurrentActivity(DetectedActivity? detected)
    {
        var name = detected?.Mapping.DisplayName ?? "None";
        if (!string.Equals(CurrentActivityName, name, StringComparison.Ordinal))
            CurrentSessionStarted = detected is null ? null : DateTimeOffset.UtcNow;
        CurrentActivityName = name;
    }

    private static AppMapping NormalizeMapping(AppMapping mapping, string processName) => new()
    {
        ProcessName = processName,
        WindowTitleContains = mapping.WindowTitleContains,
        DisplayName = string.IsNullOrWhiteSpace(mapping.DisplayName)
            ? FriendlyProcessName(processName)
            : mapping.DisplayName.Trim(),
        Details = string.IsNullOrWhiteSpace(mapping.Details)
            ? $"Using {FriendlyProcessName(processName)}"
            : mapping.Details,
        State = mapping.State,
        LargeImageKey = mapping.LargeImageKey,
        LargeImageText = mapping.LargeImageText,
        ButtonLabel = mapping.ButtonLabel,
        ButtonUrl = mapping.ButtonUrl,
        ArtworkUrl = mapping.ArtworkUrl,
        ExecutablePath = mapping.ExecutablePath,
        Source = mapping.Source,
        ContinueWhenUnfocused = mapping.ContinueWhenUnfocused,
        TrackStatistics = mapping.TrackStatistics
    };

    private static string FriendlyProcessName(string processName) =>
        processName.Replace('_', ' ').Trim();

    private static string BuildActivityKey(DetectedActivity detected)
    {
        var m = detected.Mapping;
        return string.Join("|", detected.ProcessName, m.DisplayName, m.Details, m.State,
            m.WindowTitleContains, m.LargeImageKey, m.ArtworkUrl, m.LargeImageText,
            m.ButtonLabel, m.ButtonUrl);
    }

    private void ReportStatus(string status)
    {
        try { _statusChanged(status); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Stop();
        _rpc.Dispose();
    }
}
