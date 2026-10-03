using Agterm.Core.Model;
using Agterm.Terminal;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;

namespace Agterm.Windows;

// One terminal pane: the engine is the session's IPaneSurface (ConPTY + the VT engine); this host draws
// it through the Direct2D TerminalRenderer and feeds keystrokes back as pty bytes.
public sealed class PaneHost : Grid
{
    private readonly SessionModel _session;
    private readonly bool _split;
    private readonly TerminalRenderer _renderer = new();

    public PaneHost(SessionModel session, bool split)
    {
        _session = session;
        _split = split;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        KeyDown += OnKeyDown;
        CharacterReceived += OnPaneCharacterReceived;
        Loaded += (_, _) =>
        {
            _renderer.EnsureStarted();
            Focus(FocusState.Programmatic);
        };

        _renderer.BufferProvider = () => (Surface as TerminalEmulator)?.EngineBuffer;
        _renderer.GridResized = (columns, rows) =>
        {
            if (Surface is TerminalEmulator emulator) emulator.Resize(columns, rows);
        };
        Children.Add(_renderer);
    }

    public bool RepresentsSplit => _split;

    private IPaneSurface? Surface => _split ? _session.SplitSurface : _session.Surface;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var surface = Surface;
        if (surface is null) return;
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
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
}
