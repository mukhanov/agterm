namespace Agterm.Core.Model;

/// <summary>
/// The minimal surface contract a session owns — the seam behind which the terminal engine
/// (ConPTY + VT + renderer on Windows) hides, mirroring agtermCore's TerminalSurface protocol.
/// The concrete control lives in Agterm.Terminal; the model never renders.
/// </summary>
public interface IPaneSurface : IDisposable
{
    /// <summary>Free the engine resources. Exactly-once, owned by the session.</summary>
    void Teardown();

    /// <summary>Promote the split survivor into the primary slot after the primary exits.</summary>
    void PromoteToPrimaryPane();

    /// <summary>Whether the engine surface exists and its program has spawned.</summary>
    bool IsRealized { get; }

    /// <summary>The stable spawn token baked as AGTERM_PANE_ID; survives swap/promotion.</summary>
    string PaneToken { get; }

    /// <summary>Write one printable text burst into the pane (no Return encoding).</summary>
    void TypeText(string text);

    /// <summary>Send exactly one Return keypress.</summary>
    void PressReturn();

    /// <summary>Read the visible viewport (all=false) or viewport+scrollback (all=true) as plain text.</summary>
    string ReadScreenText(bool all);

    /// <summary>Read the last N content lines of the full buffer, trailing blank rows trimmed.</summary>
    string ReadScreenLines(int lines);

    /// <summary>The zero-based cursor column.</summary>
    int ReadCursorColumn();

    /// <summary>The current selection, or null when none.</summary>
    string? ReadSelection();

    /// <summary>Current live font size in points, null when unrealized.</summary>
    double? CurrentFontSize();

    /// <summary>Apply a font-size action: "increase_font_size:1", "decrease_font_size:1", "reset_font_size".</summary>
    void PerformFontAction(string action);

    /// <summary>The OSC 7 working directory the pane last reported.</summary>
    event Action<string>? CwdChanged;

    /// <summary>The OSC 0/2 title the pane last reported.</summary>
    event Action<string>? TitleChanged;

    /// <summary>Desktop notification request (OSC 9/777).</summary>
    event Action<string, string>? NotificationRequested;

    /// <summary>The pane's process exited (code).</summary>
    event Action<int>? Exited;
}

/// <summary>Strips C0 controls and DEL, the way TerminalText.sanitized feeds sidebar names.</summary>
public static class TerminalText
{
    public static string Sanitized(string value)
    {
        var hasControls = value.Any(c => c < 0x20 || c == 0x7F);
        if (!hasControls) return value;
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
            if (c >= 0x20 && c != 0x7F)
                builder.Append(c);
        return builder.ToString();
    }
}
