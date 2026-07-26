namespace DiscordActivity;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ConfigService _configService = new();
    private readonly Control _dispatcher = new();
    private readonly GameLibraryScanner _scanner = new();
    private readonly StatisticsService _statistics;
    private readonly UpdateService _updates;
    private readonly NotifyIcon _trayIcon;
    private readonly ActivityMonitor _monitor;
    private AppConfig _config;
    private MainForm? _form;
    private ToolStripMenuItem _pauseItem = null!;
    private ToolStripMenuItem _currentItem = null!;
    private ToolStripMenuItem _sessionItem = null!;
    private ToolStripMenuItem _diagnosticItem = null!;

    public TrayApplicationContext()
    {
        _dispatcher.CreateControl();
        _config = _configService.Load();
        _statistics = new StatisticsService(_configService.ConfigDirectory);
        _updates = new UpdateService(_configService.ConfigDirectory);
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Discord Activity",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowSettings();

        _monitor = new ActivityMonitor(_config, _statistics, SetStatus);
        _monitor.Start();
        if (_config.AutoRescanLibraries)
            StartAutomaticRescan();
        if (_config.AutoCheckUpdates
            && (!string.IsNullOrWhiteSpace(_config.GitHubRepository)
                || !string.IsNullOrWhiteSpace(_config.UpdateManifestUrl)))
            StartAutomaticUpdateCheck();

        var minimized = Environment.GetCommandLineArgs()
            .Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));
        if (!minimized || string.IsNullOrWhiteSpace(_config.ClientId))
            ShowSettings();
        ShowPreviousCrashIfNeeded();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Opening += (_, _) => UpdateTrayMenu();
        menu.Items.Add("Open settings", null, (_, _) => ShowSettings());
        _pauseItem = new ToolStripMenuItem("Pause Presence") { CheckOnClick = true };
        _pauseItem.Click += async (_, _) => await _monitor.SetPausedAsync(_pauseItem.Checked);
        menu.Items.Add(_pauseItem);
        menu.Items.Add("Refresh activity", null, (_, _) => _monitor.Refresh());
        menu.Items.Add(new ToolStripSeparator());
        _currentItem = new ToolStripMenuItem("Current: None") { Enabled = false };
        _sessionItem = new ToolStripMenuItem("Session: 0m 0s") { Enabled = false };
        _diagnosticItem = new ToolStripMenuItem("Connection diagnostics…");
        _diagnosticItem.Click += (_, _) => MessageBox.Show(_monitor.LastDiagnostic,
            "Discord connection diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Information);
        menu.Items.Add(_currentItem);
        menu.Items.Add(_sessionItem);
        menu.Items.Add(_diagnosticItem);
        menu.Items.Add("Open crash logs", null, (_, _) =>
        {
            Directory.CreateDirectory(CrashLogger.LogDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "explorer.exe", CrashLogger.LogDirectory) { UseShellExecute = true });
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    private void ShowSettings()
    {
        if (_form is null || _form.IsDisposed)
        {
            _form = new MainForm(_config, _configService.ConfigPath, _scanner, _statistics, _updates);
            _form.ConfigSaved += (_, config) =>
            {
                _config = config;
                _configService.Save(_config);
                _monitor.UpdateConfig(_config);
                _monitor.Refresh();
            };
            _form.FormClosed += (_, _) => _form = null;
        }

        _form.Show();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
    }

    private void StartAutomaticRescan()
    {
        _ = Task.Run(_scanner.Scan).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully || _dispatcher.IsDisposed)
            {
                if (task.Exception is not null)
                    CrashLogger.LogWarning("Automatic game-library scan failed.", task.Exception);
                return;
            }
            try
            {
                _dispatcher.BeginInvoke(() =>
                {
                    GameLibraryScanner.MergeInto(_config.Mappings, task.Result);
                    var removed = _config.RemoveUninstalledGames
                        ? GameLibraryScanner.RemoveMissingAutoMappings(_config.Mappings)
                        : 0;
                    _configService.Save(_config);
                    _monitor.UpdateConfig(_config);
                    _form?.MergeDiscovered(task.Result);
                    if (_config.RemoveUninstalledGames)
                        _form?.RemoveMissingMappings();
                    SetStatus($"Library scan complete — {task.Result.Count} detected, {removed} removed");
                });
            }
            catch (InvalidOperationException) { }
        });
    }

    private void StartAutomaticUpdateCheck()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _updates.CheckAsync(
                    _config.GitHubRepository, _config.UpdateManifestUrl);
                if (!result.UpdateAvailable || _dispatcher.IsDisposed) return;
                _dispatcher.BeginInvoke(() =>
                {
                    _trayIcon.BalloonTipTitle = "Discord Activity update available";
                    _trayIcon.BalloonTipText = result.Message;
                    _trayIcon.ShowBalloonTip(5000);
                });
            }
            catch (Exception ex)
            {
                // Background checks stay silent; manual checks show detailed errors.
                CrashLogger.LogWarning("Automatic GitHub update check failed.", ex);
            }
        });
    }

    private void ShowPreviousCrashIfNeeded()
    {
        var previousCrash = CrashLogger.ConsumePreviousCrash();
        if (string.IsNullOrWhiteSpace(previousCrash)) return;
        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                var answer = MessageBox.Show(
                    $"{previousCrash}\n\nWould you like to open the crash-log folder?",
                    "Discord Activity recovered from a crash",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
                Directory.CreateDirectory(CrashLogger.LogDirectory);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", CrashLogger.LogDirectory) { UseShellExecute = true });
            });
        }
        catch (InvalidOperationException) { }
    }

    private void UpdateTrayMenu()
    {
        _pauseItem.Checked = _monitor.IsPaused;
        _currentItem.Text = $"Current: {_monitor.CurrentActivityName}";
        var elapsed = _monitor.CurrentSessionElapsed;
        _sessionItem.Text = $"Session: {(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s";
    }

    private void SetStatus(string status)
    {
        if (_dispatcher.InvokeRequired)
        {
            try { _dispatcher.BeginInvoke(() => SetStatus(status)); }
            catch (InvalidOperationException) { }
            return;
        }
        if (_trayIcon.Container is null && !_trayIcon.Visible) return;
        var tooltip = status.Length > 63 ? status[..63] : status;
        try
        {
            _trayIcon.Text = tooltip;
            _form?.SetStatus(status);
        }
        catch (ObjectDisposedException) { }
    }

    private void Exit()
    {
        _trayIcon.Visible = false;
        _form?.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Dispose();
            _statistics.Dispose();
            _trayIcon.Dispose();
            _form?.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
