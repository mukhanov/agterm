using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>Which agterm is serving this control socket. Derived once by the app and projected unchanged
/// into both <c>tree</c> and <c>version</c>, so the two can never disagree.</summary>
public sealed class AppIdentity
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";

    /// <summary>The build's git commit, omitted for a build that recorded none.</summary>
    [JsonPropertyName("commit")] public string? Commit { get; set; }

    public AppIdentity() { }

    public AppIdentity(string version, string? commit = null)
    {
        Version = version;
        Commit = commit;
    }

    /// <summary>Drop a commit that is absent, empty, or the literal "unknown" the build emits when git
    /// cannot answer, so no caller renders "0.24.0 (unknown)".</summary>
    public static AppIdentity FromRecorded(string version, string? recordedCommit) =>
        new(version, recordedCommit is null or "" or "unknown" ? null : recordedCommit);
}

/// <summary>The addressed surface's cursor position for <c>surface.cursor</c>: a zero-based column,
/// counted from the left edge of the grid.</summary>
public sealed class ControlCursor
{
    [JsonPropertyName("column")] public int Column { get; set; }

    public ControlCursor() { }

    public ControlCursor(int column) => Column = column;
}
