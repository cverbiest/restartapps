using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DevWorkspaceLauncher;

/// <summary>A credential read from Windows Credential Manager.</summary>
public sealed record StoredCredential(string? UserName, string Password);

/// <summary>Reads, writes and removes Windows Credential Manager entries.</summary>
public static class CredentialStore
{
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_TYPE_DOMAIN_PASSWORD = 2;
    private const int CRED_PERSIST_SESSION = 1;
    private const int ERROR_NOT_FOUND = 1168;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
    private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);

    /// <summary>The name prefix of the generic credentials the RDP session editor offers.</summary>
    public const string SessionCredentialPrefix = "DevLauncher_";

    /// <summary>
    /// The generic credentials whose name starts with <paramref name="prefix"/>, with their usernames,
    /// sorted by name. Passwords are not read.
    /// </summary>
    public static IReadOnlyList<(string Name, string? UserName)> ListGeneric(string prefix)
    {
        if (!CredEnumerate(prefix + "*", 0, out var count, out var array))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ERROR_NOT_FOUND ? [] : throw new System.ComponentModel.Win32Exception(error);
        }

        try
        {
            var found = new List<(string Name, string? UserName)>();
            for (var i = 0; i < count; i++)
            {
                var credential = Marshal.PtrToStructure<Credential>(Marshal.ReadIntPtr(array, i * IntPtr.Size));
                if (credential.Type == CRED_TYPE_GENERIC)
                {
                    found.Add((credential.TargetName, credential.UserName));
                }
            }

            return found.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
        finally
        {
            CredFree(array);
        }
    }

    /// <summary>The generic credential named <paramref name="target"/>, or null when there is none.</summary>
    public static StoredCredential? ReadGeneric(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            return error == ERROR_NOT_FOUND
                ? null
                : throw new System.ComponentModel.Win32Exception(error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(pointer);
            var password = credential.CredentialBlobSize == 0
                ? string.Empty
                : Marshal.PtrToStringUni(credential.CredentialBlob, credential.CredentialBlobSize / 2);
            return new StoredCredential(credential.UserName, password);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    /// <summary>
    /// Whether any credential - generic or the domain kind mstsc's "Remember me" saves - exists for
    /// <paramref name="target"/>. Only its existence is checked; nothing in it is read.
    /// </summary>
    public static bool Exists(string target)
    {
        foreach (var type in new[] { CRED_TYPE_GENERIC, CRED_TYPE_DOMAIN_PASSWORD })
        {
            if (CredRead(target, type, 0, out var pointer))
            {
                CredFree(pointer);
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes a generic credential that lasts at most until logoff.</summary>
    public static void WriteGenericForSession(string target, string userName, string password)
    {
        var blob = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var credential = new Credential
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                UserName = userName,
                CredentialBlob = blob,
                CredentialBlobSize = password.Length * 2,
                Persist = CRED_PERSIST_SESSION
            };

            if (!CredWrite(ref credential, 0))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUnicode(blob);
        }
    }

    public static void DeleteGeneric(string target) => CredDelete(target, CRED_TYPE_GENERIC, 0);

    /// <summary>Opens Windows Credential Manager, where the credentials sessions refer to are kept.</summary>
    public static void OpenManager(Action<string> log)
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.CredentialManager") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log($"Failed to open Credential Manager: {ex.Message}");
        }
    }
}

/// <summary>Starts mstsc for a set of resolved settings.</summary>
public static class RdpLauncher
{
    /// <summary>How long the generated <c>.rdp</c> file is kept for mstsc to read.</summary>
    private static readonly TimeSpan FileLifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Launches one session. Returns the running mstsc process and a task that completes when the
    /// clean-up is done: the temporary file deleted and, when one was written, the temporary
    /// <c>TERMSRV/</c> credential removed after mstsc exits. A caller that may exit before mstsc does
    /// must await it, or the credential stays until logoff.
    /// </summary>
    public static (Process Mstsc, Task Cleanup) Launch(RdpSettings settings, string? label, Action<string> log)
    {
        settings = Copy(settings);
        var name = label ?? settings.Host!;

        string? baseText = null;
        if (settings.RdpFile is { } rdpFile)
        {
            var path = Environment.ExpandEnvironmentVariables(rdpFile);
            baseText = File.Exists(path)
                ? File.ReadAllText(path)
                : throw new RdpLaunchException(RdpExitCode.ConnectionFileNotFound, $"Connection file not found: {path}");
        }

        var (host, port) = RdpResolver.SplitHost(settings.Host!);
        var termsrv = port == 3389 ? $"TERMSRV/{host}" : $"TERMSRV/{host}:{port}";
        StoredCredential? credential = null;
        if (settings.Credential is { } target)
        {
            credential = CredentialStore.ReadGeneric(target)
                ?? throw new RdpLaunchException(RdpExitCode.ProfileNotFound,
                    $"No credential '{target}' in Windows Credential Manager. Add it as a generic credential.",
                    RdpFix.OpenCredentialManager);
            settings.Username = RdpFileWriter.Username(settings, credential.UserName);
            settings.Domain = null;
        }

        var monitor = RdpLayout.PickMonitor(Displays.Monitors(), settings.Monitor);
        var geometry = RdpLayout.Compute(settings, monitor, Displays.Chrome(monitor.Dpi));
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rdp");
        File.WriteAllText(file, RdpFileWriter.Build(settings, geometry, baseText));
        if (settings.SigningCertificate is { } thumbprint)
        {
            Sign(file, thumbprint, log);
        }

        var wroteCredential = false;
        if (credential is not null)
        {
            if (CredentialStore.Exists(termsrv))
            {
                log($"{termsrv} is already in Credential Manager; left alone, so mstsc logs in with that one.");
            }
            else if (settings.Username is null)
            {
                log($"Credential '{settings.Credential}' has no username; mstsc will ask for one.");
            }
            else
            {
                CredentialStore.WriteGenericForSession(termsrv, settings.Username, credential.Password);
                wroteCredential = true;
            }
        }

        Process mstsc;
        try
        {
            var arguments = settings.Admin == true ? $"\"{file}\" /admin" : $"\"{file}\"";
            mstsc = Process.Start(new ProcessStartInfo("mstsc.exe", arguments) { UseShellExecute = false })
                ?? throw new RdpLaunchException(RdpExitCode.GeneralError, "mstsc.exe did not start.");
        }
        catch
        {
            if (wroteCredential)
            {
                CredentialStore.DeleteGeneric(termsrv);
            }

            File.Delete(file);
            throw;
        }

        log($"Connecting to {name} ({geometry.DesktopWidth}x{geometry.DesktopHeight}{(geometry.ScreenModeId == 2 ? ", full screen" : string.Empty)}).");
        var cleanup = CleanUpAsync(mstsc, file, wroteCredential ? termsrv : null);
        if (settings.Title is { } title)
        {
            cleanup = Task.WhenAll(cleanup, SessionWindow.KeepTitleAsync(mstsc, ExpandTitle(title, host, settings.Username)));
        }

        return (mstsc, cleanup);
    }

    /// <summary>
    /// Waits until the session is connected - mstsc keeps its session window hidden until then, showing
    /// only its progress, credential and error dialogs. Ends early when mstsc shows its error, as it does
    /// when the computer cannot be reached, or exits, as it does when the user cancels.
    /// </summary>
    public static async Task<RdpConnectOutcome> WaitForConnectionAsync(Process mstsc, CancellationToken cancellation = default)
    {
        while (!mstsc.HasExited)
        {
            if (SessionWindow.IsShown(mstsc.Id))
            {
                return RdpConnectOutcome.Connected;
            }

            if (SessionWindow.IsShowingError(mstsc.Id))
            {
                return RdpConnectOutcome.Failed;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellation).ConfigureAwait(false);
        }

        return RdpConnectOutcome.Exited;
    }

    /// <summary>Ends an mstsc that failed to connect and is still showing its error.</summary>
    public static void Dismiss(Process mstsc)
    {
        try
        {
            if (!mstsc.HasExited && !SessionWindow.IsShown(mstsc.Id))
            {
                mstsc.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // Exited meanwhile.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Exiting already.
        }
    }

    /// <summary>
    /// Expands <c>%s</c> to the computer and <c>%u</c> to the user, then local environment variables.
    /// A <c>%s</c> or <c>%u</c> that starts a variable name, as in <c>%systemroot%</c>, is left for that.
    /// </summary>
    public static string ExpandTitle(string template, string host, string? user)
    {
        var expanded = System.Text.RegularExpressions.Regex.Replace(
            template,
            "%([su])(?![A-Za-z0-9_]*%)",
            match => match.Groups[1].Value == "s" ? host : user ?? string.Empty);
        return Environment.ExpandEnvironmentVariables(expanded);
    }

    /// <summary>
    /// Signs <paramref name="file"/> in place with rdpsign. A failure is logged, not thrown: the file
    /// still connects, with mstsc's unknown-publisher warning.
    /// </summary>
    private static void Sign(string file, string thumbprint, Action<string> log)
    {
        thumbprint = thumbprint.Replace(" ", string.Empty);
        try
        {
            using var rdpsign = Process.Start(new ProcessStartInfo("rdpsign.exe", $"/sha256 {thumbprint} \"{file}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("rdpsign.exe did not start.");

            var output = rdpsign.StandardOutput.ReadToEndAsync();
            var error = rdpsign.StandardError.ReadToEndAsync();
            if (!rdpsign.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                rdpsign.Kill();
                log("Could not sign the .rdp file: rdpsign did not finish; mstsc will warn about an unknown publisher.");
                return;
            }

            if (rdpsign.ExitCode != 0)
            {
                var message = string.Join(" ", output.Result.Trim(), error.Result.Trim()).Trim();
                log($"Could not sign the .rdp file with certificate {thumbprint}: {message}; mstsc will warn about an unknown publisher.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log($"Could not sign the .rdp file: {ex.Message}; mstsc will warn about an unknown publisher.");
        }
    }

    private static async Task CleanUpAsync(Process mstsc, string file, string? termsrv)
    {
        await Task.Delay(FileLifetime).ConfigureAwait(false);
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Still open; it is in %TEMP% and holds no secret.
        }

        if (termsrv is not null)
        {
            await mstsc.WaitForExitAsync().ConfigureAwait(false);
            CredentialStore.DeleteGeneric(termsrv);
        }
    }

    private static RdpSettings Copy(RdpSettings settings)
    {
        var copy = new RdpSettings();
        copy.Overlay(settings);
        return copy;
    }
}

/// <summary>The <c>rdp</c> command line, run without a window.</summary>
public static class RdpCommand
{
    /// <summary>
    /// Parses, resolves and launches. Returns the exit code and, on failure, the message. Waits for the
    /// clean-up, so the temporary file and credential are gone before the process exits - with a
    /// credential, that means until mstsc exits, whether or not <c>/wait</c> was given.
    /// </summary>
    public static async Task<(int ExitCode, string? Message)> RunAsync(IReadOnlyList<string> args, Func<ConfigurationFile> loadConfig, Action<string> log)
    {
        try
        {
            var commandLine = RdpCommandLine.Parse(args);
            var settings = RdpResolver.Resolve(loadConfig(), commandLine);
            var (mstsc, cleanup) = RdpLauncher.Launch(settings, commandLine.SessionName, log);
            await cleanup;
            if (commandLine.Wait)
            {
                await mstsc.WaitForExitAsync();
            }

            return (RdpExitCode.Ok, null);
        }
        catch (RdpLaunchException ex)
        {
            return (ex.ExitCode, ex.Message);
        }
        catch (Exception ex)
        {
            return (RdpExitCode.GeneralError, ex.Message);
        }
    }

    /// <summary>Whether <c>/batch</c> is among the arguments, so a failure is reported only by exit code.</summary>
    public static bool IsBatch(IEnumerable<string> args) =>
        args.Any(arg => string.Equals(arg, "/batch", StringComparison.OrdinalIgnoreCase));
}

/// <summary>How waiting for an mstsc connection ended.</summary>
public enum RdpConnectOutcome
{
    /// <summary>The session window is shown.</summary>
    Connected,

    /// <summary>mstsc shows its error and waits for the user to dismiss it.</summary>
    Failed,

    /// <summary>mstsc exited without connecting.</summary>
    Exited
}

/// <summary>The main window of an mstsc process.</summary>
internal static class SessionWindow
{
    /// <summary>The window class of mstsc's session window, whether connecting, windowed or full screen.</summary>
    private const string ShellClass = "TscShellContainerClass";

    private const string DialogClass = "#32770";

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string text);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    /// <summary>Whether the session window is visible, which it is once the connection is made.</summary>
    public static bool IsShown(int processId) => Find(processId, ShellClass).Any(IsWindowVisible);

    /// <summary>
    /// Whether mstsc shows its connection error: a task dialog with a single button, OK. Its progress
    /// dialog is a classic dialog with a Cancel button, and its warnings ask a question with two or more.
    /// </summary>
    public static bool IsShowingError(int processId) =>
        Find(processId, DialogClass).Any(dialog =>
        {
            if (!IsWindowVisible(dialog))
            {
                return false;
            }

            var taskDialog = false;
            var buttons = 0;
            EnumChildWindows(dialog, (child, _) =>
            {
                var name = ClassName(child);
                taskDialog |= name == "DirectUIHWND";
                if (name == "Button" && IsWindowVisible(child))
                {
                    buttons++;
                }

                return true;
            }, IntPtr.Zero);

            return taskDialog && buttons == 1;
        });

    /// <summary>
    /// Sets the session window's title, and sets it again whenever mstsc changes it back - it does on
    /// connecting and reconnecting - until mstsc exits.
    /// </summary>
    public static async Task KeepTitleAsync(Process mstsc, string title)
    {
        while (!mstsc.HasExited)
        {
            foreach (var window in Find(mstsc.Id, ShellClass))
            {
                var current = new System.Text.StringBuilder(512);
                GetWindowText(window, current, current.Capacity);
                if (current.ToString() != title)
                {
                    SetWindowText(window, title);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    /// <summary>The top-level windows of <paramref name="processId"/> with window class <paramref name="className"/>.</summary>
    private static List<IntPtr> Find(int processId, string className)
    {
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == processId && ClassName(window) == className)
            {
                found.Add(window);
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static string ClassName(IntPtr window)
    {
        var name = new System.Text.StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }
}
