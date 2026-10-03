using Agterm.Core.Control;
using Agterm.Core.Protocol;

namespace Agterm.Core.Model;

/// <summary>A named group of sessions — one sidebar row header.</summary>
public sealed class WorkspaceModel
{
    public Guid Id { get; }
    public string Name { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<SessionModel> Sessions { get; } = [];

    public WorkspaceModel(Guid id, string name, bool expanded = true)
    {
        Id = id;
        Name = name;
        IsExpanded = expanded;
    }
}

/// <summary>
/// The per-window store: the workspace tree plus one selection — the seam behind sidebar, deck, and the
/// control dispatcher's session/workspace arms. Mirrors agtermCore's AppStore mutations and the tree
/// projection; persistence rides the <see cref="SaveRequested"/> hook.
/// </summary>
public sealed class StoreModel
{
    public const double SidebarWidthMin = 160;
    public const double SidebarWidthMax = 560;
    public const double SplitRatioMin = 0.05;
    public const double SplitRatioMax = 0.95;

    public Guid WindowId { get; }

    public List<WorkspaceModel> Workspaces { get; } = [];

    public Guid? SelectedSessionId { get; set; }

    public bool SidebarVisible { get; set; } = true;

    public string SidebarMode { get; set; } = "tree";

    public double SidebarWidth { get; set; } = 220;

    public bool WorkspaceFilter { get; set; }

    public HashSet<Guid> FocusedWorkspaceIds { get; } = [];

    /// <summary>The app serving this socket, shared with the version command.</summary>
    public AppIdentity? App { get; set; }

    /// <summary>Fires on every structural mutation, exactly like the Swift store's save() cadence.</summary>
    public event Action? SaveRequested;

    /// <summary>The app-run event ring sink; wired by the host so appends stay single-threaded.</summary>
    public Action<ControlEventDraft>? EventSink { get; set; }

    public StoreModel(Guid windowId) => WindowId = windowId;

    // --- lookups -------------------------------------------------------------

    public SessionModel? SessionWithId(Guid id) => Workspaces
        .SelectMany(w => w.Sessions)
        .FirstOrDefault(s => s.Id == id);

    public WorkspaceModel? WorkspaceForSession(Guid sessionId) =>
        Workspaces.FirstOrDefault(w => w.Sessions.Any(s => s.Id == sessionId));

    public WorkspaceModel? WorkspaceWithId(Guid id) => Workspaces.FirstOrDefault(w => w.Id == id);

    /// <summary>The CURRENT workspace: the foreground-created one first, then the selected session's, then
    /// the last one — the id "active" resolves to for workspace commands.</summary>
    public Guid? CurrentWorkspaceId =>
        Workspaces.FirstOrDefault(w => w.Sessions.Any(s => s.Id == SelectedSessionId))?.Id
        ?? Workspaces.LastOrDefault()?.Id;

    public string DefaultWorkspaceName => $"workspace {Workspaces.Count + 1}";

    /// <summary>An empty tree is invalid: restore seeds one workspace the way the Swift recovery path
    /// does, so the set is always valid and non-empty.</summary>
    public void EnsureBootstrap()
    {
        if (Workspaces.Count == 0)
            AddWorkspace(DefaultWorkspaceName);
    }

    // --- events ----------------------------------------------------------------

    private void Emit(ControlEventKind kind, string? workspace = null, string? session = null,
        ControlEventPayload? payload = null) =>
        EventSink?.Invoke(new ControlEventDraft(kind, ControlResolve.WireId(WindowId), workspace, session, payload));

    private void ScheduleTreeChanged() => Emit(ControlEventKind.TreeChanged);

    private void EmitSessionCreated(SessionModel session, WorkspaceModel workspace)
    {
        Emit(ControlEventKind.SessionCreated, ControlResolve.WireId(workspace.Id),
            ControlResolve.WireId(session.Id), new ControlEventPayload { Name = session.DisplayName });
        ScheduleTreeChanged();
    }

    private void EmitSessionClosed(SessionModel session, WorkspaceModel workspace)
    {
        Emit(ControlEventKind.SessionClosed, ControlResolve.WireId(workspace.Id),
            ControlResolve.WireId(session.Id), new ControlEventPayload { Name = session.DisplayName });
        ScheduleTreeChanged();
    }

    // --- mutations ---------------------------------------------------------------

    public WorkspaceModel AddWorkspace(string name, bool collapsed = false)
    {
        var workspace = new WorkspaceModel(Guid.NewGuid(), TerminalText.Sanitized(name), expanded: !collapsed);
        Workspaces.Add(workspace);
        ScheduleTreeChanged();
        Save();
        return workspace;
    }

    public SessionModel AddSession(WorkspaceModel workspace, string cwd, string? name = null,
        string? command = null, bool commandWait = false, bool select = true)
    {
        var session = new SessionModel(Guid.NewGuid(), cwd,
            string.IsNullOrWhiteSpace(name) ? null : TerminalText.Sanitized(name), command, commandWait);
        workspace.Sessions.Add(session);
        if (select)
            SelectedSessionId = session.Id;
        EmitSessionCreated(session, workspace);
        Save();
        return session;
    }

    /// <summary>Selection persists immediately (a sidebar click must), and visiting a session clears an
    /// autoReset status and the unseen badge, matching the macOS behaviors scripts poll for.</summary>
    public void SelectSession(Guid sessionId)
    {
        SelectedSessionId = sessionId;
        var session = SessionWithId(sessionId);
        if (session is not null)
        {
            if (session.Indicator.AutoReset)
            {
                session.Indicator.Status = StatusKind.Idle;
                session.Indicator.StatusPane = null;
            }
            session.UnseenCount = 0;
        }
        Save();
    }

    public void RenameWorkspace(Guid workspaceId, string name)
    {
        var workspace = WorkspaceWithId(workspaceId);
        if (workspace is null) return;
        workspace.Name = TerminalText.Sanitized(name);
        ScheduleTreeChanged();
        Save();
    }

    public void RenameSession(Guid sessionId, string? name)
    {
        var session = SessionWithId(sessionId);
        if (session is null) return;
        session.CustomName = SessionModel.TrimmedOrNull(TerminalText.Sanitized(name ?? ""));
        ScheduleTreeChanged();
        Save();
    }

    public bool CloseSession(Guid sessionId)
    {
        var workspace = WorkspaceForSession(sessionId);
        if (workspace is null) return false;
        var session = workspace.Sessions.First(s => s.Id == sessionId);
        workspace.Sessions.Remove(session);
        session.Surface?.Teardown();
        session.Surface = null;
        session.SplitSurface?.Teardown();
        session.SplitSurface = null;
        session.HasSplit = false;
        if (SelectedSessionId == sessionId)
        {
            SelectedSessionId = workspace.Sessions.FirstOrDefault()?.Id
                ?? Workspaces.SelectMany(w => w.Sessions).LastOrDefault()?.Id;
        }
        EmitSessionClosed(session, workspace);
        Save();
        return true;
    }

    /// <summary>Exactly one placement intent; returns false when a referenced id is missing.</summary>
    public bool MoveSession(Guid sessionId, ControlSessionMove move)
    {
        var source = WorkspaceForSession(sessionId);
        var session = SessionWithId(sessionId);
        if (source is null || session is null) return false;

        switch (move)
        {
            case ControlSessionMove.Reorder reorder:
            {
                var index = source.Sessions.IndexOf(session);
                source.Sessions.RemoveAt(index);
                var target = reorder.Direction switch
                {
                    ReorderDirection.Up => Math.Max(0, index - 1),
                    ReorderDirection.Down => Math.Min(source.Sessions.Count, index + 1),
                    ReorderDirection.Top => 0,
                    _ => source.Sessions.Count,
                };
                source.Sessions.Insert(target, session);
                break;
            }
            case ControlSessionMove.ToWorkspace destination:
                source.Sessions.Remove(session);
                var destinationId = ControlResolve.ParseWireId(destination.Workspace) ?? Guid.Empty;
                if (WorkspaceWithId(destinationId) is { } targetWorkspace)
                {
                    targetWorkspace.Sessions.Add(session);
                }
                else
                {
                    source.Sessions.Add(session);
                    return false;
                }
                break;
            case ControlSessionMove.Place place:
            {
                var anchorSession = SessionWithId(ControlResolve.ParseWireId(place.Anchor) ?? Guid.Empty);
                if (anchorSession is null) return false;
                var anchorWorkspace = WorkspaceForSession(anchorSession.Id)!;
                source.Sessions.Remove(session);
                var anchorIndex = anchorWorkspace.Sessions.IndexOf(anchorSession);
                anchorWorkspace.Sessions.Insert(place.After ? anchorIndex + 1 : anchorIndex, session);
                break;
            }
        }
        ScheduleTreeChanged();
        Save();
        return true;
    }

    public void MoveWorkspace(Guid workspaceId, ReorderDirection direction)
    {
        var workspace = WorkspaceWithId(workspaceId);
        if (workspace is null) return;
        var index = Workspaces.IndexOf(workspace);
        Workspaces.RemoveAt(index);
        var target = direction switch
        {
            ReorderDirection.Up => Math.Max(0, index - 1),
            ReorderDirection.Down => Math.Min(Workspaces.Count, index + 1),
            ReorderDirection.Top => 0,
            _ => Workspaces.Count,
        };
        Workspaces.Insert(target, workspace);
        ScheduleTreeChanged();
        Save();
    }

    public void SetWorkspaceExpansion(Guid workspaceId, bool expanded)
    {
        var workspace = WorkspaceWithId(workspaceId);
        if (workspace is null || workspace.IsExpanded == expanded) return;
        workspace.IsExpanded = expanded;
        ScheduleTreeChanged();
        Save();
    }

    public double ClampSidebarWidth(double points) => Math.Clamp(points, SidebarWidthMin, SidebarWidthMax);

    public double ClampSplitRatio(double ratio) => Math.Clamp(ratio, SplitRatioMin, SplitRatioMax);

    // --- tree projection ------------------------------------------------------

    /// <summary>Projects the model into the control tree payload. Emission rules mirror the Swift
    /// projection: omitted-when-false optionals (hasSplit, focused, collapsed, statusBlink, commandWait,
    /// unseen), always-present state (realized, split, active), idle-gated status decoration.</summary>
    public ControlTree Tree()
    {
        var activeWorkspace = CurrentWorkspaceId;
        var nodes = Workspaces.Select(workspace =>
        {
            var sessions = workspace.Sessions.Select(session =>
            {
                var idle = session.Indicator.Status == StatusKind.Idle;
                var surfaces = new List<ControlSurfaceNode>();
                surfaces.Add(new ControlSurfaceNode
                {
                    Id = $"surface:{ControlResolve.WireId(session.Id)}:left",
                    Kind = "left",
                    Active = session.Id == SelectedSessionId && !session.SplitFocused,
                    Visible = !session.SplitFocused || !session.HasSplit,
                    BackedByZmx = false,
                });
                if (session.HasSplit)
                    surfaces.Add(new ControlSurfaceNode
                    {
                        Id = $"surface:{ControlResolve.WireId(session.Id)}:right",
                        Kind = "right",
                        Active = session.Id == SelectedSessionId && session.SplitFocused,
                        Visible = session.SplitFocused || !session.SplitShown,
                        BackedByZmx = false,
                    });
                return new ControlSessionNode
                {
                    Id = ControlResolve.WireId(session.Id),
                    Name = session.DisplayName,
                    Cwd = session.EffectiveCwd,
                    SplitCwd = session.HasSplit ? session.SplitEffectiveCwd : null,
                    Title = session.OscTitle,
                    Active = session.Id == SelectedSessionId,
                    Split = session.HasSplit && session.SplitShown,
                    HasSplit = session.HasSplit ? true : null,
                    BackedByZmx = false,
                    SplitAxis = session.HasSplit ? session.SplitAxis.WireName() : null,
                    SplitRatio = session.HasSplit ? session.SplitRatio : null,
                    SplitFocused = session.HasSplit ? session.SplitFocused : null,
                    Overlay = false,
                    Scratch = false,
                    Flagged = session.Flagged,
                    Context = session.EffectiveContext,
                    CommandWait = session.InitialCommand is not null && session.CommandWait ? true : null,
                    Status = idle ? null : session.Indicator.Status.WireName(),
                    StatusPane = idle ? null : session.Indicator.StatusPane?.WireName(),
                    StatusBlink = idle ? null : (session.Indicator.Blink ? true : null),
                    StatusColor = idle ? null : session.Indicator.Color,
                    StatusShape = idle ? null : session.Indicator.Shape?.WireName(),
                    StatusChangedAt = session.Indicator.StatusChangedAt,
                    Unseen = session.UnseenCount > 0 ? session.UnseenCount : null,
                    FontSize = session.Surface?.CurrentFontSize(),
                    SplitFontSize = session.HasSplit ? session.SplitSurface?.CurrentFontSize() : null,
                    Surfaces = surfaces,
                    Realized = session.Surface?.IsRealized ?? false,
                };
            }).ToList();
            return new ControlWorkspaceNode
            {
                Id = ControlResolve.WireId(workspace.Id),
                Name = workspace.Name,
                Active = workspace.Id == activeWorkspace,
                Focused = FocusedWorkspaceIds.Contains(workspace.Id) ? true : null,
                Collapsed = workspace.IsExpanded ? null : true,
                Sessions = sessions,
            };
        }).ToList();

        return new ControlTree
        {
            Workspaces = nodes,
            SidebarVisible = SidebarVisible,
            SidebarMode = SidebarMode,
            SidebarWidth = SidebarWidth,
            WorkspaceFilter = WorkspaceFilter,
            App = App,
        };
    }

    /// <summary>The flattened session order navigation and recency operate on.</summary>
    public List<SessionModel> FlattenedSessions() => Workspaces.SelectMany(w => w.Sessions).ToList();

    /// <summary>session.go: step the selection through the flattened session list, wrapping; the attention
    /// variants skip to the next non-idle status in either direction.</summary>
    public Guid? GoSession(SessionNavigation direction)
    {
        var sessions = FlattenedSessions();
        if (sessions.Count == 0) return null;
        var index = sessions.FindIndex(s => s.Id == SelectedSessionId);
        var target = direction switch
        {
            SessionNavigation.First => 0,
            SessionNavigation.Last => sessions.Count - 1,
            _ => index,
        };
        if (direction is SessionNavigation.Next or SessionNavigation.Previous)
        {
            if (index < 0) target = 0;
            else target = (index + (direction == SessionNavigation.Next ? 1 : sessions.Count - 1)) % sessions.Count;
        }
        else if (direction is SessionNavigation.NextAttention or SessionNavigation.PreviousAttention)
        {
            var step = direction == SessionNavigation.NextAttention ? 1 : -1;
            var found = -1;
            for (var offset = 1; offset <= sessions.Count; offset++)
            {
                var candidate = ((index < 0 ? 0 : index) + step * offset + sessions.Count * 2) % sessions.Count;
                if (sessions[candidate].Indicator.Status != StatusKind.Idle)
                {
                    found = candidate;
                    break;
                }
            }
            if (found < 0) return SelectedSessionId;
            target = found;
        }
        SelectedSessionId = sessions[target].Id;
        Save();
        return SelectedSessionId;
    }

    /// <summary>workspace.go: step the current workspace through the list, wrapping, selecting the
    /// destination's first session; null when there is nowhere to step.</summary>
    public Guid? GoWorkspace(WorkspaceNavigation direction)
    {
        if (Workspaces.Count < 2) return null;
        var current = CurrentWorkspaceId;
        var index = Workspaces.FindIndex(w => w.Id == current);
        var target = (index + (direction == WorkspaceNavigation.Next ? 1 : Workspaces.Count - 1)) % Workspaces.Count;
        var workspace = Workspaces[target];
        if (workspace.Sessions.Count > 0)
            SelectedSessionId = workspace.Sessions[0].Id;
        ScheduleTreeChanged();
        Save();
        return workspace.Id;
    }

    private void Save() => SaveRequested?.Invoke();
}
