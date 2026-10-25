using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevWorkspaceLauncher.WinUI;

internal static class Dialogs
{
    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    public static async Task<string?> PromptAsync(XamlRoot root, string title, string label, string initial)
    {
        var input = new TextBox { Header = label, Text = initial, MinWidth = 320 };
        input.SelectAll();

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = input,
            PrimaryButtonText = "OK",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = message,
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
