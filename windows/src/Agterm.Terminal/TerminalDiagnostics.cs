namespace Agterm.Terminal;

/// <summary>Optional sink for engine-side diagnostics; the host wires it to its own log at startup.</summary>
public static class TerminalDiagnostics
{
    public static Action<string>? Sink;

    public static void Write(string message) => Sink?.Invoke(message);
}
