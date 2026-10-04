using Agterm.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using XtermSharp;

namespace Agterm.Windows;

// The phase-5 terminal renderer: D3D11 swapchain composited into a SwapChainPanel, Direct2D drawing,
// DirectWrite glyphs. Cells come straight from the XtermSharp buffer; attributes decode as
// (flags << 18) | (fg << 9) | bg with 256 = default. The swapchain is sized in PHYSICAL pixels via
// XamlRoot.RasterizationScale so text stays sharp at display scaling; the context DPI carries the
// scale so all layout math stays in DIPs. Cell metrics are measured from the font, never estimated —
// the caret and every glyph column hang off them. A true glyph atlas is a later optimization.
public sealed class TerminalRenderer : SwapChainPanel, IDisposable
{
    private ID3D11Device? _device;
    private IDXGISwapChain1? _swapChain;
    private ID2D1Factory1? _factory;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _context;
    private ID2D1Bitmap1? _target;
    private IDWriteFactory? _dwrite;
    private IDWriteTextFormat? _format;
    private readonly Dictionary<int, ID2D1SolidColorBrush> _brushes = [];

    private float _fontSize = 13f;
    private float _cellWidth = 8f;
    private float _cellHeight = 17f;
    private float _scale = 1f;
    private int _columns, _rows;
    private bool _devicesReady;
    private bool _targetReady;
    private bool _renderLoop;

    /// <summary>Provides the live engine buffer; null renders the empty background.</summary>
    public Func<XtermSharp.Terminal?>? BufferProvider { get; set; }

    /// <summary>The engine resizes its buffer and the pty when the cell grid changes.</summary>
    public Action<int, int>? GridResized { get; set; }

    /// <summary>The pane's live font size (from the IPaneSurface, not the engine buffer).</summary>
    public Func<double?>? FontSizeProvider { get; set; }

    public TerminalRenderer()
    {
        Loaded += (_, _) => EnsureStarted();
        SizeChanged += (_, e) =>
        {
            try
            {
                if (_devicesReady) RecreateTarget((float)e.NewSize.Width, (float)e.NewSize.Height);
            }
            catch (Exception ex) { MainWindow.UiLog("resize: " + ex.Message); }
        };
        PointerWheelChanged += OnWheel;
    }

    /// <summary>Starts the device chain and the draw timer. The timer fires between XAML composition
    /// passes — drawing from CompositionTarget.Rendering collides with the compose of the very
    /// swapchain being presented and dies with DXGI_ERROR_INVALID_CALL under output load.</summary>
    public void EnsureStarted()
    {
        if (ActualWidth < 1)
        {
            SizeChanged += DeferredStart;
            return;
        }
        StartCore();
    }

    private void DeferredStart(object sender, SizeChangedEventArgs e)
    {
        if (ActualWidth < 1) return;
        SizeChanged -= DeferredStart;
        StartCore();
    }

    private void StartCore()
    {
        EnsureDevices();
        if (_renderLoop) return;
        _renderLoop = true;
        var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        var consecutiveFailures = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                Draw();
                consecutiveFailures = 0;
            }
            catch (Exception e)
            {
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % 100 == 0)
                    MainWindow.UiLog($"render (failure {consecutiveFailures}): " + e.Message);
                if (consecutiveFailures >= 200)
                {
                    timer.Stop();
                    _renderLoop = false;
                    MainWindow.UiLog("render loop stopped after 200 consecutive failures");
                }
            }
        };
        timer.Start();
    }

    public new void Dispose()
    {
        _target?.Dispose();
        _format?.Dispose();
        foreach (var brush in _brushes.Values) brush.Dispose();
        _brushes.Clear();
        _context?.Dispose();
        _d2dDevice?.Dispose();
        _factory?.Dispose();
        _swapChain?.Dispose();
        _device?.Dispose();
    }

    private int _frameMeasurements;

    /// <summary>Schedules a frame.</summary>
    public void Draw()
    {
        if (!_devicesReady || !_targetReady) return;
        SyncFontSize();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Render(BufferProvider?.Invoke());
        watch.Stop();
        if (_frameMeasurements < 5 || watch.ElapsedMilliseconds > 300)
            MainWindow.UiLog($"frame {_frameMeasurements}: {watch.ElapsedMilliseconds} ms");
        if (watch.ElapsedMilliseconds > 300 || _frameMeasurements < 5) _frameMeasurements++;
    }

    private void EnsureDevices()
    {
        if (_devicesReady) return;
        var result = D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport, null, out _device, out _, out _);
        if (result.Failure || _device is null)
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Warp,
                DeviceCreationFlags.BgraSupport, null, out _device, out _, out _);
        if (_device is null) return;

        var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        _factory = D2D1.D2D1CreateFactory<ID2D1Factory1>(Vortice.Direct2D1.FactoryType.SingleThreaded);
        _d2dDevice = _factory.CreateDevice(dxgiDevice);
        _context = _d2dDevice.CreateDeviceContext();
        _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);

        // a directly created factory serves composition swapchains identically to the device's own
        using var dxgiFactory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();
        _scale = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        if (_scale < 0.1f) _scale = 1f;
        var description = new SwapChainDescription1
        {
            Width = (uint)Math.Max(1, (int)(ActualWidth * _scale)),
            Height = (uint)Math.Max(1, (int)(ActualHeight * _scale)),
            Format = Format.B8G8R8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch, // composition swapchains only support stretch scaling
            SwapEffect = SwapEffect.FlipSequential,
            AlphaMode = AlphaMode.Ignore,
        };
        _swapChain = dxgiFactory.CreateSwapChainForComposition(_device, description);
        SwapChainPanelNative.SetSwapChain(this, _swapChain);

        _format = _dwrite.CreateTextFormat("Cascadia Mono", null, FontWeight.Normal, FontStyle.Normal,
            FontStretch.Normal, _fontSize, "en-us");
        MeasureCell();
        _devicesReady = true;
        RecreateTarget((float)ActualWidth, (float)ActualHeight);
    }

    /// <summary>The exact monospace advance and line height from a measured layout — estimating the
    /// advance drifts the caret by a cell every few columns.</summary>
    private void MeasureCell()
    {
        using var layout = _dwrite!.CreateTextLayout(new string('M', 64), _format, 4096, 256);
        var metrics = layout.Metrics;
        if (metrics.WidthIncludingTrailingWhitespace > 0)
            _cellWidth = metrics.WidthIncludingTrailingWhitespace / 64f;
        if (metrics.Height > 0) _cellHeight = metrics.Height;
    }

    private void RecreateTarget(float widthDips, float heightDips)
    {
        if (!_devicesReady || _swapChain is null || _context is null || widthDips < 1 || heightDips < 1) return;
        _target?.Dispose();
        _target = null;
        _context.Target = null; // DXGI refuses ResizeBuffers while the context holds a back buffer
        var pixelWidth = Math.Max(1, (uint)(widthDips * _scale));
        var pixelHeight = Math.Max(1, (uint)(heightDips * _scale));
        try { _swapChain.ResizeBuffers(0, pixelWidth, pixelHeight, Format.B8G8R8A8_UNorm, SwapChainFlags.None); }
        catch (Exception e) { MainWindow.UiLog("ResizeBuffers failed: " + e.Message); throw; }
        using var backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        using var surface = backBuffer.QueryInterface<IDXGISurface>();
        var properties = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f * _scale, 96f * _scale, BitmapOptions.Target | BitmapOptions.CannotDraw);
        try { _target = _context.CreateBitmapFromDxgiSurface(surface, properties); }
        catch (Exception e) { MainWindow.UiLog("CreateBitmapFromDxgiSurface failed: " + e.Message); throw; }
        _context.Target = _target;
        _context.SetDpi(96f * _scale, 96f * _scale);
        _targetReady = true;

        RecomputeGrid(widthDips, heightDips);
        Draw();
    }

    private void SyncFontSize()
    {
        var size = FontSizeProvider?.Invoke();
        if (size is { } live && Math.Abs((float)live - _fontSize) > 0.01f)
        {
            _fontSize = (float)live;
            _format?.Dispose();
            _format = _dwrite!.CreateTextFormat("Cascadia Mono", null, FontWeight.Normal, FontStyle.Normal,
                FontStretch.Normal, _fontSize, "en-us");
            MeasureCell();
            RecomputeGrid((float)ActualWidth, (float)ActualHeight);
        }
    }

    private void RecomputeGrid(float widthDips, float heightDips)
    {
        var columns = Math.Max(2, (int)(widthDips / _cellWidth));
        var rows = Math.Max(2, (int)(heightDips / _cellHeight));
        if (columns != _columns || rows != _rows)
        {
            _columns = columns;
            _rows = rows;
            GridResized?.Invoke(columns, rows);
        }
    }

    private static Vortice.Mathematics.Color4 FromRgb(int rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    private ID2D1SolidColorBrush Brush(Vortice.Mathematics.Color4 color)
    {
        var key = (int)(color.R * 255) << 16 | (int)(color.G * 255) << 8 | (int)(color.B * 255);
        if (!_brushes.TryGetValue(key, out var brush))
        {
            brush = _context!.CreateSolidColorBrush(color);
            _brushes[key] = brush;
        }
        return brush;
    }

    private void Render(XtermSharp.Terminal? buffer)
    {
        var context = _context!;
        var (themeBack, themeFront) = ThemeBase();
        try { context.BeginDraw(); }
        catch (Exception e) { MainWindow.UiLog("BeginDraw failed: " + e.Message); throw; }
        context.Clear(themeBack);

        var active = buffer?.Buffer;
        if (active is not null && _format is not null)
        {
            var lines = active.Lines;
            for (var row = 0; row < _rows; row++)
            {
                var index = Mod(active.YDisp + row, lines.Length);
                var line = lines[index];
                var column = 0;
                while (column < _columns && column < line.Length)
                {
                    var cell = line[column];
                    if (cell.Width == 0) { column++; continue; }
                    var flags = (FLAGS)(cell.Attribute >> 18);
                    var fg = (cell.Attribute >> 9) & 0x1ff;
                    var bg = cell.Attribute & 0x1ff;

                    // group a run of identical attributes into one background rect and one glyph draw
                    var runEnd = column + 1;
                    while (runEnd < _columns && runEnd < line.Length)
                    {
                        var next = line[runEnd];
                        if (next.Width == 0 || next.Attribute != cell.Attribute) break;
                        runEnd++;
                    }

                    var runLength = runEnd - column;
                    var inverted = flags.HasFlag(FLAGS.INVERSE);
                    var back = bg == Renderer.DefaultColor ? themeBack : BrushColor(bg);
                    var front = fg == Renderer.DefaultColor ? themeFront : BrushColor(fg);
                    if (inverted) (back, front) = (front, back);
                    if (bg != Renderer.DefaultColor || inverted)
                        context.FillRectangle(new Rect(column * _cellWidth, row * _cellHeight,
                            runLength * _cellWidth, _cellHeight), Brush(back));

                    if (!flags.HasFlag(FLAGS.INVISIBLE))
                    {
                        var text = RunText(line, column, runEnd);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var brush = Brush(front);
                            IDWriteTextLayout layout;
                            try
                            {
                                layout = _dwrite!.CreateTextLayout(
                                    text, _format, runLength * _cellWidth + 8, _cellHeight + 6);
                            }
                            catch (Exception e)
                            {
                                MainWindow.UiLog($"CreateTextLayout failed for '{text[..Math.Min(20, text.Length)]}': " + e.Message);
                                column = runEnd;
                                continue;
                            }
                            context.DrawTextLayout(new Vector2(column * _cellWidth, row * _cellHeight), layout, brush);
                            layout.Dispose();
                            var underline = row * _cellHeight + _cellHeight - 2;
                            if (flags.HasFlag(FLAGS.UNDERLINE))
                                context.DrawLine(new Vector2(column * _cellWidth, underline),
                                    new Vector2(runEnd * _cellWidth, underline), brush, 1f);
                            if (flags.HasFlag(FLAGS.CrossedOut))
                            {
                                var mid = row * _cellHeight + _cellHeight / 2;
                                context.DrawLine(new Vector2(column * _cellWidth, mid),
                                    new Vector2(runEnd * _cellWidth, mid), brush, 1f);
                            }
                        }
                    }
                    column = runEnd;
                }
            }

            // the cursor as an outlined block over the live viewport
            var cursorRow = active.Y - active.YDisp;
            if (cursorRow >= 0 && cursorRow < _rows)
                context.DrawRectangle(
                    new Rect(active.X * _cellWidth, cursorRow * _cellHeight, _cellWidth, _cellHeight),
                    Brush(themeFront), 2f);
        }

        try { context.EndDraw(); }
        catch (Exception e) { MainWindow.UiLog("EndDraw failed: " + e.Message); throw; }
        try { _swapChain!.Present(1, PresentFlags.None); }
        catch (Exception e) { MainWindow.UiLog("Present failed: " + e.Message); throw; }
    }

    private static string RunText(BufferLine line, int start, int end)
    {
        // empty cells carry the 0x0200 placeholder rune with Code 0 — they render as spaces
        var builder = new System.Text.StringBuilder(end - start);
        for (var i = start; i < end; i++)
            builder.Append(line[i].Code == 0 ? ' ' : line[i].Rune.ToString());
        return builder.ToString();
    }

    private static Vortice.Mathematics.Color4 BrushColor(int index)
    {
        var palette = XtermSharp.Color.DefaultAnsiColors;
        var color = index >= 0 && index < palette.Count ? palette[index] : XtermSharp.Color.DefaultForeground;
        return new Vortice.Mathematics.Color4(color.Red / 255f, color.Green / 255f, color.Blue / 255f, 1f);
    }

    private static (Vortice.Mathematics.Color4 back, Vortice.Mathematics.Color4 front) ThemeBase()
    {
        var (bg, fg) = UiTheme.Palette();
        return (FromRgb(bg), FromRgb(fg));
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var active = BufferProvider?.Invoke()?.Buffer;
        if (active is null) return;
        var delta = e.GetCurrentPoint(this).Properties.MouseWheelDelta;
        var lines = -(delta / 120) * 3;
        var target = Math.Clamp(active.YDisp + lines, 0, Math.Max(0, active.YBase));
        if (target != active.YDisp) { active.YDisp = target; Draw(); }
        e.Handled = true;
    }
}

/// <summary>COM interop for SwapChainPanel.SetSwapChain; Vortice.WinUI would duplicate this.</summary>
internal static class SwapChainPanelNative
{
    public static void SetSwapChain(SwapChainPanel panel, IDXGISwapChain1 swapChain)
    {
        var panelPointer = ((WinRT.IWinRTObject)panel).NativeObject.ThisPtr;
        var iid = typeof(ISwapChainPanelNative).GUID;
        Marshal.QueryInterface(panelPointer, in iid, out var nativePointer);
        try
        {
            var native = (ISwapChainPanelNative)Marshal.GetObjectForIUnknown(nativePointer);
            native.SetSwapChain(swapChain.NativePointer);
            Marshal.ReleaseComObject(native);
        }
        finally
        {
            Marshal.Release(nativePointer);
        }
    }

    [ComImport]
    [Guid("63aad0b8-7c24-40ff-85a8-640d944cc325")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISwapChainPanelNative
    {
        void SetSwapChain(IntPtr swapChain);
    }
}
