namespace Lyricod.Core.Tests;

public class IslandGestureTests
{
    // A default island at the top of the screen (x 100–300, y 0–34) and a floating one (x 100–300, y 100–140).
    static readonly (double, double, double, double) Docked = (100, 0, 200, 34), Floating = (100, 100, 200, 40);

    // As the app asks: the default island opens only on a pull down.
    static bool Opens((double, double, double, double) island, params (double X, double Y)[] path) =>
        IslandGesture.IsSweepThenPull(path, island, downOnly: island == Docked);

    [Fact]
    public void SweepingAlongTheTopThenPullingDownOutOfItOpens() =>
        Assert.True(Opens(Docked, (60, 10), (110, 10), (160, 12), (200, 12), (205, 25), (206, 40), (207, 50)));

    [Fact]
    public void SweepingBelowItThenPullingDownOpens() =>
        // Under the lyric, along the second row, then down: the pull starts over the island.
        Assert.True(Opens(Docked, (60, 28), (120, 28), (180, 30), (186, 45), (188, 64)));

    [Fact]
    public void ACurvedPullCounts() =>
        Assert.True(Opens(Docked, (40, 8), (90, 8), (140, 10), (170, 16), (184, 28), (190, 44), (192, 58)));

    [Fact]
    public void ComingInFromTheSideThenUpOpens() =>
        Assert.True(Opens(Floating, (40, 175), (90, 175), (150, 172), (156, 150), (158, 132)));

    [Fact]
    public void SweepingAcrossThenPullingUpOverAFloatingIslandOpens() =>
        Assert.True(Opens(Floating, (60, 130), (140, 130), (200, 128), (204, 112), (205, 92)));

    [Fact]
    public void PullingUpOverTheDefaultIslandDoesNotOpen()
    {
        // Along under it, then up into it: a floating island would open.
        (double, double)[] path = [(60, 60), (120, 60), (180, 58), (184, 40), (186, 20)];
        Assert.False(Opens(Docked, path));
        Assert.True(IslandGesture.IsSweepThenPull(path, Docked));
    }

    [Fact]
    public void ASmallPullDoesNotOpen() =>
        Assert.False(Opens(Docked, (60, 10), (110, 10), (160, 12), (200, 12), (203, 20), (204, 26)));

    [Fact]
    public void JustSweepingAcrossDoesNotOpen() =>
        Assert.False(Opens(Docked, (60, 10), (120, 11), (180, 12), (240, 12), (280, 13)));

    [Fact]
    public void StraightDownDoesNotOpen() =>
        Assert.False(Opens(Docked, (200, 2), (200, 15), (201, 30), (201, 50)));

    [Fact]
    public void DiagonalDoesNotOpen() =>
        Assert.False(Opens(Docked, (120, 0), (140, 20), (160, 40), (180, 60)));

    [Fact]
    public void AShortSweepDoesNotOpen() =>
        Assert.False(Opens(Docked, (180, 10), (200, 10), (204, 30), (205, 50)));

    [Fact]
    public void ThePullHasToBeOverTheIsland() =>
        // Sweep and pull well to the side of it.
        Assert.False(Opens(Docked, (400, 10), (460, 10), (520, 12), (522, 40), (523, 60)));

    [Fact]
    public void APullWhoseBoundingBoxOverlapsButWhoseSegmentsMissDoesNotOpen() =>
        Assert.False(Opens(Docked, (400, 10), (320, 10), (280, 100)));

    [Fact]
    public void APullWhoseChordCrossesButWhoseSegmentsMissDoesNotOpen() =>
        Assert.False(Opens(Floating, (200, 80), (280, 80), (320, 80), (320, 160)));

    [Fact]
    public void ACurvedPullUsesItsSegmentsEvenWhenItsChordMisses() =>
        Assert.True(Opens(Floating, (400, 80), (320, 80), (295, 120), (320, 160)));

    [Theory]
    [InlineData(305, true)]
    [InlineData(307, false)]
    public void ThePullKeepsTheSixPixelTolerance(double x, bool opens) =>
        Assert.Equal(opens, Opens(Docked, (x - 60, 10), (x, 10), (x, 60)));

    [Fact]
    public void DistancesGrowWithTheDisplayScale()
    {
        (double, double)[] path = [(150, 10), (200, 10), (204, 30), (205, 46)]; // 50 sideways, 36 down
        Assert.True(IslandGesture.IsSweepThenPull(path, Docked, scale: 1));
        Assert.False(IslandGesture.IsSweepThenPull(path, Docked, scale: 2));
    }

    [Fact]
    public void TooShortAPathDoesNotOpen() =>
        Assert.False(IslandGesture.IsSweepThenPull([(200, 10), (200, 50)], Docked));
}
