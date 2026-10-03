using Agterm.Core.Model;
using Agterm.Core.Protocol;

namespace Agterm.Core.Control;

/// <summary>
/// Platform-side window operations the store cannot perform itself. The headless host returns defaults;
/// the WinUI app supplies real window handling. Geometry and the toggles answer null for "no live window",
/// matching the closed-window omissions on the tree.
/// </summary>
public interface IWindowHost
{
    void Open(Guid windowId);
    void Close(Guid windowId);
    void Focus(Guid windowId);
    ControlWindowFrame? FrameOf(Guid windowId);
    bool? FullscreenOf(Guid windowId) => null;
    bool? ZoomedOf(Guid windowId) => null;
    bool? MinimizedOf(Guid windowId) => null;
    void Resize(Guid windowId, int width, int height) { }
    void Move(Guid windowId, int x, int y, int? display) { }
    void Minimize(Guid windowId) { }
    void Zoom(Guid windowId) { }
    void Fullscreen(Guid windowId) { }
    /// <summary>Whether notification banners are enabled; the notify command's advisory text follows it.</summary>
    bool ShowBanners => true;
}

/// <summary>The headless default: no on-screen windows, geometry-less.</summary>
public sealed class NullWindowHost : IWindowHost
{
    public static readonly NullWindowHost Instance = new();
    public void Open(Guid windowId) { }
    public void Close(Guid windowId) { }
    public void Focus(Guid windowId) { }
    public ControlWindowFrame? FrameOf(Guid windowId) => null;
}

/// <summary>
/// The model-level IControlActions: target resolution, store mutations, surface I/O, events, and window
/// projection over a WindowLibraryModel — the shared core the headless host and the WinUI app both drive.
/// The dispatcher has already validated arguments; these arms resolve targets and perform effects,
/// mirroring the ControlServer extension files on macOS.
/// </summary>
public sealed class StoreControlActions(
    WindowLibraryModel library,
    ControlEventRing ring,
    IWindowHost windowHost,
    Func<SessionModel, bool, IPaneSurface?> surfaceFactory) : IControlActions
{
    private const int RealizePollAttempts = 12;
    private const int RealizePollDelayMs = 30;

    public AppIdentity? App
    {
        get => library.Windows.Count > 0 ? library.ActiveStore?.App : null;
        set
        {
            foreach (var store in OpenStores())
                store.App = value;
        }
    }

    private IEnumerable<StoreModel> OpenStores() =>
        library.Windows.Select(w => library.StoreFor(w.Id)).OfType<StoreModel>();

    // --- resolution -----------------------------------------------------------

    private StoreModel PlacementStore(string? window)
    {
        if (window is { } target)
        {
            var resolution = ControlResolve.Resolve(target,
                library.Windows.Select(w => w.Id).ToList(),
                library.FrontmostId);
            var id = resolution.Result switch
            {
                TargetResolution.Outcome.Resolved => resolution.Id,
                _ => throw new ControlActionException(ControlResolve.ErrorMessage("window", target, resolution)),
            };
            return library.LoadStore(id);
        }
        var store = library.ActiveStore ?? library.LoadStore(library.Windows.First().Id);
        store.EnsureBootstrap();
        return store;
    }

    private sealed class ControlActionException(string message) : Exception(message)
    {
        public string MessageText { get; } = message;
    }

    private static ControlResponse Fail(Exception exception) =>
        exception is ControlActionException action
            ? ControlResponse.Fail(action.MessageText)
            : ControlResponse.Fail(exception.Message);

    private (StoreModel Store, SessionModel Session) ResolveSession(string? target, string? window)
    {
        var store = PlacementStore(window);
        var candidates = store.FlattenedSessions();
        var ids = candidates.Select(s => s.Id).ToList();
        var active = store.SelectedSessionId;
        // cross-window fallback: an id typed from another window's tree still resolves
        if (ControlResolve.Resolve(target ?? "", ids, active).Result == TargetResolution.Outcome.NotFound)
        {
            foreach (var other in OpenStores().Where(s => s != store))
            {
                ids.AddRange(other.FlattenedSessions().Select(s => s.Id));
                active ??= other.SelectedSessionId;
            }
        }
        var resolution = ControlResolve.Resolve(target ?? "", ids, active);
        if (resolution.Result != TargetResolution.Outcome.Resolved)
            throw new ControlActionException(ControlResolve.ErrorMessage("session", target ?? "", resolution));
        var session = candidates.FirstOrDefault(s => s.Id == resolution.Id)
            ?? OpenStores().SelectMany(s => s.FlattenedSessions()).First(s => s.Id == resolution.Id);
        return (store, session);
    }

    private (StoreModel Store, WorkspaceModel Workspace) ResolveWorkspace(string? target, string? window)
    {
        var store = PlacementStore(window);
        var resolution = ControlResolve.Resolve(target ?? "",
            store.Workspaces.Select(w => w.Id).ToList(),
            store.CurrentWorkspaceId);
        if (resolution.Result != TargetResolution.Outcome.Resolved)
            throw new ControlActionException(ControlResolve.ErrorMessage("workspace", target ?? "", resolution));
        return (store, store.Workspaces.First(w => w.Id == resolution.Id));
    }

    private static string IdOf(Guid id) => ControlResolve.WireId(id);

    private ControlResponse OkId(Guid id) => ControlResponse.OkWith(new ControlResult { Id = IdOf(id) });

    // --- tree / events / version -----------------------------------------------

    public ControlResponse ControlTree(string? window)
    {
        try
        {
            return ControlResponse.OkWith(new ControlResult { Tree = PlacementStore(window).Tree() });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse ReadEvents(ControlEventReadOptions options)
    {
        var result = ring.Read(options.Cursor, options.Kinds, options.Limit);
        if (result.Error is { } error)
            return ControlResponse.Fail(ControlEventRing.WireError(error));
        return ControlResponse.OkWith(new ControlResult { Events = result.Batch });
    }

    public ControlResponse AppIdentity() =>
        ControlResponse.OkWith(new ControlResult { App = App });

    // --- session lifecycle --------------------------------------------------

    public ControlResponse CreateSession(ControlSessionCreateOptions options)
    {
        try
        {
            var store = PlacementStore(options.Window);
            WorkspaceModel workspace;
            SessionModel? anchor = null;
            var after = true;
            if (options.After is { } || options.Before is { })
            {
                anchor = ResolveSession(options.After ?? options.Before, options.Window).Session;
                workspace = store.Workspaces.First(w => w.Sessions.Any(s => s.Id == anchor.Id));
                after = options.After is not null;
            }
            else if (options.WorkspaceName is { } name)
            {
                workspace = store.Workspaces.FirstOrDefault(w => w.Name == SessionModel.TrimmedOrNull(name))
                    ?? (options.CreateWorkspace == true
                        ? store.AddWorkspace(name)
                        : throw new ControlActionException($"no workspace named: {name}"));
            }
            else if (options.Workspace is { } workspaceTarget)
            {
                workspace = ResolveWorkspace(workspaceTarget, options.Window).Workspace;
            }
            else
            {
                workspace = store.Workspaces.FirstOrDefault(w => w.Id == store.CurrentWorkspaceId)
                    ?? store.Workspaces.Last();
            }

            var cwd = options.Cwd is { } given && given.Length > 0 ? given : HomeDirectory();
            var session = store.AddSession(workspace, cwd, options.Name, options.Command,
                options.Wait == true, select: options.NoSelect != true);
            if (anchor is not null && workspace.Sessions.Remove(session))
            {
                var anchorIndex = workspace.Sessions.IndexOf(anchor);
                workspace.Sessions.Insert(after ? anchorIndex + 1 : anchorIndex, session);
            }
            session.Surface = surfaceFactory(session, false);
            if (!options.NoSelect)
                store.SelectSession(session.Id);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse DuplicateSession(string? target, string? window)
    {
        try
        {
            var (_, source) = ResolveSession(target, window);
            var workspace = PlacementStore(window).Workspaces.First(w => w.Sessions.Any(s => s.Id == source.Id));
            var duplicate = PlacementStore(window).AddSession(workspace, source.FocusedCwd);
            duplicate.Surface = surfaceFactory(duplicate, false);
            return OkId(duplicate.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SelectSession(string? target, string? window)
    {
        try
        {
            var (store, session) = ResolveSession(target, window);
            store.SelectSession(session.Id);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse GoSession(string? window, SessionNavigation direction)
    {
        var landed = PlacementStore(window).GoSession(direction);
        return landed is { } id ? OkId(id) : ControlResponse.Fail("no session to navigate to");
    }

    public ControlResponse CloseSession(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            return CloseSessionAndReport(session, window);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window)
    {
        try
        {
            // resolve the whole batch first, then close: batch close is atomic
            var resolved = targets
                .Distinct()
                .Select(target => ResolveSession(target, window).Session)
                .ToList();
            var closed = 0;
            foreach (var session in resolved)
                if (CloseSessionAndReport(session, window).Ok)
                    closed++;
            return ControlResponse.OkWith(new ControlResult
            {
                Affected = closed,
                Id = resolved.Count > 0 ? IdOf(resolved[0].Id) : null,
            });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    private ControlResponse CloseSessionAndReport(SessionModel session, string? window)
    {
        var store = PlacementStore(window);
        if (!store.CloseSession(session.Id))
            return ControlResponse.Fail($"no such session: {session.Id}");
        return OkId(session.Id);
    }

    public ControlResponse RenameSession(string? target, string? window, string name)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            PlacementStore(window).RenameSession(session.Id, name);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse RevealSession(string? target, string? window)
    {
        try
        {
            var (store, session) = ResolveSession(target, window);
            store.SelectSession(session.Id);
            var windowId = library.WindowIdForSession(session.Id);
            if (windowId is { } id)
                windowHost.Focus(id);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    // --- workspace lifecycle -------------------------------------------------

    public ControlResponse CreateWorkspace(string? window, string? name, bool collapsed)
    {
        var store = PlacementStore(window);
        var workspace = store.AddWorkspace(
            SessionModel.TrimmedOrNull(name) ?? store.DefaultWorkspaceName, collapsed);
        return OkId(workspace.Id);
    }

    public ControlResponse SelectWorkspace(string? target, string? window)
    {
        try
        {
            var (store, workspace) = ResolveWorkspace(target, window);
            if (workspace.Sessions.Count > 0)
                store.SelectedSessionId = workspace.Sessions[0].Id;
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction)
    {
        var landed = PlacementStore(window).GoWorkspace(direction);
        return landed is { } id
            ? OkId(id)
            : ControlResponse.Fail("no other workspace to navigate to");
    }

    public ControlResponse RenameWorkspace(string? target, string? window, string name)
    {
        try
        {
            var (store, workspace) = ResolveWorkspace(target, window);
            store.RenameWorkspace(workspace.Id, name);
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse DeleteWorkspace(string? target, string? window)
    {
        try
        {
            var (store, workspace) = ResolveWorkspace(target, window);
            if (store.Workspaces.Count <= 1)
                return ControlResponse.Fail("cannot delete the last workspace");
            foreach (var session in workspace.Sessions.ToList())
                store.CloseSession(session.Id);
            store.Workspaces.Remove(workspace);
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction)
    {
        try
        {
            var (store, workspace) = ResolveWorkspace(target, window);
            store.MoveWorkspace(workspace.Id, direction);
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse FocusWorkspace(string? target, string? window, WorkspaceFocusMode mode)
    {
        try
        {
            var (_, workspace) = ResolveWorkspace(target, window);
            var store = PlacementStore(window);
            var marked = store.FocusedWorkspaceIds;
            switch (mode)
            {
                case WorkspaceFocusMode.On:
                    marked.Add(workspace.Id);
                    store.WorkspaceFilter = true;
                    break;
                case WorkspaceFocusMode.Off:
                    marked.Remove(workspace.Id);
                    if (marked.Count == 0)
                        store.WorkspaceFilter = false;
                    break;
                case WorkspaceFocusMode.Toggle:
                    if (marked.Count == 1 && marked.Contains(workspace.Id))
                        marked.Remove(workspace.Id);
                    else
                        marked.Add(workspace.Id);
                    store.WorkspaceFilter = marked.Count > 0;
                    break;
                case WorkspaceFocusMode.Add:
                    marked.Add(workspace.Id);
                    break;
            }
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SetWorkspaceFilter(string? window, ToggleMode mode)
    {
        var store = PlacementStore(window);
        if (mode.DesiredValue(store.WorkspaceFilter))
        {
            if (store.FocusedWorkspaceIds.Count == 0)
                return ControlResponse.Fail("cannot filter to an empty focus set");
            store.WorkspaceFilter = true;
        }
        else
        {
            store.WorkspaceFilter = false;
        }
        return ControlResponse.OkWith(new ControlResult());
    }

    public ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded)
    {
        try
        {
            var (store, workspace) = ResolveWorkspace(target, window);
            store.SetWorkspaceExpansion(workspace.Id, expanded);
            return OkId(workspace.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    // --- session ordering / state -------------------------------------------

    public ControlResponse MoveSession(string? target, string? window, ControlSessionMove move)
    {
        try
        {
            var (store, session) = ResolveSession(target, window);
            return store.MoveSession(session.Id, move)
                ? OkId(session.Id)
                : ControlResponse.Fail($"no such session: {target ?? ""}");
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move)
    {
        try
        {
            var resolved = targets.Distinct()
                .Select(target => ResolveSession(target, window).Session).ToList();
            var moved = 0;
            foreach (var session in resolved)
                if (PlacementStore(window).MoveSession(session.Id, move))
                    moved++;
            return ControlResponse.OkWith(new ControlResult { Affected = moved });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SetSessionFlag(string? target, string? window, string? mode)
    {
        try
        {
            if (mode == "clear")
            {
                foreach (var openStore in OpenStores())
                    foreach (var flagged in openStore.FlattenedSessions())
                        flagged.Flagged = false;
                return ControlResponse.OkWith(new ControlResult());
            }
            var (_, session) = ResolveSession(target, window);
            session.Flagged = (mode ?? "toggle") switch
            {
                "on" => true,
                "off" => false,
                _ => !session.Flagged,
            };
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SetSessionContext(string? target, string? window, string? context)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            session.Context = context;
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse MarkSessionSeen(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            session.UnseenCount = 0;
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var pane = ResolvePaneSlot(session, update.Pane, update.PaneId);
            // pane precedence while blocked: a write from another pane that is neither itself blocked is
            // refused whole, so a hook's active cannot erase the other pane's block.
            if (session.Indicator.Status == StatusKind.Blocked
                && update.Status != StatusKind.Blocked
                && session.Indicator.StatusPane is { } owner && owner != pane)
                return ControlResponse.Fail($"blocked status owned by pane {owner.WireName()}");
            var indicator = session.Indicator;
            indicator.Status = update.Status;
            indicator.Blink = update.Blink == true;
            indicator.AutoReset = update.AutoReset == true;
            indicator.Color = update.Color;
            indicator.Shape = update.Shape;
            indicator.StatusPane = pane;
            indicator.StatusChangedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            ring.Append(new ControlEventDraft(ControlEventKind.Status,
                Session: IdOf(session.Id),
                Payload: new ControlEventPayload
                {
                    Status = update.Status.WireName(),
                    Pane = pane?.WireName(),
                    Blink = update.Blink == true ? true : null,
                    Color = update.Color,
                    Shape = update.Shape?.WireName(),
                    Previous = null,
                }));
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    // --- splits ----------------------------------------------------------------

    public ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var desired = ToggleModeExtensions.Parse(mode).GetValueOrDefault(ToggleMode.Toggle)
                .DesiredValue(session.HasSplit && session.SplitShown);
            if (desired && !session.HasSplit)
            {
                session.HasSplit = true;
                session.SplitShown = true;
                session.SplitAxis = axis ?? session.SplitAxis;
                session.SplitInitialCwd ??= session.EffectiveCwd;
                session.SplitSurface = surfaceFactory(session, true);
                ring.Append(new ControlEventDraft(ControlEventKind.PaneSplit,
                    Session: IdOf(session.Id),
                    Payload: new ControlEventPayload { Name = session.DisplayName, Status = "shown" }));
            }
            else if (desired && session.HasSplit && !session.SplitShown)
            {
                session.SplitShown = true;
                ring.Append(new ControlEventDraft(ControlEventKind.PaneSplit,
                    Session: IdOf(session.Id),
                    Payload: new ControlEventPayload { Name = session.DisplayName, Status = "shown" }));
            }
            else if (!desired && session.SplitShown)
            {
                session.SplitShown = false;
                ring.Append(new ControlEventDraft(ControlEventKind.PaneSplit,
                    Session: IdOf(session.Id),
                    Payload: new ControlEventPayload { Name = session.DisplayName, Status = "hidden" }));
            }
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse CloseSessionSplit(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            if (!session.HasSplit) return OkId(session.Id); // idempotent
            session.SplitSurface?.Teardown();
            session.SplitSurface = null;
            session.HasSplit = false;
            session.SplitShown = false;
            session.SplitFocused = false;
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SwapSessionPanes(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            if (!session.HasSplit)
                return ControlResponse.Fail("session has no split pane");
            (session.Surface, session.SplitSurface) = (session.SplitSurface, session.Surface);
            (session.CurrentCwd, session.SplitCwd) = (session.SplitCwd, session.CurrentCwd);
            if (session.SplitInitialCwd is { } splitInitial)
            {
                session.SplitInitialCwd = session.InitialCwd;
                session.InitialCwd = splitInitial;
            }
            (session.OscTitle, session.SplitTitle) = (session.SplitTitle, session.OscTitle);
            session.SplitFocused = !session.SplitFocused;
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse FocusSessionPane(string? target, string? window, string? pane)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            if (!session.HasSplit)
                return ControlResponse.Fail("session has no split pane");
            var mode = ControlPaneFocusModeExtensions.Parse(pane)
                ?? throw new ControlActionException($"invalid pane: {pane ?? ""}");
            session.SplitFocused = mode.WantsSplit(session.SplitFocused);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize)
    {
        try
        {
            var (store, session) = ResolveSession(target, window);
            if (!session.HasSplit)
                return ControlResponse.Fail("session has no split pane");
            var ratio = resize switch
            {
                ControlSplitResize.Ratio absolute => absolute.Value,
                ControlSplitResize.Delta delta => session.SplitRatio + delta.Value,
                _ => 0.5,
            };
            session.SplitRatio = store.ClampSplitRatio(ratio);
            return ControlResponse.OkWith(new ControlResult { Id = IdOf(session.Id), Ratio = session.SplitRatio });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    // --- surface I/O ----------------------------------------------------------

    /// <summary>Resolves the pane slot a pane selector addresses: the token first (against live slots),
    /// then the role, then the on-screen default. Returns null for the primary pane.</summary>
    private static StatusPane? ResolvePaneSlot(SessionModel session, StatusPane? pane, string? paneId)
    {
        if (paneId is { Length: > 0 })
        {
            if (session.Surface?.PaneToken == paneId) return null;
            if (session.SplitSurface?.PaneToken == paneId) return StatusPane.Right;
        }
        return pane;
    }

    private IPaneSurface SurfaceFor(SessionModel session, StatusPane? pane)
    {
        if (pane == StatusPane.Right)
            return session.SplitSurface
                ?? throw new ControlActionException("session has no split pane");
        if (pane == StatusPane.Scratch)
            throw new ControlActionException("session has no scratch terminal");
        return session.Surface
            ?? throw new ControlActionException("session not realized");
    }

    /// <summary>The bounded realize poll the main pane runs before injection: 12 × 30 ms, probe first, so
    /// a realized session pays nothing and session.new --no-select + immediate type does not race.</summary>
    private static bool WaitForRealize(SessionModel session)
    {
        for (var attempt = 0; attempt < RealizePollAttempts; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(RealizePollDelayMs);
            if (session.Surface?.IsRealized == true)
                return true;
        }
        return session.Surface?.IsRealized == true;
    }

    public ControlResponse TypeSession(string? target, string? window, ControlSessionTypeOptions options)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var pane = ResolvePaneSlot(session, options.Pane, null);
            var surface = pane == StatusPane.Right
                ? session.SplitSurface ?? throw new ControlActionException("session has no split pane")
                : session.Surface;
            if (pane is null or StatusPane.Left)
            {
                var wasReady = surface?.IsRealized == true;
                if (surface is null || (!WaitForRealize(session) && !wasReady))
                {
                    if (options.Select)
                        PlacementStore(window).SelectSession(session.Id);
                    return ControlResponse.Fail("session not realized");
                }
                if (options.Select && !wasReady)
                    PlacementStore(window).SelectSession(session.Id);
            }
            else if (surface is null)
            {
                return ControlResponse.Fail(pane == StatusPane.Scratch
                    ? "session has no scratch terminal"
                    : "session has no split pane");
            }
            var paced = KeystrokeSegments.Paced(options.Text);
            foreach (var segment in paced.Head)
                ApplySegment(surface!, segment);
            if (paced.PacedReturn)
            {
                // one fixed blocking gap per call: a deferred Return can be overtaken by another injection
                Thread.Sleep((int)(KeystrokeSegments.SubmitGapSeconds * 1000));
                surface!.PressReturn();
            }
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    private static void ApplySegment(IPaneSurface surface, KeystrokeSegment segment)
    {
        switch (segment)
        {
            case KeystrokeSegment.Text text:
                surface.TypeText(text.Value);
                break;
            case KeystrokeSegment.ReturnKey:
                surface.PressReturn();
                break;
        }
    }

    public ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var pane = ResolvePaneSlot(session, options.Pane, options.PaneId);
            var surface = pane switch
            {
                StatusPane.Right => session.SplitSurface
                    ?? throw new ControlActionException("session has no split pane"),
                StatusPane.Scratch => throw new ControlActionException("session has no scratch terminal"),
                _ => session.Surface ?? throw new ControlActionException("session not realized"),
            };
            if (!surface.IsRealized)
                throw new ControlActionException("session not realized");
            var text = options.Lines is { } lines
                ? surface.ReadScreenLines(lines)
                : surface.ReadScreenText(options.All);
            return ControlResponse.OkWith(new ControlResult { Text = text });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse CopySessionSelection(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var surface = SurfaceFor(session, null);
            if (!surface.IsRealized)
                return ControlResponse.Fail("session not realized");
            return surface.ReadSelection() is { } selection
                ? ControlResponse.OkWith(new ControlResult { Text = selection })
                : ControlResponse.Fail("no selection");
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse PasteSession(string? target, string? window, StatusPane? pane)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            SurfaceFor(session, ResolvePaneSlot(session, pane, null)).PerformFontAction("paste_from_clipboard");
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SelectAllSession(string? target, string? window)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            SurfaceFor(session, null).PerformFontAction("select_all");
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse ReadSurfaceCursor(string? target, string? window)
    {
        try
        {
            // the explicit surface:<id>:<pane> vocabulary and the bare session id both resolve to a pane
            var surfaceId = target ?? "";
            var paneKind = "left";
            var sessionId = surfaceId;
            if (surfaceId.StartsWith("surface:", StringComparison.Ordinal))
            {
                var parts = surfaceId.Split(':');
                if (parts.Length == 3)
                {
                    sessionId = parts[1];
                    paneKind = parts[2];
                }
            }
            var (_, session) = ResolveSession(sessionId, window);
            var surface = paneKind is "right" or "split"
                ? session.SplitSurface ?? throw new ControlActionException("session has no split pane")
                : session.Surface ?? throw new ControlActionException("session not realized");
            if (!surface.IsRealized)
                return ControlResponse.Fail("session not realized");
            return ControlResponse.OkWith(new ControlResult { Cursor = new ControlCursor(surface.ReadCursorColumn()) });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse Font(string? target, string? window, StatusPane? pane, string action)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            var surface = pane == StatusPane.Right
                ? session.SplitSurface ?? throw new ControlActionException("session has no split pane")
                : session.Surface ?? throw new ControlActionException("session not realized");
            surface.PerformFontAction(action);
            return OkId(session.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    // --- app-level ------------------------------------------------------------

    public ControlResponse SendNotification(string? target, string? window, string? title, string body)
    {
        try
        {
            var (_, session) = ResolveSession(target, window);
            session.UnseenCount++;
            var effectiveTitle = string.IsNullOrEmpty(title) ? session.DisplayName : title;
            ring.Append(new ControlEventDraft(ControlEventKind.Notify,
                Session: IdOf(session.Id),
                Payload: new ControlEventPayload
                {
                    Name = session.DisplayName,
                    Title = effectiveTitle,
                    Body = body,
                }));
            return windowHost.ShowBanners
                ? ControlResponse.OkWith(new ControlResult())
                : ControlResponse.OkWith(new ControlResult { Text = ControlNotify.BannersOffNote });
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse SetTheme(ControlArgs? args) =>
        ControlResponse.Fail("control dispatcher did not handle theme.set");

    public ControlResponse ListThemes() =>
        ControlResponse.OkWith(new ControlResult
        {
            Themes = ["agterm-dark", "agterm-light"],
            Theme = "agterm-dark",
        });

    public ControlResponse SetSidebarVisibility(ToggleMode mode)
    {
        foreach (var store in OpenStores())
            store.SidebarVisible = mode.DesiredValue(store.SidebarVisible);
        return ControlResponse.OkWith(new ControlResult());
    }

    public ControlResponse ExpandSidebar(string? window)
    {
        foreach (var store in StoresForSidebar(window))
            store.SidebarVisible = true;
        return ControlResponse.OkWith(new ControlResult());
    }

    public ControlResponse CollapseSidebar(string? window)
    {
        foreach (var store in StoresForSidebar(window))
            store.SidebarVisible = false;
        return ControlResponse.OkWith(new ControlResult());
    }

    private IEnumerable<StoreModel> StoresForSidebar(string? window) =>
        window is null ? OpenStores() : [PlacementStore(window)];

    public ControlResponse SetSidebarWidth(double points, string? window)
    {
        var store = PlacementStore(window);
        store.SidebarWidth = store.ClampSidebarWidth(points);
        return ControlResponse.OkWith(new ControlResult { SidebarWidth = store.SidebarWidth });
    }

    // --- windows ---------------------------------------------------------------

    public ControlResponse WindowNew(string? name, bool minimized)
    {
        var info = library.NewWindow(name);
        if (!minimized)
            windowHost.Open(info.Id);
        library.ActiveStore!.App = App;
        return OkId(info.Id);
    }

    public ControlResponse WindowList() =>
        ControlResponse.OkWith(new ControlResult
        {
            Windows = library.Windows.Select(info => new ControlWindowNode
            {
                Id = IdOf(info.Id),
                Name = info.Name,
                Open = library.StoreFor(info.Id) is not null,
                Active = info.Id == library.FrontmostId,
                SidebarVisible = library.StoreFor(info.Id)?.SidebarVisible,
                Geometry = windowHost.FrameOf(info.Id),
                Fullscreen = windowHost.FullscreenOf(info.Id),
                Zoomed = windowHost.ZoomedOf(info.Id),
                Minimized = windowHost.MinimizedOf(info.Id),
            }).ToList(),
        });

    public ControlResponse WindowSelect(string? target)
    {
        try
        {
            var resolution = ControlResolve.Resolve(target ?? "active",
                library.Windows.Select(w => w.Id).ToList(), library.FrontmostId);
            if (resolution.Result != TargetResolution.Outcome.Resolved)
                throw new ControlActionException(ControlResolve.ErrorMessage("window", target ?? "", resolution));
            library.FrontmostId = resolution.Id;
            library.LoadStore(resolution.Id);
            windowHost.Focus(resolution.Id);
            return OkId(resolution.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    public ControlResponse WindowGo(WorkspaceNavigation direction)
    {
        if (library.Windows.Count < 2)
            return ControlResponse.Fail("no other window to navigate to");
        var index = library.Windows.FindIndex(w => w.Id == library.FrontmostId);
        var target = library.Windows[(index + (direction == WorkspaceNavigation.Next ? 1 : library.Windows.Count - 1)) % library.Windows.Count];
        return WindowSelect(ControlResolve.WireId(target.Id));
    }

    public ControlResponse WindowClose(string? target) => WindowMutate(target, id =>
    {
        windowHost.Close(id);
        library.CloseWindow(id);
    });

    public ControlResponse WindowRename(string? target, string name) => WindowMutate(target, id =>
    {
        library.RenameWindow(id, name);
    });

    public ControlResponse WindowDelete(string? target) => WindowMutate(target, id =>
    {
        windowHost.Close(id);
        library.DeleteWindow(id);
    });

    public ControlResponse WindowResize(string? target, int width, int height) => WindowMutate(target, id =>
    {
        windowHost.Resize(id, width, height);
    });

    public ControlResponse WindowMove(string? target, int x, int y, int? display) => WindowMutate(target, id =>
    {
        windowHost.Move(id, x, y, display);
    });

    public ControlResponse WindowZoom(string? target) => WindowMutate(target, id => windowHost.Zoom(id));

    public ControlResponse WindowFullscreen(string? target) => WindowMutate(target, id => windowHost.Fullscreen(id));

    public ControlResponse WindowMinimize(string? target, ToggleMode mode) => WindowMutate(target, id =>
    {
        if (mode.DesiredValue(windowHost.MinimizedOf(id) == true))
            windowHost.Minimize(id);
    });

    private ControlResponse WindowMutate(string? target, Action<Guid> effect)
    {
        try
        {
            var resolution = ControlResolve.Resolve(target ?? "active",
                library.Windows.Select(w => w.Id).ToList(), library.FrontmostId);
            if (resolution.Result != TargetResolution.Outcome.Resolved)
                throw new ControlActionException(ControlResolve.ErrorMessage("window", target ?? "", resolution));
            effect(resolution.Id);
            return OkId(resolution.Id);
        }
        catch (Exception exception) { return Fail(exception); }
    }

    private static string HomeDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}

/// <summary>The advisory text notify returns when the banner toggle is off (#286).</summary>
public static class ControlNotify
{
    public const string BannersOffNote =
        "badge updated, but \"Show notification banners\" is off, so no banner was posted";
}
