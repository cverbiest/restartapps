using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace DevWorkspaceLauncher.WinUI;

/// <summary>
/// Lets the app see raw window messages, which WinUI does not surface - in particular the
/// WM_SETTINGCHANGE "Environment" broadcast that <see cref="UserEnvironment"/> reacts to.
/// </summary>
internal static class WindowMessageHook
{
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc callback, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    // Held so the garbage collector does not free the delegate while Windows still calls it.
    private static readonly List<SubclassProc> Callbacks = [];

    public static void Attach(Window window, Action<uint, IntPtr> onMessage)
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        SubclassProc callback = (hWnd, message, wParam, lParam, _, _) =>
        {
            onMessage(message, lParam);
            return DefSubclassProc(hWnd, message, wParam, lParam);
        };

        Callbacks.Add(callback);
        SetWindowSubclass(handle, callback, (UIntPtr)1, UIntPtr.Zero);
    }
}
