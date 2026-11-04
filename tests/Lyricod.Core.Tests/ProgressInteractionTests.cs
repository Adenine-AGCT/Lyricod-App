namespace Lyricod.Core.Tests;

public class ProgressInteractionTests
{
    [Fact]
    public void DwellPreviewsAfter200MillisecondsWithoutSeeking()
    {
        var state = new ProgressInteraction(hoverPreview: true);
        state.Hover(50, 2, 100, inside: true, now: 10);
        state.ActivatePreview(10.199);
        Assert.False(state.IsPreviewing);
        state.ActivatePreview(10.2);
        Assert.True(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        Assert.Equal(new ProgressDisplay(100, 180, 0.5, true), state.Display(20, 200));
        state.Hover(75, 2, 100, inside: true, now: 11);
        Assert.Equal(new ProgressDisplay(150, 170, 0.75, true), state.Display(30, 200));
        Assert.Null(state.ReleaseScrub(200, canSeek: true));
    }

    [Fact]
    public void DragTakesPriorityAndReleaseRequestsItsTarget()
    {
        var state = new ProgressInteraction(hoverPreview: true);
        state.Hover(50, 2, 100, inside: true, now: 10);
        state.ActivatePreview(10.2);
        Assert.True(state.BeginScrub(0.2, canSeek: true));
        Assert.False(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        state.Hover(80, 2, 100, inside: true, now: 11);
        state.MoveScrub(0.3);
        Assert.Equal(new ProgressDisplay(60, 140, 0.3, false), state.Display(20, 200));
        Assert.Equal(60, state.ReleaseScrub(200, canSeek: true));
        Assert.Null(state.ReleaseScrub(200, canSeek: true));
        Assert.Equal(new ProgressDisplay(25, 175, 0.125, false), state.Display(25, 200));
    }

    [Fact]
    public void LeavingCancelsPreviewAndRestoresPlayback()
    {
        var state = new ProgressInteraction(hoverPreview: true);
        state.Hover(50, 2, 100, inside: true, now: 10);
        state.ActivatePreview(10.2);
        state.Hover(50, 20, 100, inside: false, now: 11);
        state.ActivatePreview(20);
        Assert.False(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        Assert.Equal(new ProgressDisplay(40, 160, 0.2, false), state.Display(40, 200));
    }
}
