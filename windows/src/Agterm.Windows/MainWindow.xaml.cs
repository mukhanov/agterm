using IoPath = System.IO.Path;
using Windows.System;
using Agterm.Core;
using Agterm.Core.Control;
using Agterm.Core.Model;
using Agterm.Core.Protocol;
using Agterm.Terminal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Microsoft.UI.Input;
using Windows.UI.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Agterm.Windows;

// The macOS-shaped single window: a sidebar over the window library, a deck of mounted pane hosts, and
// the control socket served on the UI thread.
public sealed partial class MainWindow : Window
{
    private const string StateDirOverrideKey = nameof(StateDirOverrideKey); // reserved, unused today

    private readonly string _socketPath = SocketPathResolver.Resolve(null);
    private readonly SnapshotStore _persistence;
    private readonly WindowLibraryModel _library;
    private readonly ControlEventRing _ring = new();
    private readonly StoreControlActions _actions;
    private readonly ControlServer _server;
    private readonly ShellProfile? _profile;
    private readonly Dictionary<Guid, Grid> _paneAreas = [];
    private bool _dashboardMode;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _queue;

    public MainWindow()
    {
        InitializeComponent();
        _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var stateDir = IoPath.GetDirectoryName(_socketPath)!;
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
            (session, split) =>
            {
                try
                {
                    return library.StoreForSession(session.Id) is { } owner
                        ? CreateSurface(owner, session, split)
                        : null;
                }
                catch (Exception e)
                {
                    UiLog("surface factory: " + e);
                    throw;
                }
            });
        _actions.App = new AppIdentity("0.1.0-windows", "windows-port");

        _server = new ControlServer(_socketPath, new ControlDispatcher(_actions), MarshalToUi);
        _server.Start();

        Title = "agterm";
        AddWindowAccelerators();
        Activated += (_, _) => FocusActivePane();
        Closed += (_, _) => Shutdown();
        foreach (var window in library.Windows)
            foreach (var session in library.StoreFor(window.Id)?.Workspaces.SelectMany(w => w.Sessions) ?? [])
                _queue.TryEnqueue(() => MountPane(session));

        // a fresh install opens with a live shell, like the macOS first launch
        var bootstrap = ActiveStore;
        if (bootstrap is not null && !bootstrap.Workspaces.Any(w => w.Sessions.Count > 0))
        {
            var workspace = bootstrap.Workspaces.LastOrDefault();
            if (workspace is not null)
            {
                var session = bootstrap.AddSession(workspace, Environment.CurrentDirectory, select: true);
                session.Surface = CreateSurface(bootstrap, session, split: false);
            }
        }

        RefreshAll();
    }

    internal static void UiLog(string message) =>
        File.AppendAllText(IoPath.Combine(IoPath.GetTempPath(), "agterm-ui.log"),
            DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);

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
            return new EchoPane(session);

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
            var workspaceHeader = new TextBlock
            {
                Text = workspace.Name.ToUpperInvariant(),
                FontSize = 11,
                Opacity = 0.6,
                Margin = new Thickness(8, 10, 4, 3),
                IsDoubleTapEnabled = true,
            };
            var workspaceId = workspace.Id;
            workspaceHeader.DoubleTapped += (_, _) => BeginRename(
                workspaceHeader, workspace.Name, name => store.RenameWorkspace(workspaceId, name));
            SidebarList.Children.Add(workspaceHeader);
            foreach (var session in workspace.Sessions)
            {
                var id = session.Id;
                var rowContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                var statusDot = StatusDot(session);
                if (statusDot is not null) rowContent.Children.Add(statusDot);
                rowContent.Children.Add(new TextBlock { Text = session.DisplayName });
                var row = new Button
                {
                    Content = rowContent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 4, 8, 4),
                    CornerRadius = new CornerRadius(4),
                    Background = id == selected ? new SolidColorBrush(Color.FromArgb(40, 88, 140, 255)) : null,
                    IsDoubleTapEnabled = true,
                };
                var closeItem = new MenuFlyoutItem { Text = "Close session" };
                closeItem.Click += (_, _) => CloseSession(id);
                row.ContextFlyout = new MenuFlyout { Items = { closeItem } };
                row.Click += (_, _) =>
                {
                    store.SelectSession(id);
                    RefreshAll();
                    FocusActivePane();
                };
                row.DoubleTapped += (_, _) => BeginRename(
                    row, session.CustomName ?? session.DisplayName,
                    name => store.RenameSession(id, string.IsNullOrWhiteSpace(name) ? null : name));
                SidebarList.Children.Add(row);
            }
        }
    }

    private static Ellipse? StatusDot(SessionModel session)
    {
        var color = session.Indicator.Status switch
        {
            StatusKind.Active => Color.FromArgb(255, 52, 199, 89),
            StatusKind.Completed => Color.FromArgb(255, 48, 176, 199),
            StatusKind.Blocked => Color.FromArgb(255, 255, 69, 58),
            _ => (Color?)null,
        };
        if (color is null) return null;
        return new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(color.Value), VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>Swaps a sidebar element's content for an inline rename box; Enter commits, Esc cancels.</summary>
    private void BeginRename(FrameworkElement host, string current, Action<string> commit)
    {
        var box = new TextBox { Text = current, Margin = host.Margin, FontSize = 12 };
        var parent = host.Parent as Panel;
        if (parent is null) return;
        var index = parent.Children.IndexOf(host);
        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, box);
        box.Focus(FocusState.Programmatic);
        box.SelectAll();
        var done = false;
        void Finish(bool save)
        {
            if (done) return;
            done = true;
            if (save) commit(box.Text);
            RefreshAll();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) Finish(true);
            else if (e.Key == VirtualKey.Escape) Finish(false);
        };
        box.LostFocus += (_, _) => Finish(false);
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
        if (_dashboardMode)
        {
            DeckHost.Child = BuildDashboard(store);
            return;
        }
        var session = store.SessionWithId(selected.Value);
        if (session is not null && _paneAreas.TryGetValue(session.Id, out var area))
        {
            Detach(area);
            DeckHost.Child = area;
            foreach (var host in area.Children.OfType<PaneHost>()) host.ActivateRenderer();
            FocusActivePane();
        }
        else
        {
            DeckHost.Child = null;
        }
    }

    private void MountPane(SessionModel session, bool forceSplitMount = false)
    {
        try
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
        catch (Exception e)
        {
            UiLog("MountPane: " + e);
        }
    }

    private void RebuildSplit(SessionModel session, Grid area)
    {
        var wantsSplit = session.SplitSurface is not null && session.SplitShown;
        var oldSplit = area.Children.OfType<PaneHost>().FirstOrDefault(h => h.RepresentsSplit);
        var oldDivider = area.Children.OfType<Rectangle>().FirstOrDefault();
        if (!wantsSplit)
        {
            if (oldSplit is not null) area.Children.Remove(oldSplit);
            if (oldDivider is not null) area.Children.Remove(oldDivider);
            area.ColumnDefinitions.Clear();
            area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return;
        }
        if (oldSplit is not null && oldDivider is not null) return;

        area.Children.Remove(oldSplit);
        area.Children.Remove(oldDivider);
        area.ColumnDefinitions.Clear();
        var ratio = Math.Clamp(session.SplitRatio, StoreModel.SplitRatioMin, StoreModel.SplitRatioMax);
        area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - ratio, GridUnitType.Star) });
        area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        area.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ratio, GridUnitType.Star) });

        var host = new PaneHost(session, split: true);
        Grid.SetColumn(host, 2);
        area.Children.Add(host);

        var divider = new Rectangle
        {
            Width = 6,
            Fill = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetColumn(divider, 1);
        divider.PointerPressed += (_, e) =>
        {
            divider.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        divider.PointerMoved += (_, e) =>
        {
            if (!e.Pointer.IsInContact || area.ActualWidth < 40) return;
            var fraction = e.GetCurrentPoint(area).Position.X / area.ActualWidth;
            session.SplitRatio = Math.Clamp(fraction, StoreModel.SplitRatioMin, StoreModel.SplitRatioMax);
            area.ColumnDefinitions[0].Width = new GridLength(1 - session.SplitRatio, GridUnitType.Star);
            area.ColumnDefinitions[2].Width = new GridLength(session.SplitRatio, GridUnitType.Star);
            e.Handled = true;
        };
        divider.PointerReleased += (_, e) => divider.ReleasePointerCapture(e.Pointer);
        area.Children.Add(divider);
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
        FocusActivePane();
    }

    private void OnNewWorkspace(object sender, RoutedEventArgs e)
    {
        var store = ActiveStore;
        if (store is null) return;
        store.AddWorkspace(store.DefaultWorkspaceName);
        RefreshAll();
    }

    private void CloseSession(Guid id)
    {
        var store = ActiveStore;
        if (store is null) return;
        var session = store.SessionWithId(id);
        if (session is null) return;
        session.Surface?.Teardown();
        session.SplitSurface?.Teardown();
        _paneAreas.Remove(id);
        store.CloseSession(id);
        RefreshAll();
    }

    private void CloseActiveSession()
    {
        var store = ActiveStore;
        var id = store?.SelectedSessionId;
        if (store is not null && id is not null) CloseSession(id.Value);
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
            // Ctrl+Shift like Windows Terminal: plain Ctrl combos belong to the shell
            Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
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

    /// <summary>The dashboard: every session of the active workspace in a two-column grid; clicking a
    /// cell selects that session and returns to the single-session deck.</summary>
    private Grid BuildDashboard(StoreModel store)
    {
        var grid = new Grid { Background = new SolidColorBrush(Color.FromArgb(255, 18, 18, 18)) };
        var sessions = store.Workspaces.SelectMany(w => w.Sessions).ToList();
        if (sessions.Count == 0)
        {
            grid.Children.Add(new TextBlock
            {
                Text = "нет сессий — нажмите session+",
                Opacity = 0.6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return grid;
        }
        var columns = 2;
        var rowCount = (sessions.Count + columns - 1) / columns;
        for (var rowIdx = 0; rowIdx < rowCount; rowIdx++)
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        for (var colIdx = 0; colIdx < columns; colIdx++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < sessions.Count; index++)
        {
            var session = sessions[index];
            FrameworkElement? content = null;
            if (_paneAreas.TryGetValue(session.Id, out var area))
            {
                Detach(area);
                content = area;
            }
            var cell = new Border
            {
                Margin = new Thickness(4),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                BorderThickness = new Thickness(1),
                Child = content,
                IsTabStop = true,
                UseSystemFocusVisuals = false,
            };
            cell.PointerPressed += (_, e) => cell.Focus(FocusState.Pointer);
            Grid.SetRow(cell, index / columns);
            Grid.SetColumn(cell, index % columns);
            var id = session.Id;
            cell.PointerPressed += (_, _) =>
            {
                _dashboardMode = false;
                store.SelectSession(id);
                RefreshAll();
            };
            grid.Children.Add(cell);
        }
        return grid;
    }

    private void OnSplit(object sender, RoutedEventArgs e) => SplitActiveSession();

    private void OnDashboard(object sender, RoutedEventArgs e)
    {
        _dashboardMode = !_dashboardMode;
        RefreshDeck();
    }

    /// <summary>An element can live under one parent; every re-parenting goes through this.</summary>
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentPresenter presenter: presenter.Content = null; break;
        }
    }

    /// <summary>Returns keyboard focus to the active pane so typing lands in the terminal. Rename
    /// boxes keep their focus.</summary>
    internal void FocusActivePane()
    {
        if (FocusIsInTextBox()) return; // a rename box keeps its focus
        var store = ActiveStore;
        var id = store?.SelectedSessionId;
        if (id is null) return;
        if (_paneAreas.TryGetValue(id.Value, out var area))
            foreach (var host in area.Children.OfType<PaneHost>())
                host.Focus(FocusState.Programmatic);
    }

    private static bool FocusIsInTextBox()
    {
        var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement();
        return focused is Microsoft.UI.Xaml.Controls.TextBox;
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
    private sealed class EchoPane : IPaneSurface
    {
        private readonly System.Text.StringBuilder _screen = new();

        public EchoPane(SessionModel session) =>
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
