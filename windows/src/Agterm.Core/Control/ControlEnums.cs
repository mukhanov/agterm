namespace Agterm.Core.Control;

/// <summary>Agent state for session.status. Wire: idle|active|completed|blocked.</summary>
public enum StatusKind
{
    Idle,
    Active,
    Completed,
    Blocked,
}

public static class StatusKindExtensions
{
    public static string WireName(this StatusKind status) => status switch
    {
        StatusKind.Idle => "idle",
        StatusKind.Active => "active",
        StatusKind.Completed => "completed",
        StatusKind.Blocked => "blocked",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static StatusKind? FromWireName(string wire) => wire switch
    {
        "idle" => StatusKind.Idle,
        "active" => StatusKind.Active,
        "completed" => StatusKind.Completed,
        "blocked" => StatusKind.Blocked,
        _ => null,
    };
}

/// <summary>Which pane a status/input/read addresses. Wire (canonical): left|right|scratch. The parser also
/// accepts the role and position aliases (top/primary, bottom/split).</summary>
public enum StatusPane
{
    Left,
    Right,
    Scratch,
}

public static class StatusPaneExtensions
{
    public static string WireName(this StatusPane pane) => pane switch
    {
        StatusPane.Left => "left",
        StatusPane.Right => "right",
        StatusPane.Scratch => "scratch",
        _ => throw new ArgumentOutOfRangeException(nameof(pane)),
    };

    /// <summary>Parses every accepted positional or role spelling while keeping the canonical wire value
    /// stable for read-back and the AGTERM_PANE environment.</summary>
    public static StatusPane? FromControlName(string name) => name switch
    {
        "left" or "top" or "primary" => StatusPane.Left,
        "right" or "bottom" or "split" => StatusPane.Right,
        "scratch" => StatusPane.Scratch,
        _ => null,
    };
}

/// <summary>The glyph silhouette a status draws. Wire: circle|square|triangle|diamond|capsule|star.</summary>
public enum StatusShape
{
    Circle,
    Square,
    Triangle,
    Diamond,
    Capsule,
    Star,
}

public static class StatusShapeExtensions
{
    public static string WireName(this StatusShape shape) => shape switch
    {
        StatusShape.Circle => "circle",
        StatusShape.Square => "square",
        StatusShape.Triangle => "triangle",
        StatusShape.Diamond => "diamond",
        StatusShape.Capsule => "capsule",
        StatusShape.Star => "star",
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    public static StatusShape? FromWireName(string wire) => wire switch
    {
        "circle" => StatusShape.Circle,
        "square" => StatusShape.Square,
        "triangle" => StatusShape.Triangle,
        "diamond" => StatusShape.Diamond,
        "capsule" => StatusShape.Capsule,
        "star" => StatusShape.Star,
        _ => null,
    };

    /// <summary>The pipe-joined accepted names for the dispatcher's rejection message.</summary>
    public const string ValidNamesList = "circle|square|triangle|diamond|capsule|star";
}

/// <summary>Parsed binary control mode with the shared default/toggle semantics.</summary>
public enum ToggleMode
{
    On,
    Off,
    Toggle,
}

public static class ToggleModeExtensions
{
    /// <summary>Parse a mode string (null = toggle); the on/off tokens keep each command's own wire
    /// spellings ("show"/"hide" for sidebar and zoom, "on"/"off" elsewhere).</summary>
    public static ToggleMode? Parse(string? mode, string onToken = "on", string offToken = "off")
    {
        var value = mode ?? "toggle";
        if (value == onToken) return ToggleMode.On;
        if (value == offToken) return ToggleMode.Off;
        if (value == "toggle") return ToggleMode.Toggle;
        return null;
    }

    public static bool DesiredValue(this ToggleMode mode, bool current) => mode switch
    {
        ToggleMode.On => true,
        ToggleMode.Off => false,
        _ => !current,
    };
}

/// <summary>Parsed pane selector for session.focus, including the default/toggle aliases.</summary>
public enum ControlPaneFocusMode
{
    Primary,
    Split,
    Toggle,
}

public static class ControlPaneFocusModeExtensions
{
    public static ControlPaneFocusMode? Parse(string? pane) => (pane ?? "other") switch
    {
        "left" or "top" or "primary" => ControlPaneFocusMode.Primary,
        "right" or "bottom" or "split" => ControlPaneFocusMode.Split,
        "other" or "toggle" => ControlPaneFocusMode.Toggle,
        _ => null,
    };

    public static bool WantsSplit(this ControlPaneFocusMode mode, bool currentSplitFocused) => mode switch
    {
        ControlPaneFocusMode.Primary => false,
        ControlPaneFocusMode.Split => true,
        _ => !currentSplitFocused,
    };
}

public enum SessionNavigation
{
    Next,
    Previous,
    First,
    Last,
    NextAttention,
    PreviousAttention,
}

public static class SessionNavigationExtensions
{
    /// <summary>Maps a control direction string to a case, null for an unknown one. Both prev and previous
    /// spellings are accepted, as on macOS.</summary>
    public static SessionNavigation? FromWireName(string wire) => wire switch
    {
        "next" => SessionNavigation.Next,
        "prev" or "previous" => SessionNavigation.Previous,
        "first" => SessionNavigation.First,
        "last" => SessionNavigation.Last,
        "next-attention" => SessionNavigation.NextAttention,
        "prev-attention" or "previous-attention" => SessionNavigation.PreviousAttention,
        _ => null,
    };
}

public enum WorkspaceNavigation
{
    Next,
    Previous,
}

public static class WorkspaceNavigationExtensions
{
    public static WorkspaceNavigation? FromWireName(string wire) => wire switch
    {
        "next" => WorkspaceNavigation.Next,
        "prev" or "previous" => WorkspaceNavigation.Previous,
        _ => null,
    };
}

public enum ReorderDirection
{
    Up,
    Down,
    Top,
    Bottom,
}

public static class ReorderDirectionExtensions
{
    public static ReorderDirection? FromWireName(string wire) => wire switch
    {
        "up" => ReorderDirection.Up,
        "down" => ReorderDirection.Down,
        "top" => ReorderDirection.Top,
        "bottom" => ReorderDirection.Bottom,
        _ => null,
    };
}

public enum SplitAxis
{
    Vertical,
    Horizontal,
}

public static class SplitAxisExtensions
{
    public static string WireName(this SplitAxis axis) => axis switch
    {
        SplitAxis.Vertical => "vertical",
        _ => "horizontal",
    };

    public static SplitAxis? FromWireName(string wire) => wire switch
    {
        "vertical" => SplitAxis.Vertical,
        "horizontal" => SplitAxis.Horizontal,
        _ => null,
    };
}

public enum WorkspaceFocusMode
{
    On,
    Off,
    Toggle,
    Add,
}

public static class WorkspaceFocusModeExtensions
{
    public const string ValidNamesList = "on|off|toggle|add";

    public static WorkspaceFocusMode? FromWireName(string wire) => wire switch
    {
        "on" => WorkspaceFocusMode.On,
        "off" => WorkspaceFocusMode.Off,
        "toggle" => WorkspaceFocusMode.Toggle,
        "add" => WorkspaceFocusMode.Add,
        _ => null,
    };
}
