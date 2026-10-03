using System.Globalization;
using System.Text.Json;

namespace Agterm.Core.Control;

/// <summary>The outcome of resolving a control target string against a candidate id set.</summary>
public readonly record struct TargetResolution
{
    public enum Outcome
    {
        Resolved,
        NotFound,
        Ambiguous,
    }

    private TargetResolution(Outcome outcome, Guid id, IReadOnlyList<Guid> hits)
    {
        Result = outcome;
        Id = id;
        Hits = hits;
    }

    public Outcome Result { get; }
    public Guid Id { get; }
    public IReadOnlyList<Guid> Hits { get; }

    public static TargetResolution ResolvedOf(Guid id) => new(Outcome.Resolved, id, []);
    public static TargetResolution NotFoundOf() => new(Outcome.NotFound, Guid.Empty, []);
    public static TargetResolution AmbiguousOf(IReadOnlyList<Guid> hits) => new(Outcome.Ambiguous, Guid.Empty, hits);
}

/// <summary>
/// Pure resolvers shared by the socket server and any client so the wire contract cannot drift — a direct
/// port of agtermCore's ControlResolve.
/// </summary>
public static class ControlResolve
{
    /// <summary>
    /// Resolve a target string against a candidate id set. Matching order: empty → notFound (an empty
    /// prefix would otherwise match everything); "active" → the active id or notFound; exact uuidString
    /// (case-insensitive) → resolved; otherwise a prefix match on the lowercased uuidString: 1 hit →
    /// resolved, 0 → notFound, ≥2 → ambiguous.
    /// </summary>
    public static TargetResolution Resolve(string target, IReadOnlyList<Guid> candidates, Guid? active)
    {
        if (target.Length == 0) return TargetResolution.NotFoundOf();

        if (target == "active")
            return active is { } activeId ? TargetResolution.ResolvedOf(activeId) : TargetResolution.NotFoundOf();

        var needle = target.ToLowerInvariant();
        foreach (var candidate in candidates)
            if (string.Equals(WireId(candidate), needle, StringComparison.OrdinalIgnoreCase))
                return TargetResolution.ResolvedOf(candidate);

        var hits = candidates.Where(c => WireId(c).StartsWith(needle, StringComparison.OrdinalIgnoreCase)).ToList();
        return hits.Count switch
        {
            0 => TargetResolution.NotFoundOf(),
            1 => TargetResolution.ResolvedOf(hits[0]),
            _ => TargetResolution.AmbiguousOf(hits),
        };
    }

    /// <summary>The canonical not-found control error for a target resolution miss.</summary>
    public static string NotFoundMessage(string noun, string target) => $"no such {noun}: {target}";

    /// <summary>The canonical ambiguous-prefix control error, listing matching ids by their first 8 characters.</summary>
    public static string AmbiguousMessage(string noun, string target, IReadOnlyList<Guid> hits)
    {
        var listed = string.Join(", ", hits.Select(h => WireId(h)[..8]));
        return $"ambiguous {noun} prefix '{target}' → {listed}";
    }

    /// <summary>The canonical control error for an unresolved target. Resolved maps to not-found so callers
    /// that resolve an id but cannot find its owner keep the same wire contract as a normal miss.</summary>
    public static string ErrorMessage(string noun, string target, TargetResolution resolution) =>
        resolution.Result == TargetResolution.Outcome.Ambiguous
            ? AmbiguousMessage(noun, target, resolution.Hits)
            : NotFoundMessage(noun, target);

    /// <summary>Derive the control socket path: stateDir/agterm.sock when set, else appSupport/agterm.sock.</summary>
    public static string SocketPath(string? stateDir, string appSupport) =>
        Path.Combine(stateDir ?? appSupport, "agterm.sock");

    /// <summary>The control socket's ownership lock path.</summary>
    public static string OwnershipLockPath(string socketPath) => socketPath + ".lock";

    /// <summary>Ids travel uppercase on the wire, exactly like Swift's UUID.uuidString.</summary>
    public static string WireId(Guid id) => id.ToString("D").ToUpperInvariant();

    /// <summary>Parses the canonical hyphenated UUID form, case-insensitively, like UUID(uuidString:).</summary>
    public static Guid? ParseWireId(string text) =>
        Guid.TryParseExact(text, "D", out var id) ? id : null;
}

/// <summary>The outcome of checking a session.context value, carrying the message the control response
/// reports on rejection — a port of Session.validateContext.</summary>
public static class SessionContextValidation
{
    public const int ByteLimit = 256;

    public static (string? Value, string? Error) Validate(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return (null, "context must not be empty (use --clear to remove it)");
        if (System.Text.Encoding.UTF8.GetByteCount(trimmed) > ByteLimit)
            return (null, $"context must be at most {ByteLimit} UTF-8 bytes");
        // The scan reads raw, NOT trimmed: trimming first would silently repair "PR #517\n" into a valid
        // value, both accepting input the contract rejects and letting the snapshot decoder rewrite a
        // hand-edited value instead of dropping it.
        foreach (var rune in raw.EnumerateRunes())
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
            if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator
                or UnicodeCategory.ParagraphSeparator)
                return (null, "context must not contain control characters or line breaks");
        }
        return (trimmed, null);
    }
}

/// <summary>Port of WatermarkConfig.isValidColorHex: an optional leading '#', then exactly six ASCII hex
/// digits.</summary>
public static class ColorHex
{
    public static bool IsValid(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        return s.Length == 6 && s.All(c => char.IsAsciiHexDigit(c));
    }
}
