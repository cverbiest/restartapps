using System.ComponentModel;
using System.Diagnostics;
using Krypton.Navigator;
using Krypton.Toolkit;

namespace DevWorkspaceLauncher;

internal static class Program
{
    private static readonly KryptonManager ThemeManager = new() { GlobalPaletteMode = PaletteMode.Microsoft365Blue };

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            var setupMessage = AppConfigurationStore.EnsureExists();
            Application.Run(new MainForm(AppConfigurationStore.Load(), setupMessage));
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

internal sealed class MainForm : KryptonForm
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

    private readonly KryptonNavigator _tabs = new();
    private readonly KryptonPage _workspacesPage = new();
    private readonly KryptonDataGridView _workspaceGrid = new();
    private readonly KryptonCheckBox _showAllWorkspaces = new();
    private readonly KryptonLabel _workspaceCount = new();
    private readonly KryptonButton _btnRefreshWorkspaces = new();
    private readonly KryptonButton _btnCloseWorkspaces = new();
    private readonly KryptonButton _btnOpenWorkspaces = new();
    private readonly KryptonComboBox _workspaceProfiles = new();
    private readonly FlowLayoutPanel _workspaceQuickRun = new();
    private readonly List<KryptonButton> _workspaceProfileButtons = [];
    private readonly System.Windows.Forms.Timer _workspaceRefreshTimer = new() { Interval = 300 };

    /// <summary>
    /// The Close and Open ticks, by workspace URI. Held outside the grid so a refresh - which happens
    /// whenever the window is activated - does not wipe a selection the user is still building.
    /// </summary>
    private readonly Dictionary<string, (bool Close, bool Open)> _workspaceTicks = new(StringComparer.OrdinalIgnoreCase);

    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = 400 };
    private FileSystemWatcher? _watcher;
    private string _lastKnownJson;
    private int _reloadRetries;
    private bool _dropTargetsEnabled;
    private bool _busy;

    public MainForm(ConfigurationSnapshot snapshot, string? setupMessage = null)
    {
        _config = snapshot.Configuration;
        _lastKnownJson = snapshot.Json;

        Text = AppIdentity.DisplayName;
        MinimumSize = new Size(660, 788);
        Size = new Size(740, 888);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(layout);

        layout.Controls.Add(BuildHeader(), 0, 0);

        // Both sets of one-click buttons sit outside the tabs so they stay reachable from either tab.
        _workspaceQuickRun.Dock = DockStyle.Fill;
        _workspaceQuickRun.BackColor = Color.Transparent;
        _workspaceQuickRun.WrapContents = false;
        _workspaceQuickRun.AutoScroll = true;
        _workspaceQuickRun.Padding = new Padding(0, 2, 0, 2);
        layout.Controls.Add(_workspaceQuickRun, 0, 1);

        _quickRun.Dock = DockStyle.Fill;
        _quickRun.BackColor = Color.Transparent;
        _quickRun.WrapContents = false;
        _quickRun.AutoScroll = true;
        _quickRun.Padding = new Padding(0, 2, 0, 2);
        layout.Controls.Add(_quickRun, 0, 2);

        layout.Controls.Add(BuildTabs(), 0, 3);
        layout.Controls.Add(BuildActionBar(), 0, 4);

        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.WordWrap = false;
        _txtLog.StateCommon.Content.Font = new Font("Consolas", 9F);
        layout.Controls.Add(_txtLog, 0, 5);

        _reloadTimer.Tick += ReloadTimer_Tick;

        _workspaceRefreshTimer.Tick += (_, _) =>
        {
            _workspaceRefreshTimer.Stop();
            RefreshWorkspaces();
        };

        _tabs.SelectedPageChanged += (_, _) =>
        {
            if (_tabs.SelectedPage == _workspacesPage)
            {
                RefreshWorkspaces();
            }
        };

        // Coming back to the window is the moment the open/closed state is most likely stale.
        Activated += (_, _) =>
        {
            if (_tabs.SelectedPage == _workspacesPage)
            {
                _workspaceRefreshTimer.Stop();
                _workspaceRefreshTimer.Start();
            }
        };

        ApplyConfiguration();

        Shown += (_, _) =>
        {
            Activate();
            if (setupMessage is not null)
            {
                LogMessage(setupMessage);
            }

            EnableDropTarget(this);
            _dropTargetsEnabled = true;
            StartWatchingConfiguration();
        };

        FormClosed += (_, _) =>
        {
            _reloadTimer.Stop();
            _workspaceRefreshTimer.Stop();
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
            Text = AppIdentity.DisplayName,
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

    private KryptonNavigator BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.NavigatorMode = NavigatorMode.BarTabGroup;
        _tabs.Button.CloseButtonDisplay = ButtonDisplay.Hide;
        _tabs.AllowPageDrag = false;
        _tabs.Pages.Add(BuildWorkspacesPage());
        _tabs.Pages.Add(BuildAppsPage());
        _tabs.SelectedIndex = 0;
        return _tabs;
    }

    private KryptonPage BuildAppsPage()
    {
        var page = new KryptonPage
        {
            Text = "Apps",
            TextTitle = string.Empty,
            TextDescription = string.Empty
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildProfileBar(), 0, 0);

        BuildGrid();
        layout.Controls.Add(_grid, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private KryptonPage BuildWorkspacesPage()
    {
        _workspacesPage.Text = "Workspaces";
        _workspacesPage.TextTitle = string.Empty;
        _workspacesPage.TextDescription = string.Empty;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Padding = new Padding(4)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(BuildWorkspaceProfileBar(), 0, 0);
        layout.Controls.Add(BuildWorkspaceBar(), 0, 1);

        BuildWorkspaceGrid();
        layout.Controls.Add(_workspaceGrid, 0, 2);

        _workspacesPage.Controls.Add(layout);
        return _workspacesPage;
    }

    private TableLayoutPanel BuildWorkspaceProfileBar()
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

        bar.Controls.Add(new KryptonLabel { Text = "Session:", Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);

        _workspaceProfiles.Dock = DockStyle.Fill;
        _workspaceProfiles.Margin = new Padding(0, 2, 6, 2);
        _workspaceProfiles.DropDownStyle = ComboBoxStyle.DropDownList;
        bar.Controls.Add(_workspaceProfiles, 1, 0);

        bar.Controls.Add(CreateBarButton("Load", "Tick this session's workspaces in the grid.", LoadWorkspaceProfile_Click), 2, 0);
        bar.Controls.Add(CreateBarButton("Save As...", "Store the ticked Open workspaces as a new session.", SaveWorkspaceProfileAs_Click), 3, 0);
        bar.Controls.Add(CreateBarButton("Update", "Overwrite the selected session with the ticked Open workspaces.", UpdateWorkspaceProfile_Click), 4, 0);
        bar.Controls.Add(CreateBarButton("Delete", "Remove the selected session.", DeleteWorkspaceProfile_Click), 5, 0);
        return bar;
    }

    private TableLayoutPanel BuildWorkspaceBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));

        _showAllWorkspaces.Text = "Show all recent";
        _showAllWorkspaces.Dock = DockStyle.Fill;
        _showAllWorkspaces.Margin = new Padding(0, 2, 6, 2);
        _showAllWorkspaces.CheckedChanged += (_, _) => RefreshWorkspaces();
        bar.Controls.Add(_showAllWorkspaces, 0, 0);

        _workspaceCount.Dock = DockStyle.Fill;
        _workspaceCount.Margin = new Padding(0, 2, 6, 2);
        bar.Controls.Add(_workspaceCount, 1, 0);

        _btnCloseWorkspaces.Text = "Close Ticked";
        _btnCloseWorkspaces.Dock = DockStyle.Fill;
        _btnCloseWorkspaces.Margin = new Padding(0, 2, 6, 2);
        _btnCloseWorkspaces.ToolTipValues.EnableToolTips = true;
        _btnCloseWorkspaces.ToolTipValues.Description =
            "Ask the windows ticked Close to close. Never forced, so unsaved work still prompts.";
        _btnCloseWorkspaces.Click += CloseWorkspacesButton_Click;
        bar.Controls.Add(_btnCloseWorkspaces, 2, 0);

        _btnOpenWorkspaces.Text = "Open Ticked";
        _btnOpenWorkspaces.Dock = DockStyle.Fill;
        _btnOpenWorkspaces.Margin = new Padding(0, 2, 6, 2);
        _btnOpenWorkspaces.ToolTipValues.EnableToolTips = true;
        _btnOpenWorkspaces.ToolTipValues.Description = "Open every workspace ticked Open.";
        _btnOpenWorkspaces.Click += OpenWorkspacesButton_Click;
        bar.Controls.Add(_btnOpenWorkspaces, 3, 0);

        _btnRefreshWorkspaces.Text = "Refresh";
        _btnRefreshWorkspaces.Dock = DockStyle.Fill;
        _btnRefreshWorkspaces.Margin = new Padding(0, 2, 0, 2);
        _btnRefreshWorkspaces.ToolTipValues.EnableToolTips = true;
        _btnRefreshWorkspaces.ToolTipValues.Description = "Re-read the workspace list and check which ones are open.";
        _btnRefreshWorkspaces.Click += (_, _) => RefreshWorkspaces();
        bar.Controls.Add(_btnRefreshWorkspaces, 4, 0);

        return bar;
    }

    private void BuildWorkspaceGrid()
    {
        _workspaceGrid.Dock = DockStyle.Fill;
        _workspaceGrid.AutoGenerateColumns = false;
        _workspaceGrid.AllowUserToAddRows = false;
        _workspaceGrid.AllowUserToDeleteRows = false;
        _workspaceGrid.AllowUserToResizeRows = false;
        _workspaceGrid.AllowUserToOrderColumns = false;
        _workspaceGrid.RowHeadersVisible = false;
        _workspaceGrid.MultiSelect = false;
        _workspaceGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _workspaceGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

        _workspaceGrid.Columns.Add(CreateWorkspaceFlagColumn(
            "Preferred",
            "Pref",
            42,
            "Keep this workspace on the preferred list."));

        _workspaceGrid.Columns.Add(CreateWorkspaceFlagColumn(
            "Close",
            "Close",
            50,
            "Include this window in Close Ticked."));

        _workspaceGrid.Columns.Add(CreateWorkspaceFlagColumn(
            "Open",
            "Open",
            48,
            "Include this workspace in Open Ticked, and in a saved session."));

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Workspace",
            HeaderText = "Workspace",
            Width = 140,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Location",
            HeaderText = "Location",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewTextBoxColumn
        {
            Name = "Status",
            HeaderText = "Status",
            Width = 56,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.Columns.Add(new KryptonDataGridViewButtonColumn
        {
            Name = "Action",
            HeaderText = string.Empty,
            Width = 76,
            UseColumnTextForButtonValue = false,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        _workspaceGrid.CellContentClick += WorkspaceGrid_CellContentClick;

        // Tick boxes otherwise only commit when the cell loses focus.
        _workspaceGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_workspaceGrid.IsCurrentCellDirty && _workspaceGrid.CurrentCell is DataGridViewCheckBoxCell)
            {
                _workspaceGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _workspaceGrid.CellValueChanged += WorkspaceGrid_CellValueChanged;
    }

    private static KryptonDataGridViewCheckBoxColumn CreateWorkspaceFlagColumn(
        string name,
        string header,
        int width,
        string tooltip)
    {
        return new KryptonDataGridViewCheckBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = width,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            ToolTipText = tooltip,
            DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter }
        };
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
        RebuildWorkspaceProfileControls();
        RefreshWorkspaces();
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
            Text = _config.Profiles.Count == 0 ? "No profiles yet - use Save As..." : "One click apps:",
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

    // ------------------------------------------------------------ workspaces

    /// <summary>Re-reads the workspace list and re-checks which of the shown rows are open.</summary>
    private void RefreshWorkspaces()
    {
        // A tick box left mid-edit would otherwise throw when the rows are replaced.
        _workspaceGrid.EndEdit();

        var discovered = VsCodeWorkspaces.Discover(VsCodeWorkspaces.DefaultStorageRoot);
        var rows = WorkspaceRows.Build(discovered, _config.Workspaces, _showAllWorkspaces.Checked);

        // With VS Code shut down nothing can be open, and the probe can be skipped entirely.
        var running = VsCodeWorkspaces.IsRunning();
        foreach (var row in rows)
        {
            row.IsOpen = running
                && row.Id is not null
                && VsCodeWorkspaces.IsOpen(VsCodeWorkspaces.DefaultStorageRoot, row.Id);

            if (_workspaceTicks.TryGetValue(row.Uri, out var ticks))
            {
                row.Close = ticks.Close;
                row.Open = ticks.Open;
            }
        }

        _workspaceGrid.Rows.Clear();
        foreach (var row in rows)
        {
            var index = _workspaceGrid.Rows.Add(
                row.Preferred,
                row.Close,
                row.Open,
                row.Name,
                row.Location,
                row.IsOpen ? "Open" : string.Empty,
                row.IsOpen ? "Focus" : "Launch");

            var gridRow = _workspaceGrid.Rows[index];
            gridRow.Tag = row;
            gridRow.Cells["Location"].ToolTipText = row.Uri;

            // Closing needs a window to act on, so the tick is meaningless for a closed workspace.
            var close = gridRow.Cells["Close"];
            close.ReadOnly = !row.IsOpen;
            close.Style.BackColor = row.IsOpen ? Color.Empty : Color.FromArgb(240, 240, 240);
        }

        _workspaceCount.Text = _showAllWorkspaces.Checked
            ? $"{rows.Count} recent"
            : $"{rows.Count} preferred";
    }

    private void WorkspaceGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _workspaceGrid.Rows[e.RowIndex].Tag is not WorkspaceRow row)
        {
            return;
        }

        var ticked = _workspaceGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is true;

        switch (_workspaceGrid.Columns[e.ColumnIndex].Name)
        {
            case "Close":
                row.Close = ticked;
                break;

            case "Open":
                row.Open = ticked;
                break;

            default:
                return;
        }

        _workspaceTicks[row.Uri] = (row.Close, row.Open);
    }

    private List<WorkspaceRow> WorkspaceRowsInGrid()
    {
        return _workspaceGrid.Rows
            .Cast<DataGridViewRow>()
            .Select(gridRow => gridRow.Tag)
            .OfType<WorkspaceRow>()
            .ToList();
    }

    private async void OpenWorkspacesButton_Click(object? sender, EventArgs e)
    {
        var ticked = WorkspaceRowsInGrid().Where(row => row.Open).ToList();
        if (ticked.Count == 0)
        {
            LogMessage("No workspace is ticked Open.");
            return;
        }

        await OpenWorkspacesAsync(ticked.Select(row => (row.Uri, row.Name)).ToList());
    }

    /// <summary>Opens each workspace in turn. VS Code focuses one it already has open.</summary>
    private async Task OpenWorkspacesAsync(IReadOnlyList<(string Uri, string Name)> workspaces)
    {
        var executable = VsCodeLauncher.ResolveExecutable(_config.VsCodeExecutable);
        if (executable is null)
        {
            LogMessage("Could not find VS Code. Set \"VsCodeExecutable\" in the configuration file.");
            return;
        }

        foreach (var (uri, name) in workspaces)
        {
            var (kind, _, _) = VsCodeUri.Describe(uri);
            LogMessage($"Opening {name}...");

            try
            {
                Process.Start(VsCodeLauncher.CreateStartInfo(executable, VsCodeLauncher.BuildArguments(uri, kind)));
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to open {name}: {ex.Message}");
            }

            // VS Code drops requests that arrive while it is still starting the previous window.
            await Task.Delay(400);
        }

        RefreshWorkspaces();
    }

    private async void CloseWorkspacesButton_Click(object? sender, EventArgs e)
    {
        var ticked = WorkspaceRowsInGrid().Where(row => row.Close && row.IsOpen).ToList();
        if (ticked.Count == 0)
        {
            LogMessage("No open workspace is ticked Close.");
            return;
        }

        foreach (var row in ticked)
        {
            await CloseWorkspaceAsync(row);
        }

        RefreshWorkspaces();
    }

    /// <summary>
    /// Asks the workspace's window to close. Nothing is ever forced, and nothing is closed unless
    /// exactly one window matches - VS Code shares one process and one AppUserModelID across every
    /// window, so a wrong guess would close somebody else's editor.
    /// </summary>
    private async Task CloseWorkspaceAsync(WorkspaceRow row)
    {
        var windows = WindowFinder.FindByProcessName("Code");
        var titles = windows.Select(window => window.Title).ToList();
        var matches = VsCodeWindows.MatchAll(titles, row.Name, row.WindowTitle);

        if (matches.Count == 0)
        {
            LogMessage($"{row.Name}: no VS Code window matches. Set \"WindowTitle\" for it in the configuration.");
            return;
        }

        if (matches.Count > 1)
        {
            var candidates = string.Join(", ", matches.Select(index => $"\"{titles[index]}\""));
            LogMessage($"{row.Name}: {matches.Count} windows match ({candidates}); nothing closed. Set \"WindowTitle\" to pin it.");
            return;
        }

        var target = windows[matches[0]];
        LogMessage($"Closing {row.Name} (\"{target.Title}\")...");
        WindowFinder.Close(target.Handle);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (!WindowFinder.StillOpen(target.Handle))
            {
                LogMessage($"{row.Name}: closed.");
                return;
            }

            await Task.Delay(150);
        }

        LogMessage($"{row.Name}: still open after 3 seconds - VS Code may be asking about unsaved work.");
    }

    // ------------------------------------------------------- workspace sessions

    private WorkspaceProfile? SelectedWorkspaceProfile()
    {
        return _workspaceProfiles.SelectedItem is string name
            ? _config.WorkspaceProfiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>Ticks Open for the session's workspaces and clears it everywhere else.</summary>
    private void ApplyWorkspaceProfile(WorkspaceProfile profile)
    {
        var wanted = new HashSet<string>(profile.Workspaces, StringComparer.OrdinalIgnoreCase);

        foreach (var uri in _workspaceTicks.Keys.ToList())
        {
            _workspaceTicks[uri] = (_workspaceTicks[uri].Close, wanted.Contains(uri));
        }

        foreach (var uri in wanted)
        {
            _workspaceTicks.TryGetValue(uri, out var ticks);
            _workspaceTicks[uri] = (ticks.Close, true);
        }

        RefreshWorkspaces();

        var shown = WorkspaceRowsInGrid().Count(row => row.Open);
        var hidden = wanted.Count - shown;
        var suffix = hidden <= 0 ? string.Empty : $" ({hidden} not in the current list - Open Ticked still opens them)";
        LogMessage($"Loaded session '{profile.Name}'.{suffix}");
    }

    private List<string> TickedWorkspaceUris()
    {
        return WorkspaceRowsInGrid().Where(row => row.Open).Select(row => row.Uri).ToList();
    }

    private void LoadWorkspaceProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedWorkspaceProfile();
        if (profile is null)
        {
            LogMessage("No session selected.");
            return;
        }

        ApplyWorkspaceProfile(profile);
    }

    private void SaveWorkspaceProfileAs_Click(object? sender, EventArgs e)
    {
        var ticked = TickedWorkspaceUris();
        if (ticked.Count == 0)
        {
            LogMessage("Tick Open on the workspaces the session should open first.");
            return;
        }

        var name = PromptDialog.Show(this, "Save session as", "Session name:", _workspaceProfiles.SelectedItem as string ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        var existing = _config.WorkspaceProfiles
            .FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            var answer = KryptonMessageBox.Show(
                $"Session '{existing.Name}' already exists. Overwrite it?",
                "Save session as",
                KryptonMessageBoxButtons.YesNo,
                KryptonMessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            existing.Workspaces = ticked;
            LogMessage($"Overwrote session '{existing.Name}' ({ticked.Count} workspace(s)).");
        }
        else
        {
            _config.WorkspaceProfiles.Add(new WorkspaceProfile { Name = name, Workspaces = ticked });
            LogMessage($"Created session '{name}' ({ticked.Count} workspace(s)).");
        }

        SaveConfiguration();
        RebuildWorkspaceProfileControls();
        _workspaceProfiles.SelectedItem = _config.WorkspaceProfiles
            .First(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase)).Name;
    }

    private void UpdateWorkspaceProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedWorkspaceProfile();
        if (profile is null)
        {
            LogMessage("No session selected.");
            return;
        }

        var ticked = TickedWorkspaceUris();
        if (ticked.Count == 0)
        {
            LogMessage("Tick Open on the workspaces the session should open first.");
            return;
        }

        profile.Workspaces = ticked;
        SaveConfiguration();
        LogMessage($"Updated session '{profile.Name}' ({ticked.Count} workspace(s)).");
    }

    private void DeleteWorkspaceProfile_Click(object? sender, EventArgs e)
    {
        var profile = SelectedWorkspaceProfile();
        if (profile is null)
        {
            LogMessage("No session selected.");
            return;
        }

        var answer = KryptonMessageBox.Show(
            $"Delete session '{profile.Name}'?",
            "Delete session",
            KryptonMessageBoxButtons.YesNo,
            KryptonMessageBoxIcon.Warning);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        _config.WorkspaceProfiles.Remove(profile);
        SaveConfiguration();
        RebuildWorkspaceProfileControls();
        LogMessage($"Deleted session '{profile.Name}'.");
    }

    private async void WorkspaceQuickRunButton_Click(object? sender, EventArgs e)
    {
        if (sender is not KryptonButton { Tag: WorkspaceProfile profile })
        {
            return;
        }

        ApplyWorkspaceProfile(profile);

        // Straight from the profile, so a workspace hidden by the current filter still opens.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in WorkspaceRowsInGrid())
        {
            names[row.Uri] = row.Name;
        }

        var workspaces = profile.Workspaces
            .Select(uri => (Uri: uri, Name: names.TryGetValue(uri, out var name) ? name : VsCodeUri.Describe(uri).DisplayName))
            .ToList();

        await OpenWorkspacesAsync(workspaces);
    }

    private void RebuildWorkspaceProfileControls()
    {
        var selected = _workspaceProfiles.SelectedItem as string;

        _workspaceProfiles.Items.Clear();
        foreach (var profile in _config.WorkspaceProfiles)
        {
            _workspaceProfiles.Items.Add(profile.Name);
        }

        if (selected is not null && _workspaceProfiles.Items.Contains(selected))
        {
            _workspaceProfiles.SelectedItem = selected;
        }
        else if (_workspaceProfiles.Items.Count > 0)
        {
            _workspaceProfiles.SelectedIndex = 0;
        }

        foreach (var button in _workspaceProfileButtons)
        {
            _workspaceQuickRun.Controls.Remove(button);
            button.Dispose();
        }

        _workspaceProfileButtons.Clear();
        _workspaceQuickRun.Controls.Clear();

        _workspaceQuickRun.Controls.Add(new KryptonLabel
        {
            Text = _config.WorkspaceProfiles.Count == 0 ? "No sessions yet - tick Open, then Save As..." : "One click sessions:",
            Margin = new Padding(0, 8, 8, 0),
            AutoSize = true
        });

        foreach (var profile in _config.WorkspaceProfiles)
        {
            var button = new KryptonButton
            {
                Text = profile.Name,
                AutoSize = true,
                Margin = new Padding(0, 4, 6, 4),
                Tag = profile
            };
            button.ToolTipValues.EnableToolTips = true;
            button.ToolTipValues.Description = $"Open the {profile.Workspaces.Count} workspace(s) in '{profile.Name}'.";
            button.Click += WorkspaceQuickRunButton_Click;

            _workspaceProfileButtons.Add(button);
            _workspaceQuickRun.Controls.Add(button);
        }
    }

    private void WorkspaceGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || _workspaceGrid.Rows[e.RowIndex].Tag is not WorkspaceRow row)
        {
            return;
        }

        switch (_workspaceGrid.Columns[e.ColumnIndex].Name)
        {
            case "Action":
                LaunchWorkspace(row);
                return;

            case "Preferred":
                TogglePreferred(row);
                return;
        }
    }

    /// <summary>Preferred is a stated preference, so unlike the Kill/Restart ticks it is saved at once.</summary>
    private void TogglePreferred(WorkspaceRow row)
    {
        var existing = _config.Workspaces
            .FirstOrDefault(workspace => string.Equals(workspace.Uri, row.Uri, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            _config.Workspaces.Add(new WorkspaceFavorite { Name = row.Name, Uri = row.Uri });
            LogMessage($"Added {row.Name} to the preferred workspaces.");
        }
        else
        {
            _config.Workspaces.Remove(existing);
            LogMessage($"Removed {existing.Name} from the preferred workspaces.");
        }

        SaveConfiguration();
        RefreshWorkspaces();
    }

    private void LaunchWorkspace(WorkspaceRow row)
    {
        var executable = VsCodeLauncher.ResolveExecutable(_config.VsCodeExecutable);
        if (executable is null)
        {
            LogMessage("Could not find VS Code. Set \"VsCodeExecutable\" in the configuration file.");
            return;
        }

        try
        {
            Process.Start(VsCodeLauncher.CreateStartInfo(executable, VsCodeLauncher.BuildArguments(row.Uri, row.Kind)));
            LogMessage(row.IsOpen ? $"Focusing {row.Name}..." : $"Launching {row.Name}...");
        }
        catch (Exception ex)
        {
            LogMessage($"Failed to launch {row.Name}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------ misc

    protected override void WndProc(ref Message m)
    {
        if (UserEnvironment.IsEnvironmentChange((uint)m.Msg, m.LParam))
        {
            try
            {
                UserEnvironment.Refresh();
                LogMessage("Environment variables changed; apps started from now on get the new values.");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to reload environment variables: {ex.Message}");
            }
        }

        base.WndProc(ref m);
    }

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
