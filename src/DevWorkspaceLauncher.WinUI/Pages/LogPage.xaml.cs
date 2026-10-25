using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DevWorkspaceLauncher.WinUI.Pages;

/// <summary>The full log, newest at the bottom; the banner only ever shows the latest line.</summary>
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

    private void Log_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        if (App.State.Log.Count > 0)
        {
            Lines.ScrollIntoView(App.State.Log[^1]);
        }
    }
}
