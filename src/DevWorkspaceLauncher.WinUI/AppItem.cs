using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// An <see cref="AppConfiguration"/> as the Apps list shows it. The ticks write straight through to the
/// configuration object, as the WinForms grid binding does; they are saved only with a profile.
/// </summary>
public sealed class AppItem(AppConfiguration app) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public AppConfiguration App { get; } = app;

    public string Name => App.Name;
    public bool CanStop => App.CanStop;

    public string KillTooltip => App.CanStop
        ? "Stop this app with Kill & Start Selected."
        : "StopMethod is None: this application is never stopped.";

    public bool Kill
    {
        get => App.Kill;
        set { App.Kill = value && App.CanStop; OnPropertyChanged(); }
    }

    public bool Restart
    {
        get => App.Restart;
        set { App.Restart = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
