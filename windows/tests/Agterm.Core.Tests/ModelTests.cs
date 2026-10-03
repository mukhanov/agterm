using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;
using Xunit;

namespace Agterm.Core.Tests;

public class ModelTests
{
    private sealed class FakeSurface : IPaneSurface
    {
        public bool TeardownCalled { get; private set; }
        public void Teardown() => TeardownCalled = true;
        public void PromoteToPrimaryPane() { }
        public bool IsRealized => true;
        public string PaneToken => "token";
        public void TypeText(string text) => Typed.Add(text);
        public void PressReturn() => Returns++;
        public List<string> Typed { get; } = [];
        public int Returns { get; set; }
        public string ReadScreenText(bool all) => "screen";
    public string ReadScreenLines(int lines) => "screen";
        public int ReadCursorColumn() => 0;
        public string? ReadSelection() => null;
        public double? CurrentFontSize() => 12;
        public void PerformFontAction(string action) { }
#pragma warning disable CS0067
        public event Action<string>? CwdChanged;
        public event Action<string>? TitleChanged;
        public event Action<string, string>? NotificationRequested;
        public event Action<int>? Exited;
#pragma warning restore CS0067
        public void Dispose() { }
    }

    private static (StoreModel Store, WorkspaceModel Workspace, SessionModel Session) Seed()
    {
        var store = new StoreModel(Guid.NewGuid());
        var workspace = store.AddWorkspace("work");
        var session = store.AddSession(workspace, "/Users/x/proj");
        return (store, workspace, session);
    }

    [Fact]
    public void DefaultWorkspaceNameCountsUp()
    {
        var store = new StoreModel(Guid.NewGuid());
        Assert.Equal("workspace 1", store.DefaultWorkspaceName);
        store.AddWorkspace("one");
        Assert.Equal("workspace 2", store.DefaultWorkspaceName);
    }

    [Fact]
    public void DisplayNameUsesCustomNameOverTitleOverBasename()
    {
        var session = new SessionModel(Guid.NewGuid(), "/Users/x/proj");
        Assert.Equal("proj", session.DisplayName);
        session.OscTitle = "agent — zsh";
        Assert.Equal("agent — zsh", session.DisplayName);
        session.CustomName = " build ";
        Assert.Equal("build", session.DisplayName);
    }

    [Theory]
    [InlineData("/Users/x/proj", "proj")]
    [InlineData("/Users/x/proj/", "proj")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("C:\\dev\\repo", "repo")]
    public void BasenamePins(string path, string expected) =>
        Assert.Equal(expected, SessionModel.Basename(path));

    [Fact]
    public void EmptyCwdShowsHomeShorthand()
    {
        var session = new SessionModel(Guid.NewGuid(), "");
        Assert.Equal("~", session.DisplayName);
    }

    [Fact]
    public void TreeOmitsFalseOptionals()
    {
        var (store, _, _) = Seed();
        var tree = store.Tree();
        var node = tree.Workspaces.Single().Sessions.Single();
        Assert.Null(node.HasSplit);
        Assert.Null(node.Status);
        Assert.Null(node.Unseen);
        Assert.True(node.Active);
        Assert.False(node.Split);
        Assert.True(node.Realized == false); // no surface attached
    }

    [Fact]
    public void TreeReportsSplitStateWhenPresent()
    {
        var (store, _, session) = Seed();
        session.HasSplit = true;
        session.SplitShown = true;
        session.SplitRatio = 0.55;
        var node = store.Tree().Workspaces.Single().Sessions.Single();
        Assert.True(node.HasSplit);
        Assert.True(node.Split);
        Assert.Equal(0.55, node.SplitRatio);
        Assert.Equal("vertical", node.SplitAxis);
        Assert.False(node.SplitFocused!.Value);
        Assert.NotNull(node.SplitCwd);
    }

    [Fact]
    public void TreeGatesStatusDecorationOnIdle()
    {
        var (store, _, session) = Seed();
        session.Indicator.Status = StatusKind.Blocked;
        session.Indicator.Blink = true;
        session.Indicator.Color = "#ff8800";
        session.Indicator.StatusPane = StatusPane.Scratch;
        var node = store.Tree().Workspaces.Single().Sessions.Single();
        Assert.Equal("blocked", node.Status);
        Assert.Equal("scratch", node.StatusPane);
        Assert.True(node.StatusBlink);
        Assert.Equal("#ff8800", node.StatusColor);
    }

    [Fact]
    public void SelectClearsAutoResetStatusAndUnseen()
    {
        var (store, _, session) = Seed();
        session.Indicator.Status = StatusKind.Completed;
        session.Indicator.AutoReset = true;
        session.UnseenCount = 3;
        store.SelectedSessionId = null;
        store.SelectSession(session.Id);
        Assert.Equal(StatusKind.Idle, session.Indicator.Status);
        Assert.Equal(0, session.UnseenCount);
    }

    [Fact]
    public void CloseTeardownsSurfacesAndReselects()
    {
        var (store, workspace, first) = Seed();
        var second = store.AddSession(workspace, "/tmp", select: true);
        var surface = new FakeSurface();
        second.Surface = surface;
        store.SelectedSessionId = second.Id;
        Assert.True(store.CloseSession(second.Id));
        Assert.True(surface.TeardownCalled);
        Assert.Equal(first.Id, store.SelectedSessionId);
    }

    [Fact]
    public void MoveSessionReorders()
    {
        var (store, workspace, _) = Seed();
        var first = workspace.Sessions[0];
        var b = store.AddSession(workspace, "/b");
        store.MoveSession(first.Id, new ControlSessionMove.Reorder(ReorderDirection.Bottom));
        Assert.Equal([b.Id, first.Id], workspace.Sessions.Select(s => s.Id).ToArray());
        store.MoveSession(first.Id, new ControlSessionMove.Reorder(ReorderDirection.Up));
        Assert.Equal([first.Id, b.Id], workspace.Sessions.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void GoSessionWraps()
    {
        var (store, workspace, first) = Seed();
        var second = store.AddSession(workspace, "/2", select: true);
        var landed = store.GoSession(SessionNavigation.Next);
        Assert.Equal(first.Id, landed);
        landed = store.GoSession(SessionNavigation.Previous);
        Assert.Equal(second.Id, landed);
    }

    [Fact]
    public void GoWorkspaceRefusesSingleWorkspace()
    {
        var (store, _, _) = Seed();
        Assert.Null(store.GoWorkspace(WorkspaceNavigation.Next));
    }

    [Fact]
    public void RingBootstrapAnchorsAtTail()
    {
        var ring = new ControlEventRing(now: () => 100.0);
        ring.Append(new ControlEventDraft(ControlEventKind.TreeChanged));
        var result = ring.Read(null);
        Assert.Empty(result.Batch!.Items);
        Assert.Equal(1UL, result.Batch.Next);
    }

    [Fact]
    public void RingReadsAfterCursorWithKindsAndLimit()
    {
        var ring = new ControlEventRing(now: () => 100.0);
        ring.Append(new ControlEventDraft(ControlEventKind.Status, Payload: new ControlEventPayload { Status = "active" }));
        ring.Append(new ControlEventDraft(ControlEventKind.TreeChanged));
        ring.Append(new ControlEventDraft(ControlEventKind.Status, Payload: new ControlEventPayload { Status = "blocked" }));
        var page = ring.Read(new ControlEventCursor(ring.RunId, 0),
            kinds: new HashSet<ControlEventKind> { ControlEventKind.Status }, limit: 1);
        Assert.Single(page.Batch!.Items);
        Assert.Equal(1UL, page.Batch.Next);
    }

    [Fact]
    public void RingCursorErrorsCarryAnchor()
    {
        var ring = new ControlEventRing(now: () => 100.0);
        var other = new ControlEventRing(now: () => 100.0);
        Assert.Equal(ControlEventRing.ReadError.RunChanged,
            ring.Read(new ControlEventCursor(other.RunId, 0)).Error);
        Assert.Equal(ControlEventRing.ReadError.CursorAhead,
            ring.Read(new ControlEventCursor(ring.RunId, 5)).Error);
    }

    [Fact]
    public void RingExpiresWhenEntriesDropped()
    {
        var ring = new ControlEventRing(capacity: 2, now: () => 100.0);
        for (var i = 0; i < 4; i++)
            ring.Append(new ControlEventDraft(ControlEventKind.TreeChanged));
        // oldest is seq 3; a cursor at 1 has aged out
        Assert.Equal(ControlEventRing.ReadError.CursorExpired,
            ring.Read(new ControlEventCursor(ring.RunId, 1)).Error);
        var ok = ring.Read(new ControlEventCursor(ring.RunId, 2));
        Assert.Null(ok.Error);
    }

    [Fact]
    public void SnapshotRoundTripsThroughJson()
    {
        var (store, workspace, session) = Seed();
        session.CustomName = "build";
        session.Flagged = true;
        var second = store.AddSession(workspace, "/tmp");
        second.HasSplit = true;
        second.SplitShown = true;
        second.SplitRatio = 0.6;
        var snapshot = SnapshotStore.SnapshotOf(store);
        var restored = new StoreModel(Guid.NewGuid());
        SnapshotStore.RestoreInto(restored, snapshot, _ => null);
        Assert.Equal("work", restored.Workspaces.Single().Name);
        Assert.Equal(2, restored.Workspaces.Single().Sessions.Count);
        Assert.Equal("build", restored.Workspaces.Single().Sessions[0].DisplayName);
        Assert.True(restored.Workspaces.Single().Sessions[1].HasSplit);
        Assert.Equal(0.6, restored.Workspaces.Single().Sessions[1].SplitRatio);
    }

    [Fact]
    public void SnapshotIdsAreStableUppercase()
    {
        var (store, _, _) = Seed();
        var snapshot = SnapshotStore.SnapshotOf(store);
        Assert.Matches("^[0-9A-F-]{36}$", snapshot.Workspaces.Single().Id);
    }
}
