using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Text;
using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;
using Xunit;

namespace Agterm.Core.Tests;

/// <summary>
/// Socket round-trips: a real AF_UNIX client speaking the wire protocol against the server over the real
/// model — the same path agtermctl.exe exercises. Runs on macOS during authoring; AF_UNIX behaves the same
/// on Windows 10+.
/// </summary>
public class ControlServerTests : IDisposable
{
    private readonly string _directory;
    private ControlServer _server = null!;
    private string _socketPath = null!;

    private sealed class RecordingSurface : IPaneSurface
    {
        public List<string> Typed { get; } = [];
        public int Returns { get; set; }
        public string Screen { get; set; } = "";
        public int Column { get; set; } = 7;
        public bool Realized { get; set; } = true;
        public string Token { get; set; } = Guid.NewGuid().ToString("N")[..12];
        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
        public bool IsRealized => Realized;
        public string PaneToken => Token;
        public void TypeText(string text) => Typed.Add(text);
        public void PressReturn() => Returns++;
        public string ReadScreenText(bool all) => Screen;
        public string ReadScreenLines(int lines) => string.Join('\n', Screen.Split('\n')[^Math.Min(lines, Screen.Split('\n').Length)..]);
        public int ReadCursorColumn() => Column;
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

    public ControlServerTests()
    {
        // sun_path caps at 104 bytes on macOS: keep the rendezvous short
        _directory = Path.Combine("/tmp", "agtt-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);
    }

    private void StartServer()
    {
        _socketPath = Path.Combine(_directory, "agterm.sock");
        var library = new WindowLibraryModel(id => new StoreModel(id));
        var info = library.NewWindow();
        var store = library.LoadStore(info.Id);
        store.AddWorkspace("work");
        var ring = new ControlEventRing();
        store.EventSink += draft => ring.Append(draft);
        var actions = new StoreControlActions(library, ring, NullWindowHost.Instance,
            (session, split) =>
            {
                var surface = new RecordingSurface { Token = $"{ControlResolve.WireId(session.Id)[..8]}-{(split ? "right" : "left")}" };
                return surface;
            });
        actions.App = new AppIdentity("0.1.0-test", "deadbee");
        _server = new ControlServer(_socketPath, new ControlDispatcher(actions));
        Assert.True(_server.Start(), "server must bind");
    }

    private JsonNode RoundTrip(string requestLine)
    {
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(new UnixDomainSocketEndPoint(_socketPath));
        client.Send(Encoding.UTF8.GetBytes(requestLine + "\n"));
        using var reader = new StreamReader(new NetworkStream(client), Encoding.UTF8);
        var reply = reader.ReadLine();
        Assert.NotNull(reply);
        return JsonNode.Parse(reply!)!;
    }

    [Fact]
    public void VersionAnswersAppIdentity()
    {
        StartServer();
        var reply = RoundTrip("{\"cmd\":\"version\"}");
        Assert.True((bool)reply!["ok"]!);
        Assert.Equal("0.1.0-test", reply["result"]!["app"]!["version"]!.ToString());
        Assert.Equal("deadbee", reply["result"]!["app"]!["commit"]!.ToString());
    }

    [Fact]
    public void UnknownCommandNamesTheCmd()
    {
        StartServer();
        var reply = RoundTrip("{\"cmd\":\"totally.bogus\"}");
        Assert.False((bool)reply!["ok"]!);
        Assert.Equal("invalid request: Cannot initialize Command from invalid String value totally.bogus",
            reply["error"]!.ToString());
    }

    [Fact]
    public void DeferredCommandAnswersUnhandledFallback()
    {
        StartServer();
        var reply = RoundTrip("{\"cmd\":\"zmx.list\"}");
        Assert.False((bool)reply!["ok"]!);
        Assert.Equal("control dispatcher did not handle zmx.list", reply["error"]!.ToString());
    }

    [Fact]
    public void SessionLifecycleOverTheSocket()
    {
        StartServer();
        var created = RoundTrip("{\"cmd\":\"session.new\",\"args\":{\"cwd\":\"/tmp/proj\",\"name\":\"build\"}}");
        var id = created!["result"]!["id"]!.ToString();
        Assert.Matches("^[0-9A-F-]{36}$", id);

        var tree = RoundTrip("{\"cmd\":\"tree\"}");
        var node = tree["result"]!["tree"]!["workspaces"]!.AsArray()[0]!["sessions"]!.AsArray()[0]!;
        Assert.Equal("build", node["name"]!.ToString());
        Assert.Equal("/tmp/proj", node["cwd"]!.ToString());
        Assert.Equal("true", node["realized"]!.ToString());

        var typed = RoundTrip($"{{\"cmd\":\"session.type\",\"target\":\"{id}\",\"args\":{{\"text\":\"make test\\n\"}}}}");
        Assert.True((bool)typed!["ok"]!);

        var read = RoundTrip($"{{\"cmd\":\"session.text\",\"target\":\"{id}\"}}");
        Assert.True((bool)read!["ok"]!);
    }

    [Fact]
    public void EventsReadAnchorsBootstrapAndServesCursorReads()
    {
        StartServer();
        // bootstrap: subscribe-from-now — no history, an anchor at the tail
        var bootstrap = RoundTrip("{\"cmd\":\"events.read\"}")!["result"]!["events"]!;
        var run = bootstrap["run"]!.ToString();
        var after = (ulong)bootstrap["next"]!;
        Assert.Empty(bootstrap["items"]!.AsArray());

        RoundTrip("{\"cmd\":\"workspace.new\",\"args\":{\"name\":\"extra\"}}");

        var page = RoundTrip("{\"cmd\":\"events.read\",\"args\":{\"run\":\"" + run + "\",\"after\":\"" + after + "\"}}")!["result"]!["events"]!;
        Assert.NotEmpty(page["items"]!.AsArray());
        Assert.Equal(run, page["run"]!.ToString());

        var badRun = RoundTrip("{\"cmd\":\"events.read\",\"args\":{\"run\":\"" + Guid.NewGuid() + "\",\"after\":\"" + after + "\"}}");
        Assert.Equal("event run changed", badRun!["error"]!.ToString());
    }

    [Fact]
    public void SecondInstanceIsRefusedAndAdvertisesUnavailable()
    {
        StartServer();
        var second = new ControlServer(_socketPath, new ControlDispatcher(new StubActions()));
        Assert.True(second.Refused);
        Assert.Equal(_socketPath + ".unavailable", second.ResolvedSocketPath);
        Assert.False(second.Start());
        second.Dispose();
        // the owner keeps serving
        var reply = RoundTrip("{\"cmd\":\"version\"}");
        Assert.True((bool)reply!["ok"]!);
    }

    [Fact]
    public void StoppedOwnerReleasesAndSuccessorTakesOver()
    {
        StartServer();
        _server.Stop();
        var successor = new ControlServer(_socketPath, new ControlDispatcher(new StubActions()));
        Assert.False(successor.Refused);
        Assert.True(successor.Start());
        successor.Dispose();
    }

    [Fact]
    public void OversizedRequestIsRejectedWithReadError()
    {
        StartServer();
        var huge = "{\"cmd\":\"session.type\",\"args\":{\"text\":\"" + new string('x', ControlWire.MaxRequestLineBytes) + "\"}}";
        var reply = RoundTrip(huge);
        Assert.False((bool)reply!["ok"]!);
        Assert.Equal("request too large or read failed", reply["error"]!.ToString());
    }

    [Fact]
    public void PathResolutionPrefersExplicitThenEnv()
    {
        var env = new Dictionary<string, string> { ["AGTERM_CONTROL_SOCKET"] = "/tmp/from-env.sock" };
        Assert.Equal("/explicit", SocketPathResolver.Resolve("/explicit", env));
        Assert.Equal("/tmp/from-env.sock", SocketPathResolver.Resolve(null, env));
        env = new Dictionary<string, string> { ["AGTERM_STATE_DIR"] = "/tmp/state" };
        Assert.Equal(Path.Combine("/tmp/state", "agterm.sock"), SocketPathResolver.Resolve(null, env));
    }

    private sealed class StubActions : IControlActions
    {
        public ControlResponse ControlTree(string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ReadEvents(ControlEventReadOptions options) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse AppIdentity() => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CreateSession(ControlSessionCreateOptions options) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse DuplicateSession(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SelectSession(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse GoSession(string? window, SessionNavigation direction) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CloseSession(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CloseSessions(IReadOnlyList<string> targets, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse RenameSession(string? target, string? window, string name) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse RevealSession(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CreateWorkspace(string? window, string? name, bool collapsed) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SelectWorkspace(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse GoWorkspace(string? window, WorkspaceNavigation direction) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse RenameWorkspace(string? target, string? window, string name) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse DeleteWorkspace(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse MoveWorkspace(string? target, string? window, ReorderDirection direction) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse FocusWorkspace(string? target, string? window, WorkspaceFocusMode mode) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetWorkspaceFilter(string? window, ToggleMode mode) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetWorkspaceExpansion(string? target, string? window, bool expanded) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse MoveSession(string? target, string? window, ControlSessionMove move) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse MoveSessions(IReadOnlyList<string> targets, string? window, ControlSessionMove move) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetSessionFlag(string? target, string? window, string? mode) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetSessionContext(string? target, string? window, string? context) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse MarkSessionSeen(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetSessionStatus(string? target, string? window, ControlSessionStatusUpdate update) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SplitSession(string? target, string? window, string? mode, SplitAxis? axis) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CloseSessionSplit(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SwapSessionPanes(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse FocusSessionPane(string? target, string? window, string? pane) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ResizeSplit(string? target, string? window, ControlSplitResize resize) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ReadSurfaceCursor(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse Font(string? target, string? window, StatusPane? pane, string action) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse TypeSession(string? target, string? window, ControlSessionTypeOptions options) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CopySessionSelection(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse PasteSession(string? target, string? window, StatusPane? pane) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SelectAllSession(string? target, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ReadSessionText(string? target, string? window, ControlSessionTextOptions options) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SendNotification(string? target, string? window, string? title, string body) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetTheme(ControlArgs? args) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ListThemes() => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetSidebarVisibility(ToggleMode mode) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse ExpandSidebar(string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse CollapseSidebar(string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse SetSidebarWidth(double points, string? window) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowNew(string? name, bool minimized) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowList() => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowSelect(string? target) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowGo(WorkspaceNavigation direction) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowClose(string? target) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowRename(string? target, string name) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowDelete(string? target) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowResize(string? target, int width, int height) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowMove(string? target, int x, int y, int? display) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowZoom(string? target) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowFullscreen(string? target) => ControlResponse.OkWith(new ControlResult());
        public ControlResponse WindowMinimize(string? target, ToggleMode mode) => ControlResponse.OkWith(new ControlResult());
    }

    public void Dispose()
    {
        _server?.Dispose();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }
}
