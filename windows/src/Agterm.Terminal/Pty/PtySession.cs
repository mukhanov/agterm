using System.Runtime.InteropServices;
using System.Text;
using static Agterm.Terminal.Pty.ConPty;

namespace Agterm.Terminal.Pty;

/// <summary>
/// One ConPTY session: pipes, the pseudoconsole, the child process, and the background read pump.
/// Feed/output never touch the UI thread — Output fires from the pump thread and the host marshals.
/// </summary>
public sealed class PtySession : IDisposable
{
    private readonly IntPtr _pseudoConsole;
    private readonly IntPtr _inputWrite;   // app → child stdin
    private readonly IntPtr _outputRead;   // child stdout → app
    private readonly IntPtr _process;
    private readonly Thread _pump;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private volatile bool _disposed;

    /// <summary>Raw VT bytes from the child, on the pump thread.</summary>
    public event Action<byte[]>? Output;

    /// <summary>The child exited with this code, on the watch thread.</summary>
    public event Action<int>? Exited;

    public int ProcessId { get; }

    private PtySession(IntPtr pseudoConsole, IntPtr inputWrite, IntPtr outputRead, IntPtr process, int processId)
    {
        _pseudoConsole = pseudoConsole;
        _inputWrite = inputWrite;
        _outputRead = outputRead;
        _process = process;
        ProcessId = processId;
        _pump = new Thread(PumpLoop) { IsBackground = true, Name = $"agterm-pty-{processId}" };
        _pump.Start();
        var watcher = new Thread(() =>
        {
            WaitForSingleObject(process, unchecked((uint)-1)); // INFINITE
            GetExitCodeProcess(process, out var code);
            Exited?.Invoke((int)code);
        })
        { IsBackground = true, Name = $"agterm-pty-exit-{processId}" };
        watcher.Start();
    }

    /// <summary>Spawns the profile's command line in a pseudoconsole seeded at cols×rows cells.</summary>
    public static PtySession Start(string commandLine, string? workingDirectory, IReadOnlyDictionary<string, string> environment,
        int cols = 80, int rows = 24)
    {
        var security = new Interop.SecurityAttributes { Length = Marshal.SizeOf<Interop.SecurityAttributes>() };
        if (!CreatePipe(out var inputRead, out var inputWrite, ref security, 0))
            throw new IOException("ConPTY input pipe failed");
        if (!CreatePipe(out var outputRead, out var outputWrite, ref security, 0))
            throw new IOException("ConPTY output pipe failed");

        var size = new Coord((short)cols, (short)rows);
        var created = CreatePseudoConsole(size, inputRead, outputWrite, IntPtr.Zero, out var pseudoConsole);
        // the pseudoconsole holds its ends from here
        CloseHandle(inputRead);
        CloseHandle(outputWrite);
        if (created != 0)
            throw new IOException($"CreatePseudoConsole failed: {created}");

        // attribute list: the STARTUPINFOEX carries the pseudoconsole handle to the child
        var bufferSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bufferSize);
        var attributeList = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref bufferSize))
                throw new IOException("InitializeProcThreadAttributeList failed");
            var pseudoConsolePtr = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(pseudoConsolePtr, pseudoConsole);
                if (!UpdateProcThreadAttribute(attributeList, 0,
                        (IntPtr)ProcThreadAttributePseudoConsole, pseudoConsolePtr,
                        (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                    throw new IOException("UpdateProcThreadAttribute failed");
            }
            finally
            {
                Marshal.FreeHGlobal(pseudoConsolePtr);
            }

            var startup = new StartupInfoEx
            {
                StartupInfo = { Cb = Marshal.SizeOf<StartupInfoEx>() },
                AttributeList = attributeList,
            };

            var environmentBlock = BuildEnvironment(environment);
            var command = new StringBuilder(commandLine);
            if (!CreateProcessW(null, command.ToString(), IntPtr.Zero, IntPtr.Zero,
                    inheritHandles: false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    environmentBlock,
                    workingDirectory,
                    ref startup, out var information))
                throw new IOException($"CreateProcessW failed: {Marshal.GetLastWin32Error()}");
            Marshal.FreeHGlobal(environmentBlock);
            CloseHandle(information.Thread);

            return new PtySession(pseudoConsole, inputWrite, outputRead, information.Process, information.ProcessId);
        }
        catch
        {
            Marshal.FreeHGlobal(attributeList);
            throw;
        }
    }

    /// <summary>An ordered UTF-16 environment block: inherited variables overridden by the AGTERM_ set.</summary>
    private static IntPtr BuildEnvironment(IReadOnlyDictionary<string, string> overrides)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && entry.Value is string value)
                variables[key] = value;
        foreach (var (key, value) in overrides)
            variables[key] = value;

        var size = 4; // trailing double-NUL
        foreach (var (key, value) in variables)
            size += Encoding.Unicode.GetByteCount($"{key}={value}") + 2;
        var block = Marshal.AllocHGlobal(size);
        var offset = 0;
        var bytes = new byte[size];
        foreach (var (key, value) in variables)
        {
            var entry = Encoding.Unicode.GetBytes($"{key}={value}");
            entry.CopyTo(bytes, offset);
            offset += entry.Length + 2; // entry + its NUL
        }
        Marshal.Copy(bytes, 0, block, size);
        return block;
    }

    /// <summary>Resize the pseudoconsole grid, in cells. Debounce at the caller.</summary>
    public void Resize(int cols, int rows) =>
        ResizePseudoConsole(_pseudoConsole, new Coord((short)cols, (short)rows));

    /// <summary>Writes raw bytes to the child's stdin (UTF-8 text or key encodings).</summary>
    public void Write(byte[] data)
    {
        if (_disposed) return;
        if (!WindowsWriteFile(_inputWrite, data, (uint)data.Length, out var written, IntPtr.Zero)
            || written != data.Length)
            throw new IOException($"pty write failed: {Marshal.GetLastWin32Error()}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WindowsWriteFile(IntPtr handle, byte[] buffer, uint bytesToWrite,
        out uint bytesWritten, IntPtr overlapped);

    private void PumpLoop()
    {
        while (!_disposed)
        {
            int read;
            try
            {
                read = ReadFile(_outputRead, _buffer, (uint)_buffer.Length, out var bytesRead, IntPtr.Zero)
                    ? (int)bytesRead
                    : -1;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            if (read <= 0)
                break;
            var chunk = new byte[read];
            Array.Copy(_buffer, chunk, read);
            Output?.Invoke(chunk);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr handle, byte[] buffer, uint bytesToRead, out uint bytesRead,
        IntPtr overlapped);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // closing the app-side pipes makes the pump's ReadFile return and the watcher see child exit
        CloseHandle(_inputWrite);
        CloseHandle(_outputRead);
        ClosePseudoConsole(_pseudoConsole);
        CloseHandle(_process);
    }
}
