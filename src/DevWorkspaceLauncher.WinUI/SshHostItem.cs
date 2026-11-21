namespace DevWorkspaceLauncher.WinUI;

/// <summary>An <see cref="SshHost"/> as the SSH hosts list shows it.</summary>
public sealed class SshHostItem(SshHost host) : GridRow<SshHostItem>
{
    public SshHost Host { get; private set; } = host;

    public override string Key => Host.Alias;

    public string Alias => Host.Alias;
    public string HostName => Host.HostName;
    public string User => Host.User;
    public string IdentityFile => Host.IdentityFile;
    public string Also => Host.Also;

    public string FavoriteGlyph => LaunchItem.PinGlyph(Host.Favorite);

    public Microsoft.UI.Xaml.Media.Brush FavoriteBrush => LaunchItem.PinBrush(Host.Favorite);

    public string FavoriteTooltip => LaunchItem.PinTooltip(Host.Favorite);

    protected override void CopyFrom(SshHostItem fresh) => Host = fresh.Host;
}
