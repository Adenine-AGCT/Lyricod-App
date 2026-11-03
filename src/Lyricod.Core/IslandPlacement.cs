namespace Lyricod.Core;

/// <summary>A rectangle on screen, in physical pixels.</summary>
public readonly record struct ScreenRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

/// <summary>Which side of a floating island stays put while it grows.</summary>
public enum IslandAnchorX { Centre, Left, Right }

/// <summary>Whether a floating island grows down from its top or up from its bottom.</summary>
public enum IslandAnchorY { Top, Bottom }

/// <summary>
/// Where the floating island sits. It's saved as one point and grows away from the screen's
/// edges: from its centre when its widest layout fits around that point, otherwise from the side
/// nearest the edge; down from its top when its tallest layout (the mini player) fits below,
/// otherwise up from its bottom. So a long line or the mini player never runs off the screen.
/// </summary>
public static class IslandPlacement
{
    /// <summary>
    /// The point to save for a pill dropped at <paramref name="pill"/>: its centre, left or right
    /// edge across, and its top or bottom edge down, kept inside <paramref name="area"/>. Whole
    /// pixels, rounded so that <see cref="Anchors"/> reads the same edges back from it.
    /// </summary>
    public static (int X, int Y) Spot(ScreenRect pill, ScreenRect area, double maxWidth, double maxHeight)
    {
        double centre = pill.Left + pill.Width / 2;
        double x = AnchorX(centre, area, maxWidth) switch
        {
            IslandAnchorX.Centre => Math.Min(Math.Max(Math.Round(centre), Math.Ceiling(area.Left + maxWidth / 2)), Math.Floor(area.Right - maxWidth / 2)),
            IslandAnchorX.Left => Math.Round(Math.Max(pill.Left, area.Left)),
            _ => Math.Round(Math.Min(pill.Right, area.Right)),
        };
        double y = pill.Top + maxHeight <= area.Bottom
            ? Math.Floor(Math.Max(pill.Top, area.Top))
            : Math.Ceiling(Math.Min(pill.Bottom, area.Bottom));
        return ((int)x, (int)y);
    }

    /// <summary>A point inside the owning display, separate from the saved edge used for placement.</summary>
    public static (int X, int Y) DisplayPoint(ScreenRect pill, ScreenRect display) =>
        ((int)Math.Clamp(Math.Floor(pill.Left + pill.Width / 2), display.Left, display.Right - 1),
         (int)Math.Clamp(Math.Floor(pill.Top + pill.Height / 2), display.Top, display.Bottom - 1));

    /// <summary>The edges an island saved at (<paramref name="x"/>, <paramref name="y"/>) grows from.</summary>
    public static (IslandAnchorX X, IslandAnchorY Y) Anchors(double x, double y, ScreenRect area, double maxWidth, double maxHeight) =>
        (AnchorX(x, area, maxWidth), y + maxHeight <= area.Bottom ? IslandAnchorY.Top : IslandAnchorY.Bottom);

    static IslandAnchorX AnchorX(double x, ScreenRect area, double maxWidth) =>
        x - maxWidth / 2 >= area.Left && x + maxWidth / 2 <= area.Right ? IslandAnchorX.Centre
        : x - maxWidth / 2 < area.Left ? IslandAnchorX.Left : IslandAnchorX.Right;

    /// <summary>
    /// Top-left of a box (the window, or the pill itself) that holds the pill against its anchored
    /// edges at the saved point.
    /// </summary>
    public static (double Left, double Top) Origin(double x, double y, (IslandAnchorX X, IslandAnchorY Y) anchors, double width, double height) =>
        (anchors.X switch { IslandAnchorX.Left => x, IslandAnchorX.Right => x - width, _ => x - width / 2 },
         anchors.Y == IslandAnchorY.Top ? y : y - height);
}
