using Agterm.Core.Control;
using Agterm.Core.Protocol;

namespace Agterm.Core.Model;

/// <summary>
/// Bounded, non-destructive event history for one app process — a port of agtermCore's ControlEventRing.
/// Single-threaded by contract: the host serializes appends and reads through the same dispatcher the model
/// mutations run on (the UI thread in the app, plain sequence in tests).
/// </summary>
public sealed class ControlEventRing
{
    public const int DefaultCapacity = 4096;

    public enum ReadError
    {
        RunChanged,
        CursorExpired,
        CursorAhead,
    }

    public sealed record ReadResult(ControlEventBatch? Batch, ReadError? Error, ControlEventBatch Anchor);

    private readonly int _capacity;
    private readonly Guid _runId;
    private readonly Func<double> _now;
    private readonly List<ControlEvent> _entries = [];
    private ulong _currentSequence;

    public ControlEventRing(int capacity = DefaultCapacity, Guid? runId = null, Func<double>? now = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _runId = runId ?? Guid.NewGuid();
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
    }

    public Guid RunId => _runId;
    public ulong CurrentSequence => _currentSequence;

    public ControlEvent Append(ControlEventDraft draft)
    {
        _currentSequence++;
        var command = new ControlEvent
        {
            Seq = _currentSequence,
            Ts = _now(),
            Kind = draft.Kind,
            Window = draft.Window,
            Workspace = draft.Workspace,
            Session = draft.Session,
            Payload = draft.Payload ?? new ControlEventPayload(),
        };
        _entries.Add(command);
        if (_entries.Count > _capacity)
            _entries.RemoveRange(0, _entries.Count - _capacity);
        return command;
    }

    /// <summary>Read retained entries after the cursor, optionally filtering by kind. A null cursor is the
    /// subscribe-from-now bootstrap: it anchors at the tail and intentionally returns no history.</summary>
    public ReadResult Read(ControlEventCursor? cursor, IReadOnlySet<ControlEventKind>? kinds = null, int limit = 100)
    {
        var anchor = new ControlEventBatch { Run = ControlResolve.WireId(_runId), Next = _currentSequence };
        if (cursor is null) return new ReadResult(anchor, null, anchor);
        if (cursor.Value.Run != _runId) return new ReadResult(null, ReadError.RunChanged, anchor);
        if (cursor.Value.After > _currentSequence)
            return new ReadResult(null, ReadError.CursorAhead, anchor);
        if (_entries.Count > 0 && cursor.Value.After < _entries[0].Seq - 1)
            return new ReadResult(null, ReadError.CursorExpired, anchor);

        var items = new List<ControlEvent>();
        foreach (var entry in _entries)
        {
            if (entry.Seq <= cursor.Value.After) continue;
            if (kinds is not null && !kinds.Contains(entry.Kind)) continue;
            items.Add(entry);
            if (items.Count == limit)
                return new ReadResult(
                    new ControlEventBatch { Run = ControlResolve.WireId(_runId), Next = entry.Seq, Items = items },
                    null, anchor);
        }
        return new ReadResult(
            new ControlEventBatch { Run = ControlResolve.WireId(_runId), Next = _currentSequence, Items = items },
            null, anchor);
    }

    public static string WireError(ReadError error) => error switch
    {
        ReadError.RunChanged => "event run changed",
        ReadError.CursorExpired => "event cursor expired",
        _ => "event cursor is ahead of the current sequence",
    };
}

/// <summary>An event before the ring assigns its app-run sequence and timestamp.</summary>
public readonly record struct ControlEventDraft(
    ControlEventKind Kind,
    string? Window = null,
    string? Workspace = null,
    string? Session = null,
    ControlEventPayload? Payload = null);
