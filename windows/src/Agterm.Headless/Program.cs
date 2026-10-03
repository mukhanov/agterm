using Agterm.Core;
using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;
using Agterm.Terminal;

// Serves the control protocol with no UI. Default panes echo into a shared screen buffer; --live spawns
// real shells through ConPTY + the vendored VT engine, one process per pane. Run it, then point agtermctl
// at the socket it prints.

var live = args.Contains("--live");
var socketPath = SocketPathResolver.Resolve(
    args.FirstOrDefault(a => a != "--live"));

var stateDir = Path.GetDirectoryName(socketPath)!;
Directory.CreateDirectory(stateDir);
var persistence = new SnapshotStore(stateDir);

// Surface-event callbacks fire on the pty's own threads; the gate serializes their model mutations
// against socket requests, which the single accept thread dispatches inline.
var gate = new object();
var profile = live ? ShellProfiles.Default() : null;

WindowLibraryModel library = null!; // the surface factory captures it; set before any command runs
library = WindowLibraryModel.Restore(
    persistence,
    id => new StoreModel(id),
    SnapshotStore.SnapshotOf,
    (store, snapshot) => SnapshotStore.RestoreInto(store, snapshot, s => CreateSurface(store, s, split: false)),
    session => new EchoSurface(session)); // Restore never invokes this arm today

var ring = new ControlEventRing();
foreach (var window in library.Windows)
{
    var store = library.StoreFor(window.Id);
    if (store is null) continue;
    store.EventSink += draft => ring.Append(draft);
}

var actions = new StoreControlActions(library, ring, NullWindowHost.Instance,
    (session, split) => library.StoreForSession(session.Id) is { } owner
        ? CreateSurface(owner, session, split)
        : null);
actions.App = new AppIdentity("0.1.0-headless", "windows-port");

using var server = new ControlServer(socketPath, new ControlDispatcher(actions));
if (!server.Start())
{
    Console.Error.WriteLine(server.Refused
        ? $"another instance owns {socketPath}; serving nothing ({socketPath}.unavailable)"
        : $"could not bind {socketPath}");
    return 1;
}
Console.WriteLine($"agterm headless serving {server.ResolvedSocketPath}");
Console.WriteLine("press Enter to stop");
Console.ReadLine();
// quit-time capture: the live cwd rides the snapshot only when a save happens after the last OSC 7
foreach (var window in library.Windows)
{
    var store = library.StoreFor(window.Id);
    if (store is not null)
        persistence.Save(SnapshotStore.SnapshotOf(store), $"windows/{ControlResolve.WireId(window.Id)}.json");
}
return 0;

IPaneSurface CreateSurface(StoreModel store, SessionModel session, bool split)
{
    if (!live || profile is null)
        return new EchoSurface(session);

    var paneToken = Guid.NewGuid().ToString("N")[..12];
    var env = new Dictionary<string, string>
    {
        ["AGTERM_ENABLED"] = "1",
        ["AGTERM_SESSION_ID"] = ControlResolve.WireId(session.Id),
        ["AGTERM_SOCKET"] = socketPath,
        ["AGTERM_WINDOW_ID"] = ControlResolve.WireId(store.WindowId),
        ["AGTERM_WORKSPACE_ID"] = ControlResolve.WireId(store.WorkspaceForSession(session.Id)?.Id ?? Guid.Empty),
        ["AGTERM_PANE"] = split ? "right" : "left",
        ["AGTERM_PANE_ID"] = paneToken,
        ["TERM"] = "xterm-256color",
        ["TERM_PROGRAM"] = "agterm",
    };
    var surface = new TerminalEmulator(new TerminalSpawn(
        profile.CommandLine, session.InitialCwd, env, PaneToken: paneToken));
    surface.Exited += _ =>
    {
        lock (gate)
            library.StoreForSession(session.Id)?.HandlePaneExit(session.Id, surface);
    };
    surface.CwdChanged += cwd =>
    {
        lock (gate)
        {
            if (split) session.SplitCwd = cwd;
            else session.CurrentCwd = cwd;
        }
    };
    surface.TitleChanged += title =>
    {
        lock (gate)
        {
            if (split) session.SplitTitle = title;
            else session.OscTitle = title;
        }
    };
    return surface;
}

/// <summary>An in-memory pane: echoes typed text and Return into a small screen buffer.</summary>
internal sealed class EchoSurface : IPaneSurface
{
    private readonly System.Text.StringBuilder _screen = new();

    public EchoSurface(SessionModel session) =>
        _screen.AppendLine($"agterm headless — {session.DisplayName}");

    public void Teardown() { }
    public void PromoteToPrimaryPane() { }
    public bool IsRealized => true;
    public string PaneToken { get; } = Guid.NewGuid().ToString("N")[..12];
    public void TypeText(string text) => _screen.Append(text);
    public void PressReturn() => _screen.AppendLine();
    public string ReadScreenText(bool all) => _screen.ToString();
    public string ReadScreenLines(int lines)
    {
        var rows = _screen.ToString().Split('\n');
        return string.Join('\n', rows[^Math.Min(lines, rows.Length)..]);
    }
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
