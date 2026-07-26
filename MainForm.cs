using System.ComponentModel;
using System.Diagnostics;

namespace DiscordActivity;

internal sealed class MainForm : Form
{
    private readonly TextBox _clientId = new();
    private readonly NumericUpDown _pollInterval = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _showUnmapped = new();
    private readonly CheckBox _autoRescan = new();
    private readonly CheckBox _removeUninstalled = new();
    private readonly CheckBox _idleDetection = new();
    private readonly NumericUpDown _idleMinutes = new();
    private readonly CheckBox _darkMode = new();
    private readonly CheckBox _autoUpdates = new();
    private readonly TextBox _githubRepository = new();
    private readonly TextBox _updateUrl = new();
    private readonly DataGridView _grid = new();
    private readonly DataGridView _statsGrid = new();
    private readonly PictureBox _artworkPreview = new();
    private readonly Label _artworkStatus = new();
    private readonly TextBox _privacy = new();
    private readonly Label _status = new();
    private readonly string _configPath;
    private readonly GameLibraryScanner _scanner;
    private readonly StatisticsService _statistics;
    private readonly UpdateService _updates;
    private BindingList<AppMapping> _mappings;

    public event EventHandler<AppConfig>? ConfigSaved;

    public MainForm(AppConfig config, string configPath, GameLibraryScanner scanner,
        StatisticsService statistics, UpdateService updates)
    {
        _configPath = configPath;
        _scanner = scanner;
        _statistics = statistics;
        _updates = updates;
        _mappings = new BindingList<AppMapping>(config.Mappings.Select(Clone).ToList());

        Text = "Discord Activity";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 620);
        Size = new Size(1180, 720);
        FormBorderStyle = FormBorderStyle.Sizable;

        BuildLayout();
        _clientId.Text = config.ClientId;
        _pollInterval.Value = Math.Clamp(config.PollIntervalSeconds, 1, 60);
        _startWithWindows.Checked = config.StartWithWindows;
        _showUnmapped.Checked = config.ShowUnmappedApplications;
        _autoRescan.Checked = config.AutoRescanLibraries;
        _removeUninstalled.Checked = config.RemoveUninstalledGames;
        _idleDetection.Checked = config.IdleDetectionEnabled;
        _idleMinutes.Value = Math.Clamp(config.IdleTimeoutMinutes, 1, 1440);
        _darkMode.Checked = config.DarkMode;
        _autoUpdates.Checked = config.AutoCheckUpdates;
        _githubRepository.Text = config.GitHubRepository;
        _updateUrl.Text = config.UpdateManifestUrl;
        _privacy.Lines = config.ExcludedProcesses.ToArray();
        _grid.DataSource = _mappings;
        RefreshStatistics();
        _statistics.Changed += StatisticsChanged;
        ApplyTheme(config.DarkMode);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = "Discord Activity",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        });
        root.Controls.Add(new Label
        {
            Text = "Automatically detect games, publish Rich Presence, and track your playtime.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        });

        root.Controls.Add(BuildSettings());
        root.Controls.Add(BuildTabs());
        root.Controls.Add(BuildBottomBar());
        Controls.Add(root);
    }

    private Control BuildSettings()
    {
        var settings = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 14)
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        settings.Controls.Add(new Label
        {
            Text = "Discord Application ID:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 0);
        _clientId.Dock = DockStyle.Fill;
        _clientId.PlaceholderText = "Paste the Application ID from Discord Developer Portal";
        settings.Controls.Add(_clientId, 1, 0);
        var portal = new Button { Text = "Open Developer Portal", AutoSize = true };
        portal.Click += (_, _) => OpenUrl("https://discord.com/developers/applications");
        settings.Controls.Add(portal, 2, 0);

        settings.Controls.Add(new Label
        {
            Text = "Check every:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 3, 0)
        }, 0, 1);
        var intervalPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        _pollInterval.Minimum = 1;
        _pollInterval.Maximum = 60;
        _pollInterval.Width = 60;
        intervalPanel.Controls.Add(_pollInterval);
        intervalPanel.Controls.Add(new Label
        {
            Text = "seconds", AutoSize = true, Margin = new Padding(3, 6, 0, 0)
        });
        settings.Controls.Add(intervalPanel, 1, 1);

        var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        ConfigureCheckBox(_startWithWindows, "Start with Windows");
        ConfigureCheckBox(_showUnmapped, "Show unmapped applications");
        ConfigureCheckBox(_autoRescan, "Rescan game libraries automatically");
        ConfigureCheckBox(_removeUninstalled, "Remove uninstalled games");
        options.Controls.Add(_startWithWindows);
        options.Controls.Add(_showUnmapped);
        options.Controls.Add(_autoRescan);
        options.Controls.Add(_removeUninstalled);
        settings.Controls.Add(options, 2, 1);
        settings.SetColumnSpan(options, 2);

        ConfigureCheckBox(_idleDetection, "Clear presence when idle for");
        _idleMinutes.Minimum = 1;
        _idleMinutes.Maximum = 1440;
        _idleMinutes.Width = 65;
        var idlePanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        idlePanel.Controls.Add(_idleDetection);
        idlePanel.Controls.Add(_idleMinutes);
        idlePanel.Controls.Add(new Label
        {
            Text = "minutes", AutoSize = true, Margin = new Padding(3, 6, 0, 0)
        });
        settings.Controls.Add(idlePanel, 1, 2);
        settings.SetColumnSpan(idlePanel, 3);
        return settings;
    }

    private Control BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildMappingsTab());
        tabs.TabPages.Add(BuildStatisticsTab());
        tabs.TabPages.Add(BuildPrivacyTab());
        tabs.TabPages.Add(BuildBackupAndUpdatesTab());
        return tabs;
    }

    private TabPage BuildMappingsTab()
    {
        var page = new TabPage("Games & applications") { Padding = new Padding(8) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tools = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        var rescan = new Button { Text = "Rescan game libraries", AutoSize = true };
        rescan.Click += async (_, _) => await RescanLibrariesAsync(rescan);
        var addCurrent = new Button { Text = "Add Current Game", AutoSize = true };
        addCurrent.Click += async (_, _) => await AddCurrentGameAsync();
        tools.Controls.Add(rescan);
        tools.Controls.Add(addCurrent);
        tools.Controls.Add(new Label
        {
            Text = "Steam artwork is assigned automatically. Manual image keys override it.",
            AutoSize = true,
            Margin = new Padding(10, 7, 0, 0)
        });
        layout.Controls.Add(tools);

        ConfigureMappingGrid();
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 850,
            FixedPanel = FixedPanel.Panel2
        };
        split.Panel1.Controls.Add(_grid);
        var previewPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            ColumnCount = 1,
            RowCount = 3
        };
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        previewPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewPanel.Controls.Add(new Label
        {
            Text = "Artwork preview",
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        });
        _artworkPreview.Dock = DockStyle.Fill;
        _artworkPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _artworkPreview.BackColor = Color.FromArgb(24, 24, 28);
        _artworkPreview.LoadCompleted += (_, args) =>
            _artworkStatus.Text = args.Error is null ? "Artwork loaded" : "Artwork could not be loaded";
        previewPanel.Controls.Add(_artworkPreview);
        _artworkStatus.AutoSize = true;
        _artworkStatus.MaximumSize = new Size(240, 0);
        previewPanel.Controls.Add(_artworkStatus);
        split.Panel2.Controls.Add(previewPanel);
        layout.Controls.Add(split);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildStatisticsTab()
    {
        var page = new TabPage("Playtime statistics") { Padding = new Padding(8) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var tools = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => RefreshStatistics();
        var reset = new Button { Text = "Reset statistics", AutoSize = true };
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show("Reset all recorded playtime and session counts?", "Reset statistics",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _statistics.Reset();
            RefreshStatistics();
        };
        tools.Controls.Add(refresh);
        tools.Controls.Add(reset);
        layout.Controls.Add(tools);

        _statsGrid.Dock = DockStyle.Fill;
        _statsGrid.ReadOnly = true;
        _statsGrid.AllowUserToAddRows = false;
        _statsGrid.AllowUserToDeleteRows = false;
        _statsGrid.RowHeadersVisible = false;
        _statsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        layout.Controls.Add(_statsGrid);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildPrivacyTab()
    {
        var page = new TabPage("Privacy exclusions") { Padding = new Padding(12) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "These processes are never shown or counted, even when unmapped-app detection is enabled. " +
                   "Enter one executable name per line; .exe is optional.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 10)
        });
        _privacy.Multiline = true;
        _privacy.AcceptsReturn = true;
        _privacy.ScrollBars = ScrollBars.Vertical;
        _privacy.Dock = DockStyle.Fill;
        _privacy.Font = new Font(FontFamily.GenericMonospace, 10);
        layout.Controls.Add(_privacy);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildBackupAndUpdatesTab()
    {
        var page = new TabPage("Backup & updates") { Padding = new Padding(16) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ConfigureCheckBox(_darkMode, "Use dark mode");
        _darkMode.CheckedChanged += (_, _) => ApplyTheme(_darkMode.Checked);
        layout.Controls.Add(_darkMode, 0, 0);
        layout.SetColumnSpan(_darkMode, 2);

        var backupPanel = new FlowLayoutPanel { AutoSize = true };
        var export = new Button { Text = "Export backup", AutoSize = true };
        export.Click += (_, _) => ExportBackup();
        var import = new Button { Text = "Restore backup", AutoSize = true };
        import.Click += (_, _) => RestoreBackup();
        backupPanel.Controls.Add(export);
        backupPanel.Controls.Add(import);
        layout.Controls.Add(new Label
        {
            Text = "Configuration and statistics:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 1);
        layout.Controls.Add(backupPanel, 1, 1);

        ConfigureCheckBox(_autoUpdates, "Check for updates automatically");
        layout.Controls.Add(_autoUpdates, 0, 2);
        layout.SetColumnSpan(_autoUpdates, 2);
        layout.Controls.Add(new Label
        {
            Text = "GitHub repository:",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 3);
        _githubRepository.Dock = DockStyle.Fill;
        _githubRepository.PlaceholderText = "owner/repository";
        layout.Controls.Add(_githubRepository, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "Custom HTTPS manifest (optional fallback):",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        }, 0, 4);
        _updateUrl.Dock = DockStyle.Fill;
        _updateUrl.PlaceholderText = "https://example.com/discord-activity/update.json";
        layout.Controls.Add(_updateUrl, 1, 4);
        var check = new Button { Text = "Check for updates now", AutoSize = true };
        check.Click += async (_, _) => await CheckForUpdatesAsync(check);
        layout.Controls.Add(check, 1, 5);
        var openLogs = new Button { Text = "Open crash logs", AutoSize = true };
        openLogs.Click += (_, _) => OpenLogFolder();
        layout.Controls.Add(openLogs, 1, 6);
        layout.Controls.Add(new Label
        {
            Text = "GitHub updates use the latest public release and assets named DiscordActivity.exe " +
                   "and DiscordActivity.sha256. Downloads are installed only after checksum verification.",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Margin = new Padding(0, 12, 0, 0)
        }, 1, 7);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildBottomBar()
    {
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new Padding(0, 12, 0, 0)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.Text = "Starting…";
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.Anchor = AnchorStyles.Left;
        bottom.Controls.Add(_status, 0, 0);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        var openConfig = new Button { Text = "Open data folder", AutoSize = true };
        openConfig.Click += (_, _) => OpenConfigFolder();
        var save = new Button { Text = "Save", AutoSize = true };
        save.Click += (_, _) => SaveConfig();
        var hide = new Button { Text = "Hide to tray", AutoSize = true };
        hide.Click += (_, _) => Hide();
        buttons.Controls.Add(openConfig);
        buttons.Controls.Add(save);
        buttons.Controls.Add(hide);
        bottom.Controls.Add(buttons, 1, 0);
        return bottom;
    }

    private void ConfigureMappingGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.RowHeadersVisible = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.SelectionChanged += (_, _) => UpdateArtworkPreview();

        AddColumn("ProcessName", "Process", 70);
        AddColumn("WindowTitleContains", "Title filter", 65);
        AddColumn("DisplayName", "Display name", 90);
        AddColumn("Details", "Details", 105);
        AddColumn("State", "State", 65);
        AddColumn("LargeImageKey", "Manual image key", 65);
        AddColumn("ArtworkUrl", "Automatic artwork URL", 100);
        AddColumn("Source", "Source", 55, readOnly: true);
        AddCheckColumn("ContinueWhenUnfocused", "Keep in background", 55);
        AddCheckColumn("TrackStatistics", "Count stats", 45);
        AddColumn("ButtonLabel", "Button label", 60);
        AddColumn("ButtonUrl", "Button URL", 80);
    }

    private void AddCheckColumn(string property, string header, float weight)
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            FillWeight = weight,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void AddColumn(string property, string header, float weight, bool readOnly = false)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = property,
            HeaderText = header,
            FillWeight = weight,
            ReadOnly = readOnly,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private async Task RescanLibrariesAsync(Button button)
    {
        button.Enabled = false;
        SetStatus("Scanning installed game libraries…");
        try
        {
            var discovered = await Task.Run(_scanner.Scan);
            var added = MergeDiscovered(discovered);
            var removed = _removeUninstalled.Checked
                ? GameLibraryScanner.RemoveMissingAutoMappings(_mappings)
                : 0;
            SetStatus($"Scan complete — {discovered.Count} found, {added} added, {removed} removed");
        }
        catch (Exception ex)
        {
            CrashLogger.LogWarning("Game-library scan failed.", ex);
            SetStatus($"Library scan failed: {ex.Message}");
        }
        finally
        {
            if (!button.IsDisposed) button.Enabled = true;
        }
    }

    public int MergeDiscovered(IEnumerable<AppMapping> discovered)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => MergeDiscovered(discovered));
            return 0;
        }
        _grid.EndEdit();
        var added = GameLibraryScanner.MergeInto(_mappings, discovered);
        _grid.Refresh();
        return added;
    }

    public int RemoveMissingMappings()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RemoveMissingMappings());
            return 0;
        }
        var removed = GameLibraryScanner.RemoveMissingAutoMappings(_mappings);
        if (removed > 0) _grid.Refresh();
        return removed;
    }

    private async Task AddCurrentGameAsync()
    {
        SetStatus("Switch to the game you want to add — capturing in 3 seconds…");
        Hide();
        await Task.Delay(3000);
        var app = ForegroundAppDetector.GetCurrent();
        Show();
        Activate();

        if (app is null)
        {
            MessageBox.Show("I could not read the foreground application. Try again while the game is open.",
                "No game detected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var existing = _mappings.FirstOrDefault(m =>
            string.Equals(Path.GetFileNameWithoutExtension(m.ProcessName), app.ProcessName,
                StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            _grid.CurrentCell = _grid.Rows[_mappings.IndexOf(existing)].Cells[0];
            SetStatus($"{existing.DisplayName} is already mapped");
            return;
        }

        var mapping = new AppMapping
        {
            ProcessName = app.ProcessName,
            DisplayName = app.DisplayName,
            Details = $"Playing {app.DisplayName}",
            ExecutablePath = app.ExecutablePath,
            Source = "Captured",
            ContinueWhenUnfocused = true,
            TrackStatistics = true
        };
        _mappings.Add(mapping);
        _grid.CurrentCell = _grid.Rows[_mappings.Count - 1].Cells[0];
        SetStatus($"Added {app.DisplayName} — edit its details, then click Save");
    }

    private void SaveConfig()
    {
        _grid.EndEdit();
        var clientId = _clientId.Text.Trim();
        if (clientId.Length > 0 && !clientId.All(char.IsDigit))
        {
            MessageBox.Show("The Discord Application ID should contain only numbers.", "Invalid Application ID",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var config = BuildConfig();
        ConfigSaved?.Invoke(this, config);
        SetStatus("Saved — activity will refresh automatically");
    }

    private AppConfig BuildConfig() => new()
    {
        ClientId = _clientId.Text.Trim(),
        PollIntervalSeconds = (int)_pollInterval.Value,
        StartWithWindows = _startWithWindows.Checked,
        ShowUnmappedApplications = _showUnmapped.Checked,
        AutoRescanLibraries = _autoRescan.Checked,
        RemoveUninstalledGames = _removeUninstalled.Checked,
        IdleDetectionEnabled = _idleDetection.Checked,
        IdleTimeoutMinutes = (int)_idleMinutes.Value,
        DarkMode = _darkMode.Checked,
        AutoCheckUpdates = _autoUpdates.Checked,
        GitHubRepository = _githubRepository.Text.Trim(),
        UpdateManifestUrl = _updateUrl.Text.Trim(),
        ExcludedProcesses = _privacy.Lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(),
        Mappings = _mappings.Where(m => m.IsValid).Select(Clone).ToList()
    };

    private void RefreshStatistics()
    {
        var rows = _statistics.Snapshot().Select(stat => new
        {
            Game = stat.DisplayName,
            Process = stat.ProcessName,
            ActiveTime = FormatDuration(stat.TotalSeconds),
            RunningTime = FormatDuration(stat.RunningSeconds),
            Sessions = stat.Sessions,
            LastPlayed = stat.LastPlayed?.LocalDateTime.ToString("g") ?? ""
        }).ToList();
        _statsGrid.DataSource = rows;
    }

    private void UpdateArtworkPreview()
    {
        if (_grid.CurrentRow?.DataBoundItem is not AppMapping mapping)
        {
            _artworkPreview.ImageLocation = null;
            _artworkStatus.Text = "Select a mapping";
            return;
        }

        if (Uri.TryCreate(mapping.ArtworkUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            if (!string.Equals(_artworkPreview.ImageLocation, uri.ToString(), StringComparison.Ordinal))
            {
                _artworkStatus.Text = "Loading artwork…";
                _artworkPreview.ImageLocation = uri.ToString();
                _artworkPreview.LoadAsync();
            }
        }
        else
        {
            _artworkPreview.ImageLocation = null;
            _artworkStatus.Text = string.IsNullOrWhiteSpace(mapping.LargeImageKey)
                ? "No artwork assigned"
                : $"Manual Discord asset: {mapping.LargeImageKey}";
        }
    }

    private void ExportBackup()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export Discord Activity backup",
            Filter = "Discord Activity backup (*.json)|*.json",
            FileName = $"DiscordActivity-backup-{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            BackupService.Export(dialog.FileName, BuildConfig(), _statistics.Export());
            SetStatus($"Backup exported to {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            CrashLogger.LogWarning("Backup export failed.", ex);
            MessageBox.Show(ex.Message, "Backup failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RestoreBackup()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Restore Discord Activity backup",
            Filter = "Discord Activity backup (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var bundle = BackupService.Import(dialog.FileName);
            ApplyConfigToControls(bundle.Config);
            _statistics.Import(bundle.Statistics);
            ConfigSaved?.Invoke(this, BuildConfig());
            SetStatus($"Backup restored from {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            CrashLogger.LogWarning("Backup restore failed.", ex);
            MessageBox.Show(ex.Message, "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyConfigToControls(AppConfig config)
    {
        _clientId.Text = config.ClientId;
        _pollInterval.Value = Math.Clamp(config.PollIntervalSeconds, 1, 60);
        _startWithWindows.Checked = config.StartWithWindows;
        _showUnmapped.Checked = config.ShowUnmappedApplications;
        _autoRescan.Checked = config.AutoRescanLibraries;
        _removeUninstalled.Checked = config.RemoveUninstalledGames;
        _idleDetection.Checked = config.IdleDetectionEnabled;
        _idleMinutes.Value = Math.Clamp(config.IdleTimeoutMinutes, 1, 1440);
        _darkMode.Checked = config.DarkMode;
        _autoUpdates.Checked = config.AutoCheckUpdates;
        _githubRepository.Text = config.GitHubRepository;
        _updateUrl.Text = config.UpdateManifestUrl;
        _privacy.Lines = config.ExcludedProcesses.ToArray();
        _mappings = new BindingList<AppMapping>(config.Mappings.Select(Clone).ToList());
        _grid.DataSource = _mappings;
        ApplyTheme(config.DarkMode);
    }

    private async Task CheckForUpdatesAsync(Button button)
    {
        button.Enabled = false;
        SetStatus("Checking for updates…");
        try
        {
            var result = await _updates.CheckAsync(_githubRepository.Text.Trim(), _updateUrl.Text.Trim());
            SetStatus(result.Message);
            if (!result.UpdateAvailable || result.Manifest is null)
            {
                MessageBox.Show(result.Message, "Updates", MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var notes = string.IsNullOrWhiteSpace(result.Manifest.ReleaseNotes)
                ? ""
                : $"\n\n{result.Manifest.ReleaseNotes}";
            if (MessageBox.Show($"{result.Message}{notes}\n\nDownload and install it now?",
                    "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                != DialogResult.Yes) return;
            SetStatus("Downloading and verifying update…");
            var downloaded = await _updates.DownloadAsync(result.Manifest);
            _updates.ApplyOnExit(downloaded);
            Application.Exit();
        }
        catch (Exception ex)
        {
            CrashLogger.LogWarning("Update check or installation failed.", ex);
            SetStatus($"Update check failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!button.IsDisposed) button.Enabled = true;
        }
    }

    private void ApplyTheme(bool dark)
    {
        var background = dark ? Color.FromArgb(30, 31, 34) : SystemColors.Control;
        var surface = dark ? Color.FromArgb(43, 45, 49) : SystemColors.Window;
        var foreground = dark ? Color.FromArgb(230, 230, 235) : SystemColors.ControlText;
        ApplyThemeRecursive(this, dark, background, surface, foreground);
        BackColor = background;
        ForeColor = foreground;
        _grid.BackgroundColor = surface;
        _statsGrid.BackgroundColor = surface;
    }

    private static void ApplyThemeRecursive(Control parent, bool dark, Color background,
        Color surface, Color foreground)
    {
        foreach (Control control in parent.Controls)
        {
            control.ForeColor = foreground;
            control.BackColor = control switch
            {
                TextBox or NumericUpDown or DataGridView => surface,
                Button => dark ? Color.FromArgb(55, 57, 63) : SystemColors.Control,
                TabPage => background,
                _ => background
            };
            if (control is DataGridView grid)
            {
                grid.DefaultCellStyle.BackColor = surface;
                grid.DefaultCellStyle.ForeColor = foreground;
                grid.DefaultCellStyle.SelectionBackColor = dark
                    ? Color.FromArgb(88, 101, 242)
                    : SystemColors.Highlight;
                grid.ColumnHeadersDefaultCellStyle.BackColor = dark
                    ? Color.FromArgb(43, 45, 49)
                    : SystemColors.Control;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = foreground;
                grid.EnableHeadersVisualStyles = !dark;
            }
            if (control.HasChildren)
                ApplyThemeRecursive(control, dark, background, surface, foreground);
        }
    }

    private void StatisticsChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed)
        {
            try { BeginInvoke(() => RefreshStatistics()); }
            catch (InvalidOperationException) { }
        }
    }

    public void SetStatus(string value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(value));
            return;
        }
        _status.Text = value;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _statistics.Changed -= StatisticsChanged;
        base.OnFormClosing(e);
    }

    private void OpenConfigFolder()
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private static void OpenLogFolder()
    {
        Directory.CreateDirectory(CrashLogger.LogDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", CrashLogger.LogDirectory)
            { UseShellExecute = true });
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void ConfigureCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
    }

    private static string FormatDuration(long seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m {duration.Seconds}s";
    }

    private static AppMapping Clone(AppMapping source) => new()
    {
        ProcessName = source.ProcessName,
        WindowTitleContains = source.WindowTitleContains,
        DisplayName = source.DisplayName,
        Details = source.Details,
        State = source.State,
        LargeImageKey = source.LargeImageKey,
        LargeImageText = source.LargeImageText,
        ButtonLabel = source.ButtonLabel,
        ButtonUrl = source.ButtonUrl,
        ArtworkUrl = source.ArtworkUrl,
        ExecutablePath = source.ExecutablePath,
        Source = source.Source,
        ContinueWhenUnfocused = source.ContinueWhenUnfocused,
        TrackStatistics = source.TrackStatistics
    };
}
