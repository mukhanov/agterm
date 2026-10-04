using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using Agterm.Core.Protocol;

namespace Agterm.Core.Control;

/// <summary>
/// The control socket server: an AF_UNIX listener serving one newline-delimited JSON request per
/// connection — a port of agterm's ControlServer transport semantics. Ownership is decided at
/// construction (a refused instance advertises &lt;socket&gt;.unavailable through ResolvedSocketPath and
/// never binds); Start retries acquisition so a refused instance takes over once the owner quits.
/// </summary>
public sealed class ControlServer : IDisposable
{
    private readonly OwnershipLock _ownership = new();
    private readonly string _socketPath;
    private readonly ControlDispatcher _dispatcher;
    private readonly Func<Func<ControlResponse>, ControlResponse> _marshal;
    private Socket? _listener;
    private Thread? _acceptThread;
    private volatile bool _stopping;

    public ControlServer(string socketPath, ControlDispatcher dispatcher,
        Func<Func<ControlResponse>, ControlResponse>? marshal = null)
    {
        _socketPath = socketPath;
        _dispatcher = dispatcher;
        _marshal = marshal ?? (work => work());
        Refused = !_ownership.TryAcquire(ControlResolve.OwnershipLockPath(socketPath));
    }

    /// <summary>True when another instance owns the socket; clears on a later successful acquire.</summary>
    public bool Refused { get; private set; }

    /// <summary>The path spawned shells receive as AGTERM_SOCKET: the live default when owned, the
    /// .unavailable rendezvous when refused — never omitted, never the live default while refused.</summary>
    public string ResolvedSocketPath => Refused ? _socketPath + ".unavailable" : _socketPath;

    /// <summary>Bind and serve. Idempotent; retries ownership so a refused instance can take over.</summary>
    public bool Start()
    {
        if (_listener is not null) return true;
        if (!_ownership.IsHeld)
            Refused = !_ownership.TryAcquire(ControlResolve.OwnershipLockPath(_socketPath));
        if (Refused || _stopping) return false;
        if (Encoding.UTF8.GetByteCount(_socketPath) >= 104)
            return false; // sockaddr_un.sun_path caps at 104 bytes incl. NUL

        try
        {
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            }
            catch (SocketException)
            {
                // a stale socket file from a force-quit owner: unlinking it is safe here because the
                // ownership lock proves no live owner; nothing on disk distinguishes the two cases.
                try { File.Delete(_socketPath); } catch (IOException) { }
                listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
            }
            listener.Listen(backlog: 8);
            _listener = listener;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "agterm-control-accept" };
            _acceptThread.Start();
            return true;
        }
        catch (SocketException)
        {
            _listener = null;
            return false;
        }
    }

    private void AcceptLoop()
    {
        var listener = _listener!;
        while (!_stopping)
        {
            // Poll-then-accept rather than a blocking Accept: Close() from Stop() waits for a pending
            // blocking Accept to release the socket, which never happens when nothing more connects —
            // a half-second poll trades a bounded shutdown latency for freedom from that deadlock.
            try
            {
                if (!listener.Poll(500_000, SelectMode.SelectRead))
                    continue;
            }
            catch (SocketException)
            {
                break;
            }
            Socket connection;
            try
            {
                connection = listener.Accept();
            }
            catch (SocketException) when (_stopping)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            if (_stopping)
            {
                try { connection.Close(); } catch (SocketException) { }
                break;
            }
            try
            {
                HandleConnection(connection);
            }
            catch (SocketException)
            {
                // client vanished mid-request; nothing to answer
            }
            finally
            {
                try { connection.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
                try { connection.Close(); } catch (SocketException) { }
            }
        }
    }

    private void HandleConnection(Socket connection)
    {
        connection.ReceiveTimeout = 5000;
        connection.SendTimeout = 5000;
        var line = ReadLine(connection);
        if (line is null)
        {
            WriteReply(connection, ControlResponse.Fail("request too large or read failed"));
            return;
        }
        ControlResponse response;
        try
        {
            var request = ControlJson.Deserialize<ControlRequest>(line);
            response = _marshal(() => _dispatcher.Dispatch(request));
        }
        catch (JsonException exception)
        {
            response = ControlResponse.Fail(ControlWire.InvalidRequestMessage(exception));
        }
        catch (Exception exception)
        {
            // diagnostics: an escaping dispatcher bug must be visible, never a silent dead accept thread
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "agterm-ui.log"),
                DateTime.Now.ToString("HH:mm:ss.fff ") + "dispatch: " + exception + Environment.NewLine);
            response = ControlResponse.Fail("internal error: " + exception.Message);
        }
        WriteReply(connection, response);
    }

    /// <summary>Reads to the newline, enforcing the 1 MiB cap; null on EOF/truncation. A request over the
    /// cap gets the too-large error, never an unbounded buffer.</summary>
    private static string? ReadLine(Socket connection)
    {
        var buffer = new byte[64];
        using var accumulated = new MemoryStream();
        var overallDeadline = Environment.TickCount64 + 10_000;
        while (true)
        {
            if (Environment.TickCount64 > overallDeadline) return null;
            int read;
            try
            {
                var available = connection.Receive(buffer);
                if (available == 0)
                    return accumulated.Length == 0 ? null : Encoding.UTF8.GetString(accumulated.ToArray());
                read = available;
            }
            catch (SocketException)
            {
                return null;
            }
            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            if (newline >= 0)
            {
                accumulated.Write(buffer, 0, newline);
                if (accumulated.Length > ControlWire.MaxRequestLineBytes) return null;
                return Encoding.UTF8.GetString(accumulated.ToArray());
            }
            accumulated.Write(buffer, 0, read);
            if (accumulated.Length > ControlWire.MaxRequestLineBytes) return null;
        }
    }

    private static void WriteReply(Socket connection, ControlResponse response)
    {
        var payload = Encoding.UTF8.GetBytes(ControlJson.Serialize(response) + "\n");
        try
        {
            connection.Send(payload);
        }
        catch (SocketException)
        {
            // the reader is gone; the reply is lost, which the client reports as a read failure
        }
    }

    /// <summary>Stops serving. Releases the lock but never deletes the lock file, and never unlinks a
    /// socket this instance does not own.</summary>
    public void Stop()
    {
        _stopping = true;
        var listener = _listener;
        _listener = null;
        if (listener is not null)
        {
            try { listener.Close(); } catch (SocketException) { }
            if (_ownership.IsHeld)
            {
                try { File.Delete(_socketPath); } catch (IOException) { }
            }
        }
        _acceptThread?.Join(TimeSpan.FromSeconds(2));
        _acceptThread = null;
        _ownership.Release();
    }

    public void Dispose() => Stop();
}
