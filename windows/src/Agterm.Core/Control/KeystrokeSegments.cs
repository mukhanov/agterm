namespace Agterm.Core.Control;

/// <summary>A host-free description of the synthetic keystrokes used by session.type.</summary>
public abstract record KeystrokeSegment
{
    public sealed record Text(string Value) : KeystrokeSegment;

    public sealed record ReturnKey() : KeystrokeSegment;
}

public sealed record PacedKeystrokes(IReadOnlyList<KeystrokeSegment> Head, bool PacedReturn);

/// <summary>
/// Splits injected text into printable runs and Return keypresses — a port of agtermCore's
/// KeystrokeSegments.
/// </summary>
public static class KeystrokeSegments
{
    /// <summary>The seconds between a payload's text and its final Return: Claude Code takes a Return
    /// that arrives in the same burst as a long text run as pasted content and does not submit (#679).</summary>
    public const double SubmitGapSeconds = 0.01;

    /// <summary>Holds back the final Return of a payload that ends in a line ending and has text before it;
    /// earlier Returns stay in Head, and a payload of Returns alone is not paced.</summary>
    public static PacedKeystrokes Paced(string text)
    {
        var segments = Split(text);
        var hasText = segments.Any(s => s is KeystrokeSegment.Text);
        if (segments.Count > 0 && segments[^1] is KeystrokeSegment.ReturnKey && hasText)
            return new PacedKeystrokes(segments.Take(segments.Count - 1).ToList(), true);
        return new PacedKeystrokes(segments, false);
    }

    /// <summary>Normalizes CRLF and CR line endings to LF, then emits every line ending as exactly one Return.</summary>
    public static List<KeystrokeSegment> Split(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var parts = normalized.Split('\n');
        var segments = new List<KeystrokeSegment>(parts.Length * 2);
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0)
                segments.Add(new KeystrokeSegment.Text(parts[index]));
            if (index < parts.Length - 1)
                segments.Add(new KeystrokeSegment.ReturnKey());
        }
        return segments;
    }
}
