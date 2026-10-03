using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>
/// A bag of optional command parameters. Each command reads only the fields it needs; the rest stay null
/// and are omitted from the JSON. Field names and null-omission mirror Swift's synthesized Codable
/// (agtermCore/ControlProtocol.swift); an unknown argument decodes as a no-op exactly as on macOS, where a
/// server that predates a field drops it silently.
/// </summary>
public sealed class ControlArgs
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("cwd")] public string? Cwd { get; set; }

    [JsonPropertyName("targets")] public List<string>? Targets { get; set; }

    [JsonPropertyName("workspace")] public string? Workspace { get; set; }

    [JsonPropertyName("workspaceName")] public string? WorkspaceName { get; set; }

    [JsonPropertyName("createWorkspace")] public bool? CreateWorkspace { get; set; }

    [JsonPropertyName("collapsed")] public bool? Collapsed { get; set; }

    [JsonPropertyName("minimized")] public bool? Minimized { get; set; }

    [JsonPropertyName("force")] public bool? Force { get; set; }

    [JsonPropertyName("host")] public string? Host { get; set; }

    [JsonPropertyName("noSelect")] public bool? NoSelect { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("select")] public bool? Select { get; set; }

    [JsonPropertyName("mode")] public string? Mode { get; set; }

    [JsonPropertyName("axis")] public string? Axis { get; set; }

    [JsonPropertyName("path")] public string? Path { get; set; }

    [JsonPropertyName("color")] public string? Color { get; set; }

    [JsonPropertyName("textColor")] public string? TextColor { get; set; }

    [JsonPropertyName("shape")] public string? Shape { get; set; }

    [JsonPropertyName("opacity")] public double? Opacity { get; set; }

    [JsonPropertyName("fit")] public string? Fit { get; set; }

    [JsonPropertyName("position")] public string? Position { get; set; }

    [JsonPropertyName("repeats")] public bool? Repeats { get; set; }

    [JsonPropertyName("pane")] public string? Pane { get; set; }

    [JsonPropertyName("paneID")] public string? PaneID { get; set; }

    [JsonPropertyName("ratio")] public double? Ratio { get; set; }

    [JsonPropertyName("ratioDelta")] public double? RatioDelta { get; set; }

    [JsonPropertyName("all")] public bool? All { get; set; }

    [JsonPropertyName("lines")] public int? Lines { get; set; }

    [JsonPropertyName("to")] public string? To { get; set; }

    [JsonPropertyName("after")] public string? After { get; set; }

    [JsonPropertyName("before")] public string? Before { get; set; }

    [JsonPropertyName("run")] public string? Run { get; set; }

    [JsonPropertyName("kinds")] public List<string>? Kinds { get; set; }

    [JsonPropertyName("limit")] public int? Limit { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("body")] public string? Body { get; set; }

    [JsonPropertyName("command")] public string? Command { get; set; }

    [JsonPropertyName("wait")] public bool? Wait { get; set; }

    [JsonPropertyName("sizePercent")] public int? SizePercent { get; set; }

    [JsonPropertyName("full")] public bool? Full { get; set; }

    [JsonPropertyName("follow")] public bool? Follow { get; set; }

    [JsonPropertyName("message")] public string? Message { get; set; }

    [JsonPropertyName("detail")] public string? Detail { get; set; }

    [JsonPropertyName("spinner")] public string? Spinner { get; set; }

    [JsonPropertyName("hideAfter")] public double? HideAfter { get; set; }

    [JsonPropertyName("markdown")] public bool? Markdown { get; set; }

    /// <summary>Pick items for pick.open — decode passthrough only, never emitted by this host.</summary>
    [JsonPropertyName("items")] public JsonElement? Items { get; set; }

    [JsonPropertyName("prompt")] public string? Prompt { get; set; }

    [JsonPropertyName("query")] public string? Query { get; set; }

    [JsonPropertyName("allowCustom")] public bool? AllowCustom { get; set; }

    [JsonPropertyName("selection")] public string? Selection { get; set; }

    /// <summary>Ask buttons for ask.open — decode passthrough only, never emitted by this host.</summary>
    [JsonPropertyName("buttons")] public JsonElement? Buttons { get; set; }

    [JsonPropertyName("defaultButton")] public string? DefaultButton { get; set; }

    [JsonPropertyName("style")] public string? Style { get; set; }

    [JsonPropertyName("align")] public string? Align { get; set; }

    [JsonPropertyName("destructiveButton")] public string? DestructiveButton { get; set; }

    [JsonPropertyName("window")] public string? Window { get; set; }

    [JsonPropertyName("width")] public int? Width { get; set; }

    [JsonPropertyName("height")] public int? Height { get; set; }

    [JsonPropertyName("sidebarWidth")] public double? SidebarWidth { get; set; }

    [JsonPropertyName("x")] public int? X { get; set; }

    [JsonPropertyName("y")] public int? Y { get; set; }

    [JsonPropertyName("display")] public int? Display { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("blink")] public bool? Blink { get; set; }

    [JsonPropertyName("autoReset")] public bool? AutoReset { get; set; }

    [JsonPropertyName("sound")] public string? Sound { get; set; }

    [JsonPropertyName("light")] public string? Light { get; set; }

    [JsonPropertyName("dark")] public string? Dark { get; set; }

    [JsonPropertyName("close")] public bool? Close { get; set; }

    [JsonPropertyName("fontSize")] public double? FontSize { get; set; }

    [JsonPropertyName("autoSize")] public bool? AutoSize { get; set; }

    [JsonPropertyName("mru")] public bool? Mru { get; set; }

    [JsonPropertyName("html")] public string? Html { get; set; }

    [JsonPropertyName("current")] public bool? Current { get; set; }

    [JsonPropertyName("navigation")] public bool? Navigation { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("javascript")] public bool? Javascript { get; set; }

    [JsonPropertyName("value")] public string? Value { get; set; }

    [JsonPropertyName("page")] public string? Page { get; set; }

    [JsonPropertyName("chromeless")] public bool? Chromeless { get; set; }

    [JsonPropertyName("persistent")] public bool? Persistent { get; set; }
}

/// <summary>One control request: a command, an optional target (session or workspace id / "active" /
/// prefix), and an optional args bag. One request per connection, newline-delimited JSON.</summary>
public sealed class ControlRequest
{
    [JsonPropertyName("cmd")] public ControlCommand Cmd { get; set; }

    [JsonPropertyName("target")] public string? Target { get; set; }

    [JsonPropertyName("args")] public ControlArgs? Args { get; set; }
}
