namespace Lyricod.Core.Tests;

public class OverlayContentGateTests
{
    [Theory]
    [InlineData(OverlayContentChanges.None)]
    [InlineData(OverlayContentChanges.Clock)]
    [InlineData(OverlayContentChanges.Lyrics)]
    public void FirstVisibleUpdateAlwaysTakesACompleteSnapshot(OverlayContentChanges first)
    {
        var gate = new OverlayContentGate();
        Assert.Equal(OverlayContentChanges.All, gate.Take(true, first));
        Assert.Equal(OverlayContentChanges.None, gate.Take(true, OverlayContentChanges.None));
    }

    [Fact]
    public void HiddenModelUpdatesDoNoViewWorkAndCoalesceIntoOneSnapshot()
    {
        var gate = new OverlayContentGate();
        gate.Take(true, OverlayContentChanges.All);
        var changes = new[] { OverlayContentChanges.Clock, OverlayContentChanges.Lyrics,
            OverlayContentChanges.Song, OverlayContentChanges.Artwork };
        for (int i = 0; i < 1000; i++)
            Assert.Equal(OverlayContentChanges.None, gate.Take(false, changes[i % changes.Length]));
        Assert.Equal(OverlayContentChanges.All, gate.Take(true, OverlayContentChanges.None));
        Assert.Equal(OverlayContentChanges.Clock, gate.Take(true, OverlayContentChanges.Clock));
        Assert.Equal(OverlayContentChanges.None, gate.Take(true, OverlayContentChanges.None));
    }

    [Theory]
    [InlineData(OverlayContentChanges.Clock)] // resume after the pause timeout
    [InlineData(OverlayContentChanges.Song | OverlayContentChanges.Lyrics)] // a song replaces no song
    [InlineData(OverlayContentChanges.None)] // the visibility setting is turned back on
    public void ShowingAfterAHiddenIntervalRefreshesEvenWithoutALyricEvent(OverlayContentChanges trigger)
    {
        var gate = new OverlayContentGate();
        gate.Take(true, OverlayContentChanges.All);
        Assert.Equal(OverlayContentChanges.None, gate.Take(false, OverlayContentChanges.None));
        Assert.Equal(OverlayContentChanges.All, gate.Take(true, trigger));
        Assert.Equal(trigger, gate.Take(true, trigger));
    }

    [Theory]
    [InlineData(OverlayContentChanges.Clock)]
    [InlineData(OverlayContentChanges.Artwork)]
    [InlineData(OverlayContentChanges.Song | OverlayContentChanges.Lyrics)]
    [InlineData(OverlayContentChanges.Progress)] // duration and seek capability can change while paused
    public void AVisibleViewOnlyProcessesTheRequestedParts(OverlayContentChanges change)
    {
        var gate = new OverlayContentGate();
        gate.Take(true, OverlayContentChanges.None);
        Assert.Equal(change, gate.Take(true, change));
    }

    [Fact]
    public void TheTwoViewsKeepIndependentVisibilityAndRefreshState()
    {
        var desktop = new OverlayContentGate();
        var island = new OverlayContentGate();
        desktop.Take(true, OverlayContentChanges.All);
        island.Take(true, OverlayContentChanges.All);
        Assert.Equal(OverlayContentChanges.None, desktop.Take(false, OverlayContentChanges.Clock));
        Assert.Equal(OverlayContentChanges.Clock, island.Take(true, OverlayContentChanges.Clock));
        Assert.Equal(OverlayContentChanges.All, desktop.Take(true, OverlayContentChanges.Clock));
        Assert.Equal(OverlayContentChanges.Clock, island.Take(true, OverlayContentChanges.Clock));
    }
}
