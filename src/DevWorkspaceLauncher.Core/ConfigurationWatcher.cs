namespace DevWorkspaceLauncher;

/// <summary>
/// Reloads the configuration file when something else writes it.
/// <para>
/// Editors write in bursts, and often through a temporary file plus a rename, so changes are debounced.
/// A file still locked by its writer is retried a few times. Content equal to <see cref="LastKnownJson"/>
/// is ignored: the owner sets it to whatever it saves, so its own writes do not come back as reloads,
/// while another app writing the same file does.
/// </para>
/// </summary>
public sealed class ConfigurationWatcher : IDisposable
{
    private const int DebounceMilliseconds = 400;
    private const int MaxRetries = 5;

    private readonly string _path;
    private readonly Func<string, ConfigurationSnapshot> _load;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private SynchronizationContext? _context;
    private string _lastKnownJson = string.Empty;
    private int _retries;

    public ConfigurationWatcher(string path)
        : this(path, AppConfigurationStore.Load)
    {
    }

    internal ConfigurationWatcher(string path, Func<string, ConfigurationSnapshot> load)
    {
        _path = path;
        _load = load;
        _timer = new Timer(_ => Reload());
    }

    /// <summary>The JSON last loaded or saved by the owner; a file with this content is not reloaded.</summary>
    public string LastKnownJson
    {
        get { lock (_gate) { return _lastKnownJson; } }
        set { lock (_gate) { _lastKnownJson = value; } }
    }

    /// <summary>Raised with the new configuration, on the synchronization context <see cref="Start"/> ran on.</summary>
    public event EventHandler<ConfigurationSnapshot>? Reloaded;

    /// <summary>Raised with a log line when the file changed but could not be loaded.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>Starts watching. Throws if the folder cannot be watched.</summary>
    public void Start()
    {
        _context = SynchronizationContext.Current;

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Folder not found: {directory}");
        }

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            _retries = 0;
        }

        _timer.Change(DebounceMilliseconds, Timeout.Infinite);
    }

    private void Reload()
    {
        ConfigurationSnapshot snapshot;
        try
        {
            snapshot = _load(_path);
        }
        catch (IOException) when (TryRetry())
        {
            // Still locked by the writer - try again shortly.
            _timer.Change(DebounceMilliseconds, Timeout.Infinite);
            return;
        }
        catch (Exception ex)
        {
            Raise(() => Failed?.Invoke(this, $"Failed to reload configuration: {ex.Message}"));
            return;
        }

        lock (_gate)
        {
            if (snapshot.Json == _lastKnownJson)
            {
                return;
            }

            _lastKnownJson = snapshot.Json;
        }

        Raise(() => Reloaded?.Invoke(this, snapshot));
    }

    private bool TryRetry()
    {
        lock (_gate)
        {
            if (_retries >= MaxRetries)
            {
                return false;
            }

            _retries++;
            return true;
        }
    }

    private void Raise(Action raise)
    {
        if (_context is null)
        {
            raise();
        }
        else
        {
            _context.Post(_ => raise(), null);
        }
    }
}
