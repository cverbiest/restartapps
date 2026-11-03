namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// An <see cref="ActionConfiguration"/> as the Actions list shows it, with whether it (an application)
/// was running at the last refresh.
/// </summary>
public sealed class ActionItem(ActionConfiguration action, bool running)
{
    public ActionConfiguration Target { get; } = action;

    public string Name => Target.Name;

    public string Kind => KindOf(Target);

    public string Glyph => GlyphOf(Target);

    /// <summary>An application's command line, or what a built-in action does.</summary>
    public string Details => Target switch
    {
        ApplicationAction app => app.Commandline,
        BuiltInAction builtIn => builtIn.Description,
        _ => string.Empty
    };

    public string StopMethod => Target switch
    {
        ApplicationAction { StopMethod: DevWorkspaceLauncher.StopMethod.Window } app => $"Close windows ({app.WindowAumid})",
        ApplicationAction { StopMethod: DevWorkspaceLauncher.StopMethod.None } => "Never stopped",
        ApplicationAction => "Kill process",
        _ => string.Empty
    };

    public bool Running { get; } = running;

    public bool Disabled => Target is ApplicationAction { Disabled: true };

    public string Status => Disabled ? "Disabled" : Running ? "Running" : string.Empty;

    public string FavoriteGlyph => LaunchItem.PinGlyph(Target.Favorite);

    public Microsoft.UI.Xaml.Media.Brush FavoriteBrush => LaunchItem.PinBrush(Target.Favorite);

    /// <summary>The kind as the lists show it.</summary>
    public static string KindOf(ActionConfiguration action) => action is BuiltInAction ? "Built-in" : "Application";

    public static string GlyphOf(ActionConfiguration action) => action is BuiltInAction ? "\uE7F4" : "\uE71D";
}
