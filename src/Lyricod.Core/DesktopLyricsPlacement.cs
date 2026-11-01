namespace Lyricod.Core;

/// <summary>Places the desktop strip in physical pixels, keeping a resized custom strip's bottom edge still.</summary>
public static class DesktopLyricsPlacement
{
    /// <summary>The saved position is a top-left, including on the first restore. Only resize from
    /// the actual current bottom after that first restore. A reset has no saved spot.</summary>
    public static (int X, int Y) Origin((int X, int Y)? saved, ScreenRect? current,
        ScreenRect workArea, int width, int height, int bottomMargin)
    {
        if (saved is { } spot)
        {
            if (current is { } window)
                return ((int)window.Left, (int)window.Bottom - height);
            return spot;
        }
        return ((int)workArea.Left + ((int)workArea.Width - width) / 2,
            (int)workArea.Bottom - height - bottomMargin);
    }
}
