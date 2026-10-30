namespace DevWorkspaceLauncher.WinUI;

/// <summary>A <see cref="WorkspaceRow"/> as the VS Code workspaces list shows it.</summary>
public sealed class WorkspaceItem(WorkspaceRow row)
{
    public WorkspaceRow Row { get; } = row;

    public string Name => Row.Name;

    /// <summary>The name with the remote host after it, so "develop" here and on a server differ.</summary>
    public string Title => VsCodeUri.Title(Row.Name, Row.Uri);

    public string Location => Row.Location;
    public string Uri => Row.Uri;
    public bool IsOpen => Row.IsOpen;
    public string Status => Row.IsOpen ? "Open" : string.Empty;

    public string LastUsedText => Row.LastUsed?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    /// <summary>Filled star for a preferred workspace, outline otherwise (Segoe Fluent Icons).</summary>
    public string PreferredGlyph => Row.Preferred ? "" : "";

    public Microsoft.UI.Xaml.Media.Brush PreferredBrush => LaunchItem.PinBrush(Row.Preferred);

    public string PreferredTooltip => Row.Preferred
        ? "Preferred. Click to remove it from the preferred list."
        : "Keep this workspace on the preferred list.";
}
