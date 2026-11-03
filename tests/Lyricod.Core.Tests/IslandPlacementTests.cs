namespace Lyricod.Core.Tests;

public class IslandPlacementTests
{
    static readonly ScreenRect Screen = new(0, 0, 1920, 1040);
    const double MaxWidth = 560, MaxHeight = 150;

    static (IslandAnchorX, IslandAnchorY) Drop(double left, double top, double width = 300, double height = 34)
    {
        var (x, y) = IslandPlacement.Spot(new(left, top, width, height), Screen, MaxWidth, MaxHeight);
        return IslandPlacement.Anchors(x, y, Screen, MaxWidth, MaxHeight);
    }

    [Fact]
    public void GrowsFromItsCentreWhenThereIsRoom() =>
        Assert.Equal((IslandAnchorX.Centre, IslandAnchorY.Top), Drop(810, 16));

    [Fact]
    public void GrowsAwayFromTheEdgeItIsNear()
    {
        Assert.Equal((IslandAnchorX.Left, IslandAnchorY.Top), Drop(10, 16));
        Assert.Equal((IslandAnchorX.Right, IslandAnchorY.Top), Drop(1600, 16));
        Assert.Equal((IslandAnchorX.Centre, IslandAnchorY.Bottom), Drop(810, 990));
        Assert.Equal((IslandAnchorX.Left, IslandAnchorY.Bottom), Drop(10, 990));
    }

    [Fact]
    public void ReloadingPutsThePillWhereItWasDropped()
    {
        // Within the pixel the saved spot rounds to: a misread edge would move it half its width.
        var random = new Random(7);
        for (int i = 0; i < 20000; i++)
        {
            double width = 120 + random.NextDouble() * (MaxWidth - 120), height = 34 + random.NextDouble() * 26;
            var pill = new ScreenRect(random.NextDouble() * (Screen.Width - width), random.NextDouble() * (Screen.Height - height), width, height);
            var (x, y) = IslandPlacement.Spot(pill, Screen, MaxWidth, MaxHeight);
            var anchors = IslandPlacement.Anchors(x, y, Screen, MaxWidth, MaxHeight);
            var (left, top) = IslandPlacement.Origin(x, y, anchors, width, height);
            Assert.InRange(left - pill.Left, -1, 1);
            Assert.InRange(top - pill.Top, -1, 1);
        }
    }

    [Fact]
    public void TheWidestAndTallestLayoutStaysOnScreen()
    {
        var random = new Random(11);
        for (int i = 0; i < 5000; i++)
        {
            // Anywhere, even partly off the screen (dragged past an edge).
            var pill = new ScreenRect(random.Next(-200, 1900), random.Next(-30, 1030), 300, 34);
            var (x, y) = IslandPlacement.Spot(pill, Screen, MaxWidth, MaxHeight);
            var anchors = IslandPlacement.Anchors(x, y, Screen, MaxWidth, MaxHeight);
            var (left, top) = IslandPlacement.Origin(x, y, anchors, MaxWidth, MaxHeight);
            Assert.InRange(left, Screen.Left, Screen.Right - MaxWidth);
            Assert.InRange(top, Screen.Top, Screen.Bottom - MaxHeight);
        }
    }

    [Theory]
    [InlineData(0, 0, 1620, 100, 1920, 100)]
    [InlineData(0, 0, 100, 1006, 100, 1040)]
    [InlineData(0, 0, 1620, 1006, 1920, 1040)]
    [InlineData(-1920, -1040, -300, -34, 0, 0)]
    public void FlushEdgesKeepTheirDisplayAndPlacement(int displayX, int displayY, int left, int top, int edgeX, int edgeY)
    {
        var display = new ScreenRect(displayX, displayY, 1920, 1040);
        var pill = new ScreenRect(left, top, 300, 34);
        var spot = IslandPlacement.Spot(pill, display, MaxWidth, MaxHeight);
        Assert.Equal((edgeX, edgeY), spot);
        var lookup = IslandPlacement.DisplayPoint(pill, display);
        Assert.InRange(lookup.X, display.Left, display.Right - 1);
        Assert.InRange(lookup.Y, display.Top, display.Bottom - 1);
        // Adjacent displays start at the right or bottom edge.
        Assert.True(lookup.X < display.Right && lookup.Y < display.Bottom);
        var anchors = IslandPlacement.Anchors(spot.X, spot.Y, display, MaxWidth, MaxHeight);
        Assert.Equal((pill.Left, pill.Top), IslandPlacement.Origin(spot.X, spot.Y, anchors, pill.Width, pill.Height));
    }

    [Fact]
    public void DisplayLookupStaysInsideEvenWhenDraggedPastAnEdge() =>
        Assert.Equal((1919, 1039), IslandPlacement.DisplayPoint(new(1900, 1030, 300, 34), Screen));

    [Fact]
    public void WindowOriginKeepsTheAnchoredEdgeInPlace()
    {
        Assert.Equal((100.0, 50.0), IslandPlacement.Origin(100, 50, (IslandAnchorX.Left, IslandAnchorY.Top), 600, 160));
        Assert.Equal((-500.0, 50.0), IslandPlacement.Origin(100, 50, (IslandAnchorX.Right, IslandAnchorY.Top), 600, 160));
        Assert.Equal((-200.0, -110.0), IslandPlacement.Origin(100, 50, (IslandAnchorX.Centre, IslandAnchorY.Bottom), 600, 160));
    }
}
