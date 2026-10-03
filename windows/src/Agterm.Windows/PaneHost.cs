using Agterm.Core.Model;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Agterm.Windows;

// One terminal pane: the engine is the session's IPaneSurface (ConPTY + the VT engine); this host draws
// the viewport as monospace text with a caret block and feeds keystrokes back as pty bytes. The
// Direct2D renderer replaces the visuals without touching the wiring.
public sealed class PaneHost : Canvas
{
    private readonly SessionModel _session;
    private readonly bool _split;
    private readonly TextBlock _screen = new()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
        FontSize = 13,
        TextWrapping = TextWrapping.NoWrap,
    };
    private readonly Rectangle _caret = new()
    {
        Fill = new SolidColorBrush(Color.FromArgb(150, 210, 210, 210)),
    };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
    private double _charWidth = 7.2;
    private double _lineHeight = 16.6;
    private string _lastText = "";

    public PaneHost(SessionModel session, bool split)
    {
        _session = session;
        _split = split;
        Background = new SolidColorBrush(Color.FromArgb(255, 12, 12, 12));
        Children.Add(_screen);
        Children.Add(_caret);
        IsTabStop = true;
        KeyDown += OnKeyDown;
        CharacterReceived += OnPaneCharacterReceived;
        GotFocus += (_, _) => _caret.Fill = new SolidColorBrush(Color.FromArgb(200, 235, 235, 235));
        LostFocus += (_, _) => _caret.Fill = new SolidColorBrush(Color.FromArgb(70, 160, 160, 160));
        Loaded += (_, _) =>
        {
            MeasureFont();
            Focus(FocusState.Programmatic);
        };
        SizeChanged += (_, _) => UpdateCaret();

        _timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += (_, _) => RefreshScreen();
        _timer.Start();
    }

    public bool RepresentsSplit => _split;

    private IPaneSurface? Surface => _split ? _session.SplitSurface : _session.Surface;

    private void MeasureFont()
    {
        // Cascadia Mono advance is 0.6em; a text probe needs Windows.Foundation.Size, which the WinUI
        // projection does not surface here
        _charWidth = _screen.FontSize * 0.6;
        _lineHeight = _screen.FontSize * 1.28;
        _caret.Width = Math.Max(3, _charWidth - 1);
        _caret.Height = _lineHeight - 2;
    }

    private void RefreshScreen()
    {
        var surface = Surface;
        if (surface is null)
        {
            Opacity = 0.35;
            _caret.Visibility = Visibility.Collapsed;
            return;
        }
        Opacity = 1;
        var text = surface.ReadScreenText(all: false);
        if (text != _lastText)
        {
            _lastText = text;
            _screen.Text = text;
        }
        UpdateCaret();
    }

    private void UpdateCaret()
    {
        var surface = Surface;
        if (surface is null)
        {
            _caret.Visibility = Visibility.Collapsed;
            return;
        }
        _caret.Visibility = Visibility.Visible;
        var column = surface.ReadCursorColumn();
        var rows = _lastText.Split('\n');
        var row = rows.Length - 1;
        while (row > 0 && rows[row].Length == 0) row--;
        SetLeft(_caret, column * _charWidth + 4);
        SetTop(_caret, row * _lineHeight + 2);
    }

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
