namespace Lyricod.Core.Tests;

public class DesktopLyricsPlacementTests
{
    static readonly ScreenRect WorkArea = new(0, 0, 1920, 1040);

    static ScreenRect Place(Settings settings, ScreenRect? current, int height)
    {
        (int X, int Y)? saved = settings.DesktopX is int x && settings.DesktopY is int y ? (x, y) : null;
        var spot = DesktopLyricsPlacement.Origin(saved, current, WorkArea, 900, height, 40);
        return new ScreenRect(spot.X, spot.Y, 900, height);
    }

    [Theory]
    [InlineData(70)]
    [InlineData(116)]
    public void FirstRestoreUsesSavedTopLeftRegardlessOfHeight(int height)
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100 };
        var strip = Place(settings, null, height);
        Assert.Equal(360, strip.Left);
        Assert.Equal(100, strip.Top);
    }

    [Fact]
    public void ResizingKeepsTheCurrentBottomWithoutRewritingTheSavedPosition()
    {
        var settings = new Settings { DesktopX = 360, DesktopY = 100 };
        var strip = new ScreenRect(-1200, 300, 900, 96);
        double bottom = strip.Bottom;
        foreach (int height in new[] { 116, 70, 96 })
        {
            strip = Place(settings, strip, height);
            Assert.Equal(-1200, strip.Left);
            Assert.Equal(bottom, strip.Bottom);
        }
        Assert.Equal(360, settings.DesktopX);
        Assert.Equal(100, settings.DesktopY);
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
