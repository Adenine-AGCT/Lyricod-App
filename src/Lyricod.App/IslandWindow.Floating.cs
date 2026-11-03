using Lyricod.Core;
using Lyricod.Platform;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Lyricod;

/// <summary>
/// The floating style (Settings › Lyricod Island › Island style): rounded on all four corners and
/// left where it was dragged. Unlocked, it takes clicks and moves with the pointer; locked, it
/// behaves like the default island. Near a screen edge it grows away from it (see <see cref="IslandPlacement"/>).
/// </summary>
public sealed partial class IslandWindow
{
    bool Floating => _settings.IslandStyle == IslandStyle.Floating;

    /// <summary>Floating and unlocked: takes clicks and can be dragged, with no hover action.</summary>
    bool Movable => Floating && !_settings.IslandLocked;

    /// <summary>The widest and tallest the pill gets (a long line, the mini player), in view pixels.</summary>
    double MaxPillWidth => Math.Max(2 * SideWidth + MaxText, ExpandedMinWidth);
    double MaxPillHeight => RowHeight + SecondHeight + ExpandedHeight;

    /// <summary>Where it was dragged to, or just below the top centre of the main display.</summary>
    void PlaceFloating(double k)
    {
        // A saved spot on a screen that's no longer there (unplugged) falls back to the default.
        DisplayArea? display = _settings.IslandX is int sx && _settings.IslandY is int sy
            ? DisplayArea.GetFromPoint(new PointInt32(_settings.IslandDisplayX ?? sx, _settings.IslandDisplayY ?? sy), DisplayAreaFallback.None) : null;
        var area = (display ?? DisplayArea.Primary).WorkArea;
        _screenWidth = area.Width / k;
        double x = display != null ? _settings.IslandX!.Value : area.X + area.Width / 2.0;
        double y = display != null ? _settings.IslandY!.Value : area.Y + 16 * k;
        var anchors = IslandPlacement.Anchors(x, y, new ScreenRect(area.X, area.Y, area.Width, area.Height), MaxPillWidth * k, MaxPillHeight * k);
        Pill.HorizontalAlignment = anchors.X switch
        {
            IslandAnchorX.Left => HorizontalAlignment.Left,
            IslandAnchorX.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        Pill.VerticalAlignment = anchors.Y == IslandAnchorY.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        // Keep room for an old, larger layout until its shrink animation finishes.
        double width = Math.Max(WindowWidth, Pill.Width + 20 * LayoutScale);
        double height = Math.Max(WindowHeight, Pill.Height + 12 * LayoutScale);
        var size = new SizeInt32((int)Math.Ceiling(width * k), (int)Math.Ceiling(height * k));
        var (left, top) = IslandPlacement.Origin(x, y, anchors, size.Width, size.Height);
        var rect = new RectInt32((int)Math.Round(left), (int)Math.Round(top), size.Width, size.Height);
        if (AppWindow.Size.Width != rect.Width || AppWindow.Size.Height != rect.Height || AppWindow.Position.X != rect.X || AppWindow.Position.Y != rect.Y)
            AppWindow.MoveAndResize(rect);
    }

    /// <summary>The pill's bounds on screen, in physical pixels.</summary>
    (double Left, double Top, double Width, double Height) PillBounds()
    {
        double k = _overlay.Scale;
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        double width = Pill.ActualWidth * k, height = Pill.ActualHeight * k;
        double left = pos.X + Pill.HorizontalAlignment switch
        {
            HorizontalAlignment.Left => 0,
            HorizontalAlignment.Right => size.Width - width,
            _ => (size.Width - width) / 2,
        };
        double top = pos.Y + (Pill.VerticalAlignment == VerticalAlignment.Bottom ? size.Height - height : 0);
        return (left, top, width, height);
    }

    /// <summary>
    /// Unlocked (floating only): the island takes clicks and turns dark grey (see <see cref="ApplyTheme"/>),
    /// so it's clear it can be dragged. Locked: click-through until the pointer is over it.
    /// </summary>
    void ApplyMoveMode()
    {
        SetHovered(false);
        _overlay.SetClickThrough(!Movable);
        ApplyTheme();
    }

    void OnPillPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!Movable) return;
            // Windows moves the window as if its title bar were dragged, and returns once the button is let go.
            Win32.ReleaseCapture();
            Win32.SendMessage(_overlay.Handle, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, 0);
            if (!_closed) SaveSpot();
        }
        catch (Exception) { /* the screen or window went away */ }
    }

    /// <summary>Remembers where the pill was dropped, then places it from there.</summary>
    void SaveSpot()
    {
        double k = _overlay.Scale;
        var (left, top, width, height) = PillBounds();
        var display = DisplayArea.GetFromRect(new RectInt32((int)left, (int)top, Math.Max(1, (int)width), Math.Max(1, (int)height)),
            DisplayAreaFallback.Nearest);
        var area = display.WorkArea;
        var bounds = display.OuterBounds;
        var (dx, dy) = IslandPlacement.DisplayPoint(new ScreenRect(left, top, width, height),
            new ScreenRect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        _settings.IslandDisplayX = dx;
        _settings.IslandDisplayY = dy;
        // The screen it was dropped on decides how wide a line can get.
        _screenWidth = area.Width / k;
        var (x, y) = IslandPlacement.Spot(new ScreenRect(left, top, width, height), new ScreenRect(area.X, area.Y, area.Width, area.Height),
            MaxPillWidth * k, MaxPillHeight * k);
        _settings.IslandX = x;
        _settings.IslandY = y;
        Place();
    }
}
