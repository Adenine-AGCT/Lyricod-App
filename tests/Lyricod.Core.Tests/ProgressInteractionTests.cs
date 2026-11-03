namespace Lyricod.Core.Tests;

public class ProgressInteractionTests
{
    static ProgressInteraction Island(double duration = 200)
    {
        var state = new ProgressInteraction(hoverPreview: true);
        state.SetMedia("song", 1, duration);
        return state;
    }

    static void Preview(ProgressInteraction state, double x = 50)
    {
        state.Hover(x, 2, 100, inside: true, now: 10);
        state.ActivatePreview(10.2);
        Assert.True(state.IsPreviewing);
    }

    [Fact]
    public void HoverIsOptInAndCannotSeekEvenWhenReleased()
    {
        var state = new ProgressInteraction();
        state.SetMedia("song", 1, 200);
        state.Hover(50, 2, 100, inside: true, now: 10);
        state.ActivatePreview(20);
        Assert.Null(state.HoverDueAt);
        Assert.False(state.IsPreviewing);
        Assert.Equal(20, state.Display(20).Elapsed);
        Assert.Null(state.ReleaseScrub(canSeek: true));
    }

    [Fact]
    public void DwellActivatesAt200MillisecondsWithoutAPlaybackTick()
    {
        var state = Island();
        state.Hover(50, 2, 100, inside: true, now: 10);
        state.ActivatePreview(10.199);
        Assert.False(state.IsPreviewing);
        state.ActivatePreview(10.2);
        Assert.True(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        Assert.Equal(new ProgressDisplay(100, 180, 0.5, true), state.Display(20));
    }

    [Fact]
    public void JitterWithinFourDipsKeepsDeadlineAndUsesLatestPointerPosition()
    {
        var state = Island();
        state.Hover(50, 2, 100, true, 10);
        state.Hover(54, 2, 100, true, 10.15);
        Assert.Equal(10.2, state.HoverDueAt);
        state.ActivatePreview(10.2);
        Assert.Equal(108, state.Display(20).Elapsed);
    }

    [Theory]
    [InlineData(54.01, 2)]
    [InlineData(50, 6.01)]
    [InlineData(53, 5)]
    public void MovingMoreThanFourDipsRestartsFromTheDwellAnchor(double x, double y)
    {
        var state = Island();
        state.Hover(50, 2, 100, true, 10);
        state.Hover(52, 2, 100, true, 10.1);
        state.Hover(x, y, 100, true, 10.15);
        state.ActivatePreview(10.2);
        Assert.False(state.IsPreviewing);
        Assert.Equal(10.35, state.HoverDueAt);
        state.ActivatePreview(10.35);
        Assert.True(state.IsPreviewing);
    }

    [Fact]
    public void ActivePreviewFollowsPointerAndSurvivesRealProgressUpdates()
    {
        var state = Island();
        Preview(state);
        state.Hover(75, 2, 100, true, 11);
        for (int i = 20; i <= 30; i++)
        {
            state.SetMedia("song", 1, 200);
            Assert.Equal(new ProgressDisplay(150, 200 - i, 0.75, true), state.Display(i));
        }
        Assert.Null(state.HoverDueAt);
        // This is the only output through which the UI can ask the model to seek.
        Assert.Null(state.ReleaseScrub(canSeek: true));
    }

    [Fact]
    public void ReadOnlyPlayerCanPreviewButCannotBeginOrCommitScrubbing()
    {
        var state = Island();
        Preview(state);
        Assert.Equal(100, state.Display(20).Elapsed);
        Assert.False(state.BeginScrub(0.8, canSeek: false));
        Assert.False(state.IsScrubbing);
        Assert.Null(state.ReleaseScrub(canSeek: false));
    }

    [Fact]
    public void DragTakesPriorityAndReleaseRequestsExactlyItsTarget()
    {
        var state = Island();
        Preview(state);
        Assert.True(state.BeginScrub(0.2, canSeek: true));
        Assert.False(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        state.Hover(80, 2, 100, true, 11);
        state.MoveScrub(0.3);
        Assert.Equal(new ProgressDisplay(60, 140, 0.3, false), state.Display(20));
        Assert.Equal(60, state.ReleaseScrub(canSeek: true));
        Assert.Null(state.ReleaseScrub(canSeek: true));
        Assert.Equal(new ProgressDisplay(25, 175, 0.125, false), state.Display(25));
    }

    [Fact]
    public void LeavingDuringDragCancelsOnlyTheHoverNotTheCapturedDrag()
    {
        var state = Island();
        state.BeginScrub(0.5, true);
        state.CancelPreview();
        state.MoveScrub(1.5);
        Assert.Equal(200, state.ReleaseScrub(true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavingCancelsWaitingAndActivePreviewAndRestoresCurrentPosition(bool activate)
    {
        var state = Island();
        state.Hover(50, 2, 100, true, 10);
        if (activate) state.ActivatePreview(10.2);
        state.Hover(50, 20, 100, false, 11);
        state.ActivatePreview(20);
        Assert.False(state.IsPreviewing);
        Assert.Null(state.HoverDueAt);
        Assert.Equal(new ProgressDisplay(40, 160, 0.2, false), state.Display(40));
    }

    [Theory]
    [InlineData("next", 1, 200)]
    [InlineData("song", 2, 200)]
    [InlineData("song", 1, 210)]
    [InlineData(null, 0, 0)]
    public void NewMediaInvalidatesBothPreviewAndDrag(string? track, long session, double duration)
    {
        var state = Island();
        Preview(state);
        state.SetMedia(track, session, duration);
        Assert.False(state.IsPreviewing);
        state.SetMedia("song", 1, 200);
        state.BeginScrub(0.7, true);
        state.SetMedia(track, session, duration);
        Assert.Null(state.ReleaseScrub(true));
    }

    [Fact]
    public void CancelOnCollapseHideCloseOrCaptureLossDiscardsEveryPendingAction()
    {
        var state = Island();
        state.Hover(50, 2, 100, true, 10);
        state.Cancel();
        state.ActivatePreview(20);
        Assert.False(state.IsPreviewing);
        Preview(state);
        state.Cancel();
        Assert.False(state.IsPreviewing);
        state.BeginScrub(0.5, true);
        state.Cancel();
        Assert.Null(state.ReleaseScrub(true));
        Assert.Null(state.HoverDueAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationCannotPreviewOrSeek(double duration)
    {
        var state = Island(duration);
        state.Hover(50, 2, 100, true, 10);
        state.ActivatePreview(20);
        Assert.False(state.IsPreviewing);
        Assert.False(state.BeginScrub(0.5, true));
        Assert.Null(state.ReleaseScrub(true));
        Assert.Equal(new ProgressDisplay(0, 0, 0, false), state.Display(20));
    }

    [Fact]
    public void LosingSeekSupportBeforeReleaseDoesNotSendARequest()
    {
        var state = Island();
        state.BeginScrub(0.5, true);
        Assert.Null(state.ReleaseScrub(false));
        Assert.False(state.IsScrubbing);
    }
}
