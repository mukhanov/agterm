using Agterm.Core;
using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;

// Serves the control protocol with in-memory surfaces and no UI. Every pane echoes into a shared
// screen buffer, so `session.type` → `session.text` round-trips exactly as the e2e marker idiom
// expects. Run it, then point agtermctl at the socket it prints.

var socketPath = SocketPathResolver.Resolve(
    args.Length > 0 ? args[0] : null);

var stateDir = Path.GetDirectoryName(socketPath)!;
Directory.CreateDirectory(stateDir);
var persistence = new SnapshotStore(stateDir);

var library = WindowLibraryModel.Restore(
    persistence,
    id => new StoreModel(id),
    SnapshotStore.SnapshotOf,
    (store, snapshot) => SnapshotStore.RestoreInto(store, snapshot, session => new EchoSurface(session)),
    session => new EchoSurface(session));

var ring = new ControlEventRing();
foreach (var window in library.Windows)
{
    var store = library.StoreFor(window.Id);
    if (store is null) continue;
    store.EventSink += draft => ring.Append(draft);
}

var actions = new StoreControlActions(library, ring, NullWindowHost.Instance,
    (session, split) => new EchoSurface(session));
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
return 0;

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
