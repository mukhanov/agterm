using Agterm.Core;
using Agterm.Core.Control;
using Agterm.Core.Protocol;
using Xunit;

namespace Agterm.Core.Tests;

/// <summary>
/// Dispatcher routing and validation parity: every MVP command routes with the parsed options, every
/// invalid input answers the macOS error string verbatim, validation ORDER is pinned, and deferred
/// commands answer the unhandled fallback.
/// </summary>
public class DispatcherTests
{
    private sealed class RecordingActions : IControlActions
    {
        public List<(string Method, object? Arg)> Calls { get; } = [];

        public ControlResponse Reply(string method, object? arg = null)
        {
            Calls.Add((method, arg));
            return ControlResponse.OkWith(new ControlResult { Id = "ok" });
        }

        public ControlResponse ControlTree(string? window) => Reply("tree", window);
        public ControlResponse ReadEvents(ControlEventReadOptions options) => Reply("events", options);
        public ControlResponse AppIdentity() => Reply("version");
        public ControlResponse CreateSession(ControlSessionCreateOptions options) => Reply("session.new", options);
        public ControlResponse DuplicateSession(string? target, string? window) => Reply("session.duplicate", target);
        public ControlResponse SelectSession(string? target, string? window) => Reply("session.select", target);
        public ControlResponse GoSession(string? window, SessionNavigation direction) => Reply("session.go", direction);
        public ControlResponse CloseSession(string? target, string? window) => Reply("session.close", target);
        public ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window) => Reply("session.close.batch", targets);
        public ControlResponse RenameSession(string? target, string? window, string name) => Reply("session.rename", name);
        public ControlResponse RevealSession(string? target, string? window) => Reply("session.reveal", target);
        public ControlResponse CreateWorkspace(string? window, string? name, bool collapsed) => Reply("workspace.new", (name, collapsed));
        public ControlResponse SelectWorkspace(string? target, string? window) => Reply("workspace.select", target);
        public ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction) => Reply("workspace.go", direction);
        public ControlResponse RenameWorkspace(string? target, string? window, string name) => Reply("workspace.rename", name);
        public ControlResponse DeleteWorkspace(string? target, string? window) => Reply("workspace.delete", target);
        public ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction) => Reply("workspace.move", direction);
        public ControlResponse FocusWorkspace(string? target, string? window, WorkspaceFocusMode mode) => Reply("workspace.focus", mode);
        public ControlResponse SetWorkspaceFilter(string? window, ToggleMode mode) => Reply("workspace.filter", mode);
        public ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded) => Reply("workspace.expansion", expanded);
        public ControlResponse MoveSession(string? target, string? window, ControlSessionMove move) => Reply("session.move", move);
        public ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move) => Reply("session.move.batch", (targets, move));
        public ControlResponse SetSessionFlag(string? target, string? window, string? mode) => Reply("session.flag", mode);
        public ControlResponse SetSessionContext(string? target, string? window, string? context) => Reply("session.context", context);
        public ControlResponse MarkSessionSeen(string? target, string? window) => Reply("session.seen", target);
        public ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update) => Reply("session.status", update);
        public ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis) => Reply("session.split", (mode, axis));
        public ControlResponse CloseSessionSplit(string? target, string? window) => Reply("session.split.close", target);
        public ControlResponse SwapSessionPanes(string? target, string? window) => Reply("session.swap", target);
        public ControlResponse FocusSessionPane(string? target, string? window, string? pane) => Reply("session.focus", pane);
        public ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize) => Reply("session.resize", resize);
        public ControlResponse ReadSurfaceCursor(string? target, string? window) => Reply("surface.cursor", target);
        public ControlResponse Font(string? target, string? window, StatusPane? pane, string action) => Reply("font", (pane, action));
        public ControlResponse TypeSession(string? target, string? window, ControlSessionTypeOptions options) => Reply("session.type", options);
        public ControlResponse CopySessionSelection(string? target, string? window) => Reply("session.copy", target);
        public ControlResponse PasteSession(string? target, string? window, StatusPane? pane) => Reply("session.paste", pane);
        public ControlResponse SelectAllSession(string? target, string? window) => Reply("session.selectall", target);
        public ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options) => Reply("session.text", options);
        public ControlResponse SendNotification(string? target, string? window, string? title, string body) => Reply("notify", (title, body));
        public ControlResponse SetTheme(ControlArgs? args) => Reply("theme.set", args?.Name);
        public ControlResponse ListThemes() => Reply("theme.list");
        public ControlResponse SetSidebarVisibility(ToggleMode mode) => Reply("sidebar", mode);
        public ControlResponse ExpandSidebar(string? window) => Reply("sidebar.expand", window);
        public ControlResponse CollapseSidebar(string? window) => Reply("sidebar.collapse", window);
        public ControlResponse SetSidebarWidth(double points, string? window) => Reply("sidebar.width", points);
        public ControlResponse WindowNew(string? name, bool minimized) => Reply("window.new", (name, minimized));
        public ControlResponse WindowList() => Reply("window.list");
        public ControlResponse WindowSelect(string? target) => Reply("window.select", target);
        public ControlResponse WindowGo(WorkspaceNavigation direction) => Reply("window.go", direction);
        public ControlResponse WindowClose(string? target) => Reply("window.close", target);
        public ControlResponse WindowRename(string? target, string name) => Reply("window.rename", name);
        public ControlResponse WindowDelete(string? target) => Reply("window.delete", target);
        public ControlResponse WindowResize(string? target, int width, int height) => Reply("window.resize", (width, height));
        public ControlResponse WindowMove(string? target, int x, int y, int? display) => Reply("window.move", (x, y, display));
        public ControlResponse WindowZoom(string? target) => Reply("window.zoom", target);
        public ControlResponse WindowFullscreen(string? target) => Reply("window.fullscreen", target);
        public ControlResponse WindowMinimize(string? target, ToggleMode mode) => Reply("window.minimize", mode);
    }

    private static (ControlDispatcher Dispatcher, RecordingActions Actions) Make()
    {
        var actions = new RecordingActions();
        return (new ControlDispatcher(actions), actions);
    }

    private static ControlResponse Dispatch(ControlCommand cmd, string? target = null, ControlArgs? args = null) =>
        Make().Dispatcher.Dispatch(new ControlRequest { Cmd = cmd, Target = target, Args = args });

    private static string DispatchError(ControlCommand cmd, string? target = null, ControlArgs? args = null)
    {
        var response = Dispatch(cmd, target, args);
        Assert.False(response.Ok);
        return response.Error ?? "";
    }

    // --- routing -------------------------------------------------------------

    [Fact]
    public void TreeRoutesWithWindow() =>
        Assert.True(Dispatch(ControlCommand.Tree, args: new ControlArgs { Window = "w1" }).Ok);

    [Fact]
    public void VersionRoutes()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest { Cmd = ControlCommand.Version });
        Assert.Equal("version", actions.Calls.Single().Method);
    }

    [Fact]
    public void SessionNewParsesAllOptions()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.SessionNew,
            Args = new ControlArgs
            {
                Cwd = "/tmp", Name = "s", Command = "cmd", Wait = true, NoSelect = true,
                WorkspaceName = "work", CreateWorkspace = true,
            },
        });
        var options = Assert.IsType<ControlSessionCreateOptions>(actions.Calls.Single().Arg);
        Assert.Equal("/tmp", options.Cwd);
        Assert.Equal("s", options.Name);
        Assert.Equal("cmd", options.Command);
        Assert.True(options.Wait);
        Assert.True(options.NoSelect);
        Assert.Equal("work", options.WorkspaceName);
        Assert.True(options.CreateWorkspace);
    }

    [Theory]
    [InlineData("next", SessionNavigation.Next)]
    [InlineData("prev", SessionNavigation.Previous)]
    [InlineData("previous", SessionNavigation.Previous)]
    [InlineData("first", SessionNavigation.First)]
    [InlineData("last", SessionNavigation.Last)]
    [InlineData("next-attention", SessionNavigation.NextAttention)]
    [InlineData("prev-attention", SessionNavigation.PreviousAttention)]
    [InlineData("previous-attention", SessionNavigation.PreviousAttention)]
    public void SessionGoParsesDirections(string wire, SessionNavigation expected)
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest { Cmd = ControlCommand.SessionGo, Args = new ControlArgs { To = wire } });
        Assert.Equal(expected, actions.Calls.Single().Arg);
    }

    [Fact]
    public void SessionGoRequiresDirection() =>
        Assert.Equal("session.go requires --to next|prev|first|last|next-attention|prev-attention",
            DispatchError(ControlCommand.SessionGo));

    [Fact]
    public void CloseBatchRequiresTarget() =>
        Assert.Equal("session.close requires at least one --target",
            DispatchError(ControlCommand.SessionClose, args: new ControlArgs { Targets = [] }));

    [Fact]
    public void CloseSingleRoutesWithoutBatch() =>
        Assert.Equal("session.close",
            DispatchOkMethod(CommandWithTarget(ControlCommand.SessionClose, "x"), "session.close"));

    private static string DispatchOkMethod(ControlRequest request, string expected)
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(request);
        Assert.Equal(expected, actions.Calls.Single().Method);
        return expected;
    }

    private static ControlRequest CommandWithTarget(ControlCommand cmd, string target) => new() { Cmd = cmd, Target = target };

    // --- session.new rejections (order pinned) --------------------------------

    [Fact]
    public void SessionNewRejectsAfterAndBefore() =>
        Assert.Equal("use either --after or --before, not both",
            DispatchError(ControlCommand.SessionNew, args: new ControlArgs { After = "a", Before = "b" }));

    [Fact]
    public void SessionNewRejectsAnchorPlusWorkspace() =>
        Assert.Equal("session.new takes --after/--before or a workspace, not both",
            DispatchError(ControlCommand.SessionNew, args: new ControlArgs { After = "a", Workspace = "w" }));

    [Fact]
    public void SessionNewRejectsWorkspacePlusName() =>
        Assert.Equal("use either --workspace or --workspace-name, not both",
            DispatchError(ControlCommand.SessionNew, args: new ControlArgs { Workspace = "w", WorkspaceName = "n" }));

    [Fact]
    public void SessionNewRejectsCreateWithoutName() =>
        Assert.Equal("--create-workspace requires --workspace-name",
            DispatchError(ControlCommand.SessionNew, args: new ControlArgs { CreateWorkspace = true }));

    [Fact]
    public void SessionNewRejectsWaitWithoutCommand() =>
        Assert.Equal("--wait requires --command",
            DispatchError(ControlCommand.SessionNew, args: new ControlArgs { Wait = true }));

    // --- session.move matrix ---------------------------------------------------

    [Fact]
    public void SessionMoveRejectsAfterAndBefore() =>
        Assert.Equal("use either --after or --before, not both",
            DispatchError(ControlCommand.SessionMove, args: new ControlArgs { After = "a", Before = "b" }));

    [Fact]
    public void SessionMoveRejectsAnchorPlusTo() =>
        Assert.Equal("session.move takes --after/--before or --to, not both",
            DispatchError(ControlCommand.SessionMove, args: new ControlArgs { After = "a", To = "up" }));

    [Fact]
    public void SessionMoveRejectsAnchorPlusWorkspace() =>
        Assert.Equal("session.move takes --after/--before or a workspace, not both",
            DispatchError(ControlCommand.SessionMove, args: new ControlArgs { Before = "b", Workspace = "w" }));

    [Fact]
    public void SessionMoveRejectsToPlusWorkspace() =>
        Assert.Equal("session.move takes either --to or a workspace, not both",
            DispatchError(ControlCommand.SessionMove, args: new ControlArgs { To = "up", Workspace = "w" }));

    [Fact]
    public void SessionMoveRejectsInvalidTo() =>
        Assert.Equal("session.move --to must be up|down|top|bottom",
            DispatchError(ControlCommand.SessionMove, args: new ControlArgs { To = "sideways" }));

    [Fact]
    public void SessionMoveRejectsBatchWithTo() =>
        Assert.Equal("session.move --target can be repeated only with a workspace or --after/--before",
            DispatchError(ControlCommand.SessionMove,
                args: new ControlArgs { To = "up", Targets = ["a", "b"] }));

    [Fact]
    public void SessionMoveRequiresIntent() =>
        Assert.Equal("session.move requires --to or a workspace", DispatchError(ControlCommand.SessionMove));

    [Fact]
    public void SessionMoveAnchorRoutesPlace()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.SessionMove,
            Target = "x",
            Args = new ControlArgs { Before = "anchor" },
        });
        var move = Assert.IsType<ControlSessionMove.Place>(actions.Calls.Single().Arg);
        Assert.Equal("anchor", move.Anchor);
        Assert.False(move.After);
    }

    [Fact]
    public void SessionMoveBatchUsesSinglePathForOne() =>
        DispatchOkMethod(new ControlRequest
        {
            Cmd = ControlCommand.SessionMove,
            Args = new ControlArgs { Workspace = "w", Targets = ["only"] },
        }, "session.move");

    [Fact]
    public void SessionMoveBatchRoutesForMany() =>
        DispatchOkMethod(new ControlRequest
        {
            Cmd = ControlCommand.SessionMove,
            Args = new ControlArgs { Workspace = "w", Targets = ["a", "b"] },
        }, "session.move.batch");

    // --- session.status validation ---------------------------------------------

    [Fact]
    public void StatusRejectsUnknownValue() =>
        Assert.Equal("invalid status", DispatchError(ControlCommand.SessionStatus, args: new ControlArgs { Status = "purple" }));

    [Fact]
    public void StatusRejectsBadColor() =>
        Assert.Equal("invalid color (expected #rrggbb)",
            DispatchError(ControlCommand.SessionStatus, args: new ControlArgs { Status = "active", Color = "#12345" }));

    [Fact]
    public void StatusAcceptsUnprefixedHex() =>
        Assert.True(Dispatch(ControlCommand.SessionStatus,
            args: new ControlArgs { Status = "active", Color = "aabbcc" }).Ok);

    [Fact]
    public void StatusRejectsBadShape() =>
        Assert.Equal("invalid shape: blob (circle|square|triangle|diamond|capsule|star)",
            DispatchError(ControlCommand.SessionStatus, args: new ControlArgs { Status = "active", Shape = "blob" }));

    [Fact]
    public void StatusRejectsBadPaneWithCanonicalError() =>
        Assert.Equal("--pane must be left, right, or scratch",
            DispatchError(ControlCommand.SessionStatus, args: new ControlArgs { Status = "active", Pane = "middle" }));

    [Fact]
    public void StatusParsesPaneAliases()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.SessionStatus,
            Args = new ControlArgs { Status = "active", Pane = "split" },
        });
        var update = Assert.IsType<ControlSessionStatusUpdate>(actions.Calls.Single().Arg);
        Assert.Equal(StatusPane.Right, update.Pane);
    }

    // --- session.context validation --------------------------------------------

    [Fact]
    public void ContextSetRequiresText() =>
        Assert.Equal("session.context set requires text",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "set" }));

    [Fact]
    public void ContextClearRejectsText() =>
        Assert.Equal("session.context clear takes no text",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "clear", Text = "x" }));

    [Fact]
    public void ContextRejectsUnknownMode() =>
        Assert.Equal("invalid context mode: flip (set|clear)",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "flip" }));

    [Fact]
    public void ContextRejectsBlankAfterTrim() =>
        Assert.Equal("context must not be empty (use --clear to remove it)",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "set", Text = "   " }));

    [Fact]
    public void ContextRejectsControlCharacters() =>
        Assert.Equal("context must not contain control characters or line breaks",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "set", Text = "a\nb" }));

    [Fact]
    public void ContextRejectsParagraphSeparator() =>
        Assert.Equal("context must not contain control characters or line breaks",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "set", Text = "a\u2028b" }));

    [Fact]
    public void ContextRejectsOverByteLimit() =>
        Assert.Equal("context must be at most 256 UTF-8 bytes",
            DispatchError(ControlCommand.SessionContext, args: new ControlArgs { Mode = "set", Text = new string('т', 129) }));

    [Fact]
    public void ContextTrimsOuterSpaces()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.SessionContext,
            Args = new ControlArgs { Mode = "set", Text = "  spaced  " },
        });
        Assert.Equal("spaced", actions.Calls.Single().Arg);
    }

    // --- session.type / session.text / font panes ------------------------------

    [Fact]
    public void TypeRequiresText() =>
        Assert.Equal("session.type requires text", DispatchError(ControlCommand.SessionType));

    [Fact]
    public void TypeRejectsNul() =>
        Assert.Equal("text must not contain a NUL byte",
            DispatchError(ControlCommand.SessionType, args: new ControlArgs { Text = "a\0b" }));

    [Fact]
    public void TypeRejectsBadPaneWithPerCommandError() =>
        Assert.Equal("invalid pane: middle",
            DispatchError(ControlCommand.SessionType, args: new ControlArgs { Text = "x", Pane = "middle" }));

    [Fact]
    public void TextExtentCheckedBeforePane() =>
        Assert.Equal("use either --all or --lines, not both",
            DispatchError(ControlCommand.SessionText,
                args: new ControlArgs { All = true, Lines = 5, Pane = "middle" }));

    [Fact]
    public void TextRejectsNonPositiveLines() =>
        Assert.Equal("--lines must be greater than 0",
            DispatchError(ControlCommand.SessionText, args: new ControlArgs { Lines = 0 }));

    [Fact]
    public void FontParsesPaneAlias()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.FontInc,
            Args = new ControlArgs { Pane = "top" },
        });
        var (pane, action) = ((StatusPane?, string))actions.Calls.Single().Arg!;
        Assert.Equal(StatusPane.Left, pane);
        Assert.Equal("increase_font_size:1", action);
    }

    // --- events.read ------------------------------------------------------------

    [Fact]
    public void EventsRequiresRunAfterPair() =>
        Assert.Equal("events.read requires --run and --after together",
            DispatchError(ControlCommand.EventsRead, args: new ControlArgs { Run = "x" }));

    [Fact]
    public void EventsRejectsBadRun() =>
        Assert.Equal("invalid event run id",
            DispatchError(ControlCommand.EventsRead, args: new ControlArgs { Run = "not-a-uuid", After = "1" }));

    [Fact]
    public void EventsRejectsBadCursor() =>
        Assert.Equal("invalid event cursor",
            DispatchError(ControlCommand.EventsRead,
                args: new ControlArgs { Run = "0B79E4F2-9C3D-4E5F-8A6B-1C2D3E4F5A6B", After = "x" }));

    [Fact]
    public void EventsRejectsOutOfRangeLimit() =>
        Assert.Equal("event limit must be between 1 and 1000",
            DispatchError(ControlCommand.EventsRead, args: new ControlArgs { Limit = 1001 }));

    [Fact]
    public void EventsDefaultsLimitTo100()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest { Cmd = ControlCommand.EventsRead });
        var options = Assert.IsType<ControlEventReadOptions>(actions.Calls.Single().Arg);
        Assert.Equal(100, options.Limit);
        Assert.Null(options.Cursor);
    }

    [Fact]
    public void EventsSplitsKindsOnCommaAndTrims()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest
        {
            Cmd = ControlCommand.EventsRead,
            Args = new ControlArgs { Kinds = [" status ,notify ", "tree.changed"], Limit = 10 },
        });
        var options = Assert.IsType<ControlEventReadOptions>(actions.Calls.Single().Arg);
        Assert.Equal(3, options.Kinds!.Count);
    }

    [Fact]
    public void EventsRejectsUnknownKind() =>
        Assert.Equal("invalid event kind: weird",
            DispatchError(ControlCommand.EventsRead, args: new ControlArgs { Kinds = ["weird"] }));

    // --- app / window commands ---------------------------------------------------

    [Fact]
    public void NotifyRequiresBody() =>
        Assert.Equal("notify requires a body", DispatchError(ControlCommand.Notify));

    [Fact]
    public void NotifyRejectsEmptyBody() =>
        Assert.Equal("notify requires a body", DispatchError(ControlCommand.Notify, args: new ControlArgs { Body = "" }));

    [Fact]
    public void SidebarParsesShowHide() =>
        Assert.Equal("invalid sidebar mode: grow",
            DispatchError(ControlCommand.Sidebar, args: new ControlArgs { Mode = "grow" }));

    [Fact]
    public void SidebarWidthRequiresFinite() =>
        Assert.Equal("sidebar.width requires a width in points",
            DispatchError(ControlCommand.SidebarWidth, args: new ControlArgs { SidebarWidth = double.NaN }));

    [Fact]
    public void WorkspaceMoveRequiresTo() =>
        Assert.Equal("workspace.move requires --to", DispatchError(ControlCommand.WorkspaceMove));

    [Fact]
    public void WorkspaceMoveRejectsInvalidTo() =>
        Assert.Equal("workspace.move --to must be up|down|top|bottom",
            DispatchError(ControlCommand.WorkspaceMove, args: new ControlArgs { To = "left" }));

    [Fact]
    public void WorkspaceFocusRejectsUnknownMode() =>
        Assert.Equal("invalid focus mode: mark (on|off|toggle|add)",
            DispatchError(ControlCommand.WorkspaceFocus, args: new ControlArgs { Mode = "mark" }));

    [Fact]
    public void WorkspaceFilterDefaultsToToggle()
    {
        var (dispatcher, actions) = Make();
        dispatcher.Dispatch(new ControlRequest { Cmd = ControlCommand.WorkspaceFilter });
        Assert.Equal(ToggleMode.Toggle, actions.Calls.Single().Arg);
    }

    [Fact]
    public void WorkspaceRenameRequiresTrimmedName() =>
        Assert.Equal("workspace.rename requires a name",
            DispatchError(ControlCommand.WorkspaceRename, args: new ControlArgs { Name = "   " }));

    [Fact]
    public void WindowResizeRequiresPositive() =>
        Assert.Equal("window.resize requires positive width and height",
            DispatchError(ControlCommand.WindowResize, args: new ControlArgs { Width = 0, Height = 100 }));

    [Fact]
    public void WindowMoveRequiresCoordinates() =>
        Assert.Equal("window.move requires x and y", DispatchError(ControlCommand.WindowMove));

    [Fact]
    public void WindowMinimizeParsesMode() =>
        Assert.Equal("invalid window minimize mode: maybe",
            DispatchError(ControlCommand.WindowMinimize, args: new ControlArgs { Mode = "maybe" }));

    [Fact]
    public void WindowGoRequiresDirection() =>
        Assert.Equal("window.go requires --to next|prev", DispatchError(ControlCommand.WindowGo));

    [Fact]
    public void SplitRejectsBadAxis() =>
        Assert.Equal("invalid split axis: diagonal (vertical|horizontal)",
            DispatchError(ControlCommand.SessionSplit, args: new ControlArgs { Axis = "diagonal" }));

    [Fact]
    public void ResizeRequiresIntent() =>
        Assert.Equal("session.resize requires --split-ratio, --grow-left, or --grow-right",
            DispatchError(ControlCommand.SessionResize));

    [Fact]
    public void ResizeRejectsBothForms() =>
        Assert.Equal("session.resize: --split-ratio is mutually exclusive with --grow-left/--grow-right",
            DispatchError(ControlCommand.SessionResize, args: new ControlArgs { Ratio = 0.5, RatioDelta = 0.1 }));

    // --- deferred commands ---------------------------------------------------

    [Theory]
    [InlineData(ControlCommand.ZmxList)]
    [InlineData(ControlCommand.SessionOverlayOpen)]
    [InlineData(ControlCommand.SessionHudOpen)]
    [InlineData(ControlCommand.Quick)]
    [InlineData(ControlCommand.Dashboard)]
    [InlineData(ControlCommand.PickOpen)]
    [InlineData(ControlCommand.KeymapReload)]
    [InlineData(ControlCommand.SessionSearch)]
    [InlineData(ControlCommand.SessionRestore)]
    public void DeferredCommandsAnswerUnhandledFallback(ControlCommand command)
    {
        var response = Dispatch(command);
        Assert.False(response.Ok);
        Assert.Equal($"control dispatcher did not handle {command.WireName()}", response.Error);
    }
}

public class ResolveTests
{
    private static readonly Guid First = Guid.Parse("1F0A2B3C-4D5E-4F60-8A7B-8C9D0E1F2A3B");
    private static readonly Guid Second = Guid.Parse("1F0A9999-4D5E-4F60-8A7B-8C9D0E1F2A3B");
    private static readonly Guid Third = Guid.Parse("2A0B1C2D-3E4F-4051-9B8A-7C6D5E4F3B2C");

    [Fact]
    public void EmptyTargetIsNotFound() =>
        Assert.Equal(TargetResolution.Outcome.NotFound,
            ControlResolve.Resolve("", [First], First).Result);

    [Fact]
    public void ActiveResolvesActiveId() =>
        Assert.Equal(First, ControlResolve.Resolve("active", [First, Second], First).Id);

    [Fact]
    public void ActiveWithoutSelectionIsNotFound() =>
        Assert.Equal(TargetResolution.Outcome.NotFound,
            ControlResolve.Resolve("active", [First], null).Result);

    [Fact]
    public void ExactMatchIsCaseInsensitive() =>
        Assert.Equal(First, ControlResolve.Resolve(First.ToString().ToLowerInvariant(), [First, Second], null).Id);

    [Fact]
    public void UniquePrefixResolves() =>
        Assert.Equal(Second, ControlResolve.Resolve("1f0a9999", [First, Second], null).Id);

    [Fact]
    public void SharedPrefixIsAmbiguous()
    {
        var resolution = ControlResolve.Resolve("1f0a", [First, Second, Third], null);
        Assert.Equal(TargetResolution.Outcome.Ambiguous, resolution.Result);
        Assert.Equal(2, resolution.Hits.Count);
    }

    [Fact]
    public void UnknownPrefixIsNotFound() =>
        Assert.Equal(TargetResolution.Outcome.NotFound,
            ControlResolve.Resolve("ffff", [First], null).Result);

    [Fact]
    public void NotFoundMessageWording() =>
        Assert.Equal("no such session: zz", ControlResolve.NotFoundMessage("session", "zz"));

    [Fact]
    public void AmbiguousMessageListsEightCharPrefixes() =>
        Assert.Equal("ambiguous session prefix '1f0a' → 1F0A2B3C, 1F0A9999",
            ControlResolve.AmbiguousMessage("session", "1f0a", [First, Second]));

    [Fact]
    public void SocketPathDerivation()
    {
        Assert.Equal(Path.Combine("/tmp/x", "agterm.sock"), ControlResolve.SocketPath("/tmp/x", "/ignored"));
        Assert.Equal(Path.Combine("/state", "agterm.sock"), ControlResolve.SocketPath(null, "/state"));
        Assert.Equal("/state/agterm.sock.lock", ControlResolve.OwnershipLockPath("/state/agterm.sock"));
    }

    [Fact]
    public void WireIdsAreUppercase() =>
        Assert.Equal("1F0A2B3C-4D5E-4F60-8A7B-8C9D0E1F2A3B", ControlResolve.WireId(First));
}
