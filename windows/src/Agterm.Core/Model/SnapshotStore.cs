using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using Agterm.Core.Control;
using Agterm.Core.Protocol;

namespace Agterm.Core.Model;

/// <summary>
/// The persisted form of one window's tree — key-compatible with agtermCore's Snapshot (same JSON field
/// names, UUIDs uppercase, nulls omitted), so a state directory is readable on either platform. Fields the
/// Windows build never writes are simply absent, and the lossy-optional decode on the Swift side drops
/// unknown members, which is exactly what a Mac-written file's extra fields do here.
/// </summary>
public sealed class Snapshot
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("selectedSessionID")] public string? SelectedSessionId { get; set; }

    [JsonPropertyName("workspaces")] public List<WorkspaceSnapshot> Workspaces { get; set; } = [];

    [JsonPropertyName("sidebarWidth")] public double? SidebarWidth { get; set; }

    [JsonPropertyName("sidebarVisible")] public bool? SidebarVisible { get; set; }

    [JsonPropertyName("sidebarMode")] public string? SidebarMode { get; set; }
}

public sealed class WorkspaceSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("sessions")] public List<SessionSnapshot> Sessions { get; set; } = [];

    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }
}

public sealed class SessionSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("customName")] public string? CustomName { get; set; }

    [JsonPropertyName("cwd")] public string Cwd { get; set; } = "";

    [JsonPropertyName("isSplit")] public bool? IsSplit { get; set; }

    [JsonPropertyName("hasSplit")] public bool? HasSplit { get; set; }

    [JsonPropertyName("splitAxis")] public string? SplitAxis { get; set; }

    [JsonPropertyName("fontSize")] public double? FontSize { get; set; }

    [JsonPropertyName("splitCwd")] public string? SplitCwd { get; set; }

    [JsonPropertyName("splitRatio")] public double? SplitRatio { get; set; }

    [JsonPropertyName("flagged")] public bool? Flagged { get; set; }

    [JsonPropertyName("initialCommand")] public string? InitialCommand { get; set; }

    [JsonPropertyName("commandWait")] public bool? CommandWait { get; set; }

    [JsonPropertyName("context")] public string? Context { get; set; }
}

/// <summary>The ordered window index: windows.json.</summary>
public sealed class WindowsIndex
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("frontmost")] public string? Frontmost { get; set; }

    [JsonPropertyName("windows")] public List<WindowInfoSnapshot> Windows { get; set; } = [];
}

public sealed class WindowInfoSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    [JsonPropertyName("name")] public string Name { get; set; } = "";

    [JsonPropertyName("isOpen")] public bool IsOpen { get; set; } = true;
}

/// <summary>Atomic JSON file persistence for the snapshots — a port of PersistenceStore's contract
/// (injectable directory, load returning nil for absent/corrupt, atomic writes).</summary>
public sealed class SnapshotStore(string directory)
{
    public string Directory { get; } = directory;

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public T? Load<T>(string fileName)
    {
        try
        {
            var path = Path.Combine(Directory, fileName);
            if (!File.Exists(path)) return default;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    public bool Save<T>(T value, string fileName)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, fileName);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Converts a live store into its persisted form (the structural fields the Windows build
    /// carries; cwd rides the CURRENT report when present, matching what quit-time capture does).</summary>
    public static Snapshot SnapshotOf(StoreModel store)
    {
        var snapshot = new Snapshot
        {
            SelectedSessionId = store.SelectedSessionId is { } selected ? ControlResolve.WireId(selected) : null,
            SidebarVisible = store.SidebarVisible,
            SidebarMode = store.SidebarMode,
            SidebarWidth = store.SidebarWidth,
            Workspaces = store.Workspaces.Select(workspace => new WorkspaceSnapshot
            {
                Id = ControlResolve.WireId(workspace.Id),
                Name = workspace.Name,
                Collapsed = workspace.IsExpanded ? null : true,
                Sessions = workspace.Sessions.Select(session => new SessionSnapshot
                {
                    Id = ControlResolve.WireId(session.Id),
                    CustomName = session.CustomName,
                    Cwd = session.EffectiveCwd,
                    HasSplit = session.HasSplit ? true : null,
                    IsSplit = session.HasSplit && session.SplitShown ? true : null,
                    SplitAxis = session.HasSplit ? session.SplitAxis.WireName() : null,
                    SplitCwd = session.SplitEffectiveCwd,
                    SplitRatio = session.HasSplit ? session.SplitRatio : null,
                    Flagged = session.Flagged ? true : null,
                    InitialCommand = session.InitialCommand,
                    CommandWait = session.InitialCommand is not null && session.CommandWait ? true : null,
                    Context = session.Context,
                }).ToList(),
            }).ToList(),
        };
        return snapshot;
    }

    /// <summary>Restores the model tree with fresh shells in the saved layout (restore mode 1 of 3). Ids
    /// are stable persistence keys, so a restored layout keeps them and scripts survive relaunch.</summary>
    public static void RestoreInto(StoreModel store, Snapshot snapshot, Func<SessionModel, IPaneSurface?> surfaceFactory)
    {
        store.Workspaces.Clear();
        foreach (var workspaceSnapshot in snapshot.Workspaces)
        {
            var workspace = new WorkspaceModel(
                ControlResolve.ParseWireId(workspaceSnapshot.Id) ?? Guid.NewGuid(),
                workspaceSnapshot.Name,
                expanded: workspaceSnapshot.Collapsed != true);
            foreach (var sessionSnapshot in workspaceSnapshot.Sessions)
            {
                var session = new SessionModel(
                    ControlResolve.ParseWireId(sessionSnapshot.Id) ?? Guid.NewGuid(),
                    sessionSnapshot.Cwd,
                    sessionSnapshot.CustomName,
                    sessionSnapshot.InitialCommand,
                    sessionSnapshot.CommandWait == true)
                {
                    Flagged = sessionSnapshot.Flagged == true,
                    Context = sessionSnapshot.Context,
                    HasSplit = sessionSnapshot.HasSplit == true,
                    SplitShown = sessionSnapshot.IsSplit == true,
                    SplitAxis = sessionSnapshot.SplitAxis is { } axis && SplitAxisExtensions.FromWireName(axis) is { } parsed
                        ? parsed
                        : SplitAxis.Vertical,
                    SplitRatio = sessionSnapshot.SplitRatio ?? 0.5,
                    SplitInitialCwd = sessionSnapshot.SplitCwd,
                };
                session.Surface = surfaceFactory(session);
                if (session.HasSplit)
                    session.SplitSurface = surfaceFactory(session);
                workspace.Sessions.Add(session);
            }
            store.Workspaces.Add(workspace);
        }
        store.SelectedSessionId = snapshot.SelectedSessionId is { } selected
            ? ControlResolve.ParseWireId(selected)
            : null;
        store.SidebarVisible = snapshot.SidebarVisible ?? true;
        store.SidebarMode = snapshot.SidebarMode ?? "tree";
        store.SidebarWidth = snapshot.SidebarWidth ?? 220;
    }
}
