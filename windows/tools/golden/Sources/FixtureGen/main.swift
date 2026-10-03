// Encodes a matrix of ControlRequest/ControlResponse values into one-line JSON fixtures plus a manifest.
// Each fixture pins one wire shape; the C# tests decode+re-encode each and compare bytes.
import Foundation
import agtermCore

let outputDir = CommandLine.arguments.count > 1
    ? URL(fileURLWithPath: CommandLine.arguments[1])
    : URL(fileURLWithPath: #filePath, isDirectory: false)
        .deletingLastPathComponent()          // …/Sources/FixtureGen
        .deletingLastPathComponent()          // …/Sources
        .deletingLastPathComponent()          // …/golden
        .deletingLastPathComponent()          // …/tools
        .deletingLastPathComponent()          // …/windows
        .appendingPathComponent("tests/Agterm.Core.Tests/Golden")

try? FileManager.default.createDirectory(at: outputDir, withIntermediateDirectories: true)
// start clean so removed fixtures do not linger as stale expectations
for file in (try? FileManager.default.contentsOfDirectory(at: outputDir, includingPropertiesForKeys: nil)) ?? [] {
    try? FileManager.default.removeItem(at: file)
}

let encoder = JSONEncoder() // bare, exactly as the socket server encodes
var manifest: [String] = []

// pinned ids so fixtures are deterministic
let sid = "1F0A2B3C-4D5E-4F60-8A7B-8C9D0E1F2A3B"
let sid2 = "2A0B1C2D-3E4F-4051-9B8A-7C6D5E4F3B2C"
let wid = "AA11BB22-CC33-4DD4-8EE5-FF6600778899"
let wid2 = "BB22CC33-DD44-4EE5-8FF6-0077118899AB"
let windowID = "AB12CD34-EF56-4789-90AB-CDEF01234567"
let runID = "0B79E4F2-9C3D-4E5F-8A6B-1C2D3E4F5A6B"

@MainActor func request(_ name: String, _ value: ControlRequest) {
    manifest.append("\(name) request")
    try! Data((String(decoding: encoder.encode(value), as: UTF8.self) + "\n").utf8)
        .write(to: outputDir.appendingPathComponent("\(name).json"))
}

@MainActor func response(_ name: String, _ value: ControlResponse) {
    manifest.append("\(name) response")
    try! Data((String(decoding: encoder.encode(value), as: UTF8.self) + "\n").utf8)
        .write(to: outputDir.appendingPathComponent("\(name).json"))
}

@MainActor func errorResponse(_ name: String, _ message: String) {
    response(name, ControlResponse(ok: false, error: message))
}

// MARK: - requests

request("req_tree", ControlRequest(cmd: .tree))
request("req_tree_window", ControlRequest(cmd: .tree, target: nil, args: ControlArgs(window: windowID)))
request("req_version", ControlRequest(cmd: .version))
request("req_events_bootstrap", ControlRequest(cmd: .eventsRead, args: ControlArgs(kinds: ["status", "tree.changed"], limit: 50)))
request("req_events_cursor", ControlRequest(cmd: .eventsRead, args: ControlArgs(after: "12", run: runID)))
request("req_events_after_only", ControlRequest(cmd: .eventsRead, args: ControlArgs(after: "7")))

request("req_workspace_new", ControlRequest(cmd: .workspaceNew, args: ControlArgs(name: "work")))
request("req_workspace_new_collapsed", ControlRequest(cmd: .workspaceNew, args: ControlArgs(name: "work", collapsed: true)))
request("req_workspace_rename", ControlRequest(cmd: .workspaceRename, target: wid, args: ControlArgs(name: "renamed")))
request("req_workspace_delete", ControlRequest(cmd: .workspaceDelete, target: wid))
request("req_workspace_select", ControlRequest(cmd: .workspaceSelect, target: "active"))
request("req_workspace_go", ControlRequest(cmd: .workspaceGo, args: ControlArgs(to: "prev")))
request("req_workspace_move", ControlRequest(cmd: .workspaceMove, target: wid, args: ControlArgs(to: "top")))
request("req_workspace_focus", ControlRequest(cmd: .workspaceFocus, target: wid, args: ControlArgs(mode: "add")))
request("req_workspace_filter", ControlRequest(cmd: .workspaceFilter, args: ControlArgs(mode: "toggle")))
request("req_workspace_collapse", ControlRequest(cmd: .workspaceCollapse, target: wid))
request("req_workspace_expand", ControlRequest(cmd: .workspaceExpand, target: wid))

request("req_session_new", ControlRequest(cmd: .sessionNew))
request("req_session_new_cwd_name", ControlRequest(cmd: .sessionNew, args: ControlArgs(name: "build", cwd: "/Users/agent/proj")))
request("req_session_new_command", ControlRequest(cmd: .sessionNew, args: ControlArgs(noSelect: true, command: "ssh host", wait: true)))
request("req_session_new_workspace_name", ControlRequest(cmd: .sessionNew, args: ControlArgs(workspaceName: "work", createWorkspace: true)))
request("req_session_new_after", ControlRequest(cmd: .sessionNew, args: ControlArgs(after: sid)))
request("req_session_new_before", ControlRequest(cmd: .sessionNew, args: ControlArgs(before: sid2)))
request("req_session_new_minimized", ControlRequest(cmd: .sessionNew, args: ControlArgs(minimized: true)))
request("req_session_duplicate", ControlRequest(cmd: .sessionDuplicate, target: sid))
request("req_session_close", ControlRequest(cmd: .sessionClose, target: sid))
request("req_session_close_batch", ControlRequest(cmd: .sessionClose, target: sid, args: ControlArgs(targets: [sid, sid2])))
request("req_session_select", ControlRequest(cmd: .sessionSelect, target: sid))
request("req_session_go", ControlRequest(cmd: .sessionGo, args: ControlArgs(to: "next-attention")))
request("req_session_rename", ControlRequest(cmd: .sessionRename, target: sid, args: ControlArgs(name: "renamed")))
request("req_session_reveal", ControlRequest(cmd: .sessionReveal, target: sid))
request("req_session_move_to", ControlRequest(cmd: .sessionMove, target: sid, args: ControlArgs(to: "down")))
request("req_session_move_workspace", ControlRequest(cmd: .sessionMove, target: sid, args: ControlArgs(workspace: wid)))
request("req_session_move_after", ControlRequest(cmd: .sessionMove, target: sid, args: ControlArgs(after: sid2)))

request("req_session_type", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "make test\r")))
request("req_session_type_lines", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "a\nb\r\n")))
request("req_session_type_select", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "y", select: true)))
request("req_session_type_pane", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "n", pane: "right")))
request("req_session_type_pane_id", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "n", paneID: "token-123")))
request("req_session_text", ControlRequest(cmd: .sessionText, target: sid))
request("req_session_text_all", ControlRequest(cmd: .sessionText, target: sid, args: ControlArgs(all: true)))
request("req_session_text_lines", ControlRequest(cmd: .sessionText, target: sid, args: ControlArgs(lines: 40)))
request("req_session_text_pane", ControlRequest(cmd: .sessionText, target: sid, args: ControlArgs(pane: "left")))

request("req_session_status", ControlRequest(cmd: .sessionStatus, target: sid, args: ControlArgs(status: "active")))
request("req_session_status_full", ControlRequest(cmd: .sessionStatus, target: sid, args: ControlArgs(
    pane: "scratch", status: "blocked", blink: true, autoReset: true, color: "#ff8800", shape: "diamond")))
request("req_session_status_pane_id", ControlRequest(cmd: .sessionStatus, target: sid, args: ControlArgs(paneID: "token-123", status: "idle")))
request("req_session_flag", ControlRequest(cmd: .sessionFlag, target: sid, args: ControlArgs(mode: "toggle")))
request("req_session_flag_clear", ControlRequest(cmd: .sessionFlag, args: ControlArgs(mode: "clear")))
request("req_session_seen", ControlRequest(cmd: .sessionSeen, target: sid))
request("req_session_context_set", ControlRequest(cmd: .sessionContext, target: sid, args: ControlArgs(text: "refactor the parser", mode: "set")))
request("req_session_context_clear", ControlRequest(cmd: .sessionContext, target: sid, args: ControlArgs(mode: "clear")))

request("req_session_split", ControlRequest(cmd: .sessionSplit, target: sid))
request("req_session_split_axis", ControlRequest(cmd: .sessionSplit, target: sid, args: ControlArgs(mode: "on", axis: "horizontal")))
request("req_session_split_off", ControlRequest(cmd: .sessionSplit, target: sid, args: ControlArgs(mode: "off")))
request("req_session_split_close", ControlRequest(cmd: .sessionSplitClose, target: sid))
request("req_session_swap", ControlRequest(cmd: .sessionSwap, target: sid))
request("req_session_focus", ControlRequest(cmd: .sessionFocus, target: sid, args: ControlArgs(to: "other")))
request("req_session_resize", ControlRequest(cmd: .sessionResize, target: sid, args: ControlArgs(ratio: 0.65)))
request("req_session_resize_delta", ControlRequest(cmd: .sessionResize, target: sid, args: ControlArgs(ratioDelta: -0.1)))
request("req_session_copy", ControlRequest(cmd: .sessionCopy, target: sid))
request("req_session_paste", ControlRequest(cmd: .sessionPaste, target: sid, args: ControlArgs(pane: "right")))
request("req_session_selectall", ControlRequest(cmd: .sessionSelectAll, target: sid))

request("req_surface_cursor", ControlRequest(cmd: .surfaceCursor, target: "surface:\(sid):left"))
request("req_font_inc", ControlRequest(cmd: .fontInc, target: sid))
request("req_font_inc_pane", ControlRequest(cmd: .fontInc, target: sid, args: ControlArgs(pane: "right")))
request("req_font_dec", ControlRequest(cmd: .fontDec, target: sid))
request("req_font_reset", ControlRequest(cmd: .fontReset, target: sid))

request("req_window_new", ControlRequest(cmd: .windowNew))
request("req_window_new_named", ControlRequest(cmd: .windowNew, args: ControlArgs(name: "side", minimized: true)))
request("req_window_list", ControlRequest(cmd: .windowList))
request("req_window_select", ControlRequest(cmd: .windowSelect, target: windowID))
request("req_window_go", ControlRequest(cmd: .windowGo, args: ControlArgs(to: "next")))
request("req_window_close", ControlRequest(cmd: .windowClose, target: windowID))
request("req_window_rename", ControlRequest(cmd: .windowRename, target: windowID, args: ControlArgs(name: "main")))
request("req_window_delete", ControlRequest(cmd: .windowDelete, target: windowID))
request("req_window_resize", ControlRequest(cmd: .windowResize, target: windowID, args: ControlArgs(width: 1200, height: 800)))
request("req_window_move", ControlRequest(cmd: .windowMove, target: windowID, args: ControlArgs(x: 100, y: 50, display: 1)))
request("req_window_minimize", ControlRequest(cmd: .windowMinimize, target: windowID))
request("req_window_zoom", ControlRequest(cmd: .windowZoom, target: windowID))
request("req_window_fullscreen", ControlRequest(cmd: .windowFullscreen, target: windowID))

request("req_sidebar", ControlRequest(cmd: .sidebar, args: ControlArgs(mode: "toggle")))
request("req_sidebar_width", ControlRequest(cmd: .sidebarWidth, args: ControlArgs(sidebarWidth: 300)))
request("req_theme_set", ControlRequest(cmd: .themeSet, args: ControlArgs(name: "catppuccin-mocha")))
request("req_theme_set_slots", ControlRequest(cmd: .themeSet, args: ControlArgs(light: "Builtin Light", dark: "catppuccin-mocha")))
request("req_theme_list", ControlRequest(cmd: .themeList))
request("req_notify", ControlRequest(cmd: .notify, target: sid, args: ControlArgs(title: "done", body: "tests passed")))
request("req_notify_body_only", ControlRequest(cmd: .notify, args: ControlArgs(body: "hello")))

request("req_deferred_zmx_list", ControlRequest(cmd: .zmxList))
request("req_deferred_overlay_open", ControlRequest(cmd: .sessionOverlayOpen, target: sid, args: ControlArgs(command: "htop", sizePercent: 60)))
request("req_deferred_unknown_field", ControlRequest(cmd: .sessionNew, args: ControlArgs(name: "x", cwd: "/tmp")))

request("req_unicode_text", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "привет 🌏 中文")))
request("req_control_chars_text", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "a\u{01}b\u{1f}c")))
request("req_slash_cwd", ControlRequest(cmd: .sessionNew, args: ControlArgs(cwd: "/Users/a//b")))
request("req_quote_text", ControlRequest(cmd: .sessionType, target: sid, args: ControlArgs(text: "say \"hi\" \\ done")))

// MARK: - responses

errorResponse("resp_error_not_found", "no such session: \(sid)")
errorResponse("resp_error_ambiguous", "ambiguous session prefix 'a' → \(sid), \(sid2)")
errorResponse("resp_error_dispatcher", "control dispatcher did not handle zmx.list")
errorResponse("resp_error_nul", "text must not contain a NUL byte")
errorResponse("resp_error_lines", "--lines must be greater than 0")
errorResponse("resp_error_events_pair", "events.read requires --run and --after together")

response("resp_ok_id", ControlResponse(ok: true, result: ControlResult(id: sid)))
response("resp_ok_affected", ControlResponse(ok: true, result: ControlResult(id: sid, affected: 3)))
response("resp_empty_text", ControlResponse(ok: true, result: ControlResult(text: "")))
response("resp_screen_text", ControlResponse(ok: true, result: ControlResult(text: "$ make test\nok")))
response("resp_version", ControlResponse(ok: true, result: ControlResult(app: AppIdentity(version: "0.34.0", commit: "1f09f69"))))
response("resp_version_no_commit", ControlResponse(ok: true, result: ControlResult(app: AppIdentity(version: "0.34.0"))))
response("resp_cursor", ControlResponse(ok: true, result: ControlResult(cursor: ControlCursor(column: 42))))
response("resp_resize_ratio", ControlResponse(ok: true, result: ControlResult(id: sid, ratio: 0.5)))
response("resp_resize_clamped", ControlResponse(ok: true, result: ControlResult(id: sid, ratio: 0.95)))
response("resp_resize_delta", ControlResponse(ok: true, result: ControlResult(id: sid, ratio: 0.123)))
response("resp_sidebar_width", ControlResponse(ok: true, result: ControlResult(sidebarWidth: 300)))
response("resp_notify_banners_off", ControlResponse(ok: true, result: ControlResult(text: ControlNotify.bannersOffNote)))

response("resp_events", ControlResponse(ok: true, result: ControlResult(events: ControlEventBatch(
    run: UUID(uuidString: runID)!,
    next: 41,
    items: [
        ControlEvent(seq: 39, ts: 1750000000.5, kind: .status, window: windowID, workspace: wid, session: sid,
                     payload: ControlEventPayload(status: "blocked", pane: "left", blink: true, color: "#ff8800", previous: "active")),
        ControlEvent(seq: 40, ts: 1750000001.0, kind: .treeChanged, window: windowID),
        ControlEvent(seq: 41, ts: 1750000002.25, kind: .sessionCreated, window: windowID, workspace: wid, session: sid2,
                     payload: ControlEventPayload(name: "build")),
    ]))))

response("resp_events_empty", ControlResponse(ok: true, result: ControlResult(events: ControlEventBatch(
    run: UUID(uuidString: runID)!, next: 0, items: []))))

response("resp_windows", ControlResponse(ok: true, result: ControlResult(windows: [
    ControlWindowNode(id: windowID, name: "main", open: true, active: true, autoFollowMs: 120000,
                      sidebarVisible: true,
                      geometry: ControlWindowFrame(x: 0, y: 0, width: 1440, height: 900, display: 0),
                      fullscreen: false, zoomed: false, minimized: false),
    ControlWindowNode(id: wid, name: "side", open: false, active: false),
])))

let minimalSession = ControlSessionNode(
    id: sid, name: "proj", cwd: "/Users/agent/proj", active: true, split: false, backedByZmx: false, realized: true)
response("resp_tree_minimal", ControlResponse(ok: true, result: ControlResult(tree: ControlTree(
    workspaces: [ControlWorkspaceNode(id: wid, name: "work", active: true, sessions: [minimalSession])],
    sidebarVisible: true, sidebarMode: "tree", app: AppIdentity(version: "0.34.0", commit: "1f09f69")))))

let fullSession = ControlSessionNode(
    id: sid, name: "агент 🚀", cwd: "/Users/agent/проект — work", title: "agent — zsh",
    active: true, split: true, hasSplit: true, backedByZmx: false,
    splitAxis: "vertical", splitRatio: 0.55, splitFocused: false,
    scratch: false, flagged: true,
    commandWait: true,
    foregroundShell: "zsh",
    status: "blocked", statusPane: "left", statusBlink: true, statusColor: "#ff8800", statusShape: "star",
    statusChangedAt: 1750000000.5,
    unseen: 2, fontSize: 12.5, splitFontSize: 13,
    surfaces: [
        ControlSurfaceNode(id: "surface:\(sid):left", kind: "left", active: true, visible: true, backedByZmx: false),
        ControlSurfaceNode(id: "surface:\(sid):right", kind: "right", active: false, visible: true, backedByZmx: false),
    ],
    realized: true, context: "port the codec")
let idleSession = ControlSessionNode(
    id: sid2, name: "scratch", cwd: "/tmp", active: false, split: false, backedByZmx: false, realized: false)
response("resp_tree_full", ControlResponse(ok: true, result: ControlResult(tree: ControlTree(
    workspaces: [
        ControlWorkspaceNode(id: wid, name: "work", active: true, focused: true, sessions: [fullSession, idleSession]),
        ControlWorkspaceNode(id: wid2, name: "personal", active: false, collapsed: true, sessions: []),
    ],
    idleMs: 4200, autoFollowMs: 120000,
    sidebarVisible: true, sidebarMode: "tree", sidebarFlaggedLayout: "flat",
    sidebarWidth: 220, workspaceFilter: false,
    app: AppIdentity(version: "0.34.0", commit: "1f09f69")))))

// exotic doubles: exponent forms and integral-valued doubles that must keep their ".0"
response("resp_doubles_exotic", ControlResponse(ok: true, result: ControlResult(tree: ControlTree(
    workspaces: [
        ControlWorkspaceNode(id: wid, name: "w", active: true, sessions: [
            ControlSessionNode(id: sid, name: "d", cwd: "/tmp", active: true, split: true, hasSplit: true,
                               backedByZmx: false, splitRatio: 1e-07, status: "active",
                               statusChangedAt: 1e+20, fontSize: 300, realized: true),
        ]),
    ],
    sidebarWidth: 0.001))))

// unknown command: pin Swift's exact invalid-request wording (plain text, not JSON)
do {
    _ = try JSONDecoder().decode(ControlRequest.self, from: Data(#"{"cmd":"bogus.command","args":{}}"#.utf8))
} catch {
    manifest.append("resp_error_unknown_cmd text")
    try! Data((ControlWire.invalidRequestMessage(error) + "\n").utf8)
        .write(to: outputDir.appendingPathComponent("resp_error_unknown_cmd.json"))
}

try! manifest.joined(separator: "\n").appending("\n")
    .write(to: outputDir.appendingPathComponent("manifest.txt"), atomically: true, encoding: .utf8)

print("wrote \(manifest.count) fixtures to \(outputDir.path)")
