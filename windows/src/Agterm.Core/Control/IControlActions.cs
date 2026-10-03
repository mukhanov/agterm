using Agterm.Core.Protocol;

namespace Agterm.Core.Control;

/// <summary>
/// Host-facing operations for commands routed through <see cref="ControlDispatcher"/>. The dispatcher owns
/// command parsing, validation, and response shape; the host keeps target resolution and platform side
/// effects — the same seam as Swift's ControlActions, trimmed to the Windows MVP command set.
/// </summary>
public interface IControlActions
{
    ControlResponse ControlTree(string? window);
    ControlResponse ReadEvents(ControlEventReadOptions options);
    ControlResponse AppIdentity();

    ControlResponse CreateSession(ControlSessionCreateOptions options);
    ControlResponse DuplicateSession(string? target, string? window);
    ControlResponse SelectSession(string? target, string? window);
    ControlResponse GoSession(string? window, SessionNavigation direction);
    ControlResponse CloseSession(string? target, string? window);
    ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window);
    ControlResponse RenameSession(string? target, string? window, string name);
    ControlResponse RevealSession(string? target, string? window);

    ControlResponse CreateWorkspace(string? window, string? name, bool collapsed);
    ControlResponse SelectWorkspace(string? target, string? window);
    ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction);
    ControlResponse RenameWorkspace(string? target, string? window, string name);
    ControlResponse DeleteWorkspace(string? target, string? window);
    ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction);
    ControlResponse FocusWorkspace(string? target, string? window, WorkspaceFocusMode mode);
    ControlResponse SetWorkspaceFilter(string? window, ToggleMode mode);
    ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded);

    ControlResponse MoveSession(string? target, string? window, ControlSessionMove move);
    ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move);
    ControlResponse SetSessionFlag(string? target, string? window, string? mode);
    ControlResponse SetSessionContext(string? target, string? window, string? context);
    ControlResponse MarkSessionSeen(string? target, string? window);
    ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update);

    ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis);
    ControlResponse CloseSessionSplit(string? target, string? window);
    ControlResponse SwapSessionPanes(string? target, string? window);
    ControlResponse FocusSessionPane(string? target, string? window, string? pane);
    ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize);

    ControlResponse ReadSurfaceCursor(string? target, string? window);
    ControlResponse Font(string? target, string? window, StatusPane? pane, string action);
    ControlResponse TypeSession(string? target, string? window, ControlSessionTypeOptions options);
    ControlResponse CopySessionSelection(string? target, string? window);
    ControlResponse PasteSession(string? target, string? window, StatusPane? pane);
    ControlResponse SelectAllSession(string? target, string? window);
    ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options);

    ControlResponse SendNotification(string? target, string? window, string? title, string body);
    ControlResponse SetTheme(ControlArgs? args);
    ControlResponse ListThemes();
    ControlResponse SetSidebarVisibility(ToggleMode mode);
    ControlResponse ExpandSidebar(string? window);
    ControlResponse CollapseSidebar(string? window);
    ControlResponse SetSidebarWidth(double points, string? window);

    ControlResponse WindowNew(string? name, bool minimized);
    ControlResponse WindowList();
    ControlResponse WindowSelect(string? target);
    ControlResponse WindowGo(WorkspaceNavigation direction);
    ControlResponse WindowClose(string? target);
    ControlResponse WindowRename(string? target, string name);
    ControlResponse WindowDelete(string? target);
    ControlResponse WindowResize(string? target, int width, int height);
    ControlResponse WindowMove(string? target, int x, int y, int? display);
    ControlResponse WindowZoom(string? target);
    ControlResponse WindowFullscreen(string? target);
    ControlResponse WindowMinimize(string? target, ToggleMode mode);
}

/// <summary>
/// Routes control commands through a host-provided action seam — a port of the MVP subset of
/// agtermCore's ControlDispatcher, keeping the validation order and error strings byte-identical.
/// Commands outside the Windows MVP answer the macOS server's unhandled-command fallback, so a script
/// gets a recognizable failure instead of a hang or a decode error.
/// </summary>
public sealed class ControlDispatcher(IControlActions actions)
{
    public ControlResponse Dispatch(ControlRequest request)
    {
        switch (request.Cmd)
        {
            case ControlCommand.Tree:
                return actions.ControlTree(request.Args?.Window);

            case ControlCommand.EventsRead:
                return DispatchEventsRead(request);

            case ControlCommand.SessionNew:
                return DispatchSessionNew(request);
            case ControlCommand.SessionDuplicate:
                // no options: the source session names its own workspace AND its cwd, so a duplicate is
                // fully described by the target.
                return actions.DuplicateSession(request.Target, request.Args?.Window);
            case ControlCommand.SessionSelect:
                return actions.SelectSession(request.Target, request.Args?.Window);
            case ControlCommand.SessionGo:
                var direction = request.Args?.To is { } to ? SessionNavigationExtensions.FromWireName(to) : null;
                return direction is null
                    ? Fail("session.go requires --to next|prev|first|last|next-attention|prev-attention")
                    : actions.GoSession(request.Args?.Window, direction.Value);
            case ControlCommand.SessionClose:
                if (request.Args?.Targets is { } targets)
                {
                    if (targets.Count == 0)
                        return Fail("session.close requires at least one --target");
                    return actions.CloseSessions(targets, request.Args?.Window);
                }
                return actions.CloseSession(request.Target, request.Args?.Window);
            case ControlCommand.SessionRename:
                return request.Args?.Name is { } name
                    ? actions.RenameSession(request.Target, request.Args?.Window, name)
                    : Fail("session.rename requires a name");
            case ControlCommand.SessionReveal:
                return actions.RevealSession(request.Target, request.Args?.Window);
            case ControlCommand.SessionMove:
                return DispatchSessionMove(request);
            case ControlCommand.SessionFlag:
                return actions.SetSessionFlag(request.Target, request.Args?.Window, request.Args?.Mode);
            case ControlCommand.SessionContext:
                return DispatchSessionContext(request);
            case ControlCommand.SessionSeen:
                return actions.MarkSessionSeen(request.Target, request.Args?.Window);
            case ControlCommand.SessionStatus:
                return DispatchSessionStatus(request);

            case ControlCommand.SessionSplit:
                SplitAxis? axis = null;
                if (request.Args?.Axis is { } rawAxis)
                {
                    axis = SplitAxisExtensions.FromWireName(rawAxis);
                    if (axis is null)
                        return Fail($"invalid split axis: {rawAxis} (vertical|horizontal)");
                }
                return actions.SplitSession(request.Target, request.Args?.Window, request.Args?.Mode, axis);
            case ControlCommand.SessionSplitClose:
                return actions.CloseSessionSplit(request.Target, request.Args?.Window);
            case ControlCommand.SessionSwap:
                return actions.SwapSessionPanes(request.Target, request.Args?.Window);
            case ControlCommand.SessionFocus:
                return actions.FocusSessionPane(request.Target, request.Args?.Window, request.Args?.Pane);
            case ControlCommand.SessionResize:
                return DispatchSessionResize(request);

            case ControlCommand.SurfaceCursor:
                return actions.ReadSurfaceCursor(request.Target, request.Args?.Window);

            case ControlCommand.SessionType:
                return DispatchSessionType(request);
            case ControlCommand.SessionCopy:
                return actions.CopySessionSelection(request.Target, request.Args?.Window);
            case ControlCommand.SessionPaste:
                var pastePane = ParseRolePane(request.Args?.Pane);
                return pastePane.Rejection
                    ?? actions.PasteSession(request.Target, request.Args?.Window, pastePane.Pane);
            case ControlCommand.SessionSelectAll:
                return actions.SelectAllSession(request.Target, request.Args?.Window);
            case ControlCommand.SessionText:
                return DispatchSessionText(request);

            case ControlCommand.FontInc:
                return DispatchFont(request, "increase_font_size:1");
            case ControlCommand.FontDec:
                return DispatchFont(request, "decrease_font_size:1");
            case ControlCommand.FontReset:
                return DispatchFont(request, "reset_font_size");

            case ControlCommand.WorkspaceNew:
                return actions.CreateWorkspace(request.Args?.Window, request.Args?.Name,
                    request.Args?.Collapsed ?? false);
            case ControlCommand.WorkspaceSelect:
                return actions.SelectWorkspace(request.Target, request.Args?.Window);
            case ControlCommand.WorkspaceGo:
                var workspaceDirection = request.Args?.To is { } wire
                    ? WorkspaceNavigationExtensions.FromWireName(wire)
                    : null;
                return workspaceDirection is null
                    ? Fail("workspace.go requires --to next|prev")
                    : actions.GoWorkspace(request.Args?.Window, workspaceDirection.Value);
            case ControlCommand.WorkspaceRename:
                var trimmedName = TrimmedOrNull(request.Args?.Name);
                return trimmedName is null
                    ? Fail("workspace.rename requires a name")
                    : actions.RenameWorkspace(request.Target, request.Args?.Window, trimmedName);
            case ControlCommand.WorkspaceDelete:
                return actions.DeleteWorkspace(request.Target, request.Args?.Window);
            case ControlCommand.WorkspaceMove:
                if (request.Args?.To is null)
                    return Fail("workspace.move requires --to");
                var reorder = ReorderDirectionExtensions.FromWireName(request.Args.To);
                return reorder is null
                    ? Fail("workspace.move --to must be up|down|top|bottom")
                    : actions.MoveWorkspace(request.Target, request.Args?.Window, reorder.Value);
            case ControlCommand.WorkspaceFocus:
                // parsed + rejected BEFORE the host runs, so an unknown mode can never half-apply.
                var rawFocusMode = request.Args?.Mode ?? "toggle";
                var focusMode = WorkspaceFocusModeExtensions.FromWireName(rawFocusMode);
                return focusMode is null
                    ? Fail($"invalid focus mode: {rawFocusMode} ({WorkspaceFocusModeExtensions.ValidNamesList})")
                    : actions.FocusWorkspace(request.Target, request.Args?.Window, focusMode.Value);
            case ControlCommand.WorkspaceFilter:
                var filterMode = ToggleModeExtensions.Parse(request.Args?.Mode);
                return filterMode is null
                    ? Fail($"invalid workspace filter mode: {request.Args?.Mode ?? "toggle"}")
                    : actions.SetWorkspaceFilter(request.Args?.Window, filterMode.Value);
            case ControlCommand.WorkspaceCollapse:
                return actions.SetWorkspaceExpansion(request.Target, request.Args?.Window, expanded: false);
            case ControlCommand.WorkspaceExpand:
                return actions.SetWorkspaceExpansion(request.Target, request.Args?.Window, expanded: true);

            case ControlCommand.Notify:
                if (string.IsNullOrEmpty(request.Args?.Body))
                    return Fail("notify requires a body");
                return actions.SendNotification(request.Target, request.Args?.Window,
                    request.Args?.Title, request.Args!.Body);
            case ControlCommand.ThemeSet:
                return actions.SetTheme(request.Args);
            case ControlCommand.ThemeList:
                return actions.ListThemes();
            case ControlCommand.Sidebar:
                var sidebarMode = ToggleModeExtensions.Parse(request.Args?.Mode, "show", "hide");
                return sidebarMode is null
                    ? Fail($"invalid sidebar mode: {request.Args?.Mode ?? "toggle"}")
                    : actions.SetSidebarVisibility(sidebarMode.Value);
            case ControlCommand.SidebarExpand:
                return actions.ExpandSidebar(request.Args?.Window);
            case ControlCommand.SidebarCollapse:
                return actions.CollapseSidebar(request.Args?.Window);
            case ControlCommand.SidebarWidth:
                var points = request.Args?.SidebarWidth;
                if (points is null || !double.IsFinite(points.Value))
                    return Fail("sidebar.width requires a width in points");
                return actions.SetSidebarWidth(points.Value, request.Args?.Window);

            case ControlCommand.Version:
                return actions.AppIdentity();

            case ControlCommand.WindowNew:
                return actions.WindowNew(request.Args?.Name, request.Args?.Minimized ?? false);
            case ControlCommand.WindowList:
                return actions.WindowList();
            case ControlCommand.WindowSelect:
                return actions.WindowSelect(request.Target);
            case ControlCommand.WindowGo:
                var windowDirection = request.Args?.To is { } windowWire
                    ? WorkspaceNavigationExtensions.FromWireName(windowWire)
                    : null;
                return windowDirection is null
                    ? Fail("window.go requires --to next|prev")
                    : actions.WindowGo(windowDirection.Value);
            case ControlCommand.WindowClose:
                return actions.WindowClose(request.Target);
            case ControlCommand.WindowRename:
                var windowName = TrimmedOrNull(request.Args?.Name);
                return windowName is null
                    ? Fail("window.rename requires a name")
                    : actions.WindowRename(request.Target, windowName);
            case ControlCommand.WindowDelete:
                return actions.WindowDelete(request.Target);
            case ControlCommand.WindowResize:
                var width = request.Args?.Width;
                var height = request.Args?.Height;
                return width is null || height is null || width <= 0 || height <= 0
                    ? Fail("window.resize requires positive width and height")
                    : actions.WindowResize(request.Target, width.Value, height.Value);
            case ControlCommand.WindowMove:
                var x = request.Args?.X;
                var y = request.Args?.Y;
                return x is null || y is null
                    ? Fail("window.move requires x and y")
                    : actions.WindowMove(request.Target, x.Value, y.Value, request.Args?.Display);
            case ControlCommand.WindowZoom:
                return actions.WindowZoom(request.Target);
            case ControlCommand.WindowFullscreen:
                return actions.WindowFullscreen(request.Target);
            case ControlCommand.WindowMinimize:
                var minimizeMode = ToggleModeExtensions.Parse(request.Args?.Mode);
                return minimizeMode is null
                    ? Fail($"invalid window minimize mode: {request.Args?.Mode ?? "toggle"}")
                    : actions.WindowMinimize(request.Target, minimizeMode.Value);

            default:
                // The macOS server's unhandled-command fallback, verbatim, for every command the Windows
                // build has not migrated yet.
                return Fail($"control dispatcher did not handle {request.Cmd.WireName()}");
        }
    }

    private static ControlResponse Fail(string error) => ControlResponse.Fail(error);

    private static string? TrimmedOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private ControlResponse DispatchEventsRead(ControlRequest request)
    {
        var args = request.Args;
        ControlEventCursor? cursor;
        switch (args?.Run, args?.After)
        {
            case (null, null):
                cursor = null;
                break;
            case (_, null) or (null, _):
                return Fail("events.read requires --run and --after together");
            default:
                var runArgs = args!;
                if (ControlResolve.ParseWireId(runArgs.Run!) is not { } run)
                    return Fail("invalid event run id");
                if (!ulong.TryParse(runArgs.After!, System.Globalization.NumberStyles.AllowLeadingSign, null, out var after))
                    return Fail("invalid event cursor");
                cursor = new ControlEventCursor(run, after);
                break;
        }

        var limit = args?.Limit ?? 100;
        if (limit is < 1 or > 1000)
            return Fail("event limit must be between 1 and 1000");

        HashSet<Protocol.ControlEventKind>? kinds = null;
        foreach (var field in args?.Kinds ?? [])
        foreach (var component in field.Split(',', StringSplitOptions.None))
        {
            var rawKind = component.Trim();
            if (!Protocol.ControlEventKindExtensions.FromWireName(rawKind, out var kind))
                return Fail($"invalid event kind: {rawKind}");
            (kinds ??= []).Add(kind);
        }
        return actions.ReadEvents(new ControlEventReadOptions(cursor, kinds, limit));
    }

    private ControlResponse DispatchSessionNew(ControlRequest request)
    {
        var args = request.Args;
        if (args?.After is not null && args.Before is not null)
            return Fail("use either --after or --before, not both");
        // The anchor sid carries its own workspace, so placement can't also name one.
        if ((args?.After is not null || args?.Before is not null)
            && (args?.Workspace is not null || args?.WorkspaceName is not null))
            return Fail("session.new takes --after/--before or a workspace, not both");
        if (args?.Workspace is not null && args.WorkspaceName is not null)
            return Fail("use either --workspace or --workspace-name, not both");
        if (args?.CreateWorkspace == true && args?.WorkspaceName is null)
            return Fail("--create-workspace requires --workspace-name");
        // --wait holds the surface after the command exits, so it is meaningless without a command.
        if (args?.Wait == true && args?.Command is null)
            return Fail("--wait requires --command");
        return actions.CreateSession(new ControlSessionCreateOptions(
            args?.Window, args?.Cwd, args?.Workspace, args?.WorkspaceName, args?.CreateWorkspace,
            args?.Command, args?.Wait, args?.Name, args?.After, args?.Before, args?.NoSelect == true));
    }

    private ControlResponse DispatchSessionMove(ControlRequest request)
    {
        var args = request.Args;
        if (args?.After is not null && args.Before is not null)
            return Fail("use either --after or --before, not both");
        // Placement mode: the anchor sid self-identifies the destination workspace, so it's mutually
        // exclusive with --to and with a workspace parameter.
        if (args?.After is { } || args?.Before is { })
        {
            if (args.To is not null)
                return Fail("session.move takes --after/--before or --to, not both");
            if (args.Workspace is not null)
                return Fail("session.move takes --after/--before or a workspace, not both");
            var anchorMove = new ControlSessionMove.Place(args.After ?? args.Before!, args.After is not null);
            if (args.Targets is { } anchorTargets)
                return DispatchSessionMoveBatch(anchorTargets, args.Window, anchorMove);
            return actions.MoveSession(request.Target, args.Window, anchorMove);
        }
        if (args?.To is not null && args?.Workspace is not null)
            return Fail("session.move takes either --to or a workspace, not both");
        if (args?.To is { } to)
        {
            if (ReorderDirectionExtensions.FromWireName(to) is not { } direction)
                return Fail("session.move --to must be up|down|top|bottom");
            if (args.Targets is not null)
                return Fail("session.move --target can be repeated only with a workspace or --after/--before");
            return actions.MoveSession(request.Target, args!.Window, new ControlSessionMove.Reorder(direction));
        }
        if (args?.Workspace is not { } workspace)
            return Fail("session.move requires --to or a workspace");
        var move = new ControlSessionMove.ToWorkspace(workspace);
        if (args?.Targets is { } targets)
            return DispatchSessionMoveBatch(targets, args!.Window, move);
        return actions.MoveSession(request.Target, args!.Window, move);
    }

    private ControlResponse DispatchSessionMoveBatch(IReadOnlyList<string> targets, string? window, ControlSessionMove move)
    {
        if (targets.Count == 0)
            return Fail("session.move requires at least one --target");
        if (targets.Count == 1)
            return actions.MoveSession(targets[0], window, move);
        return actions.MoveSessions(targets, window, move);
    }

    /// <summary>session.context: set takes text, clear takes none. An invalid value is REJECTED, never
    /// normalized, so clear stays the only route to nil and a refused call leaves the previous context
    /// standing.</summary>
    private ControlResponse DispatchSessionContext(ControlRequest request)
    {
        var args = request.Args;
        switch (args?.Mode ?? "")
        {
            case "set":
                if (args?.Text is not { } text)
                    return Fail("session.context set requires text");
                var (value, error) = SessionContextValidation.Validate(text);
                if (error is not null)
                    return Fail(error);
                return actions.SetSessionContext(request.Target, args?.Window, value);
            case "clear":
                if (args?.Text is not null)
                    return Fail("session.context clear takes no text");
                return actions.SetSessionContext(request.Target, args?.Window, null);
            default:
                return Fail($"invalid context mode: {args?.Mode ?? ""} (set|clear)");
        }
    }

    private ControlResponse DispatchSessionStatus(ControlRequest request)
    {
        var args = request.Args;
        if (StatusKindExtensions.FromWireName(args?.Status ?? "") is not { } status)
            return Fail("invalid status");
        if (args?.Color is { } color && !ColorHex.IsValid(color))
            return Fail("invalid color (expected #rrggbb)");
        StatusShape? shape = null;
        if (args?.Shape is { } rawShape)
        {
            shape = StatusShapeExtensions.FromWireName(rawShape);
            if (shape is null)
                return Fail($"invalid shape: {rawShape} ({StatusShapeExtensions.ValidNamesList})");
        }
        var pane = ParseRolePane(args?.Pane);
        if (pane.Rejection is not null)
            return pane.Rejection;
        return actions.SetSessionStatus(request.Target, args?.Window, new ControlSessionStatusUpdate(
            status, args?.Blink, args?.AutoReset, args?.Sound, args?.Color, shape, pane.Pane, args?.PaneID));
    }

    private ControlResponse DispatchSessionResize(ControlRequest request)
    {
        var args = request.Args;
        switch (args?.Ratio, args?.RatioDelta)
        {
            case (null, null):
                return Fail("session.resize requires --split-ratio, --grow-left, or --grow-right");
            case (not null, not null):
                return Fail("session.resize: --split-ratio is mutually exclusive with --grow-left/--grow-right");
            case ({ } ratio, null):
                return actions.ResizeSplit(request.Target, args?.Window, new ControlSplitResize.Ratio(ratio));
            default:
                return actions.ResizeSplit(request.Target, args?.Window,
                    new ControlSplitResize.Delta(args!.RatioDelta!.Value));
        }
    }

    private ControlResponse DispatchSessionType(ControlRequest request)
    {
        if (request.Args?.Text is not { } text)
            return Fail("session.type requires text");
        if (text.Contains('\0'))
            return Fail("text must not contain a NUL byte");
        var pane = ParseSurfacePane(request.Args?.Pane);
        if (pane.Rejection is not null)
            return pane.Rejection;
        return actions.TypeSession(request.Target, request.Args?.Window,
            new ControlSessionTypeOptions(text, request.Args?.Select ?? false, pane.Pane));
    }

    /// <summary>The extent is checked first, so an --all --lines caller still gets that error before a pane
    /// one — the same ordering macOS pins.</summary>
    private ControlResponse DispatchSessionText(ControlRequest request)
    {
        var args = request.Args;
        var all = args?.All ?? false;
        var lines = args?.Lines;
        if (all && lines is not null)
            return Fail("use either --all or --lines, not both");
        if (lines is <= 0)
            return Fail("--lines must be greater than 0");
        var pane = ParseSurfacePane(args?.Pane);
        if (pane.Rejection is not null)
            return pane.Rejection;
        return actions.ReadSessionText(request.Target, args?.Window,
            new ControlSessionTextOptions(pane.Pane, args?.PaneID, all, lines));
    }

    /// <summary>The three font.* arms share one parse, so their pane vocabulary cannot drift apart.</summary>
    private ControlResponse DispatchFont(ControlRequest request, string action)
    {
        var pane = ParseSurfacePane(request.Args?.Pane);
        return pane.Rejection ?? actions.Font(request.Target, request.Args?.Window, pane.Pane, action);
    }

    /// <summary>The role selector (session.status, session.paste): the stable rejection names the canonical
    /// left|right|scratch read-back values.</summary>
    private static (StatusPane? Pane, ControlResponse? Rejection) ParseRolePane(string? raw) =>
        ParsePane(raw, "--pane must be left, right, or scratch");

    /// <summary>The surface I/O selector (session.type, session.text, font.*): same vocabulary, but the
    /// rejection keeps the per-command "invalid pane: value" spelling.</summary>
    private static (StatusPane? Pane, ControlResponse? Rejection) ParseSurfacePane(string? raw) =>
        ParsePane(raw, $"invalid pane: {raw ?? ""}");

    private static (StatusPane? Pane, ControlResponse? Rejection) ParsePane(string? raw, string error)
    {
        if (raw is null) return (null, null);
        var parsed = StatusPaneExtensions.FromControlName(raw);
        return parsed is null ? (null, Fail(error)) : (parsed, null);
    }
}
