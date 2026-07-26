using System.Text.Json;

namespace DiscordActivity;

internal sealed class StatisticsService : IDisposable
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
    private readonly Dictionary<string, GameStatistic> _games;
    private string? _activeKey;
    private DateTimeOffset _lastTick = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastSave = DateTimeOffset.MinValue;

    public event EventHandler? Changed;

    public StatisticsService(string configDirectory)
    {
        _path = Path.Combine(configDirectory, "statistics.json");
        _games = Load().Games
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .GroupBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public void Tick(AppMapping? activeMapping, IReadOnlyCollection<AppMapping> runningMappings,
        DateTimeOffset now)
    {
        var newKey = activeMapping is null || !activeMapping.TrackStatistics
            ? null
            : BuildKey(activeMapping);
        var changed = false;

        lock (_sync)
        {
            var elapsed = (long)Math.Floor((now - _lastTick).TotalSeconds);
            _lastTick = now;
            if (elapsed is > 0 and <= 120)
            {
                foreach (var mapping in runningMappings
                             .Where(m => m.TrackStatistics)
                             .GroupBy(BuildKey, StringComparer.OrdinalIgnoreCase)
                             .Select(group => group.First()))
                {
                    var statistic = GetOrCreate(mapping);
                    statistic.RunningSeconds += elapsed;
                    statistic.LastPlayed = now;
                    changed = true;
                }

                if (activeMapping is not null && activeMapping.TrackStatistics)
                {
                    var statistic = GetOrCreate(activeMapping);
                    statistic.TotalSeconds += elapsed;
                    statistic.LastPlayed = now;
                    changed = true;
                }
            }

            if (!string.Equals(_activeKey, newKey, StringComparison.OrdinalIgnoreCase))
            {
                _activeKey = newKey;
                if (activeMapping is not null && activeMapping.TrackStatistics)
                {
                    var statistic = GetOrCreate(activeMapping);
                    statistic.Sessions++;
                    statistic.LastPlayed = now;
                    changed = true;
                }
            }

            if (changed && (now - _lastSave).TotalSeconds >= 15)
                SaveLocked();
        }

        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<GameStatistic> Snapshot()
    {
        lock (_sync)
        {
            return _games.Values
                .OrderByDescending(g => g.TotalSeconds)
                .Select(Clone)
                .ToList();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _games.Clear();
            _activeKey = null;
            _lastTick = DateTimeOffset.UtcNow;
            SaveLocked();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public StatisticsFile Export()
    {
        lock (_sync)
            return new StatisticsFile { Games = _games.Values.Select(Clone).ToList() };
    }

    public void Import(StatisticsFile file)
    {
        lock (_sync)
        {
            _games.Clear();
            foreach (var statistic in file.Games.Where(g => !string.IsNullOrWhiteSpace(g.Key)))
                _games[statistic.Key] = Clone(statistic);
            _activeKey = null;
            _lastTick = DateTimeOffset.UtcNow;
            SaveLocked();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private GameStatistic GetOrCreate(AppMapping mapping)
    {
        var key = BuildKey(mapping);
        if (!_games.TryGetValue(key, out var statistic))
        {
            statistic = new GameStatistic
            {
                Key = key,
                ProcessName = mapping.ProcessName,
                DisplayName = string.IsNullOrWhiteSpace(mapping.DisplayName)
                    ? mapping.ProcessName
                    : mapping.DisplayName
            };
            _games[key] = statistic;
        }
        else
        {
            statistic.DisplayName = mapping.DisplayName;
            statistic.ProcessName = mapping.ProcessName;
        }
        return statistic;
    }

    private StatisticsFile Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<StatisticsFile>(File.ReadAllText(_path)) ?? new StatisticsFile();
        }
        catch
        {
            // A damaged statistics file starts fresh without blocking the app.
        }
        return new StatisticsFile();
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var file = new StatisticsFile { Games = _games.Values.OrderBy(g => g.DisplayName).ToList() };
        File.WriteAllText(_path, JsonSerializer.Serialize(file, _jsonOptions));
        _lastSave = DateTimeOffset.UtcNow;
    }

    private static string BuildKey(AppMapping mapping) =>
        $"{mapping.ProcessName}|{mapping.WindowTitleContains}".ToLowerInvariant();

    private static GameStatistic Clone(GameStatistic source) => new()
    {
        Key = source.Key,
        ProcessName = source.ProcessName,
        DisplayName = source.DisplayName,
        TotalSeconds = source.TotalSeconds,
        RunningSeconds = source.RunningSeconds,
        Sessions = source.Sessions,
        LastPlayed = source.LastPlayed
    };

    public void Dispose()
    {
        lock (_sync) SaveLocked();
    }
}
