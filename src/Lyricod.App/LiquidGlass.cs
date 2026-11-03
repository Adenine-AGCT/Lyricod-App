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
    CanvasDevice _device = null!; // shared; not owned here
    CompositionGraphicsDevice _graphics = null!;
    CompositionDrawingSurface _surface = null!;
    CompositionRoundedRectangleGeometry _shape = null!;
    CompositionSurfaceBrush _brush = null!;
    CompositionGeometricClip _clip = null!;
    SpriteVisual _visual = null!;
    Transform2DEffect _behind = null!;
    DisplacementMapEffect _bend = null!;
    GaussianBlurEffect _blur = null!;
    SaturationEffect _vivid = null!;

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
    int _pending, _generation, _startGeneration;
    /// <summary>Starts capturing a moment after a session is set up (see <see cref="Start"/>).</summary>
    readonly DispatcherQueueTimer _startDelay;
    volatile bool _disposed;
    bool _ownsAffinity;
    Windows.Foundation.TypedEventHandler<GraphicsCaptureItem, object>? _itemClosed;
    Windows.Foundation.TypedEventHandler<Direct3D11CaptureFramePool, object>? _frameArrived;
    /// <summary>Until when (<see cref="Environment.TickCount64"/>) new frames are left unused after a freeze.</summary>
    long _skipUntil;

    /// <summary>Capturing or drawing failed (the graphics card reset, the screen went away): the island falls back.</summary>
    public bool Failed { get; private set; }

    /// <summary>Only our monitor session excludes the island; other recorders can see it live.</summary>
    public bool SessionExclusion { get; private set; }

    /// <summary>The island's background: put it behind the lyrics, sized to the island.</summary>
    public SpriteVisual Visual => _visual;

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
        _startDelay = ui.CreateTimer();
        _startDelay.IsRepeating = false;
        _startDelay.Interval = TimeSpan.FromMilliseconds(400);
        _startDelay.Tick += (_, _) =>
        {
            int generation = _startGeneration;
            try { if (IsCurrent(generation)) _session?.StartCapture(); }
            catch (Exception e) { try { if (IsCurrent(generation)) Fail(e); } catch (Exception) { } }
        };
        try
        {
            _device = CanvasDevice.GetSharedDevice();
            _behind = new Transform2DEffect();
            _bend = new DisplacementMapEffect { XChannelSelect = EffectChannelSelect.Red, YChannelSelect = EffectChannelSelect.Green };
            _blur = new GaussianBlurEffect { BorderMode = EffectBorderMode.Hard, Optimization = EffectOptimization.Speed };
            _vivid = new SaturationEffect { Saturation = 1.2f };
            _graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _device);
            _surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(1, 1),
                Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied);
            _brush = compositor.CreateSurfaceBrush(_surface);
            _brush.Stretch = CompositionStretch.Fill;
            _shape = compositor.CreateRoundedRectangleGeometry();
            _visual = compositor.CreateSpriteVisual();
            _visual.Brush = _brush;
            // Smooth rounded corners, unlike a clipped window's.
            _clip = compositor.CreateGeometricClip(_shape);
            _visual.Clip = _clip;
        }
        catch
        {
            DisposeDrawingResources();
            throw;
        }
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
        if (_disposed || Failed) return;
        try
        {
            Visual.Size = size;
            // The default island's top is the screen's edge: round only the bottom corners.
            _shape.Offset = new Vector2(0, squareTop ? -radius : 0);
            _shape.Size = new Vector2(size.X, size.Y + (squareTop ? radius : 0));
            _shape.CornerRadius = new Vector2(radius, radius);
            float scale = size.X > 0 ? pill.Width / size.X : 1;
            _look = new Look(new RectInt32(pill.X - origin.X, pill.Y - origin.Y, pill.Width, pill.Height), radius * scale, squareTop,
                blur * scale, refraction * scale, tint, shine ? 1.5f * scale : 0);
            if (monitor != _monitor) Start(monitor);
            else if (_screen != null) Draw(_screen, _look);
        }
        catch (Exception e) { Fail(e); }
    }

    void Start(nint monitor)
    {
        Stop();
        if (_disposed) return;
        int generation = Volatile.Read(ref _generation);
        _monitor = monitor;
        _item = ForMonitor(monitor);
        _itemClosed = (_, _) =>
        {
            try
            {
                if (!IsCurrent(generation)) return;
                _ui.TryEnqueue(() =>
                {
                    try { if (IsCurrent(generation)) Fail(null); }
                    catch (Exception) { }
                });
            }
            catch (Exception) { }
        };
        _item.Closed += _itemClosed;
        _poolSize = _item.Size;
        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_device, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
        DispatcherQueueHandler drawNewest = () =>
        {
            try
            {
                if (!IsCurrent(generation)) return;
                Interlocked.CompareExchange(ref _pending, 0, generation);
                DrawNewest();
            }
            catch (Exception e) { if (IsCurrent(generation)) Fail(e); }
        };
        _frameArrived = (_, _) => OnFrameArrived(generation, drawNewest);
        _pool.FrameArrived += _frameArrived;
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
        // Windows takes a moment to leave the island out of capture, either way. Capturing at once could
        // make the first frame show the island inside its own glass, and on a still screen that frame
        // stays (no new ones come until something changes). So start capturing once it has.
        _startGeneration = generation;
        _startDelay.Stop();
        _startDelay.Start();
    }

    /// <summary>The legacy screenshot workaround is unnecessary with session-only exclusion.</summary>
    public bool ShowInScreenshots()
    {
        if (_disposed || _session == null || SessionExclusion || Failed) return false;
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
        if (_disposed || Failed) return;
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
        if (_disposed) return;
        if (!Win32.SetWindowDisplayAffinity(_window, affinity))
            throw new InvalidOperationException("Could not set the island's capture affinity.");
        _ownsAffinity = affinity != Win32.WDA_NONE;
    }

    internal int Generation => Volatile.Read(ref _generation);

    internal bool IsCurrent(int generation) => !_disposed && !Failed && generation == Volatile.Read(ref _generation);

    /// <summary>Frames arrive on a capture thread; the newest is drawn on the UI thread.</summary>
    void OnFrameArrived(int generation, DispatcherQueueHandler drawNewest)
    {
        try
        {
            if (!IsCurrent(generation) || Interlocked.Exchange(ref _pending, generation) == generation) return;
            if (!_ui.TryEnqueue(drawNewest)) Interlocked.CompareExchange(ref _pending, 0, generation);
        }
        catch (Exception) { Interlocked.CompareExchange(ref _pending, 0, generation); }
    }

    void DrawNewest()
    {
        if (_disposed || _pool == null || Failed) return;
        Direct3D11CaptureFrame? newest = null;
        try
        {
            while (_pool.TryGetNextFrame() is { } frame)
            {
                var previous = newest;
                newest = frame;
                previous?.Dispose();
            }
            if (newest == null) return;
            if (newest.ContentSize.Width != _poolSize.Width || newest.ContentSize.Height != _poolSize.Height)
            {
                // Return every checked-out surface before resizing the pool.
                _poolSize = newest.ContentSize;
                RetireFrame();
                DisposeOwned(ref newest);
                _pool.Recreate(_device, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, 3, _poolSize);
                return;
            }
            if (Frozen || Environment.TickCount64 < _skipUntil) return;
            RetireFrame();
            _screen = CanvasBitmap.CreateFromDirect3D11Surface(_device, newest.Surface);
            _frame = newest;
            newest = null; // owned by _frame now
            if (_look != null) Draw(_screen, _look);
        }
        catch (Exception e) { Fail(e); }
        finally { DisposeOwned(ref newest); }
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
        DisposeOwned(ref _lens);
        _lens = CanvasBitmap.CreateFromBytes(_device, LiquidLens.Map(width, height, radius, squareTop), width, height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        _lensFor = (width, height, radius, squareTop);
        return _lens;
    }

    /// <summary>The light along the edge (see <see cref="LiquidLens.Light"/>), made again only when the island's shape changes.</summary>
    CanvasBitmap Light(int width, int height, float radius, bool squareTop, float rim)
    {
        if (_light != null && _lightFor == (width, height, radius, squareTop, rim)) return _light;
        DisposeOwned(ref _light);
        _light = CanvasBitmap.CreateFromBytes(_device, LiquidLens.Light(width, height, radius, squareTop, rim), width, height,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        _lightFor = (width, height, radius, squareTop, rim);
        return _light;
    }

    internal void Fail(Exception? e)
    {
        if (_disposed || Failed) return;
        Failed = true;
        try { if (e != null) System.Diagnostics.Debug.WriteLine("liquid glass: " + e); } catch (Exception) { }
        try { Stop(); } catch (Exception) { }
        try { _failed(); } catch (Exception) { }
    }

    void Stop()
    {
        try { _startDelay.Stop(); } catch (Exception) { }
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _pending, 0);
        _monitor = 0;
        SessionExclusion = false;
        Frozen = false; // the old session's screenshot timer is retired too
        var item = _item;
        var closed = _itemClosed;
        _item = null;
        _itemClosed = null;
        if (item != null && closed != null)
            try { item.Closed -= closed; } catch (Exception) { }
        DisposeOwned(ref _session);
        var pool = _pool;
        _pool = null;
        var arrived = _frameArrived;
        _frameArrived = null;
        if (pool != null)
        {
            try { if (arrived != null) pool.FrameArrived -= arrived; } catch (Exception) { }
            DisposeOwned(ref pool);
        }
        RetireFrame();
        bool resetAffinity = _ownsAffinity;
        _ownsAffinity = false;
        if (resetAffinity)
            try { Win32.SetWindowDisplayAffinity(_window, Win32.WDA_NONE); } catch (Exception) { }
    }

    void RetireFrame()
    {
        try { _behind.Source = null; } catch (Exception) { }
        DisposeOwned(ref _screen);
        DisposeOwned(ref _frame);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { Stop(); } catch (Exception) { }
        DisposeDrawingResources();
    }

    void DisposeDrawingResources()
    {
        DisposeOwned(ref _visual);
        DisposeOwned(ref _clip);
        DisposeOwned(ref _brush);
        DisposeOwned(ref _shape);
        DisposeOwned(ref _vivid);
        DisposeOwned(ref _blur);
        DisposeOwned(ref _bend);
        DisposeOwned(ref _behind);
        DisposeOwned(ref _lens);
        DisposeOwned(ref _light);
        DisposeOwned(ref _surface);
        DisposeOwned(ref _graphics);
    }

    static void DisposeOwned<T>(ref T resource) where T : class, IDisposable
    {
        var owned = resource;
        resource = null!;
        try { owned?.Dispose(); }
        catch (Exception) { /* keep releasing the other resources */ }
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
