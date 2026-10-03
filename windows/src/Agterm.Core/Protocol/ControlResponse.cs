using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>
/// The successful payload: a new/affected id for mutating commands, a tree for <c>tree</c>, the selected
/// text for <c>session.copy</c>. All optional; nulls omitted. Field order mirrors Swift's declaration
/// order so the encoded bytes match (agtermctl --json prints the server line unchanged).
/// </summary>
public sealed class ControlResult
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("width")] public int? Width { get; set; }

    [JsonPropertyName("height")] public int? Height { get; set; }

    [JsonPropertyName("tree")] public ControlTree? Tree { get; set; }

    [JsonPropertyName("text")] public string? Text { get; set; }

    [JsonPropertyName("windows")] public List<ControlWindowNode>? Windows { get; set; }

    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }

    [JsonPropertyName("count")] public int? Count { get; set; }

    [JsonPropertyName("affected")] public int? Affected { get; set; }

    [JsonPropertyName("theme")] public string? Theme { get; set; }

    [JsonPropertyName("themes")] public List<string>? Themes { get; set; }

    [JsonPropertyName("ratio")] public double? Ratio { get; set; }

    [JsonPropertyName("sidebarWidth")] public double? SidebarWidth { get; set; }

    [JsonPropertyName("pane")] public string? Pane { get; set; }

    [JsonPropertyName("sync")] public bool? Sync { get; set; }

    [JsonPropertyName("light")] public string? Light { get; set; }

    [JsonPropertyName("dark")] public string? Dark { get; set; }

    [JsonPropertyName("events")] public ControlEventBatch? Events { get; set; }

    [JsonPropertyName("keymap")] public JsonElement? Keymap { get; set; }

    [JsonPropertyName("hooks")] public JsonElement? Hooks { get; set; }

    [JsonPropertyName("pick")] public JsonElement? Pick { get; set; }

    [JsonPropertyName("ask")] public JsonElement? Ask { get; set; }

    [JsonPropertyName("cursor")] public ControlCursor? Cursor { get; set; }

    [JsonPropertyName("app")] public AppIdentity? App { get; set; }

    [JsonPropertyName("restore")] public JsonElement? Restore { get; set; }

    [JsonPropertyName("zmx")] public JsonElement? Zmx { get; set; }

    [JsonPropertyName("remote")] public JsonElement? Remote { get; set; }

    [JsonPropertyName("liveReset")] public JsonElement? LiveReset { get; set; }

    [JsonPropertyName("pageID")] public string? PageID { get; set; }

    [JsonPropertyName("pageOutcome")] public JsonElement? PageOutcome { get; set; }
}

/// <summary>The single response written back per connection. <c>ok</c> gates <c>result</c> (on success) vs
/// <c>error</c>.</summary>
public sealed class ControlResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }

    [JsonPropertyName("result")] public ControlResult? Result { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }

    public ControlResponse() { }

    public ControlResponse(bool ok, ControlResult? result = null, string? error = null)
    {
        Ok = ok;
        Result = result;
        Error = error;
    }

    public static ControlResponse OkWith(ControlResult result) => new(true, result);
    public static ControlResponse Fail(string error) => new(false, error: error);
}
