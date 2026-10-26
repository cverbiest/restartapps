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
                    $"No credential '{target}' in Windows Credential Manager. Add it as a generic credential.");
            settings.Username = RdpFileWriter.Username(settings, credential.UserName);
            settings.Domain = null;
        }

        var monitor = RdpLayout.PickMonitor(Displays.Monitors(), settings.Monitor);
        var geometry = RdpLayout.Compute(settings, monitor, Displays.Chrome(monitor.Dpi));
        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.rdp");
        File.WriteAllText(file, RdpFileWriter.Build(settings, geometry, baseText));

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
        return (mstsc, CleanUpAsync(mstsc, file, wroteCredential ? termsrv : null));
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
