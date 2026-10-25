using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>A <see cref="WorkspaceRow"/> as the Workspaces list shows it.</summary>
public sealed class WorkspaceItem(WorkspaceRow row) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkspaceRow Row { get; } = row;

    public string Name => Row.Name;
    public string Location => Row.Location;
    public string Uri => Row.Uri;
    public bool IsOpen => Row.IsOpen;
    public string Status => Row.IsOpen ? "Open" : string.Empty;
    public string ActionText => Row.IsOpen ? "Focus" : "Launch";

    /// <summary>Filled star for a preferred workspace, outline otherwise (Segoe Fluent Icons).</summary>
    public string PreferredGlyph => Row.Preferred ? "" : "";

    public string PreferredTooltip => Row.Preferred
        ? "Preferred. Click to remove it from the preferred list."
        : "Keep this workspace on the preferred list.";

    public bool Close
    {
        get => Row.Close;
        set { Row.Close = value; OnPropertyChanged(); }
    }

    public bool Open
    {
        get => Row.Open;
        set { Row.Open = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
