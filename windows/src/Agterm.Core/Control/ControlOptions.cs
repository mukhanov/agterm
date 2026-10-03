namespace Agterm.Core.Control;

/// <summary>An independent consumer's position within one app run.</summary>
public readonly record struct ControlEventCursor(Guid Run, ulong After);

/// <summary>Host-facing events.read options after dispatcher validation and normalization.</summary>
public sealed record ControlEventReadOptions(
    ControlEventCursor? Cursor,
    IReadOnlySet<Protocol.ControlEventKind>? Kinds,
    int Limit);

/// <summary>Host-facing session.new options after dispatcher validation.</summary>
public sealed record ControlSessionCreateOptions(
    string? Window,
    string? Cwd,
    string? Workspace,
    string? WorkspaceName,
    bool? CreateWorkspace,
    string? Command,
    bool? Wait,
    string? Name,
    string? After,
    string? Before,
    bool NoSelect);

public sealed record ControlSessionTypeOptions(string Text, bool Select, StatusPane? Pane);

public sealed record ControlSessionTextOptions(StatusPane? Pane, string? PaneId, bool All, int? Lines);

/// <summary>An agent-status write: a fresh ephemeral indicator per call, so omitted overrides clear.</summary>
public sealed record ControlSessionStatusUpdate(
    StatusKind Status,
    bool? Blink,
    bool? AutoReset,
    string? Sound,
    string? Color,
    StatusShape? Shape,
    StatusPane? Pane,
    string? PaneId);

/// <summary>One session.move placement intent, already exclusive-validated by the dispatcher.</summary>
public abstract record ControlSessionMove
{
    /// <summary>Reorder one session within its workspace.</summary>
    public sealed record Reorder(ReorderDirection Direction) : ControlSessionMove;

    /// <summary>Relocate into a workspace (by target string), appending.</summary>
    public sealed record ToWorkspace(string Workspace) : ControlSessionMove;

    /// <summary>Place relative to an anchor session; the anchor carries the destination workspace.</summary>
    public sealed record Place(string Anchor, bool After) : ControlSessionMove;
}

public abstract record ControlSplitResize
{
    public sealed record Ratio(double Value) : ControlSplitResize;

    /// <summary>A signed relative nudge: positive grows the primary pane.</summary>
    public sealed record Delta(double Value) : ControlSplitResize;
}
