namespace DevWorkspaceLauncher.WinUI;

/// <summary>An <see cref="RdpSession"/> as the RDP sessions list shows it.</summary>
public sealed class RdpSessionItem(RdpSession session) : GridRow<RdpSessionItem>
{
    public RdpSession Session { get; private set; } = session;

    public override string Key => Session.Name;

    public string Name => Session.Name;
    public string Host => Session.Host ?? string.Empty;
    public string SizeText => Session.SizeText;
    public string Credential => Session.Credential ?? string.Empty;

    public string FavoriteGlyph => LaunchItem.PinGlyph(Session.Favorite);

    public Microsoft.UI.Xaml.Media.Brush FavoriteBrush => LaunchItem.PinBrush(Session.Favorite);

    public string FavoriteTooltip => LaunchItem.PinTooltip(Session.Favorite);

    protected override void CopyFrom(RdpSessionItem fresh) => Session = fresh.Session;
}
