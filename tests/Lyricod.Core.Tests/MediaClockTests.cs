namespace Lyricod.Core.Tests;

public class MediaClockTests
{
    static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static MediaClockReport Report(double position, double stamp, bool playing, string track = "song") =>
        new(track, position, Epoch.AddSeconds(stamp), playing, true);

    [Fact]
    public void PauseAndResumeWithUnchangedTimelineKeepTheCurrentPosition()
    {
        var playhead = new Playhead { Offset = 0.4 };
        var clock = new MediaClock(playhead);
        clock.Apply(Report(10, 0, true), Epoch, 100, out _);
        clock.Apply(Report(10, 0, false), Epoch.AddSeconds(2), 102, out _);
        Assert.Equal(12, playhead.RawPosition(112), 9);

        Assert.True(clock.Apply(Report(10, 0, true), Epoch.AddSeconds(12), 112, out bool trackChanged));
        Assert.False(trackChanged);
        Assert.Equal(12, playhead.RawPosition(112), 9);
        Assert.Equal(12.4, playhead.Position(112), 9);
        clock.Apply(Report(14, 13, true), Epoch.AddSeconds(13), 113, out _);
        Assert.Equal(14, playhead.RawPosition(113), 9);
    }

    [Fact]
    public void ChangingTrackResetsEvenWithTheSameTimeline()
    {
        var playhead = new Playhead();
        var clock = new MediaClock(playhead);
        clock.Apply(Report(0, 0, true), Epoch, 100, out _);
        clock.Apply(Report(0, 0, false), Epoch.AddSeconds(10), 110, out _);
        Assert.Equal(10, playhead.RawPosition(110), 9);

        Assert.True(clock.Apply(Report(0, 0, false, track: "next"), Epoch.AddSeconds(11), 111, out bool trackChanged));
        Assert.True(trackChanged);
        Assert.Equal(0, playhead.RawPosition(111), 9);
    }

    [Fact]
    public void RepeatedTimelineDoesNotRestartTheClockOrUndoALocalSeek()
    {
        var playhead = new Playhead();
        var clock = new MediaClock(playhead);
        clock.Apply(Report(10, 0, true), Epoch, 100, out _);
        int version = playhead.Version;
        Assert.False(clock.Apply(Report(10, 0, true), Epoch.AddSeconds(2), 102, out _));
        Assert.Equal(version, playhead.Version);
        Assert.Equal(12, playhead.RawPosition(102), 9);

        playhead.Anchor(60, 102, true);
        Assert.False(clock.Apply(Report(10, 0, true), Epoch.AddSeconds(3), 103, out _));
        Assert.Equal(61, playhead.RawPosition(103), 9);
    }
}
