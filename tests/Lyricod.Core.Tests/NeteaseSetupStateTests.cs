namespace Lyricod.Core.Tests;

public class NeteaseSetupStateTests
{
    [Fact]
    public void CurrentPageSeesProgressAndCompletionAfterNavigation()
    {
        var state = new NeteaseSetupState();
        int oldRefreshes = 0, currentRefreshes = 0;
        void OldPage() => oldRefreshes++;
        void CurrentPage() => currentRefreshes++;
        state.Changed += OldPage;
        int operation = state.Start(false)!.Value;
        state.Changed -= OldPage;
        // A newly attached page reads the same running state immediately.
        Assert.True(state.Busy);
        Assert.Equal(NeteaseStage.Downloading, state.Stage);
        state.Changed += CurrentPage;
        state.Report(operation, NeteaseStage.WaitingForPermission);
        Assert.Equal(NeteaseStage.WaitingForPermission, state.Stage);
        state.Complete(operation, (NeteaseResult.Done, null));
        Assert.False(state.Busy);
        Assert.Equal(NeteaseResult.Done, state.Outcome!.Value.Result);
        Assert.Equal(1, oldRefreshes);
        Assert.Equal(2, currentRefreshes);
    }

    [Fact]
    public void ResultSurvivesAllSettingsWindowsClosing()
    {
        var state = new NeteaseSetupState();
        int operation = state.Start(true)!.Value;
        Assert.Null(state.Start(false));
        Assert.True(state.Removing);
        state.Complete(operation, (NeteaseResult.Failed, "offline"));
        Assert.False(state.Busy);
        Assert.Equal((NeteaseResult.Failed, "offline"), state.Outcome!.Value);
        Assert.True(state.Removing);
        Assert.NotNull(state.Start(false));
        Assert.False(state.Removing);
        Assert.Null(state.Outcome);
    }

    [Fact]
    public void LateProgressAndResultsCannotChangeACompletedOrNewSetup()
    {
        var state = new NeteaseSetupState();
        int old = state.Start(false)!.Value;
        state.Complete(old, (NeteaseResult.Cancelled, null));
        state.Report(old, NeteaseStage.WaitingForPermission);
        Assert.Equal(NeteaseStage.Downloading, state.Stage);
        int current = state.Start(true)!.Value;
        state.Complete(old, (NeteaseResult.Done, null));
        state.Report(old, NeteaseStage.WaitingForPermission);
        Assert.True(state.Busy);
        Assert.Null(state.Outcome);
        Assert.Equal(NeteaseStage.Downloading, state.Stage);
        state.Complete(current, (NeteaseResult.Done, null));
        Assert.False(state.Busy);
    }

    [Fact]
    public void AFailedRefreshDoesNotBlockOtherPagesOrCompletion()
    {
        var state = new NeteaseSetupState();
        int refreshes = 0;
        state.Changed += () => throw new InvalidOperationException();
        state.Changed += () => refreshes++;
        int operation = state.Start(false)!.Value;
        state.Complete(operation, (NeteaseResult.CouldntDownload, null));
        Assert.Equal(2, refreshes);
        Assert.False(state.Busy);
    }
}
