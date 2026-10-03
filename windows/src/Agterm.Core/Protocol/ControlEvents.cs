using System.Text.Json.Serialization;

namespace Agterm.Core.Protocol;

/// <summary>Event kinds retained by the app-run control event ring.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ControlEventKind>))]
public enum ControlEventKind
{
    [JsonStringEnumMemberName("status")] Status,
    [JsonStringEnumMemberName("notify")] Notify,
    [JsonStringEnumMemberName("session.created")] SessionCreated,
    [JsonStringEnumMemberName("session.closed")] SessionClosed,
    [JsonStringEnumMemberName("tree.changed")] TreeChanged,
    [JsonStringEnumMemberName("pane.split")] PaneSplit,
    [JsonStringEnumMemberName("pane.scratch")] PaneScratch,
    [JsonStringEnumMemberName("remote.opened")] RemoteOpened,
    [JsonStringEnumMemberName("remote.closed")] RemoteClosed,
}

/// <summary>Kind-specific event data. Optional fields keep the encoded payload compact while preserving one
/// stable object shape for shell/JSON consumers.</summary>
public sealed class ControlEventPayload
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("pane")] public string? Pane { get; set; }

    [JsonPropertyName("blink")] public bool? Blink { get; set; }

    [JsonPropertyName("color")] public string? Color { get; set; }

    /// <summary>The status event's per-call glyph silhouette; nil when the glyph uses the default circle.</summary>
    [JsonPropertyName("shape")] public string? Shape { get; set; }

    /// <summary>The status event's status before the accepted write; equal to status when only decoration changed.</summary>
    [JsonPropertyName("previous")] public string? Previous { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    [JsonPropertyName("body")] public string? Body { get; set; }

    /// <summary>The remote.opened / remote.closed ssh destination the row is attached to.</summary>
    [JsonPropertyName("host")] public string? Host { get; set; }
}

/// <summary>One immutable entry in the event ring.</summary>
public sealed class ControlEvent
{
    [JsonPropertyName("seq")] public ulong Seq { get; set; }

    /// <summary>Epoch seconds, shared with statusChangedAt's clock so a poller can compare the two.</summary>
    [JsonPropertyName("ts")] public double Ts { get; set; }

    [JsonPropertyName("kind")] public ControlEventKind Kind { get; set; }

    [JsonPropertyName("window")] public string? Window { get; set; }

    [JsonPropertyName("workspace")] public string? Workspace { get; set; }

    [JsonPropertyName("session")] public string? Session { get; set; }

    [JsonPropertyName("payload")] public ControlEventPayload Payload { get; set; } = new();
}

/// <summary>A page returned by a ring read. <c>next</c> is the global sequence through which the ring
/// scanned, including filtered-out entries.</summary>
public sealed class ControlEventBatch
{
    /// <summary>The app-run UUID, uppercase like Swift's UUID.uuidString.</summary>
    [JsonPropertyName("run")] public string Run { get; set; } = "";

    [JsonPropertyName("next")] public ulong Next { get; set; }

    [JsonPropertyName("items")] public List<ControlEvent> Items { get; set; } = [];
}
