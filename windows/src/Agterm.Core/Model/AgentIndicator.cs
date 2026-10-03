using Agterm.Core.Control;

namespace Agterm.Core.Model;

/// <summary>
/// The per-session agent status value: state, blink, autoReset, per-call color and shape overrides, and
/// the pane that set it. Ephemeral (never persisted), set only via the control API.
/// </summary>
public sealed class AgentIndicator
{
    public StatusKind Status { get; set; } = StatusKind.Idle;
    public bool Blink { get; set; }
    public bool AutoReset { get; set; }
    public string? Color { get; set; }
    public StatusShape? Shape { get; set; }
    public StatusPane? StatusPane { get; set; }
    /// <summary>Epoch seconds of the last set, idle and unchanged values included (stamped before the
    /// unchanged-indicator early return so every set refreshes the age).</summary>
    public double? StatusChangedAt { get; set; }
}

/// <summary>
/// One shell: identity, cwd tracking, the agent indicator, and its surface slots. Reference type on
/// purpose — the workspace, the store, and the UI all observe one instance, exactly like Swift's Session.
/// </summary>
public sealed class SessionModel
{
    public Guid Id { get; }

    public string? CustomName { get; set; }

    public string InitialCwd { get; set; }

    /// <summary>The last OSC 7 report from the primary pane; null before the first one.</summary>
    public string? CurrentCwd { get; set; }

    public string? SplitInitialCwd { get; set; }

    public string? SplitCwd { get; set; }

    /// <summary>The raw terminal title from the latest OSC 0/2 on the primary pane.</summary>
    public string? OscTitle { get; set; }

    public string? SplitTitle { get; set; }

    public AgentIndicator Indicator { get; } = new();

    public int UnseenCount { get; set; }

    public bool Flagged { get; set; }

    public string? Context { get; set; }

    /// <summary>For a --command session: hold the surface after the command exits.</summary>
    public bool CommandWait { get; set; }

    public string? InitialCommand { get; set; }

    // --- split state ---

    public bool HasSplit { get; set; }
    public bool SplitShown { get; set; }
    public bool SplitFocused { get; set; }
    public SplitAxis SplitAxis { get; set; } = SplitAxis.Vertical;
    public double SplitRatio { get; set; } = 0.5;

    // --- surface slots (engine-owned) ---

    public IPaneSurface? Surface { get; set; }
    public IPaneSurface? SplitSurface { get; set; }

    public SessionModel(Guid id, string initialCwd, string? customName = null, string? command = null, bool commandWait = false)
    {
        Id = id;
        InitialCwd = initialCwd;
        CustomName = customName;
        InitialCommand = command;
        CommandWait = commandWait;
    }

    /// <summary>The primary pane's effective cwd: the last OSC 7 report, else the spawn directory.</summary>
    public string EffectiveCwd => CurrentCwd ?? InitialCwd;

    /// <summary>The cwd of the focused pane — the split's while it has focus, else the primary's — which is
    /// what the sidebar and title bar track. The existence guard stops a promoted survivor from masking the
    /// migrated main-pane cwd.</summary>
    public string FocusedCwd => SplitFocused && SplitSurface != null && SplitCwd is not null
        ? SplitCwd
        : EffectiveCwd;

    /// <summary>The sidebar label: a non-blank custom name, else the focused pane's OSC title, else the
    /// focused cwd's basename. Basename pins: root stays root, a trailing separator is ignored, an empty
    /// path shows the home shorthand.</summary>
    public string DisplayName
    {
        get
        {
            var trimmed = TrimmedOrNull(CustomName);
            if (trimmed is not null) return trimmed;
            var title = TrimmedOrNull(FocusedOscTitle);
            if (title is not null) return title;
            var path = FocusedCwd;
            if (path.Length == 0) return "~";
            return Basename(path);
        }
    }

    private string? FocusedOscTitle => SplitFocused && SplitSurface != null ? SplitTitle : OscTitle;

    /// <summary>The read side of session.context.</summary>
    public string? EffectiveContext => Context;

    public static string? TrimmedOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>Path-separator-agnostic basename with the macOS pins: root stays root, trailing separators
    /// are ignored, empty becomes "~" at the caller.</summary>
    public static string Basename(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0)
            return path.StartsWith('/') || path.StartsWith('\\') ? path : "/";
        var lastSlash = trimmed.LastIndexOfAny(['/', '\\']);
        return lastSlash < 0 ? trimmed : trimmed[(lastSlash + 1)..];
    }

    /// <summary>The split pane's last reported or fallback directory, including hidden splits.</summary>
    public string? SplitEffectiveCwd => HasSplit
        ? SplitCwd ?? SplitInitialCwd ?? EffectiveCwd
        : null;
}
