using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>
/// A control command name, the <c>cmd</c> field of a <see cref="ControlRequest"/>. Wire strings are shared
/// with the Swift <c>Command</c> enum (agtermCore/ControlProtocol.swift); an unknown one fails decoding with
/// "Cannot initialize Command from invalid String value …", mirroring Swift's DecodingError.
/// </summary>
[JsonConverter(typeof(ControlCommandJsonConverter))]
public enum ControlCommand
{
    [JsonStringEnumMemberName("tree")] Tree,

    [JsonStringEnumMemberName("events.read")] EventsRead,

    [JsonStringEnumMemberName("workspace.new")] WorkspaceNew,
    [JsonStringEnumMemberName("workspace.rename")] WorkspaceRename,
    [JsonStringEnumMemberName("workspace.delete")] WorkspaceDelete,
    [JsonStringEnumMemberName("workspace.select")] WorkspaceSelect,
    [JsonStringEnumMemberName("workspace.go")] WorkspaceGo,
    [JsonStringEnumMemberName("workspace.move")] WorkspaceMove,
    [JsonStringEnumMemberName("workspace.focus")] WorkspaceFocus,
    [JsonStringEnumMemberName("workspace.filter")] WorkspaceFilter,
    [JsonStringEnumMemberName("workspace.collapse")] WorkspaceCollapse,
    [JsonStringEnumMemberName("workspace.expand")] WorkspaceExpand,

    [JsonStringEnumMemberName("session.new")] SessionNew,
    [JsonStringEnumMemberName("session.duplicate")] SessionDuplicate,
    [JsonStringEnumMemberName("session.close")] SessionClose,
    [JsonStringEnumMemberName("session.select")] SessionSelect,
    [JsonStringEnumMemberName("session.go")] SessionGo,
    [JsonStringEnumMemberName("session.rename")] SessionRename,
    [JsonStringEnumMemberName("session.reveal")] SessionReveal,
    [JsonStringEnumMemberName("session.move")] SessionMove,
    [JsonStringEnumMemberName("session.type")] SessionType,
    [JsonStringEnumMemberName("session.status")] SessionStatus,
    [JsonStringEnumMemberName("session.flag")] SessionFlag,
    [JsonStringEnumMemberName("session.context")] SessionContext,
    [JsonStringEnumMemberName("session.seen")] SessionSeen,
    [JsonStringEnumMemberName("session.restore")] SessionRestore,
    [JsonStringEnumMemberName("session.background")] SessionBackground,
    [JsonStringEnumMemberName("session.split")] SessionSplit,
    [JsonStringEnumMemberName("session.split.close")] SessionSplitClose,
    [JsonStringEnumMemberName("session.swap")] SessionSwap,
    [JsonStringEnumMemberName("session.lead")] SessionLead,
    [JsonStringEnumMemberName("session.scratch")] SessionScratch,
    [JsonStringEnumMemberName("session.focus")] SessionFocus,
    [JsonStringEnumMemberName("session.resize")] SessionResize,
    [JsonStringEnumMemberName("session.copy")] SessionCopy,
    [JsonStringEnumMemberName("session.paste")] SessionPaste,
    [JsonStringEnumMemberName("session.selectall")] SessionSelectAll,
    [JsonStringEnumMemberName("session.text")] SessionText,
    [JsonStringEnumMemberName("session.search")] SessionSearch,
    [JsonStringEnumMemberName("session.overlay.open")] SessionOverlayOpen,
    [JsonStringEnumMemberName("session.overlay.close")] SessionOverlayClose,
    [JsonStringEnumMemberName("session.overlay.resize")] SessionOverlayResize,
    [JsonStringEnumMemberName("session.overlay.reload")] SessionOverlayReload,
    [JsonStringEnumMemberName("session.overlay.navigate")] SessionOverlayNavigate,
    [JsonStringEnumMemberName("session.overlay.result")] SessionOverlayResult,
    [JsonStringEnumMemberName("session.overlay.submit")] SessionOverlaySubmit,
    [JsonStringEnumMemberName("session.overlay.copy")] SessionOverlayCopy,
    [JsonStringEnumMemberName("session.overlay.text")] SessionOverlayText,
    [JsonStringEnumMemberName("session.overlay.job.run")] SessionOverlayJobRun,
    [JsonStringEnumMemberName("session.hud.open")] SessionHudOpen,
    [JsonStringEnumMemberName("session.hud.update")] SessionHudUpdate,
    [JsonStringEnumMemberName("session.hud.close")] SessionHudClose,

    [JsonStringEnumMemberName("surface.zoom")] SurfaceZoom,
    [JsonStringEnumMemberName("surface.cursor")] SurfaceCursor,
    [JsonStringEnumMemberName("dashboard")] Dashboard,

    [JsonStringEnumMemberName("quick")] Quick,
    [JsonStringEnumMemberName("quick.type")] QuickType,
    [JsonStringEnumMemberName("quick.text")] QuickText,

    [JsonStringEnumMemberName("sidebar")] Sidebar,
    [JsonStringEnumMemberName("sidebar.mode")] SidebarMode,
    [JsonStringEnumMemberName("sidebar.flagged-layout")] SidebarFlaggedLayout,
    [JsonStringEnumMemberName("sidebar.expand")] SidebarExpand,
    [JsonStringEnumMemberName("sidebar.collapse")] SidebarCollapse,
    [JsonStringEnumMemberName("sidebar.width")] SidebarWidth,
    [JsonStringEnumMemberName("notify")] Notify,

    [JsonStringEnumMemberName("font.inc")] FontInc,
    [JsonStringEnumMemberName("font.dec")] FontDec,
    [JsonStringEnumMemberName("font.reset")] FontReset,

    [JsonStringEnumMemberName("window.new")] WindowNew,
    [JsonStringEnumMemberName("window.list")] WindowList,
    [JsonStringEnumMemberName("window.select")] WindowSelect,
    [JsonStringEnumMemberName("window.go")] WindowGo,
    [JsonStringEnumMemberName("window.close")] WindowClose,
    [JsonStringEnumMemberName("window.rename")] WindowRename,
    [JsonStringEnumMemberName("window.delete")] WindowDelete,
    [JsonStringEnumMemberName("window.resize")] WindowResize,
    [JsonStringEnumMemberName("window.move")] WindowMove,
    [JsonStringEnumMemberName("window.zoom")] WindowZoom,
    [JsonStringEnumMemberName("window.fullscreen")] WindowFullscreen,
    [JsonStringEnumMemberName("window.minimize")] WindowMinimize,

    [JsonStringEnumMemberName("keymap.reload")] KeymapReload,
    [JsonStringEnumMemberName("keymap.list")] KeymapList,
    [JsonStringEnumMemberName("hooks.reload")] HooksReload,
    [JsonStringEnumMemberName("hooks.list")] HooksList,
    [JsonStringEnumMemberName("browser.clear")] BrowserClear,
    [JsonStringEnumMemberName("config.reload")] ConfigReload,
    [JsonStringEnumMemberName("theme.set")] ThemeSet,
    [JsonStringEnumMemberName("theme.list")] ThemeList,

    [JsonStringEnumMemberName("pick.open")] PickOpen,
    [JsonStringEnumMemberName("pick.result")] PickResult,
    [JsonStringEnumMemberName("pick.cancel")] PickCancel,
    [JsonStringEnumMemberName("ask.open")] AskOpen,
    [JsonStringEnumMemberName("ask.result")] AskResult,
    [JsonStringEnumMemberName("ask.cancel")] AskCancel,

    [JsonStringEnumMemberName("restore.clear")] RestoreClear,
    [JsonStringEnumMemberName("version")] Version,
    [JsonStringEnumMemberName("restore.capture")] RestoreCapture,
    [JsonStringEnumMemberName("restore.mode")] RestoreMode,

    [JsonStringEnumMemberName("zmx.list")] ZmxList,
    [JsonStringEnumMemberName("zmx.prune")] ZmxPrune,
    [JsonStringEnumMemberName("zmx.kill")] ZmxKill,
    [JsonStringEnumMemberName("zmx.reset")] ZmxReset,
    [JsonStringEnumMemberName("zmx.tree")] ZmxTree,
    [JsonStringEnumMemberName("zmx.attach")] ZmxAttach,
    [JsonStringEnumMemberName("zmx.present")] ZmxPresent,

    [JsonStringEnumMemberName("debug.appearance")] DebugAppearance,
}

public static class ControlCommandExtensions
{
    private static readonly Dictionary<ControlCommand, string> WireNames = BuildWireNames();
    private static readonly Dictionary<string, ControlCommand> ByWireName =
        WireNames.ToDictionary(kv => kv.Value, kv => kv.Key);

    private static Dictionary<ControlCommand, string> BuildWireNames()
    {
        var map = new Dictionary<ControlCommand, string>();
        foreach (var field in typeof(ControlCommand).GetFields())
        if (field.GetCustomAttributes(typeof(JsonStringEnumMemberNameAttribute), false)
                .FirstOrDefault() is JsonStringEnumMemberNameAttribute named)
                map[(ControlCommand)field.GetValue(null)!] = named.Name;
        return map;
    }

    /// <summary>The wire string for this command, identical to the Swift raw value.</summary>
    public static string WireName(this ControlCommand command) => WireNames[command];

    public static bool TryFromWireName(string wireName, out ControlCommand command) =>
        ByWireName.TryGetValue(wireName, out command);
}

/// <summary>
/// Decodes the cmd field the way Swift's synthesized Codable does: an unknown raw value fails with
/// "Cannot initialize Command from invalid String value …", which the server surfaces as the
/// invalid-request error, naming the rejected command (version-skew diagnosis).
/// </summary>
public sealed class ControlCommandJsonConverter : JsonConverter<ControlCommand>
{
    public override ControlCommand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var wireName = reader.GetString()
            ?? throw new JsonException("Cannot initialize Command from invalid String value null");
        if (ControlCommandExtensions.TryFromWireName(wireName, out var command)) return command;
        throw new JsonException($"Cannot initialize Command from invalid String value {wireName}");
    }

    public override void Write(Utf8JsonWriter writer, ControlCommand value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.WireName());
}
