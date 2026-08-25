using System.ComponentModel;
using System.Diagnostics;
using Krypton.Toolkit;

namespace RestartApps;

internal static class Program
{
    private static readonly KryptonManager ThemeManager = new() { GlobalPaletteMode = PaletteMode.Microsoft365Blue };

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            Application.Run(new RestartAppsForm(AppConfigurationStore.Load()));
        }
        catch (Exception ex)
        {
            KryptonMessageBox.Show(
                ex.Message,
                "Error",
                KryptonMessageBoxButtons.OK,
                KryptonMessageBoxIcon.Error);
        }
    }
}

internal sealed class RestartAppsForm : KryptonForm
{
    private ConfigurationFile _config;

    /// <summary>The rows shown in the grid: the enabled apps, by reference into <see cref="_config"/>.</summary>
    private readonly BindingList<AppConfiguration> _visible = [];

    private readonly KryptonDataGridView _grid = new();
    private readonly KryptonTextBox _txtLog = new();
    private readonly KryptonComboBox _profiles = new();
    private readonly FlowLayoutPanel _quickRun = new();
    private readonly KryptonButton _btnRestart = new();
    private readonly KryptonButton _btnRestartSelected = new();
    private readonly KryptonButton _btnEdit = new();
    private readonly List<KryptonButton> _profileButtons = [];
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 400 };
    private FileSystemWatcher? _watcher;
    private string _lastKnownJson;
    private int _reloadRetries;
    private bool _dropTargetsEnabled;
    private bool _busy;

    public RestartAppsForm(ConfigurationSnapshot snapshot)
    {
        _config = snapshot.Configuration;
        _lastKnownJson = snapshot.Json;

        Text = "Restart Apps";
        MinimumSize = new Size(580, 700);
        Size = new Size(620, 760);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(layout);

        layout.Controls.Add(BuildHeader(), 0, 0);
        layout.Controls.Add(BuildProfileBar(), 0, 1);

        _quickRun.Dock = DockStyle.Fill;
        _quickRun.BackColor = Color.Transparent;
        _quickRun.WrapContents = false;
        _quickRun.AutoScroll = true;
        _quickRun.Padding = new Padding(0, 2, 0, 2);
        layout.Controls.Add(_quickRun, 0, 2);

        BuildGrid();
        layout.Controls.Add(_grid, 0, 3);

        layout.Controls.Add(BuildActionBar(), 0, 4);

        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.WordWrap = false;
        _txtLog.StateCommon.Content.Font = new Font("Consolas", 9F);
        layout.Controls.Add(_txtLog, 0, 5);

        _reloadTimer.Tick += ReloadTimer_Tick;

        ApplyConfiguration();

        Shown += (_, _) =>
        {
            Activate();
            EnableDropTarget(this);
            _dropTargetsEnabled = true;
            StartWatchingConfiguration();
        };

        FormClosed += (_, _) =>
        {
            _reloadTimer.Stop();
            _watcher?.Dispose();
        };
    }

    // ------------------------------------------------------------------ chrome

    private KryptonPanel BuildHeader()
    {
        var header = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10) };
        var title = new KryptonLabel
        {
            Dock = DockStyle.Top,
            Text = "Restart Apps",
            Values =
            {
                ExtraText = "Tick Kill to stop an app, Restart to start it again.\nDrop an executable or shortcut here to add it."
            }
        };
        title.StateCommon.ShortText.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
        title.StateCommon.LongText.Color1 = Color.FromArgb(92, 102, 112);
        header.Controls.Add(title);
        return header;
    }

    private TableLayoutPanel BuildProfileBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));

        var label = new KryptonLabel { Text = "Profile:", Dock = DockStyle.Fill, Margin = new Padding(0) };
        bar.Controls.Add(label, 0, 0);

        _profiles.Dock = DockStyle.Fill;
        _profiles.Margin = new Padding(0, 2, 6, 2);
        _profiles.DropDownStyle = ComboBoxStyle.DropDownList;
        bar.Controls.Add(_profiles, 1, 0);

        bar.Controls.Add(CreateBarButton("Load", "Apply this profile's ticks to the grid.", LoadProfile_Click), 2, 0);
        bar.Controls.Add(CreateBarButton("Save As...", "Store the current ticks as a new profile.", SaveProfileAs_Click), 3, 0);
        bar.Controls.Add(CreateBarButton("Update", "Overwrite the selected profile with the current ticks.", UpdateProfile_Click), 4, 0);
        bar.Controls.Add(CreateBarButton("Delete", "Remove the selected profile.", DeleteProfile_Click), 5, 0);
        return bar;
    }

    private KryptonButton CreateBarButton(string text, string tooltip, EventHandler onClick)
    {
        var button = new KryptonButton
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 4, 2)
        };
        button.ToolTipValues.EnableToolTips = true;
        button.ToolTipValues.Description = tooltip;
        button.Click += onClick;
        return button;
    }

    private TableLayoutPanel BuildActionBar()
    {
        var buttonLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4)
        };
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        _btnRestart.Dock = DockStyle.Fill;
        _btnRestart.Margin = new Padding(0, 0, 6, 0);
        _btnRestart.Text = "Kill && Start Selected";
        _btnRestart.Click += RestartButton_Click;
        buttonLayout.Controls.Add(_btnRestart, 0, 0);

        _btnRestartSelected.Dock = DockStyle.Fill;
        _btnRestartSelected.Margin = new Padding(6, 0, 6, 0);
        _btnRestartSelected.Text = "Restart Selected";
        _btnRestartSelected.Click += RestartSelectedButton_Click;
        buttonLayout.Controls.Add(_btnRestartSelected, 1, 0);

        _btnEdit.Dock = DockStyle.Fill;
        _btnEdit.Margin = new Padding(6, 0, 0, 0);
        _btnEdit.Text = "Edit Config";
        _btnEdit.Click += EditButton_Click;
        buttonLayout.Controls.Add(_btnEdit, 2, 0);
        return buttonLayout;
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        _grid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            HeaderText = "Application",
            DataPropertyName = nameof(AppConfiguration.Name),
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(CreateFlagColumn("Kill", nameof(AppConfiguration.Kill)));
        _grid.Columns.Add(CreateFlagColumn("Restart", nameof(AppConfiguration.Restart)));

        _grid.DataSource = _visible;

        // Check box edits are only committed when the cell loses focus otherwise.
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _grid.DataBindingComplete += (_, _) => StyleRows();
    }

    private static KryptonDataGridViewCheckBoxColumn CreateFlagColumn(string header, string property)
    {
        return new KryptonDataGridViewCheckBoxColumn
        {
            Name = header,
            HeaderText = header,
            DataPropertyName = property,
            Width = 64,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
    }

    /// <summary>Locks the Kill box for apps whose StopMethod is None, so the rule is visible.</summary>
    private void StyleRows()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not AppConfiguration app)
            {
                continue;
            }

            var cell = row.Cells["Kill"];
            cell.ReadOnly = !app.CanStop;
            cell.Style.BackColor = app.CanStop ? Color.Empty : Color.FromArgb(240, 240, 240);
            cell.ToolTipText = app.CanStop
                ? string.Empty
                : "StopMethod is None: this application is never stopped.";
        }
    }

    // --------------------------------------------------------------- binding

    /// <summary>Rebinds everything that comes from the configuration file.</summary>
    private void ApplyConfiguration()
    {
        _visible.RaiseListChangedEvents = false;
        _visible.Clear();
        foreach (var app in _config.Apps.Where(app => !app.Disabled))
        {
            _visible.Add(app);
        }

        _visible.RaiseListChangedEvents = true;
        _visible.ResetBindings();

        RebuildProfileControls();
    }

    private void RebuildProfileControls()
    {
        var selected = _profiles.SelectedItem as string;

        _profiles.Items.Clear();
        foreach (var profile in _config.Profiles)
        {
            _profiles.Items.Add(profile.Name);
        }

        if (selected is not null && _profiles.Items.Contains(selected))
        {
            _profiles.SelectedItem = selected;
        }
        else if (_profiles.Items.Count > 0)
        {
            _profiles.SelectedIndex = 0;
        }

        foreach (var button in _profileButtons)
        {
            _quickRun.Controls.Remove(button);
            button.Dispose();
        }

        _profileButtons.Clear();
        _quickRun.Controls.Clear();

        var caption = new KryptonLabel
        {
            Text = _config.Profiles.Count == 0 ? "No profiles yet - use Save As..." : "One click:",
            Margin = new Padding(0, 8, 8, 0),
            AutoSize = true
        };
        _quickRun.Controls.Add(caption);

        foreach (var profile in _config.Profiles)
        {
            var button = new KryptonButton
            {
                Text = profile.Name,
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 4),
                Tag = profile
            };
            button.ToolTipValues.EnableToolTips = true;
            button.ToolTipValues.Description = $"Load '{profile.Name}' and run Kill & Start Selected.";
            button.Click += QuickRunButton_Click;

            if (_dropTargetsEnabled)
            {
                EnableDropTarget(button);
            }

            _profileButtons.Add(button);
            _quickRun.Controls.Add(button);
        }
    }

    // --------------------------------------------------------------- profiles

    private Profile? SelectedProfile()
    {
        return _profiles.SelectedItem is string name
            ? _config.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private List<ProfileEntry> CaptureEntries()
    {
        _grid.EndEdit();
        return _visible
            .Select(app => new ProfileEntry { Name = app.Name, Kill = app.Kill, Restart = app.Restart })
            .ToList();
    }

    private void ApplyProfile(Profile profile)
    {
        var entries = new Dictionary<string, ProfileEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in profile.Entries)
        {
            entries[entry.Name] = entry;
        }

        var unknown = 0;
        foreach (var app in _visible)
        {
            if (entries.TryGetValue(app.Name, out var entry))
            {
                app.Kill = entry.Kill && app.CanStop;
                app.Restart = entry.Restart;
            }
            else
            {
                // An app the profile has never seen is left alone rather than acted on.
                app.Kill = false;
                app.Restart = false;
                unknown++;
            }
        }

        _visible.ResetBindings();

        var suffix = unknown == 0 ? string.Empty : $" ({unknown} app(s) not in the profile were cleared)";
        LogMessage($"Loaded profile '{profile.Name}'.{suffix}");
    }

    private void LoadProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            LogMessage("No profile selected.");
            return;
        }

        ApplyProfile(profile);
    }

    private void SaveProfileAs_Click(object? sender, EventArgs e)
    {
        var name = PromptDialog.Show(this, "Save profile as", "Profile name:", _profiles.SelectedItem as string ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var existing = _config.Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            var answer = KryptonMessageBox.Show(
                $"Profile '{existing.Name}' already exists. Overwrite it?",
                "Save profile as",
                KryptonMessageBoxButtons.YesNo,
                KryptonMessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            existing.Entries = CaptureEntries();
            LogMessage($"Overwrote profile '{existing.Name}'.");
        }
        else
        {
            _config.Profiles.Add(new Profile { Name = name, Entries = CaptureEntries() });
            LogMessage($"Created profile '{name}'.");
        }

        SaveConfiguration();
        RebuildProfileControls();
        _profiles.SelectedItem = _config.Profiles
            .First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Name;
    }

    private void UpdateProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            LogMessage("No profile selected.");
            return;
        }

        profile.Entries = CaptureEntries();
        SaveConfiguration();
        LogMessage($"Updated profile '{profile.Name}'.");
    }

    private void DeleteProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedProfile();
        if (profile is null)
        {
            LogMessage("No profile selected.");
            return;
        }

        var answer = KryptonMessageBox.Show(
            $"Delete profile '{profile.Name}'?",
            "Delete profile",
            KryptonMessageBoxButtons.YesNo,
            KryptonMessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _config.Profiles.Remove(profile);
        SaveConfiguration();
        RebuildProfileControls();
        LogMessage($"Deleted profile '{profile.Name}'.");
    }

    private async void QuickRunButton_Click(object? sender, EventArgs e)
    {
        if (sender is not KryptonButton { Tag: Profile profile })
        {
            return;
        }

        ApplyProfile(profile);
        await RestartAppsAsync(stopOnlySelected: false);
    }

    private void SaveConfiguration()
    {
        try
        {
            _lastKnownJson = AppConfigurationStore.Save(_config);
            LogMessage($"Saved {AppConfigurationStore.FilePath}.");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to save configuration: {ex.Message}");
        }
    }

    // --------------------------------------------------------------- restart

    private List<AppConfiguration> AppsToStart() => _visible.Where(app => app.Restart).ToList();

    private List<AppConfiguration> AppsToKill() => _visible.Where(app => app.Kill && app.CanStop).ToList();

    private async void RestartButton_Click(object? sender, EventArgs e)
    {
        await RestartAppsAsync(stopOnlySelected: false);
    }

    private async void RestartSelectedButton_Click(object? sender, EventArgs e)
    {
        await RestartAppsAsync(stopOnlySelected: true);
    }

    private async Task RestartAppsAsync(bool stopOnlySelected)
    {
        if (_busy)
        {
            return;
        }

        _grid.EndEdit();

        var appsToStart = AppsToStart();
        var appsToStop = stopOnlySelected
            ? appsToStart.Where(app => app.CanStop).ToList()
            : AppsToKill();

        if (appsToStop.Count == 0 && appsToStart.Count == 0)
        {
            LogMessage(stopOnlySelected
                ? "No applications selected."
                : "Nothing to do: no application is ticked Kill or Restart.");
            return;
        }

        SetBusy(true);

        try
        {
            if (appsToStop.Count == 0)
            {
                LogMessage("Nothing to stop.");
            }
            else
            {
                LogMessage(stopOnlySelected ? "STOPPING selected applications..." : "STOPPING applications...");
                foreach (var app in appsToStop)
                {
                    await StopAppAsync(app);
                }

                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            if (appsToStart.Count == 0)
            {
                LogMessage("No applications ticked Restart; nothing to start.");
            }
            else
            {
                LogMessage("STARTING selected applications...");
                foreach (var app in appsToStart)
                {
                    LogMessage($"Starting {app.Name}...");
                    try
                    {
                        StartCommand(app.ExpandedCommandline);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Failed to start {app.Name}: {ex.Message}");
                    }
                }
            }

            LogMessage("Operation Complete.");
        }
        catch (Exception ex)
        {
            LogMessage($"Error: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task StopAppAsync(AppConfiguration app)
    {
        switch (app.StopMethod)
        {
            case StopMethod.None:
                LogMessage($"Skipping {app.Name}: StopMethod is None.");
                return;

            case StopMethod.Window:
                await CloseWindowsAsync(app);
                return;

            default:
                var processName = CommandLineParser.GetProcessName(app.ExpandedCommandline);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    LogMessage($"Skipping {app.Name}: no executable found in the command line.");
                    return;
                }

                LogMessage($"Stopping {app.Name} ({processName})...");
                StopProcesses(processName);
                return;
        }
    }

    /// <summary>
    /// Closes the windows matching the app's AppUserModelID. Used for browser-hosted PWAs, which
    /// share their process with every other window of that browser.
    /// </summary>
    private async Task CloseWindowsAsync(AppConfiguration app)
    {
        if (string.IsNullOrWhiteSpace(app.WindowAumid))
        {
            LogMessage($"Skipping {app.Name}: StopMethod is Window but WindowAumid is not set.");
            return;
        }

        var windows = WindowFinder.FindByAumid(app.WindowAumid);
        if (windows.Count == 0)
        {
            LogMessage($"{app.Name}: no window matching '{app.WindowAumid}' is open.");
            return;
        }

        LogMessage($"Closing {app.Name} ({windows.Count} window(s) matching '{app.WindowAumid}')...");
        foreach (var window in windows)
        {
            WindowFinder.Close(window.Handle);
        }

        // Give the app a moment to take the close down cleanly.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (windows.All(window => !WindowFinder.StillOpen(window.Handle)))
            {
                LogMessage($"{app.Name}: closed.");
                return;
            }

            await Task.Delay(150);
        }

        var remaining = windows.Count(window => WindowFinder.StillOpen(window.Handle));
        LogMessage($"{app.Name}: {remaining} window(s) did not close within 3 seconds.");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        _btnRestart.Enabled = !busy;
        _btnRestartSelected.Enabled = !busy;
        _btnEdit.Enabled = !busy;
        foreach (var button in _profileButtons)
        {
            button.Enabled = !busy;
        }
    }

    // ------------------------------------------------------------------ edit

    private void EditButton_Click(object? sender, EventArgs e)
    {
        var path = AppConfigurationStore.FilePath;
        if (!File.Exists(path))
        {
            LogMessage($"Configuration file not found: {path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            LogMessage($"Opened {path} in the default editor.");
        }
        catch (Exception ex)
        {
            // No association for .json - fall back to Notepad.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                });
                LogMessage($"Opened {path} in Notepad.");
            }
            catch (Exception fallbackEx)
            {
                LogMessage($"Failed to open configuration: {ex.Message} / {fallbackEx.Message}");
            }
        }
    }

    // --------------------------------------------------------------- watcher

    private void StartWatchingConfiguration()
    {
        var path = AppConfigurationStore.FilePath;
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                SynchronizingObject = this
            };
            _watcher.Changed += ConfigurationFileChanged;
            _watcher.Created += ConfigurationFileChanged;
            _watcher.Renamed += ConfigurationFileChanged;
            _watcher.EnableRaisingEvents = true;
            LogMessage($"Watching {path} for changes.");
        }
        catch (Exception ex)
        {
            LogMessage($"Could not watch the configuration file: {ex.Message}");
        }
    }

    private void ConfigurationFileChanged(object? sender, FileSystemEventArgs e)
    {
        // Editors write in bursts, and often through a temp file plus a rename, so debounce.
        _reloadRetries = 0;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private void ReloadTimer_Tick(object? sender, EventArgs e)
    {
        _reloadTimer.Stop();

        ConfigurationSnapshot snapshot;
        try
        {
            snapshot = AppConfigurationStore.Load();
        }
        catch (IOException) when (_reloadRetries < 5)
        {
            // Still locked by the editor - try again shortly.
            _reloadRetries++;
            _reloadTimer.Start();
            return;
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to reload configuration: {ex.Message}");
            return;
        }

        // Also filters out the events raised by our own Save().
        if (snapshot.Json == _lastKnownJson)
        {
            return;
        }

        _lastKnownJson = snapshot.Json;
        _config = snapshot.Configuration;
        ApplyConfiguration();

        var hidden = _config.Apps.Count - _visible.Count;
        var hiddenText = hidden == 0 ? string.Empty : $", {hidden} disabled";
        LogMessage($"Configuration reloaded ({_visible.Count} application(s){hiddenText}, {_config.Profiles.Count} profile(s)).");
    }

    // ------------------------------------------------------------- drag/drop

    private void EnableDropTarget(Control control)
    {
        control.AllowDrop = true;
        control.DragEnter += DropTarget_DragEnter;
        control.DragDrop += DropTarget_DragDrop;

        foreach (Control child in control.Controls)
        {
            EnableDropTarget(child);
        }
    }

    private static void DropTarget_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void DropTarget_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        _grid.EndEdit();

        var added = new List<AppConfiguration>();

        foreach (var path in paths)
        {
            var app = CreateEntry(path);
            if (app is null)
            {
                continue;
            }

            var isDuplicate = _config.Apps.Concat(added).Any(existing =>
                string.Equals(existing.Name, app.Name, StringComparison.OrdinalIgnoreCase));
            if (isDuplicate)
            {
                LogMessage($"{app.Name} is already in the configuration.");
                continue;
            }

            added.Add(app);
            LogMessage($"Added {app.Name}: {app.Commandline}");
        }

        if (added.Count == 0)
        {
            return;
        }

        _config.Apps.AddRange(added);
        foreach (var app in added)
        {
            _visible.Add(app);
        }

        SaveConfiguration();
    }

    private AppConfiguration? CreateEntry(string path)
    {
        if (Directory.Exists(path))
        {
            LogMessage($"Ignoring folder: {path}");
            return null;
        }

        if (!File.Exists(path))
        {
            LogMessage($"Ignoring missing file: {path}");
            return null;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        var commandline = $"\"{path}\"";

        if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, arguments) = ShortcutResolver.Resolve(path);
            if (string.IsNullOrWhiteSpace(target))
            {
                LogMessage($"Could not resolve shortcut {Path.GetFileName(path)}; using the shortcut itself.");
            }
            else
            {
                commandline = string.IsNullOrWhiteSpace(arguments)
                    ? $"\"{target}\""
                    : $"\"{target}\" {arguments.Trim()}";
            }
        }

        return new AppConfiguration
        {
            Name = name,
            Commandline = commandline,
            Kill = true,
            Restart = true
        };
    }

    // ------------------------------------------------------------------ misc

    private void LogMessage(string message)
    {
        _txtLog.AppendText($"{DateTime.Now:HH:mm:ss}: {message}{Environment.NewLine}");
        Refresh();
    }

    private static void StopProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Match the PowerShell prototype's SilentlyContinue behavior.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void StartCommand(string commandline)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" {commandline}",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}

/// <summary>Small modal text prompt, used for naming profiles.</summary>
internal sealed class PromptDialog : KryptonForm
{
    private readonly KryptonTextBox _input = new();

    private PromptDialog(string title, string label, string initial)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(360, 128);
        Padding = new Padding(14);

        var caption = new KryptonLabel { Text = label, Location = new Point(14, 14), AutoSize = true };
        Controls.Add(caption);

        _input.Location = new Point(14, 40);
        _input.Width = 332;
        _input.Text = initial;
        Controls.Add(_input);

        var ok = new KryptonButton
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(186, 78),
            Width = 76
        };
        var cancel = new KryptonButton
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(270, 78),
            Width = 76
        };
        Controls.Add(ok);
        Controls.Add(cancel);

        AcceptButton = ok;
        CancelButton = cancel;

        Shown += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public static string? Show(IWin32Window owner, string title, string label, string initial)
    {
        using var dialog = new PromptDialog(title, label, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._input.Text : null;
    }
}
