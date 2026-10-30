using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>
/// The full log, newest at the bottom; the banner only ever shows the latest line. With a search, only
/// the matching lines.
/// </summary>
public sealed partial class LogPage : Page
{
    public LogPage()
    {
        InitializeComponent();
        Lines.ItemsSource = App.State.Log;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.State.Log.CollectionChanged += Log_CollectionChanged;
        ScrollToEnd();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        App.State.Log.CollectionChanged -= Log_CollectionChanged;
    }

    private void Log_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (Search.Text.Length > 0)
        {
            Filter();
        }

        ScrollToEnd();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        Filter();
        ScrollToEnd();
    }

    /// <summary>The live log without a search; a snapshot of the matching lines with one.</summary>
    private void Filter() =>
        Lines.ItemsSource = Search.Text.Length == 0
            ? App.State.Log
            : App.State.Log.Where(line => ItemActions.Matches(Search.Text, line)).ToList();

    private void ScrollToEnd()
    {
        if (Lines.ItemsSource is IList<string> { Count: > 0 } lines)
        {
            Lines.ScrollIntoView(lines[^1]);
        }
    }
}
