using Windows.System;
using Agterm.Core;
using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;
using Agterm.Terminal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Agterm.Windows;

// The macOS-shaped single window: a sidebar over the window library, a deck of mounted pane hosts, and
// the control socket served on the UI thread. Terminal drawing is plain monospace text for now; the
// Direct2D renderer replaces PaneHost's visuals without touching this wiring.
public sealed partial class MainWindow : Window
{
    private readonly string _socketPath = SocketPathResolver.Resolve(null);
    private readonly SnapshotStore _persistence;
    private readonly WindowLibraryModel _library;
    private readonly ControlEventRing _ring = new();
    private readonly StoreControlActions _actions;
    private readonly ControlServer _server;
    private readonly ShellProfile? _profile;
    private readonly Dictionary<Guid, Grid> _paneAreas = [];
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _queue;

    public MainWindow()
    {
        InitializeComponent();
        _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var stateDir = Path.GetDirectoryName(_socketPath)!;
        Directory.CreateDirectory(stateDir);
        _persistence = new SnapshotStore(stateDir);
        _profile = ShellProfiles.Default();

        WindowLibraryModel library = null!; // the surface factory captures it; set before any command runs
        library = WindowLibraryModel.Restore(
            _persistence,
            id => new StoreModel(id),
            SnapshotStore.SnapshotOf,
            (store, snapshot) => SnapshotStore.RestoreInto(store, snapshot, s => CreateSurface(store, s, split: false)),
            _ => null); // Restore never invokes this arm today
        _library = library;

        foreach (var window in library.Windows)
        {
            var store = library.StoreFor(window.Id);
            if (store is null) continue;
            store.EventSink += draft =>
            {
                lock (_ring) _ring.Append(draft);
                _queue.TryEnqueue(RefreshAll);
            };
        }

        _actions = new StoreControlActions(library, _ring, NullWindowHost.Instance,
            (session, split) => library.StoreForSession(session.Id) is { } owner
                ? CreateSurface(owner, session, split)
                : null);
        _actions.App = new AppIdentity("0.1.0-windows", "windows-port");

        _server = new ControlServer(_socketPath, new ControlDispatcher(_actions), MarshalToUi);
        _server.Start();

        Title = "agterm";
        AddWindowAccelerators();
        Closed += (_, _) => Shutdown();
        foreach (var window in library.Windows)
            foreach (var session in library.StoreFor(window.Id)?.Workspaces.SelectMany(w => w.Sessions) ?? [])
                _queue.TryEnqueue(() => MountPane(session));

        RefreshAll();
    }

    private ControlResponse MarshalToUi(Func<ControlResponse> work)
    {
        var tcs = new TaskCompletionSource<ControlResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.TryEnqueue(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    private IPaneSurface CreateSurface(StoreModel store, SessionModel session, bool split)
    {
        if (_profile is null)
            return new EchoSurface(session);

        var paneToken = Guid.NewGuid().ToString("N")[..12];
        var env = new Dictionary<string, string>
        {
            ["AGTERM_ENABLED"] = "1",
            ["AGTERM_SESSION_ID"] = ControlResolve.WireId(session.Id),
            ["AGTERM_SOCKET"] = _socketPath,
            ["AGTERM_WINDOW_ID"] = ControlResolve.WireId(store.WindowId),
            ["AGTERM_WORKSPACE_ID"] = ControlResolve.WireId(store.WorkspaceForSession(session.Id)?.Id ?? Guid.Empty),
            ["AGTERM_PANE"] = split ? "right" : "left",
            ["AGTERM_PANE_ID"] = paneToken,
            ["TERM"] = "xterm-256color",
            ["TERM_PROGRAM"] = "agterm",
        };
        var surface = new TerminalEmulator(new TerminalSpawn(
            _profile.CommandLine, session.InitialCwd, env, PaneToken: paneToken));
        var mounted = session;
        var isSplit = split;
        _queue.TryEnqueue(() => MountPane(mounted, isSplit));
        return surface;
    }

    // --- UI refresh ----------------------------------------------------------------

    private StoreModel? ActiveStore =>
        _library.ActiveStore
        ?? _library.StoreFor(_library.FrontmostId ?? Guid.Empty)
        ?? _library.StoreFor(_library.Windows.FirstOrDefault()?.Id ?? Guid.Empty);

    private void RefreshAll()
    {
        RefreshSidebar();
        RefreshDeck();
    }

    private void RefreshSidebar()
    {
        var store = ActiveStore;
        if (store is null) return;
        var selected = store.SelectedSessionId;
        SidebarList.Children.Clear();
        foreach (var workspace in store.Workspaces)
        {
            SidebarList.Children.Add(new TextBlock
            {
                Text = workspace.Name.ToUpperInvariant(),
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(8, 10, 4, 3),
            });
            foreach (var session in workspace.Sessions)
            {
                var id = session.Id;
                var row = new Button
                {
                    Content = session.DisplayName,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 4, 8, 4),
                    CornerRadius = new CornerRadius(4),
                    Background = id == selected ? new SolidColorBrush(Color.FromArgb(40, 88, 140, 255)) : null,
                };
                row.Click += (_, _) =>
                {
                    store.SelectSession(id);
                    RefreshAll();
                };
                SidebarList.Children.Add(row);
            }
        }
    }

    private void RefreshDeck()
    {
        var store = ActiveStore;
        var selected = store?.SelectedSessionId;
        if (selected is null || store is null)
        {
            DeckHost.Child = null;
            return;
        }
        var session = store.SessionWithId(selected.Value);
        DeckHost.Child = session is not null && _paneAreas.TryGetValue(session.Id, out var area) ? area : null;
    }

    private void MountPane(SessionModel session, bool forceSplitMount = false)
    {
        if (_paneAreas.TryGetValue(session.Id, out var existing))
        {
            RebuildSplit(session, existing);
            RefreshDeck();
            return;
        }
        var area = new Grid();
        var primary = new PaneHost(session, split: false);
        Grid.SetColumn(primary, 0);
        area.Children.Add(primary);
        _paneAreas[session.Id] = area;
        RebuildSplit(session, area);
        RefreshDeck();
    }

    private void RebuildSplit(SessionModel session, Grid area)
    {
        var wantsSplit = session.SplitSurface is not null && session.SplitShown;
        var oldSplit = area.Children.OfType<PaneHost>().FirstOrDefault(h => h.RepresentsSplit);
        if (!wantsSplit)
        {
            if (oldSplit is not null)
            {
                area.Children.Remove(oldSplit);
                area.ColumnDefinitions.Clear();
                area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            return;
        }
        if (oldSplit is not null) return;
        if (area.ColumnDefinitions.Count == 0)
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var ratio = Math.Clamp(session.SplitRatio, StoreModel.SplitRatioMin, StoreModel.SplitRatioMax);
        area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });
        var host = new PaneHost(session, split: true);
        Grid.SetColumn(host, 1);
        area.Children.Add(host);
    }

    // --- buttons and commands --------------------------------------------------------

    private void OnNewSession(object sender, RoutedEventArgs e)
    {
        var store = ActiveStore;
        if (store is null) return;
        var workspace = store.Workspaces.FirstOrDefault(w => w.Sessions.Any(s => s.Id == store.SelectedSessionId))
            ?? store.Workspaces.LastOrDefault();
        if (workspace is null) return;
        var session = store.AddSession(workspace, Environment.CurrentDirectory, select: true);
        session.Surface = CreateSurface(store, session, split: false);
        RefreshAll();
    }

    private void OnNewWorkspace(object sender, RoutedEventArgs e)
    {
        var store = ActiveStore;
        if (store is null) return;
        store.AddWorkspace(store.DefaultWorkspaceName);
        RefreshAll();
    }

    private void CloseActiveSession()
    {
        var store = ActiveStore;
        var id = store?.SelectedSessionId;
        if (store is null || id is null) return;
        var session = store.SessionWithId(id.Value);
        if (session is null) return;
        session.Surface?.Teardown();
        session.SplitSurface?.Teardown();
        _paneAreas.Remove(session.Id);
        store.CloseSession(session.Id);
        RefreshAll();
    }

    private void SplitActiveSession()
    {
        var store = ActiveStore;
        var id = store?.SelectedSessionId;
        if (store is null || id is null) return;
        var session = store.SessionWithId(id.Value);
        if (session is null) return;
        if (session.SplitSurface is null)
        {
            session.SplitSurface = CreateSurface(store, session, split: true);
            session.HasSplit = true;
            session.SplitShown = true;
            session.SplitFocused = true;
        }
        else
        {
            session.SplitShown = !session.SplitShown;
        }
        if (_paneAreas.TryGetValue(session.Id, out var area)) RebuildSplit(session, area);
        RefreshDeck();
    }

    private void SelectNextSession(int offset)
    {
        var store = ActiveStore;
        if (store is null) return;
        var flat = store.Workspaces.SelectMany(w => w.Sessions).ToList();
        if (flat.Count == 0) return;
        var index = flat.FindIndex(s => s.Id == store.SelectedSessionId);
        var next = flat[(index + offset % flat.Count + flat.Count) % flat.Count];
        store.SelectSession(next.Id);
        RefreshAll();
    }

    private void AddWindowAccelerators()
    {
        AddAccelerator(VirtualKey.T, () => OnNewSession(this, new RoutedEventArgs()));
        AddAccelerator(VirtualKey.W, CloseActiveSession);
        AddAccelerator(VirtualKey.D, SplitActiveSession);
        AddAccelerator(VirtualKey.Tab, () => SelectNextSession(1));
        for (var digit = 1; digit <= 9; digit++)
        {
            var nth = digit;
            AddAccelerator(VirtualKey.Number1 + digit - 1, () => SelectNth(nth));
        }
    }

    private void AddAccelerator(VirtualKey key, Action action)
    {
        var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
        {
            Modifiers = VirtualKeyModifiers.Control,
            Key = key,
        };
        accelerator.Invoked += (_, args) => { action(); args.Handled = true; };
        ((FrameworkElement)Content).KeyboardAccelerators.Add(accelerator);
    }

    private void SelectNth(int nth)
    {
        var store = ActiveStore;
        var flat = store?.Workspaces.SelectMany(w => w.Sessions).ToList();
        if (store is null || flat is null || nth > flat.Count) return;
        store.SelectSession(flat[nth - 1].Id);
        RefreshAll();
    }

    private void Shutdown()
    {
        foreach (var window in _library.Windows)
        {
            var store = _library.StoreFor(window.Id);
            if (store is not null)
                _persistence.Save(SnapshotStore.SnapshotOf(store), $"windows/{ControlResolve.WireId(window.Id)}.json");
        }
        _server.Stop();
    }

    /// <summary>The fallback when no shell profile was detected; mirrors the headless host's echo panes.</summary>
    private sealed class EchoSurface : IPaneSurface
    {
        private readonly System.Text.StringBuilder _screen = new();

        public EchoSurface(SessionModel session) =>
            _screen.AppendLine($"agterm windows — {session.DisplayName}");

        public void Teardown() { }
        public void PromoteToPrimaryPane() { }
        public bool IsRealized => true;
        public string PaneToken { get; } = Guid.NewGuid().ToString("N")[..12];
        public void TypeText(string text) => _screen.Append(text);
        public void PressReturn() => _screen.AppendLine();
        public string ReadScreenText(bool all) => _screen.ToString();
        public string ReadScreenLines(int lines) => _screen.ToString();
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
}
