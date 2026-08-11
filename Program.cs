using System.Diagnostics;
using System.Text.Json;
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
            Application.Run(new RestartAppsForm(AppConfiguration.Load()));
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
    private readonly IReadOnlyList<AppConfiguration> _appList;
    private readonly KryptonCheckedListBox _checkListBox = new();
    private readonly KryptonTextBox _txtLog = new();
    private readonly KryptonButton _btnRestart = new();
    private readonly KryptonButton _btnRestartSelected = new();

    public RestartAppsForm(IReadOnlyList<AppConfiguration> appList)
    {
        _appList = appList;

        Text = "Restart Apps";
        MinimumSize = new Size(520, 560);
        Size = new Size(560, 620);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        Padding = new Padding(16);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        Controls.Add(layout);

        var header = new KryptonPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10) };
        var title = new KryptonLabel
        {
            Dock = DockStyle.Top,
            Text = "Restart Apps",
            Values = { ExtraText = "Stop every configured app, then start only the selected ones." }
        };
        title.StateCommon.ShortText.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
        title.StateCommon.LongText.Color1 = Color.FromArgb(92, 102, 112);
        header.Controls.Add(title);
        layout.Controls.Add(header, 0, 0);

        _checkListBox.Dock = DockStyle.Fill;
        _checkListBox.CheckOnClick = true;
        foreach (var app in _appList)
        {
            var index = _checkListBox.Items.Add(app.Name);
            _checkListBox.SetItemChecked(index, app.Restart);
        }
        layout.Controls.Add(_checkListBox, 0, 1);

        var buttonLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 4)
        };
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.Controls.Add(buttonLayout, 0, 2);

        _btnRestart.Dock = DockStyle.Fill;
        _btnRestart.Margin = new Padding(0, 0, 6, 0);
        _btnRestart.Text = "Kill All && Start Selected";
        _btnRestart.Click += RestartButton_Click;
        buttonLayout.Controls.Add(_btnRestart, 0, 0);

        _btnRestartSelected.Dock = DockStyle.Fill;
        _btnRestartSelected.Margin = new Padding(6, 0, 0, 0);
        _btnRestartSelected.Text = "Restart Selected";
        _btnRestartSelected.Click += RestartSelectedButton_Click;
        buttonLayout.Controls.Add(_btnRestartSelected, 1, 0);

        _txtLog.Dock = DockStyle.Fill;
        _txtLog.Multiline = true;
        _txtLog.ScrollBars = ScrollBars.Vertical;
        _txtLog.ReadOnly = true;
        _txtLog.WordWrap = false;
        _txtLog.StateCommon.Content.Font = new Font("Consolas", 9F);
        layout.Controls.Add(_txtLog, 0, 3);

        Shown += (_, _) => Activate();
    }

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
        var selectedApps = GetSelectedApps();
        if (selectedApps.Count == 0)
        {
            LogMessage("No applications selected.");
            return;
        }

        var appsToStop = stopOnlySelected ? selectedApps : _appList;

        _btnRestart.Enabled = false;
        _btnRestartSelected.Enabled = false;
        Cursor = Cursors.WaitCursor;

        try
        {
            LogMessage(stopOnlySelected ? "STOPPING selected applications..." : "STOPPING applications...");
            foreach (var app in appsToStop)
            {
                var processName = CommandLineParser.GetProcessName(app.Commandline);
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                LogMessage($"Stopping {app.Name} ({processName})...");
                StopProcesses(processName);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));

            LogMessage("STARTING selected applications...");
            foreach (var app in selectedApps)
            {
                LogMessage($"Starting {app.Name}...");
                try
                {
                    StartCommand(app.Commandline);
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to start {app.Name}: {ex.Message}");
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
            Cursor = Cursors.Default;
            _btnRestart.Enabled = true;
            _btnRestartSelected.Enabled = true;
        }
    }

    private List<AppConfiguration> GetSelectedApps()
    {
        return _checkListBox.CheckedItems
            .Cast<string>()
            .Select(item => _appList.FirstOrDefault(app => app.Name == item))
            .Where(app => app is not null)
            .Cast<AppConfiguration>()
            .ToList();
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

internal sealed class AppConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public required string Name { get; init; }
    public required string Commandline { get; init; }
    public bool Restart { get; init; }

    public static IReadOnlyList<AppConfiguration> Load()
    {
        var configFile = FindConfigFile();
        if (!File.Exists(configFile))
        {
            throw new FileNotFoundException($"Configuration file not found: {configFile}", configFile);
        }

        using var stream = File.OpenRead(configFile);
        var apps = JsonSerializer.Deserialize<List<AppConfiguration>>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Configuration file is empty: {configFile}");

        return apps.Select(app => app.WithExpandedCommandline()).ToList();
    }

    private AppConfiguration WithExpandedCommandline()
    {
        return new AppConfiguration
        {
            Name = Name,
            Commandline = Environment.ExpandEnvironmentVariables(Commandline),
            Restart = Restart
        };
    }

    private static string FindConfigFile()
    {
        var executableConfig = Path.Combine(AppContext.BaseDirectory, "restartapps.json");
        if (File.Exists(executableConfig))
        {
            return executableConfig;
        }

        return Path.Combine(Environment.CurrentDirectory, "restartapps.json");
    }
}

internal static class CommandLineParser
{
    public static string GetProcessName(string commandline)
    {
        var executable = GetExecutablePath(commandline);
        return string.IsNullOrWhiteSpace(executable)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(executable);
    }

    private static string GetExecutablePath(string commandline)
    {
        commandline = commandline.Trim();
        if (commandline.Length == 0)
        {
            return string.Empty;
        }

        if (commandline[0] == '"')
        {
            var closingQuote = commandline.IndexOf('"', 1);
            return closingQuote > 1
                ? commandline[1..closingQuote]
                : string.Empty;
        }

        var firstSpace = commandline.IndexOf(' ');
        return firstSpace >= 0
            ? commandline[..firstSpace]
            : commandline;
    }
}