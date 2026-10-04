using Agterm.Core.Model;
using Agterm.Terminal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Agterm.Windows;

// One terminal pane: the engine is the session's IPaneSurface (ConPTY + the VT engine); this host draws
// it through the Direct2D TerminalRenderer. Keyboard input is routed at the window level (see
// MainWindow) — pane focus plays no part in it.
public sealed class PaneHost : Grid
{
    private readonly SessionModel _session;
    private readonly bool _split;
    private readonly TerminalRenderer _renderer = new();

    public PaneHost(SessionModel session, bool split)
    {
        _session = session;
        _split = split;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        PointerPressed += (_, e) => Focus(FocusState.Pointer);
        Loaded += (_, _) => Focus(FocusState.Programmatic);

        _renderer.BufferProvider = () => (Surface as TerminalEmulator)?.EngineBuffer;
        _renderer.FontSizeProvider = () => Surface?.CurrentFontSize();
        _renderer.GridResized = (columns, rows) =>
        {
            if (Surface is TerminalEmulator emulator) emulator.Resize(columns, rows);
        };
        Children.Add(_renderer);
    }

    public bool RepresentsSplit => _split;

    /// <summary>Starts the device chain when the pane first becomes visible; hidden panes keep their
    /// ConPTY alive but present nothing (the plan's occlusion parity).</summary>
    public void ActivateRenderer() => _renderer.EnsureStarted();

    private IPaneSurface? Surface => _split ? _session.SplitSurface : _session.Surface;
}
