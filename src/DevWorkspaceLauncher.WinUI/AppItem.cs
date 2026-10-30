namespace DevWorkspaceLauncher.WinUI;

/// <summary>An <see cref="AppConfiguration"/> as the Apps list shows it, with whether it was running at the last refresh.</summary>
public sealed class AppItem(AppConfiguration app, bool running)
{
    public AppConfiguration App { get; } = app;

    public string Name => App.Name;
    public string Commandline => App.Commandline;

    public string StopMethod => App.StopMethod switch
    {
        DevWorkspaceLauncher.StopMethod.Window => $"Close windows ({App.WindowAumid})",
        DevWorkspaceLauncher.StopMethod.None => "Never stopped",
        _ => "Kill process"
    };

    public bool Running { get; } = running;

    public string Status => App.Disabled ? "Disabled" : Running ? "Running" : string.Empty;

    public string FavoriteGlyph => LaunchItem.PinGlyph(App.Favorite);

    public Microsoft.UI.Xaml.Media.Brush FavoriteBrush => LaunchItem.PinBrush(App.Favorite);
}
