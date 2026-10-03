using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>
/// The read-back projections: the immutable snapshots <c>tree</c> and <c>window.list</c> return, distinct
/// from the request envelope. Field order mirrors Swift's declaration order
/// (agtermCore/ControlProjection.swift) for byte-identical encoding.
/// </summary>
public sealed class ControlSurfaceNode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>The user-facing kind: left, right, scratch, overlay.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";

    [JsonPropertyName("active")] public bool Active { get; set; }

    [JsonPropertyName("visible")] public bool Visible { get; set; }

    /// <summary>Actual zmx backing for primary/split surfaces; nil for ephemeral surfaces or older servers.</summary>
    [JsonPropertyName("backedByZmx")] public bool? BackedByZmx { get; set; }

    /// <summary>leader/follower/unowned; nil until the pane's zmx reports a role (never on Windows).</summary>
    [JsonPropertyName("lead")] public string? Lead { get; set; }
}

public sealed class ControlWorkspaceNode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("active")] public bool Active { get; set; }

    /// <summary>Whether this workspace is a MEMBER of the sidebar's focus set; nil/omitted when not.</summary>
    [JsonPropertyName("focused")] public bool? Focused { get; set; }

    /// <summary>Whether this workspace is COLLAPSED in the sidebar tree; nil when expanded (the default).</summary>
    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }

    [JsonPropertyName("sessions")] public List<ControlSessionNode> Sessions { get; set; } = [];
}

public sealed class ControlSessionNode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";

    /// <summary>The split pane's last reported or fallback directory, including hidden splits; omitted without one.</summary>
    [JsonPropertyName("splitCwd")] public string? SplitCwd { get; set; }

    /// <summary>The raw terminal title from the latest OSC 0/1/2; nil/omitted when none reported.</summary>
    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>Whether this is the window's selected session (selection, not keyboard focus).</summary>
    [JsonPropertyName("active")] public bool Active { get; set; }

    /// <summary>Whether the split is SHOWN side by side, the read side of session.split on|off.</summary>
    [JsonPropertyName("split")] public bool Split { get; set; }

    /// <summary>Whether the session HAS a split pane at all, shown or hidden; nil/omitted when it has none.</summary>
    [JsonPropertyName("hasSplit")] public bool? HasSplit { get; set; }

    /// <summary>True only when every existing primary/split pane is currently zmx-backed (always false/nil here).</summary>
    [JsonPropertyName("backedByZmx")] public bool? BackedByZmx { get; set; }

    [JsonPropertyName("liveAttribution")] public string? LiveAttribution { get; set; }

    [JsonPropertyName("splitLiveAttribution")] public string? SplitLiveAttribution { get; set; }

    /// <summary>Divider direction for a live split (vertical=left/right, horizontal=top/bottom); nil without one.</summary>
    [JsonPropertyName("splitAxis")] public string? SplitAxis { get; set; }

    /// <summary>The primary-pane fraction (0.05...0.95) of the pane area below the titlebar band; nil with no
    /// split or while the split has never been SHOWN.</summary>
    [JsonPropertyName("splitRatio")] public double? SplitRatio { get; set; }

    /// <summary>For a session that HAS a split, which pane holds keyboard focus: true = split (right); nil without one.</summary>
    [JsonPropertyName("splitFocused")] public bool? SplitFocused { get; set; }

    /// <summary>Whether a caller's PROGRAM occupies the session-wide overlay slot.</summary>
    [JsonPropertyName("overlay")] public bool Overlay { get; set; }

    /// <summary>An OPEN overlay's size; nil = FULL-pane. Absent with no overlay.</summary>
    [JsonPropertyName("overlaySizePercent")] public int? OverlaySizePercent { get; set; }

    /// <summary>The panes covered by their OWN overlay, ordered left then right; nil/omitted when neither has one.</summary>
    [JsonPropertyName("paneOverlays")] public List<string>? PaneOverlays { get; set; }

    /// <summary>The HUD panel occupying the session-wide overlay slot — decode passthrough on this host.</summary>
    [JsonPropertyName("hud")] public JsonElement? Hud { get; set; }

    /// <summary>Pending terminal ask — decode passthrough on this host.</summary>
    [JsonPropertyName("ask")] public JsonElement? Ask { get; set; }

    [JsonPropertyName("scratch")] public bool Scratch { get; set; }

    [JsonPropertyName("flagged")] public bool Flagged { get; set; }

    /// <summary>What the session is FOR (session.context); nil/omitted when not set.</summary>
    [JsonPropertyName("context")] public string? Context { get; set; }

    /// <summary>For a --command session, whether it HOLDS its surface after the command exits.</summary>
    [JsonPropertyName("commandWait")] public bool? CommandWait { get; set; }

    [JsonPropertyName("splitCommandWait")] public bool? SplitCommandWait { get; set; }

    /// <summary>The LIVE foreground process command (full argv) in the main pane; nil when the foreground IS a
    /// recognized shell or cannot be read.</summary>
    [JsonPropertyName("foreground")] public List<string>? Foreground { get; set; }

    /// <summary>The split (right) pane's live foreground command (full argv).</summary>
    [JsonPropertyName("splitForeground")] public List<string>? SplitForeground { get; set; }

    /// <summary>The main pane's foreground process when it IS a recognized shell, as its basename.</summary>
    [JsonPropertyName("foregroundShell")] public string? ForegroundShell { get; set; }

    [JsonPropertyName("splitForegroundShell")] public string? SplitForegroundShell { get; set; }

    /// <summary>The main pane's PERSISTED restore-command override (tri-state; deferred on Windows — always omitted).</summary>
    [JsonPropertyName("restoreCommand")] public string? RestoreCommand { get; set; }

    [JsonPropertyName("splitRestoreCommand")] public string? SplitRestoreCommand { get; set; }

    /// <summary>The session's agent status (active/completed/blocked); nil/omitted when idle.</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }

    /// <summary>Which pane set the agent status ("left"|"right"|"scratch"); nil/omitted when idle or unspecified.</summary>
    [JsonPropertyName("statusPane")] public string? StatusPane { get; set; }

    /// <summary>Whether the agent-status glyph blinks; nil/omitted when idle or not blinking.</summary>
    [JsonPropertyName("statusBlink")] public bool? StatusBlink { get; set; }

    /// <summary>The per-call #rrggbb glyph-tint override; nil/omitted when idle or using the Settings color.</summary>
    [JsonPropertyName("statusColor")] public string? StatusColor { get; set; }

    /// <summary>The per-call glyph-silhouette override; nil/omitted when idle or drawing the Settings shape.</summary>
    [JsonPropertyName("statusShape")] public string? StatusShape { get; set; }

    /// <summary>When the status was last set, idle and repeated values included, as epoch seconds.</summary>
    [JsonPropertyName("statusChangedAt")] public double? StatusChangedAt { get; set; }

    /// <summary>The session's background watermark spec — decode passthrough on this host.</summary>
    [JsonPropertyName("background")] public JsonElement? Background { get; set; }

    /// <summary>Per-pane background overrides — decode passthrough on this host.</summary>
    [JsonPropertyName("paneBackgrounds")] public JsonElement? PaneBackgrounds { get; set; }

    /// <summary>The session's unseen-notification badge count; nil/omitted when zero.</summary>
    [JsonPropertyName("unseen")] public int? Unseen { get; set; }

    /// <summary>The default/left pane's live font size in points; nil/omitted when unrealized.</summary>
    [JsonPropertyName("fontSize")] public double? FontSize { get; set; }

    /// <summary>The split (right) pane's live font size in points; nil/omitted with no realized split pane.</summary>
    [JsonPropertyName("splitFontSize")] public double? SplitFontSize { get; set; }

    /// <summary>The scratch terminal's live font size in points, or nil when no scratch surface is realized.</summary>
    [JsonPropertyName("scratchFontSize")] public double? ScratchFontSize { get; set; }

    /// <summary>Addressable terminal surfaces owned by this session; nil/omitted against an older server.</summary>
    [JsonPropertyName("surfaces")] public List<ControlSurfaceNode>? Surfaces { get; set; }

    /// <summary>Whether the MAIN pane's terminal exists (surface created, program spawned).</summary>
    [JsonPropertyName("realized")] public bool? Realized { get; set; }

    /// <summary>The host a teleported session is attached to; nil/omitted for a local one.</summary>
    [JsonPropertyName("remoteHost")] public string? RemoteHost { get; set; }

    /// <summary>Presentation stream state — decode passthrough on this host.</summary>
    [JsonPropertyName("presentation")] public JsonElement? Presentation { get; set; }

    /// <summary>Viewers presenting this session — decode passthrough on this host.</summary>
    [JsonPropertyName("presenters")] public JsonElement? Presenters { get; set; }

    /// <summary>Overlay slots a viewer presenting this session holds — decode passthrough on this host.</summary>
    [JsonPropertyName("remoteOverlays")] public JsonElement? RemoteOverlays { get; set; }

    /// <summary>HTML pages in this session's overlay slots — decode passthrough on this host.</summary>
    [JsonPropertyName("htmlOverlays")] public JsonElement? HtmlOverlays { get; set; }
}

/// <summary>The whole workspace tree, the payload of a <c>tree</c> response.</summary>
public sealed class ControlTree
{
    [JsonPropertyName("workspaces")] public List<ControlWorkspaceNode> Workspaces { get; set; } = [];

    /// <summary>Milliseconds since the last user input in the projected window; nil/omitted before any activity.</summary>
    [JsonPropertyName("idleMs")] public int? IdleMs { get; set; }

    /// <summary>The window's auto-follow-blocked timeout in milliseconds, or nil when disabled.</summary>
    [JsonPropertyName("autoFollowMs")] public int? AutoFollowMs { get; set; }

    /// <summary>Whether the projected window's sidebar is visible (live, tree-only).</summary>
    [JsonPropertyName("sidebarVisible")] public bool? SidebarVisible { get; set; }

    /// <summary>The projected window's sidebar VIEW mode ("tree" | "flagged").</summary>
    [JsonPropertyName("sidebarMode")] public string? SidebarMode { get; set; }

    /// <summary>How the flagged view arranges its sessions ("flat" | "tree"); app-wide.</summary>
    [JsonPropertyName("sidebarFlaggedLayout")] public string? SidebarFlaggedLayout { get; set; }

    /// <summary>The projected window's sidebar divider position in points — the read side of sidebar.width.</summary>
    [JsonPropertyName("sidebarWidth")] public double? SidebarWidth { get; set; }

    /// <summary>Whether the projected window's workspace focus FILTER is applied.</summary>
    [JsonPropertyName("workspaceFilter")] public bool? WorkspaceFilter { get; set; }

    /// <summary>Whether the projected window's quick terminal is visible.</summary>
    [JsonPropertyName("quickVisible")] public bool? QuickVisible { get; set; }

    /// <summary>The control id of the surface terminal zoom fills the window with; nil/omitted when nothing is zoomed.</summary>
    [JsonPropertyName("zoomedSurface")] public string? ZoomedSurface { get; set; }

    /// <summary>The open dashboard's cells as pane refs in grid order; nil/omitted with no dashboard.</summary>
    [JsonPropertyName("dashboardMembers")] public List<string>? DashboardMembers { get; set; }

    /// <summary>The pane ref of the dashboard's highlighted cell; nil/omitted with no dashboard.</summary>
    [JsonPropertyName("dashboardHighlighted")] public string? DashboardHighlighted { get; set; }

    /// <summary>The absolute font size applied to the dashboard cells; nil/omitted with no dashboard or untouched font.</summary>
    [JsonPropertyName("dashboardFontSize")] public double? DashboardFontSize { get; set; }

    /// <summary>The dashboard's font mode — auto | fixed | untouched; nil/omitted with no dashboard.</summary>
    [JsonPropertyName("dashboardFontMode")] public string? DashboardFontMode { get; set; }

    /// <summary>The id of the picker currently awaiting a choice, or nil when no picker is open.</summary>
    [JsonPropertyName("pickPending")] public string? PickPending { get; set; }

    /// <summary>The pending GUI ask; terminal asks are exposed on their session nodes.</summary>
    [JsonPropertyName("askPending")] public string? AskPending { get; set; }

    /// <summary>The app serving this socket. Constant rather than live like every field above it.</summary>
    [JsonPropertyName("app")] public AppIdentity? App { get; set; }

    /// <summary>Live sessions reset state — decode passthrough on this host.</summary>
    [JsonPropertyName("liveReset")] public JsonElement? LiveReset { get; set; }
}

/// <summary>An open window's on-screen frame — the read side of write-only window.move/window.resize.</summary>
public sealed class ControlWindowFrame
{
    [JsonPropertyName("x")] public int X { get; set; }

    [JsonPropertyName("y")] public int Y { get; set; }

    [JsonPropertyName("width")] public int Width { get; set; }

    [JsonPropertyName("height")] public int Height { get; set; }

    /// <summary>A screen-list index; y is measured down from that display's top edge.</summary>
    [JsonPropertyName("display")] public int Display { get; set; }
}

/// <summary>A window as projected into the <c>window.list</c> response. <c>open</c> is whether its on-screen
/// window is up; <c>active</c> is whether it is the frontmost window.</summary>
public sealed class ControlWindowNode
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("open")] public bool Open { get; set; }

    [JsonPropertyName("active")] public bool Active { get; set; }

    [JsonPropertyName("autoFollowMs")] public int? AutoFollowMs { get; set; }

    /// <summary>Whether this window's sidebar is visible; nil/omitted for a CLOSED window with no live store.</summary>
    [JsonPropertyName("sidebarVisible")] public bool? SidebarVisible { get; set; }

    /// <summary>The window's on-screen frame; nil/omitted for a CLOSED window with no live window.</summary>
    [JsonPropertyName("geometry")] public ControlWindowFrame? Geometry { get; set; }

    /// <summary>Whether the window is in native full screen; nil/omitted for a CLOSED window.</summary>
    [JsonPropertyName("fullscreen")] public bool? Fullscreen { get; set; }

    /// <summary>Whether the window is zoomed (maximized-to-screen, NOT full screen).</summary>
    [JsonPropertyName("zoomed")] public bool? Zoomed { get; set; }

    /// <summary>Whether the window is minimized; nil/omitted for a CLOSED window.</summary>
    [JsonPropertyName("minimized")] public bool? Minimized { get; set; }
}
