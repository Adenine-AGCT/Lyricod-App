using System.Numerics;
using Lyricod.Platform;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;

namespace Lyricod;

/// <summary>
/// Lyricod Glass's liquid effect (see <see cref="LiquidGlass"/>). It runs only while
/// it's the chosen glass effect and the island shows (not while the pointer hides it or it's being
/// moved). Older Windows builds hide the island globally from capture; updated builds exclude
/// it only from its own session, so screenshots and other recordings can see the live glass.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>How long the island shows in screenshots when asked (see <see cref="ShowInScreenshots"/>).</summary>
    public const int ScreenshotSeconds = 10;

    LiquidGlass? _liquid;
    DispatcherQueueTimer? _screenshotTimer;

    internal string LiquidCaptureStatus => _liquid == null ? "inactive" : _liquid.Failed ? "failed"
        : _liquid.SessionExclusion ? "session-only exclusion (live, recordable)"
        : _liquid.Frozen ? "global exclusion fallback (screenshot pause)" : "global exclusion fallback";

    bool WantsLiquid => Glass && _settings.GlassEffect == GlassEffect.Liquid && LiquidGlass.IsSupported && !Movable;

    /// <summary>Starts, follows or stops the liquid glass; returns whether it's showing.</summary>
    bool ApplyLiquid()
    {
        bool on = WantsLiquid && _shown && !(_hovered && _hoverAction == IslandHover.Hide) && _liquid?.Failed != true;
        if (!on)
        {
            StopLiquid();
            return false;
        }
        if (_liquid == null)
        {
            // If it fails later (the graphics card reset, the screen went away), fall back to clear glass.
            _liquid = new LiquidGlass(ElementCompositionPreview.GetElementVisual(LiquidHost).Compositor, DispatcherQueue, _overlay.Handle,
                () => DispatcherQueue.TryEnqueue(() => { if (_liquid?.Failed == true) ApplyTheme(); }));
            ElementCompositionPreview.SetElementChildVisual(LiquidHost, _liquid.Visual);
        }
        UpdateLiquid();
        if (!_liquid.Failed) return true;
        StopLiquid();
        return false;
    }

    /// <summary>
    /// Lets screenshots, recordings and screen sharing see the liquid glass island for a few seconds.
    /// Meanwhile the glass stops capturing and keeps showing what was behind it, so it can't capture
    /// itself; the lyrics carry on.
    /// </summary>
    public void ShowInScreenshots()
    {
        if (_liquid?.ShowInScreenshots() != true) return;
        if (_screenshotTimer == null)
        {
            _screenshotTimer = DispatcherQueue.CreateTimer();
            _screenshotTimer.Interval = TimeSpan.FromSeconds(ScreenshotSeconds);
            _screenshotTimer.IsRepeating = false;
            _screenshotTimer.Tick += (_, _) => EndScreenshot();
        }
        _screenshotTimer.Stop();
        _screenshotTimer.Start();
    }

    /// <summary>Hides the island from capture again, then lets the glass follow what's behind it.</summary>
    void EndScreenshot()
    {
        _screenshotTimer?.Stop();
        if (_liquid == null) return;
        _liquid.EndScreenshot();
    }

    void StopLiquid()
    {
        _screenshotTimer?.Stop();
        if (_liquid == null) return;
        ElementCompositionPreview.SetElementChildVisual(LiquidHost, null);
        _liquid.Dispose();
        _liquid = null;
    }

    /// <summary>Keeps the liquid glass on what's behind the island as it moves, grows and changes look.</summary>
    void UpdateLiquid()
    {
        if (_liquid == null) return;
        var (left, top, width, height) = PillBounds();
        if (width <= 0 || height <= 0) return;
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var screen = display.OuterBounds;
        _liquid.Update(Microsoft.UI.Win32Interop.GetMonitorFromDisplayId(display.DisplayId), new PointInt32(screen.X, screen.Y),
            new RectInt32((int)Math.Round(left), (int)Math.Round(top), (int)Math.Round(width), (int)Math.Round(height)),
            new Vector2((float)Pill.ActualWidth, (float)Pill.ActualHeight), (float)Pill.CornerRadius.BottomLeft, !Floating,
            blur: _settings.LiquidBlur / 100f * 16, refraction: _settings.LiquidRefraction / 100f * 14, GlassTintColour(), shine: GlassLit);
    }
}
