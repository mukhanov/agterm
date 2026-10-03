using Agterm.Core.Control;
using Agterm.Core.Protocol;

namespace Agterm.Core.Model;

/// <summary>A window's persisted metadata.</summary>
/// <param name="Id">Stable persistence key; the per-window snapshot file name.</param>
/// <param name="Name">The sidebar/tree label. "window N" is the auto form.</param>
public sealed record WindowInfo(Guid Id, string Name)
{
    public bool IsAutoName(string name) => name.StartsWith("window ", StringComparison.Ordinal);

    public static string AutoName(int index) => $"window {index}";
}

/// <summary>
/// The top-level owner above the workspace tree: ordered windows, lazily loaded per-window stores, and the
/// frontmost id — a port of WindowLibrary's model half. Persistence: windows.json (index) plus
/// &lt;id&gt;.json per window under the windows/ subdirectory.
/// </summary>
public sealed class WindowLibraryModel
{
    private readonly Func<Guid, StoreModel> _storeFactory;
    private readonly Dictionary<Guid, StoreModel> _stores = [];

    public List<WindowInfo> Windows { get; } = [];
    public Guid? FrontmostId { get; set; }

    public WindowLibraryModel(Func<Guid, StoreModel> storeFactory) => _storeFactory = storeFactory;

    public StoreModel? StoreFor(Guid windowId) => _stores.GetValueOrDefault(windowId);

    /// <summary>The lazily-loaded store; a window is "open" iff its store is loaded.</summary>
    public StoreModel LoadStore(Guid windowId)
    {
        if (_stores.TryGetValue(windowId, out var store)) return store;
        store = _storeFactory(windowId);
        _stores[windowId] = store;
        return store;
    }

    public StoreModel? ActiveStore => FrontmostId is { } frontmost ? _stores.GetValueOrDefault(frontmost) : null;

    public Guid? WindowIdForSession(Guid sessionId) =>
        _stores.FirstOrDefault(kv => kv.Value.SessionWithId(sessionId) is not null).Key;

    public StoreModel? StoreForSession(Guid sessionId) =>
        WindowIdForSession(sessionId) is { } windowId ? _stores.GetValueOrDefault(windowId) : null;

    public WindowInfo NewWindow(string? name = null)
    {
        var info = new WindowInfo(Guid.NewGuid(), string.IsNullOrWhiteSpace(name)
            ? WindowInfo.AutoName(Windows.Count + 1)
            : name!);
        Windows.Add(info);
        LoadStore(info.Id);
        FrontmostId = info.Id;
        return info;
    }

    public void CloseWindow(Guid windowId)
    {
        var info = Windows.FirstOrDefault(w => w.Id == windowId);
        if (info is null) return;
        _stores.Remove(windowId);
        if (FrontmostId == windowId)
            FrontmostId = _stores.Keys.FirstOrDefault();
    }

    public void RenameWindow(Guid windowId, string name)
    {
        var index = Windows.FindIndex(w => w.Id == windowId);
        if (index < 0) return;
        Windows[index] = Windows[index] with { Name = name };
    }

    public bool DeleteWindow(Guid windowId)
    {
        var index = Windows.FindIndex(w => w.Id == windowId);
        if (index < 0) return false;
        Windows.RemoveAt(index);
        _stores.Remove(windowId);
        if (FrontmostId == windowId)
            FrontmostId = _stores.Keys.FirstOrDefault();
        return Windows.Count > 0 || true;
    }

    /// <summary>Restores the index; always yields a valid non-empty set (seeds one window when the index is
    /// absent or corrupt — never throws), the migration/recovery contract macOS pins.</summary>
    public static WindowLibraryModel Restore(
        SnapshotStore store,
        Func<Guid, StoreModel> storeFactory,
        Func<StoreModel, Snapshot> snapshotOf,
        Action<StoreModel, Snapshot> restoreInto,
        Func<SessionModel, IPaneSurface?> surfaceFactory)
    {
        var library = new WindowLibraryModel(storeFactory);
        var index = store.Load<WindowsIndex>("windows.json");
        var valid = index is not null && index.Windows.Count > 0
            ? index
            : new WindowsIndex { Windows = [new WindowInfoSnapshot { Id = ControlResolve.WireId(Guid.NewGuid()), Name = "window 1" }] };
        foreach (var windowSnapshot in valid.Windows)
        {
            var id = ControlResolve.ParseWireId(windowSnapshot.Id) ?? Guid.NewGuid();
            library.Windows.Add(new WindowInfo(id, windowSnapshot.Name));
            if (windowSnapshot.IsOpen)
                library.LoadStore(id);
            var loaded = library.StoreFor(id);
            if (loaded is null) continue;
            var snapshot = store.Load<Snapshot>($"windows/{ControlResolve.WireId(id)}.json");
            if (snapshot is not null)
                restoreInto(loaded, snapshot);
            loaded.EnsureBootstrap();
            loaded.SaveRequested += () => store.Save(snapshotOf(loaded), $"windows/{ControlResolve.WireId(id)}.json");
        }
        library.FrontmostId = valid.Frontmost is { } frontmost && ControlResolve.ParseWireId(frontmost) is { } parsed
                && library.Windows.Any(w => w.Id == parsed)
            ? parsed
            : library.Windows.FirstOrDefault()?.Id;
        return library;
    }
}
