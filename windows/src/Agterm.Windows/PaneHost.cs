using Agterm.Core.Model;
using Agterm.Terminal;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Agterm.Windows;

// One terminal pane: the engine is the session's IPaneSurface (ConPTY + the VT engine); this host draws
// it through the Direct2D TerminalRenderer and owns the keyboard: KeyDown maps control keys, and
// CharacterReceived delivers printable characters for the current keyboard layout.
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
        KeyDown += OnKeyDown;
        CharacterReceived += OnPaneCharacterReceived;
        PointerPressed += (_, _) => Focus(FocusState.Pointer);
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

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var surface = Surface;
        if (surface is null) return;
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && shift) return; // window accelerators own Ctrl+Shift
        switch (e.Key)
        {
            case VirtualKey.Enter: surface.PressReturn(); break;
            case VirtualKey.Back: surface.TypeText("\u007F"); break;
            case VirtualKey.Tab: surface.TypeText("\t"); break;
            case VirtualKey.Escape: surface.TypeText("\u001B"); break;
            case VirtualKey.Up: surface.TypeText("\u001B[A"); break;
            case VirtualKey.Down: surface.TypeText("\u001B[B"); break;
            case VirtualKey.Right: surface.TypeText("\u001B[C"); break;
            case VirtualKey.Left: surface.TypeText("\u001B[D"); break;
            case VirtualKey.C when ctrl: surface.TypeText("\u0003"); break;
            case VirtualKey.D when ctrl: surface.TypeText("\u0004"); break;
            case VirtualKey.L when ctrl: surface.TypeText("\u000C"); break;
            case VirtualKey.PageUp when ctrl: ScrollViewport(-20); e.Handled = true; break;
            case VirtualKey.PageDown when ctrl: ScrollViewport(20); e.Handled = true; break;
            case VirtualKey.V when ctrl: PasteAsync(); break;
            default: return; // printable input arrives through CharacterReceived
        }
        e.Handled = true;
    }

    private void OnPaneCharacterReceived(object sender, CharacterReceivedRoutedEventArgs e)
    {
        var ch = e.Character;
        if (ch >= ' ') Surface?.TypeText(ch.ToString());
    }

    private async void PasteAsync()
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = await content.GetTextAsync();
            Surface?.TypeText(text.Replace("\r\n", "\r").Replace('\n', '\r'));
        }
        catch (Exception)
        {
            // a locked or empty clipboard just means no paste
        }
    }

    /// <summary>Ctrl+PgUp/PgDn scrolls the scrollback viewport without feeding the pty.</summary>
    private void ScrollViewport(int lines)
    {
        var active = (Surface as TerminalEmulator)?.EngineBuffer?.Buffer;
        if (active is null) return;
        active.YDisp = Math.Clamp(active.YDisp + lines, 0, Math.Max(0, active.YBase));
        _renderer.Draw();
    }
}