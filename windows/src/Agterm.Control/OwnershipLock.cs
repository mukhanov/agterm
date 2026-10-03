namespace Agterm.Core.Control;

/// <summary>
/// The control socket's single-instance ownership, held for the process lifetime. On macOS this is a
/// non-blocking flock over &lt;socket&gt;.lock decided at INIT (the launch window's surfaces snapshot
/// AGTERM_SOCKET before start runs); on Windows the kernel grants the same semantics through an exclusive
/// FileStream — atomic against two instances launching together, released on process death. Never delete
/// the lock file: the next instance would lock a fresh inode and exclude nobody.
/// </summary>
public sealed class OwnershipLock : IDisposable
{
    private FileStream? _stream;

    /// <summary>Try to take ownership; false means another instance owns the socket. Never throws for the
    /// contended case — a contended open is the protocol, not an error.</summary>
    public bool TryAcquire(string lockPath)
    {
        Release();
        try
        {
            _stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool IsHeld => _stream is not null;

    public void Release()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Release();
}

/// <summary>
/// Resolves the rendezvous path both sides agree on: --socket / AGTERM_CONTROL_SOCKET /
/// &lt;AGTERM_STATE_DIR&gt;/agterm.sock / the platform default directory. The Windows default is
/// %LOCALAPPDATA%\agterm, matching the Swift branch the Windows CLI carries.
/// </summary>
public static class SocketPathResolver
{
    public static string Resolve(string? explicitPath = null, IReadOnlyDictionary<string, string>? env = null)
    {
        var environment = env ?? EnvironmentVariables.Default;
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath!;
        if (environment.TryGetValue("AGTERM_CONTROL_SOCKET", out var fromEnv) && fromEnv.Length > 0)
            return fromEnv;
        if (environment.TryGetValue("AGTERM_STATE_DIR", out var stateDir) && stateDir.Length > 0)
            return Path.Combine(stateDir, "agterm.sock");
        return Path.Combine(DefaultDirectory(environment), "agterm.sock");
    }

    public static string DefaultDirectory(IReadOnlyDictionary<string, string>? env = null)
    {
        var environment = env ?? EnvironmentVariables.Default;
        if (OperatingSystem.IsWindows())
        {
            if (environment.TryGetValue("LOCALAPPDATA", out var localAppData) && localAppData.Length > 0)
                return Path.Combine(localAppData, "agterm");
            if (environment.TryGetValue("USERPROFILE", out var profile) && profile.Length > 0)
                return Path.Combine(profile, "AppData", "Local", "agterm");
        }
        else
        {
            if (environment.TryGetValue("XDG_STATE_HOME", out var xdgState) && xdgState.Length > 0)
                return Path.Combine(xdgState, "agterm");
            if (environment.TryGetValue("HOME", out var home) && home.Length > 0)
                return Path.Combine(home, ".local", "state", "agterm");
        }
        return Path.Combine(Path.GetTempPath(), "agterm");
    }
}

/// <summary>Environment abstraction for tests.</summary>
public interface EnvironmentVariables
{
    public static readonly IReadOnlyDictionary<string, string> Default =
        Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(entry => (Key: entry.Key as string, Value: entry.Value as string))
            .Where(pair => pair.Key is not null && pair.Value is not null)
            .ToDictionary(pair => pair.Key!, pair => pair.Value!);
}
