namespace Lyricod.Core.Tests;

public class DesktopLyricsPlacementTests
{
    static readonly ScreenRect WorkArea = new(0, 0, 1920, 1040);

    // Exercise the same saved top-left/current rectangle contract as DesktopLyricsWindow.Place.
    // Heights are physical-pixel inputs; WinUI sizing and monitor selection are not simulated here.
    static ScreenRect Place(Settings settings, ScreenRect? current, int height, int width = 900, int margin = 40)
    {
        (int X, int Y)? saved = settings.DesktopX is int x && settings.DesktopY is int y ? (x, y) : null;
        var spot = DesktopLyricsPlacement.Origin(saved, current, WorkArea, width, height, margin);
        var result = new ScreenRect(spot.X, spot.Y, width, height);
        if (saved != null)
        {
            settings.DesktopX = spot.X;
            settings.DesktopY = spot.Y;
        }
        return result;
    }

    [Theory]
    [InlineData(70)]
    [InlineData(96)]
    [InlineData(116)]
    [InlineData(174)]
    public void FirstRestoreUsesSavedTopLeftRegardlessOfHeight(int height)
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100 };
        var strip = Place(settings, null, height);
        Assert.Equal(360, strip.Left);
        Assert.Equal(100, strip.Top);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(2.0, true)]
    [InlineData(1.0, false)]
    public void FontAndSecondRowRoundTripsKeepTheBottomAndSavedPosition(double scale, bool locked)
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100, DesktopLocked = locked };
        int width = (int)(900 * scale), margin = (int)(40 * scale);
        var strip = Place(settings, null, (int)(96 * scale), width, margin);
        double bottom = strip.Bottom;
        // 30 -> 40 points, second row off, 40 -> 30 points, second row on.
        foreach (int dipHeight in new[] { 116, 82, 70, 96 })
        {
            strip = Place(settings, strip, (int)(dipHeight * scale), width, margin);
            Assert.Equal(bottom, strip.Bottom);
            Assert.Equal((int)strip.Top, settings.DesktopY);
        }
        Assert.Equal(100, strip.Top);
    }

    [Fact]
    public void RestartAfterAResizeRestoresTheNewTopWithoutMovingTheBottom()
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100, DesktopLocked = true };
        var before = Place(settings, null, 96);
        var resized = Place(settings, before, 116);
        Assert.Equal(80, settings.DesktopY);
        var restored = Place(settings, null, 116);
        Assert.Equal(resized, restored);
        Assert.Equal(before.Bottom, restored.Bottom);
    }

    [Fact]
    public void DraggingToANegativeCoordinateThenLockingKeepsTheNewBottom()
    {
        var settings = new Settings { DesktopX = -1500, DesktopY = -200, DesktopLocked = false };
        var dragged = new ScreenRect(-1500, -200, 900, 96);
        settings.DesktopLocked = true;
        var resized = Place(settings, dragged, 116);
        Assert.Equal(-1500, resized.Left);
        Assert.Equal(-220, resized.Top);
        Assert.Equal(dragged.Bottom, resized.Bottom);
        Assert.Equal(resized, Place(settings, null, 116));
    }

    [Fact]
    public void ActualSystemMovedWindowWinsOverAnOlderSavedPositionAfterFirstRestore()
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100, DesktopLocked = true };
        var moved = new ScreenRect(-1200, 300, 900, 96);
        var resized = Place(settings, moved, 116);
        Assert.Equal(-1200, resized.Left);
        Assert.Equal(moved.Bottom, resized.Bottom);
        Assert.Equal(-1200, settings.DesktopX);
        Assert.Equal(280, settings.DesktopY);
    }

    [Fact]
    public void ResetDiscardsTheOldCustomAnchorAndRemainsDefaultAfterResizeOrRestart()
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100 };
        var custom = Place(settings, null, 96);
        settings.DesktopX = settings.DesktopY = null;
        var reset = Place(settings, custom, 116);
        Assert.Equal(510, reset.Left);
        Assert.Equal(1000, reset.Bottom);
        var smaller = Place(settings, reset, 70);
        Assert.Equal(reset.Bottom, smaller.Bottom);
        Assert.Null(settings.DesktopX);
        Assert.Null(settings.DesktopY);
        Assert.Equal(smaller, Place(settings, null, 70));
    }
}
