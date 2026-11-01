using System.Numerics;
using System.Runtime.InteropServices;
using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Windows.Graphics;
using Windows.Graphics.Capture;
using WinRT;

namespace Lyricod;

/// <summary>
/// Lyricod Glass's liquid effect: what's behind the island, captured from the screen
/// and drawn as its background, lightly blurred and bent at the edges like the rim of a lens, with
/// the light its edge catches (the 3D effect) drawn to follow the same curve. The
/// island is excluded from its own capture session on supported Windows builds; older builds
/// need global capture exclusion to avoid feedback. Windows 11 only: Windows 10
/// outlines the whole screen in yellow while anything captures it.
/// </summary>
sealed class LiquidGlass : IDisposable
{
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 22000 && GraphicsCaptureSession.IsSupported();

    /// <summary>What to draw: where the island is on its screen (pixels), its corners, and the look
    /// (<c>Shine</c>: how wide the light along the edge is, in pixels; 0 for none).</summary>
    sealed record Look(RectInt32 Source, float Radius, bool SquareTop, float Blur, float Refraction, Windows.UI.Color Tint, float Shine);

    readonly DispatcherQueue _ui;
    readonly Action _failed;
    readonly nint _window;
    readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();
    readonly CompositionGraphicsDevice _graphics;
    readonly CompositionDrawingSurface _surface;
    readonly CompositionRoundedRectangleGeometry _shape;
    readonly Transform2DEffect _behind = new();
    readonly DisplacementMapEffect _bend = new() { XChannelSelect = EffectChannelSelect.Red, YChannelSelect = EffectChannelSelect.Green };
    readonly GaussianBlurEffect _blur = new() { BorderMode = EffectBorderMode.Hard, Optimization = EffectOptimization.Speed };
    readonly SaturationEffect _vivid = new() { Saturation = 1.2f };

    GraphicsCaptureItem? _item;
    Direct3D11CaptureFramePool? _pool;
    GraphicsCaptureSession? _session;
    SizeInt32 _poolSize;
    nint _monitor;
    /// <summary>The newest frame, kept to draw again when the island moves or resizes.</summary>
    Direct3D11CaptureFrame? _frame;
    CanvasBitmap? _screen;
    CanvasBitmap? _lens;
    (int Width, int Height, float Radius, bool SquareTop) _lensFor;
    CanvasBitmap? _light;
    (int Width, int Height, float Radius, bool SquareTop, float Rim) _lightFor;
    Look? _look;
    int _pending;
    /// <summary>Until when (<see cref="Environment.TickCount64"/>) new frames are left unused after a freeze.</summary>
    long _skipUntil;

    /// <summary>Capturing or drawing failed (the graphics card reset, the screen went away): the island falls back.</summary>
    public bool Failed { get; private set; }

    /// <summary>Only our monitor session excludes the island; other recorders can see it live.</summary>
    public bool SessionExclusion { get; private set; }

    /// <summary>The island's background: put it behind the lyrics, sized to the island.</summary>
    public SpriteVisual Visual { get; }

    /// <summary>
    /// Keeps showing what was behind the island instead of capturing it afresh. While the island shows
    /// in screenshots, it would capture itself otherwise; the glass still follows the island's size.
    /// </summary>
    public bool Frozen
    {
        get;
        set
        {
            // Frames from the next moment may still show the island, until Windows hides it from capture again.
            if (field && !value) _skipUntil = Environment.TickCount64 + 250;
            field = value;
        }
    }

    /// <param name="failed">Called (on the UI thread) when capturing or drawing fails.</param>
    public LiquidGlass(Compositor compositor, DispatcherQueue ui, nint window, Action failed)
    {
        _ui = ui;
        _failed = failed;
        _window = window;
        _graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _device);
        _surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(1, 1),
            Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
        var brush = compositor.CreateSurfaceBrush(_surface);
        brush.Stretch = CompositionStretch.Fill;
        _shape = compositor.CreateRoundedRectangleGeometry();
        Visual = compositor.CreateSpriteVisual();
        Visual.Brush = brush;
        // Smooth rounded corners, unlike a clipped window's.
        Visual.Clip = compositor.CreateGeometricClip(_shape);
    }

    /// <summary>
    /// Follows the island: the screen it's on (<paramref name="monitor"/>, whose top-left is
    /// <paramref name="origin"/>), its bounds there (<paramref name="pill"/>, pixels), its size and
    /// corner radius in view pixels, and the look (blur and refraction in view pixels; whether the
    /// edge catches the light).
    /// </summary>
    public void Update(nint monitor, PointInt32 origin, RectInt32 pill, Vector2 size, float radius, bool squareTop,
        float blur, float refraction, Windows.UI.Color tint, bool shine)
    {
        if (Failed) return;
        Visual.Size = size;
        // The default island's top is the screen's edge: round only the bottom corners.
        _shape.Offset = new Vector2(0, squareTop ? -radius : 0);
        _shape.Size = new Vector2(size.X, size.Y + (squareTop ? radius : 0));
        _shape.CornerRadius = new Vector2(radius, radius);
        float scale = size.X > 0 ? pill.Width / size.X : 1;
        _look = new Look(new RectInt32(pill.X - origin.X, pill.Y - origin.Y, pill.Width, pill.Height), radius * scale, squareTop,
            blur * scale, refraction * scale, tint, shine ? 1.5f * scale : 0);
        try
        {
            if (monitor != _monitor) Start(monitor);
            else if (_screen != null) Draw(_screen, _look);
        }
        catch (Exception e) { Fail(e); }
    }

    void Start(nint monitor)
    {
        Stop();
        _monitor = monitor;
        _item = ForMonitor(monitor);
        _item.Closed += (_, _) => _ui.TryEnqueue(() => Fail(null)); // the screen was unplugged
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
        _pool.FrameArrived += OnFrameArrived;
        _session = _pool.CreateCaptureSession(_item);
        _session.IsCursorCaptureEnabled = false;
        Borderless(_session);
        // Configure each new monitor session before its first frame: no unexcluded frames can
        // enter the pool. Do not briefly set global exclusion on the supported path either:
        // even a short global exclusion can interrupt another recorder.
        SessionExclusion = DisplayCaptureExclusion.TryExcludeWindow(((IWinRTObject)_session).NativeObject.ThisPtr,
            _window, Environment.OSVersion.Version.Build);
        if (SessionExclusion) Frozen = false;
        SetCaptureAffinity(SessionExclusion || Frozen ? Win32.WDA_NONE : Win32.WDA_EXCLUDEFROMCAPTURE);
        _session.StartCapture();
    }

    /// <summary>The legacy screenshot workaround is unnecessary with session-only exclusion.</summary>
    public bool ShowInScreenshots()
    {
        if (SessionExclusion || Failed) return false;
        try
        {
            Frozen = true; // stop consuming frames before letting capture see this window
            SetCaptureAffinity(Win32.WDA_NONE);
            return true;
        }
        catch (Exception e) { Fail(e); return false; }
    }

    public void EndScreenshot()
    {
        if (Failed) return;
        try
        {
            if (!SessionExclusion) SetCaptureAffinity(Win32.WDA_EXCLUDEFROMCAPTURE);
            Frozen = false;
        }
        catch (Exception e) { Fail(e); }
    }

    void SetCaptureAffinity(uint affinity)
    {
        // Never run live capture without either exclusion mechanism: that would feed the
        // island back into itself. Failure takes the existing clear-glass fallback instead.
        if (!Win32.SetWindowDisplayAffinity(_window, affinity))
            throw new InvalidOperationException("Could not set the island's capture affinity.");
    }

    /// <summary>Frames arrive on a capture thread; the newest is drawn on the UI thread.</summary>
    void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
    {
        if (Interlocked.Exchange(ref _pending, 1) == 0) _ui.TryEnqueue(DrawNewest);
    }

    void DrawNewest()
    {
        Volatile.Write(ref _pending, 0);
        if (_pool == null || Failed) return;
        try
        {
            Direct3D11CaptureFrame? newest = null;
            while (_pool.TryGetNextFrame() is { } frame)
            {
                newest?.Dispose();
                newest = frame;
            }
            if (newest == null) return;
            if (newest.ContentSize.Width != _poolSize.Width || newest.ContentSize.Height != _poolSize.Height)
            {
                // The screen's resolution changed.
                _poolSize = newest.ContentSize;
                _pool.Recreate(_device, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
            }
            if (Frozen || Environment.TickCount64 < _skipUntil)
            {
                newest.Dispose();
                return;
            }
            _screen?.Dispose();
            _frame?.Dispose();
            _frame = newest;
            _screen = CanvasBitmap.CreateFromDirect3D11Surface(_device, newest.Surface);
            if (_look != null) Draw(_screen, _look);
        }
        catch (Exception e) { Fail(e); }
    }

    /// <summary>The part of the screen behind the island: bent at the edges, blurred, a little more vivid, tinted, lit.</summary>
    void Draw(CanvasBitmap screen, Look look)
    {
        int width = look.Source.Width, height = look.Source.Height;
        if (width <= 0 || height <= 0) return;
        if (_surface.Size.Width != width || _surface.Size.Height != height)
            CanvasComposition.Resize(_surface, new Windows.Foundation.Size(width, height));
        _behind.Source = screen;
        _behind.TransformMatrix = Matrix3x2.CreateTranslation(-look.Source.X, -look.Source.Y);
        ICanvasImage image = _behind;
        if (look.Refraction > 0)
        {
            _bend.Source = image;
            _bend.Displacement = Lens(width, height, look.Radius, look.SquareTop);
            _bend.Amount = look.Refraction * 2; // the map's full swing is half the amount each way
            image = _bend;
        }
        if (look.Blur > 0)
        {
            _blur.Source = image;
            _blur.BlurAmount = look.Blur;
            image = _blur;
        }
        _vivid.Source = image;
        using var session = CanvasComposition.CreateDrawingSession(_surface);
        session.Clear(Microsoft.UI.Colors.Transparent);
        session.DrawImage(_vivid);
        session.FillRectangle(0, 0, width, height, look.Tint);
        if (look.Shine > 0) session.DrawImage(Light(width, height, look.Radius, look.SquareTop, look.Shine));
    }

    /// <summary>The lens map (see <see cref="LiquidLens"/>), made again only when the island's shape changes.</summary>
    CanvasBitmap Lens(int width, int height, float radius, bool squareTop)
    {
        if (_lens != null && _lensFor == (width, height, radius, squareTop)) return _lens;
        _lens?.Dispose();
        _lens = CanvasBitmap.CreateFromBytes(_device, LiquidLens.Map(width, height, radius, squareTop), width, height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        _lensFor = (width, height, radius, squareTop);
        return _lens;
    }

    /// <summary>The light along the edge (see <see cref="LiquidLens.Light"/>), made again only when the island's shape changes.</summary>
    CanvasBitmap Light(int width, int height, float radius, bool squareTop, float rim)
    {
        if (_light != null && _lightFor == (width, height, radius, squareTop, rim)) return _light;
        _light?.Dispose();
        _light = CanvasBitmap.CreateFromBytes(_device, LiquidLens.Light(width, height, radius, squareTop, rim), width, height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        _lightFor = (width, height, radius, squareTop, rim);
        return _light;
    }

    void Fail(Exception? e)
    {
        if (e != null) System.Diagnostics.Debug.WriteLine("liquid glass: " + e);
        Failed = true;
        Stop();
        _failed();
    }

    void Stop()
    {
        _session?.Dispose();
        _session = null;
        SessionExclusion = false;
        if (_pool != null)
        {
            _pool.FrameArrived -= OnFrameArrived;
            _pool.Dispose();
            _pool = null;
        }
        _screen?.Dispose();
        _screen = null;
        _frame?.Dispose();
        _frame = null;
        _item = null;
        _monitor = 0;
        Win32.SetWindowDisplayAffinity(_window, Win32.WDA_NONE);
    }

    public void Dispose()
    {
        Stop();
        _lens?.Dispose();
        _light?.Dispose();
        _surface.Dispose();
        _graphics.Dispose();
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop
    {
        nint CreateForWindow([In] nint window, [In] ref Guid iid);
        nint CreateForMonitor([In] nint monitor, [In] ref Guid iid);
    }

    /// <summary>A capture item for a whole screen, which WinRT only offers through this COM interface.</summary>
    static GraphicsCaptureItem ForMonitor(nint monitor)
    {
        var interop = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem").AsInterface<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760"); // IGraphicsCaptureItem
        nint pointer = interop.CreateForMonitor(monitor, ref iid);
        try { return GraphicsCaptureItem.FromAbi(pointer); }
        finally { Marshal.Release(pointer); }
    }

    /// <summary>
    /// Asks Windows 11 not to outline the screen while capturing. The property is newer than the
    /// Windows SDK Lyricod builds against, so it's set through its COM interface where Windows has it.
    /// </summary>
    static unsafe void Borderless(GraphicsCaptureSession session)
    {
        var iid = new Guid("F2CDD966-22AE-5EA1-9596-3A289344C3BE"); // IGraphicsCaptureSession3
        nint unknown = ((IWinRTObject)session).NativeObject.ThisPtr;
        if (Marshal.QueryInterface(unknown, in iid, out nint session3) != 0) return;
        try
        {
            // IUnknown (3) and IInspectable (3), then get_IsBorderRequired, put_IsBorderRequired.
            var put = (delegate* unmanaged[Stdcall]<nint, byte, int>)(*(void***)session3)[7];
            put(session3, 0);
        }
        finally { Marshal.Release(session3); }
    }
}
