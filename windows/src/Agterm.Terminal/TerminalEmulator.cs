using System.Text;
using Agterm.Core;
using Agterm.Core.Model;
using Agterm.Terminal.Pty;
using XtermSharp;
using XTerminal = XtermSharp.Terminal;
using Rune = System.Rune;

namespace Agterm.Terminal;

/// <summary>
/// The IPaneSurface adapter: one XtermSharp Terminal fed by one ConPTY session. Keystrokes go to the pty
/// as UTF-8 runs with one CR per Return — the same ptyBytes parity macOS's zmx path uses — and readback
/// walks the engine's buffer with the macOS session.text rules (viewport by default, screen+scrollback
/// with all/lines, trailing blanks trimmed).
/// </summary>
public sealed class TerminalEmulator : ITerminalDelegate, IPaneSurface, IDisposable
{
    private readonly XTerminal _terminal;
    private readonly PtySession _pty;
    private readonly SelectionService _selection;
    private double _fontSize;

    /// <summary>The live engine buffer for the UI renderer (phase 5); the model never reads it.</summary>
    public XTerminal EngineBuffer => _terminal;

    public TerminalEmulator(TerminalSpawn spawn)
    {
        PaneToken = spawn.PaneToken ?? Guid.NewGuid().ToString("N")[..12];
        _fontSize = spawn.FontSize ?? 12;
        _terminal = new XTerminal(this, new TerminalOptions { Cols = spawn.Cols ?? 80, Rows = spawn.Rows ?? 24 });
        _selection = new SelectionService(_terminal);
        _pty = PtySession.Start(spawn.CommandLine, spawn.WorkingDirectory, spawn.Environment,
            spawn.Cols ?? 80, spawn.Rows ?? 24, onOutput: chunk =>
            {
                // a parser exception on the pump thread would kill the whole process; log and resync
                try { _terminal.Feed(chunk); }
                catch (Exception e)
                {
                    TerminalDiagnostics.Write("feed: " + e.Message);
                }
            });
        _pty.Exited += code => Exited?.Invoke(code);
    }

    // --- ITerminalDelegate (engine → host) -------------------------------------

    public void ShowCursor(XTerminal source) { }

    public void SetTerminalTitle(XTerminal source, string title) => TitleChanged?.Invoke(title);

    public void SetTerminalIconTitle(XTerminal source, string title) { }

    public void SizeChanged(XTerminal source) { }

    /// <summary>The engine's replies (DA, cursor-position reports) go back to the child verbatim.</summary>
    public void Send(byte[] data) => _pty.Write(data);

    public string WindowCommand(XTerminal source, WindowManipulationCommand command, params int[] args) => "";

    public bool IsProcessTrusted() => true;

    public void ReportWorkingDirectory(XTerminal source, string path)
    {
        // OSC 7 carries a file:// URL; strip to the local path (parity with macOS applyPwd)
        var value = path.StartsWith("file://", StringComparison.Ordinal) ? path["file://".Length..] : path;
        CwdChanged?.Invoke(Uri.UnescapeDataString(value));
    }

    // --- IPaneSurface (host → engine) ------------------------------------------

    public void Teardown() => Dispose();

    public void PromoteToPrimaryPane()
    {
        // the pane token survives promotion by construction; nothing engine-side to move
    }

    public bool IsRealized { get; private set; } = true;

    public string PaneToken { get; }

    public void TypeText(string text) => _pty.Write(Encoding.UTF8.GetBytes(text));

    public void PressReturn() => _pty.Write([(byte)'\r']);

    public int ReadCursorColumn() => _terminal.Buffer.X;

    public string? ReadSelection()
    {
        var text = _selection.GetSelectedText();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public double? CurrentFontSize() => _fontSize;

    public void PerformFontAction(string action)
    {
        // font actions the model routes here; the renderer consumes FontSize on its next frame
        _fontSize = action switch
        {
            "increase_font_size:1" => _fontSize + 1,
            "decrease_font_size:1" => Math.Max(6, _fontSize - 1),
            "reset_font_size" => 12,
            _ => _fontSize, // paste/select_all are binding actions the surface handles at the control layer
        };
    }

    /// <summary>The visible viewport as plain text, wrapped-as-rendered, trailing blanks trimmed.</summary>
    public string ReadScreenText(bool all)
    {
        var buffer = _terminal.Buffer;
        var lines = buffer.Lines;
        if (!all)
        {
            var rows = new List<string>(buffer.Rows);
            for (var row = 0; row < buffer.Rows; row++)
            {
                var index = Mod(buffer.YDisp + row, lines.Length);
                rows.Add(LineText(lines[index]));
            }
            return string.Join("\n", rows);
        }
        var allRows = new List<string>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
            allRows.Add(LineText(lines[index]));
        return string.Join("\n", allRows);
    }

    /// <summary>The last N content lines of the full buffer, trailing blank rows dropped first.</summary>
    public string ReadScreenLines(int lines)
    {
        var rows = ReadScreenText(all: true).Split('\n').ToList();
        while (rows.Count > 0 && rows[^1].Length == 0)
            rows.RemoveAt(rows.Count - 1);
        if (rows.Count == 0) return "";
        var take = Math.Min(lines, rows.Count);
        return string.Join("\n", rows[^take..]);
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static string LineText(BufferLine line)
    {
        var builder = new StringBuilder(line.Length);
        for (var column = 0; column < line.Length; column++)
        {
            var character = line[column];
            if (character.Code == 0)
                builder.Append(' ');
            else
                builder.Append(character.Rune.ToString());
        }
        return builder.ToString().TrimEnd();
    }

    public void Resize(int cols, int rows)
    {
        _terminal.Resize(cols, rows);
        _pty.Resize(cols, rows);
    }

#pragma warning disable CS0067
    public event Action<string>? CwdChanged;
    public event Action<string>? TitleChanged;
    public event Action<string, string>? NotificationRequested;
    public event Action<int>? Exited;
#pragma warning restore CS0067

    public void Dispose() => _pty.Dispose();
}

/// <summary>What a pane's factory hands the engine: the process, its environment, and the seed grid.</summary>
public sealed record TerminalSpawn(
    string CommandLine,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int? Cols = null,
    int? Rows = null,
    double? FontSize = null,
    string? PaneToken = null);
