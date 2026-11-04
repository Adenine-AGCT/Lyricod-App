namespace Lyricod.Core.Tests;

public class MediaClockTests
{
    static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static MediaClockReport Report(double position, double stamp, bool playing, bool timeline = true,
        long session = 1, long revision = 1, string track = "song") =>
        new(session, revision, track, position, Epoch.AddSeconds(stamp), playing, timeline);

    [Theory]
    [InlineData(0.5)]
    [InlineData(2)]
    [InlineData(10)]
    [InlineData(30)]
    public void ResumeWithRepeatedPausedTimelineDoesNotCountPausedTime(double pause)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        Assert.True(clock.Apply(Report(12, 2, false), Epoch.AddSeconds(2), 102));
        Assert.Equal(12, p.RawPosition(102 + pause), 9);
        Assert.True(clock.Apply(Report(12, 2, true), Epoch.AddSeconds(2 + pause), 102 + pause));
        Assert.Equal(12, p.RawPosition(102 + pause), 9);
        Assert.False(clock.Apply(Report(12, 2, true), Epoch.AddSeconds(4 + pause), 104 + pause));
        Assert.Equal(14, p.RawPosition(104 + pause), 9);
    }

    [Fact]
    public void CapturedNeteaseResumeDoesNotJumpToTheEndOfTheLyric()
    {
        // NetEase 3.1.41, resumed from its own controls. Song identity is omitted and
        // UTC/monotonic clocks are shifted to a synthetic epoch, preserving event timing.
        // The old projection jumped from 113 to 269.887 before the fresh timeline arrived.
        var p = new Playhead();
        var clock = new MediaClock(p);
        void Apply(double at, double position, double stamp, bool playing) =>
            clock.Apply(Report(position, stamp, playing), Epoch.AddSeconds(at), 1000 + at);
        Apply(154.3187128, 113, 0, false);
        Apply(156.3309007, 113, 0, false);
        Apply(156.8872037, 113, 0, true);
        Assert.Equal(113, p.RawPosition(1156.8872037), 6);
        Apply(157.0848374, 113, 156.9173253, true);
        Assert.InRange(p.RawPosition(1157.0848374), 113, 113.25);
        Apply(157.9428476, 113.71, 157.9352092, true);
        Assert.InRange(p.RawPosition(1157.9428476), 113.7, 113.8);
        Apply(159.2472963, 115.04, 159.2427192, true);
        Assert.InRange(p.RawPosition(1159.2472963), 115, 115.1);
    }

    [Fact]
    public void CapturedIslandToggleAcrossNeteaseSessionsKeepsTheEffectivePosition()
    {
        // PR4 native island trace 401-676. Wall time is shifted from 09:30:00 UTC
        // and session IDs are local stand-ins for the two observed rebinds.
        var p = new Playhead();
        var clock = new MediaClock(p);
        bool Apply(double at, double position, double stamp, bool playing, int session)
        {
            clock.Apply(Report(position, stamp, playing, session: session), Epoch.AddSeconds(at), at, out bool trackChanged);
            return trackChanged;
        }

        Apply(18.5931508, 41.83, 18.5931508, true, 1);
        Apply(20.5438335, 41.83, 18.5931508, false, 1);
        Apply(20.6591912, 43.79, 20.6518757, false, 1);
        Apply(159.3545323, 43.79, 20.6518757, false, 1);
        Assert.Equal(43.7806827, p.RawPosition(159.3545323), 6);

        Assert.False(Apply(159.4998612, 43.79, 20.6518757, true, 2));
        Assert.Equal(43.7806827, p.RawPosition(159.4998612), 6);
        Apply(159.9954762, 44.51, 159.9882300, true, 2);
        Assert.Equal(44.5172462, p.RawPosition(159.9954762), 6);

        Assert.False(Apply(161.3298508, 44.51, 159.9882300, false, 3));
        Assert.Equal(45.8516208, p.RawPosition(162), 6);
        Apply(161.610, 46.13, 161.604318, false, 3);
        Assert.Equal(46.13, p.RawPosition(162), 6);
    }

    [Fact]
    public void PauseWithUnchangedPlayingTimelineFreezesCurrentPosition()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(10, 0, false), Epoch.AddSeconds(2), 102);
        Assert.Equal(12, p.RawPosition(150), 9);
    }

    [Fact]
    public void FreshTimelineStillCorrectsPlaybackAndKeepsLyricOffset()
    {
        var p = new Playhead { Offset = 0.4 };
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(12, 2, false), Epoch.AddSeconds(2), 102);
        clock.Apply(Report(12, 12, true), Epoch.AddSeconds(12), 112);
        Assert.True(clock.Apply(Report(15, 13, true), Epoch.AddSeconds(14), 114));
        Assert.Equal(16, p.RawPosition(114), 9);
        Assert.Equal(16.4, p.Position(114), 9);
    }

    [Fact]
    public void DelayedReadOfFreshResumeIncludesTimeActuallySpentPlaying()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(12, 2, false), Epoch.AddSeconds(2), 102);
        clock.Apply(Report(12, 12, true), Epoch.AddSeconds(14), 114);
        Assert.Equal(14, p.RawPosition(114), 9);
        Assert.False(clock.Apply(Report(12, 12, true), Epoch.AddSeconds(16), 116));
        Assert.Equal(16, p.RawPosition(116), 9);
    }

    [Fact]
    public void ResumeIgnoresNewlyArrivingTimelineFromBeforePause()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(10, 0, false), Epoch.AddSeconds(2), 102);
        clock.Apply(Report(11, 1, true), Epoch.AddSeconds(12), 112);
        Assert.Equal(12, p.RawPosition(112), 9);
        Assert.Equal(13, p.RawPosition(113), 9);
    }

    [Theory]
    [InlineData(0.5, false)]
    [InlineData(2, false)]
    [InlineData(10, false)]
    [InlineData(30, false)]
    [InlineData(0.5, true)]
    [InlineData(2, true)]
    [InlineData(10, true)]
    [InlineData(30, true)]
    public void PauseResumeReplansTheSameWordAndWordGap(double pause, bool fresh)
    {
        var line = new LyricLine
        {
            Start = 10, End = 20, Text = "ab",
            Words = [new LyricWord(10, 3, "a"), new LyricWord(15, 5, "b")],
        };
        var stops = Sweep.Stops(line, 100, text => text.Length);
        foreach (double pausedAt in new[] { 10.5, 12, 13, 14, 15, 17.5 })
        {
            var p = new Playhead();
            var clock = new MediaClock(p);
            clock.Apply(Report(10, 0, true), Epoch, 100);
            double elapsed = pausedAt - 10;
            clock.Apply(Report(pausedAt, elapsed, false), Epoch.AddSeconds(elapsed), 100 + elapsed);
            var paused = Sweep.Plan(stops, line, 100, p.Position(100 + elapsed + pause));
            Assert.Equal(Sweep.SungWidth(stops, line, 100, pausedAt), paused.Values[0], 9);
            double resumedAt = elapsed + pause;
            Assert.True(clock.Apply(Report(pausedAt, fresh ? resumedAt : elapsed, true),
                Epoch.AddSeconds(resumedAt), 100 + resumedAt));
            var resumed = Sweep.Plan(stops, line, 100, p.Position(100 + resumedAt));
            Assert.Equal(paused.Values, resumed.Values);
            Assert.Equal(paused.Times, resumed.Times);
            Assert.Equal(pausedAt + 0.1, p.Position(100 + resumedAt + 0.1), 9);
        }
    }

    [Fact]
    public void RepeatedStateTransitionsDoNotAccumulateOrRewind()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        double at = 0;
        for (int i = 0; i < 20; i++)
        {
            at += 0.75;
            Assert.True(clock.Apply(Report(10, 0, false), Epoch.AddSeconds(at), 100 + at));
            at += 2;
            Assert.True(clock.Apply(Report(10, 0, true), Epoch.AddSeconds(at), 100 + at));
            Assert.Equal(10 + (i + 1) * 0.75, p.RawPosition(100 + at), 9);
        }
    }

    [Fact]
    public void DelayedPreResumeTimelineCannotMoveTheNewAnchor()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(10, 0, false), Epoch.AddSeconds(2), 102);
        clock.Apply(Report(10, 0, true), Epoch.AddSeconds(12), 112);
        Assert.False(clock.Apply(Report(11, 1, true), Epoch.AddSeconds(12.1), 112.1));
        Assert.Equal(12.1, p.RawPosition(112.1), 9);
        Assert.True(clock.Apply(Report(13, 12.2, true), Epoch.AddSeconds(12.2), 112.2));
        Assert.Equal(13, p.RawPosition(112.2), 9);
    }

    [Fact]
    public void OutOfOrderSnapshotsCannotRewindOrChangePlayingState()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(40, 2, true, session: 2, revision: 7, track: "new"), Epoch.AddSeconds(2), 100);
        Assert.False(clock.Apply(Report(2, 3, false, session: 1, revision: 8), Epoch.AddSeconds(3), 101));
        Assert.False(clock.Apply(Report(2, 3, false, session: 2, revision: 6), Epoch.AddSeconds(3), 101));
        Assert.True(p.IsPlaying);
        Assert.Equal(41, p.RawPosition(101), 9);
    }

    [Theory]
    [InlineData("other", 10, 0, true, true)]
    [InlineData("song", 10, 0, false, true)]
    [InlineData("song", 10, 2, true, true)]
    [InlineData("song", 11, 0, true, true)]
    [InlineData("song", 10, 0, true, false)]
    public void SessionChangeKeepsLyricsWheneverTrackIdMatches(string track, double position,
        double stamp, bool playing, bool timeline)
    {
        var clock = new MediaClock(new Playhead());
        clock.Apply(Report(10, 0, false), Epoch, 100);
        clock.Apply(Report(position, stamp, playing, timeline, session: 2, track: track),
            Epoch.AddSeconds(2), 102, out bool trackChanged);
        Assert.Equal(track != "song", trackChanged);
    }

    [Fact]
    public void RebindingKeepsTimelineAndRejectsReadsFromOldSession()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, false), Epoch, 100);
        clock.Seek(clock.BeginSeek(60, Epoch.AddSeconds(1), 101), 101);
        Assert.True(clock.Apply(Report(10, 0, true, session: 2), Epoch.AddSeconds(2), 102, out bool trackChanged));
        Assert.False(trackChanged);
        Assert.False(clock.Apply(Report(60, 3, false, session: 1, revision: 20), Epoch.AddSeconds(3), 103));
        Assert.True(p.IsPlaying);
        Assert.Equal(61, p.RawPosition(103), 9);
    }

    [Fact]
    public void SwitchingSessionEvenForTheSameSongResetsTimelineHistory()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Apply(Report(10, 0, false), Epoch.AddSeconds(2), 102);
        clock.Apply(Report(80, 3, true, session: 2, revision: 2), Epoch.AddSeconds(3), 103);
        Assert.Equal(80, p.RawPosition(103), 9);
        clock.Apply(Report(0, 4, true, session: 2, revision: 3, track: "next"), Epoch.AddSeconds(4), 104);
        Assert.Equal(0, p.RawPosition(104), 9);
    }

    [Fact]
    public void LocalSeekSurvivesRepeatedAndDelayedReportsThenAcceptsFreshCorrection()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        clock.Seek(clock.BeginSeek(60, Epoch.AddSeconds(2), 102), 102);
        Assert.False(clock.Apply(Report(10, 0, true), Epoch.AddSeconds(2.1), 102.1));
        Assert.False(clock.Apply(Report(11, 1, true), Epoch.AddSeconds(2.2), 102.2));
        Assert.Equal(60.2, p.RawPosition(102.2), 9);
        Assert.True(clock.Apply(Report(61, 3, false), Epoch.AddSeconds(3), 103));
        Assert.Equal(61, p.RawPosition(150), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeekConfirmationPublishedBeforeAsyncCompletionCanCorrectTheTarget(bool playing)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, playing), Epoch, 100);
        var seek = clock.BeginSeek(60, Epoch.AddSeconds(1), 101);
        clock.Seek(seek, 102);
        Assert.True(clock.Apply(Report(59, 1.9, playing), Epoch.AddSeconds(2), 102));
        Assert.Equal(playing ? 59.1 : 59, p.RawPosition(102), 9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeekCompletionKeepsConfirmationAlreadyAppliedByAMediaEvent(bool playing)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, playing), Epoch, 100);
        var seek = clock.BeginSeek(60, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(59, 1.9, playing), Epoch.AddSeconds(1.9), 101.9);
        clock.Seek(seek, 102);
        Assert.False(clock.Apply(Report(59, 1.9, playing), Epoch.AddSeconds(2), 102));
        Assert.Equal(playing ? 59.1 : 59, p.RawPosition(102), 9);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(2)]
    public void AHeartbeatDuringAsyncSeekDoesNotCancelImmediateLocalFeedback(double target)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        var seek = clock.BeginSeek(target, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(11.2, 1.2, true), Epoch.AddSeconds(1.2), 101.2);
        clock.Seek(seek, 102);
        Assert.Equal(target, p.RawPosition(102), 9);
        Assert.False(clock.Apply(Report(11.2, 1.2, true), Epoch.AddSeconds(2), 102));
        clock.Apply(Report(target + 0.1, 2.1, true), Epoch.AddSeconds(2.1), 102.1);
        Assert.Equal(target + 0.1, p.RawPosition(102.1), 9);
    }

    [Theory]
    [InlineData(true, 60)]
    [InlineData(false, 60)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public void DelayedInFlightHeartbeatCannotUndoCompletedSeek(bool playing, double target)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, playing), Epoch, 100);
        var seek = clock.BeginSeek(target, Epoch.AddSeconds(1), 101);
        clock.Seek(seek, 102);
        Assert.False(clock.Apply(Report(playing ? 11.5 : 10, 1.5, playing), Epoch.AddSeconds(2.1), 102.1));
        Assert.Equal(playing ? target + 0.1 : target, p.RawPosition(102.1), 9);
        Assert.True(clock.Apply(Report(target - 1, 1.9, playing), Epoch.AddSeconds(2.2), 102.2));
        Assert.Equal(playing ? target - 0.7 : target - 1, p.RawPosition(102.2), 9);
        Assert.True(clock.Apply(Report(10, 2.3, playing), Epoch.AddSeconds(2.3), 102.3));
        Assert.Equal(10, p.RawPosition(102.3), 9);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, -90000)]
    [InlineData(false, -90000)]
    public void UndatedHeartbeatThenConfirmationCanArriveAfterCompletion(bool playing, double stamp)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, stamp, playing), Epoch, 100);
        var seek = clock.BeginSeek(60, Epoch.AddSeconds(1), 101);
        clock.Seek(seek, 102);
        Assert.False(clock.Apply(Report(playing ? 11.5 : 10.1, stamp, playing), Epoch.AddSeconds(2.1), 102.1));
        Assert.Equal(playing ? 60.1 : 60, p.RawPosition(102.1), 9);
        Assert.True(clock.Apply(Report(59, stamp, playing), Epoch.AddSeconds(2.2), 102.2));
        Assert.Equal(59, p.RawPosition(102.2), 9);
        Assert.True(clock.Apply(Report(10, stamp, playing), Epoch.AddSeconds(2.3), 102.3));
        Assert.Equal(10, p.RawPosition(102.3), 9);
    }

    [Fact]
    public void PausedPlayerWithConstantStampCanSeekBackAfterUnconfirmedLocalSeek()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, false), Epoch, 100);
        clock.Seek(clock.BeginSeek(60, Epoch.AddSeconds(1), 101), 102);
        Assert.False(clock.Apply(Report(10.1, 0, false), Epoch.AddSeconds(2.1), 102.1));
        Assert.Equal(60, p.RawPosition(102.1), 9);
        Assert.True(clock.Apply(Report(10.1, 0, false), Epoch.AddSeconds(62), 162));
        Assert.Equal(10.1, p.RawPosition(162), 9);
    }

    [Fact]
    public void SecondUndatedSeekCanConfirmBackAtFirstSeeksOldPosition()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, false), Epoch, 100);
        clock.Seek(clock.BeginSeek(60, Epoch.AddSeconds(1), 101), 102);
        var back = clock.BeginSeek(10, Epoch.AddSeconds(3), 103);
        clock.Apply(Report(10.1, 0, false), Epoch.AddSeconds(3.1), 103.1);
        clock.Seek(back, 104);
        Assert.Equal(10.1, p.RawPosition(104), 9);
    }

    [Fact]
    public void BackwardSeekKeepsAnAlreadyObservedConfirmation()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(60, 0, false), Epoch, 100);
        var seek = clock.BeginSeek(10, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(9, 1.9, false), Epoch.AddSeconds(1.9), 101.9);
        clock.Seek(seek, 102);
        Assert.Equal(9, p.RawPosition(102), 9);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(-90000, false)]
    [InlineData(-90000, true)]
    public void UndatedChangedPositionCanConfirmSeekBeforeCompletion(double stamp, bool playing)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, stamp, playing), Epoch, 100);
        var seek = clock.BeginSeek(60, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(59, stamp, playing), Epoch.AddSeconds(1.9), 101.9);
        clock.Seek(seek, 102);
        Assert.False(clock.Apply(Report(59, stamp, playing), Epoch.AddSeconds(2), 102));
        Assert.Equal(playing ? 59.1 : 59, p.RawPosition(102), 9);
    }

    [Theory]
    [InlineData(12)]
    [InlineData(10)]
    public void SmallSeekConfirmationSurvivesPlaybackAndHeartbeatsBeforeCompletion(double target)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        var seek = clock.BeginSeek(target, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(target, 1.1, true), Epoch.AddSeconds(1.1), 101.1);
        clock.Apply(Report(target + 1, 2.1, true), Epoch.AddSeconds(2.1), 102.1);
        clock.Seek(seek, 102.2);
        Assert.Equal(target + 1.1, p.RawPosition(102.2), 9);
    }

    [Fact]
    public void DelayedReadOfSeekConfirmationComparesSourceTimeRatherThanReceiptTime()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        var seek = clock.BeginSeek(12, Epoch.AddSeconds(1), 101);
        clock.Apply(Report(12, 1.1, true), Epoch.AddSeconds(3), 103);
        clock.Seek(seek, 103.1);
        Assert.Equal(14, p.RawPosition(103.1), 9);
    }

    [Fact]
    public void UncommittedSeekSnapshotDoesNotChangeTheClockOrBlockExternalReports()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        int version = p.Version;
        var request = clock.BeginSeek(60, Epoch.AddSeconds(1), 101);
        Assert.Equal(11, request.PreviousPosition);
        Assert.Equal(version, p.Version);
        Assert.Equal(11, p.RawPosition(101));
        Assert.True(clock.Apply(Report(30, 2, true), Epoch.AddSeconds(2), 102));
        Assert.Equal(30, p.RawPosition(102));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedPositionWithUnchangedTimestampStillSupportsExternalSeek(bool playing)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, playing), Epoch, 100);
        Assert.True(clock.Apply(Report(80, 0, playing), Epoch.AddSeconds(2), 102));
        Assert.Equal(80, p.RawPosition(102), 9);
    }

    [Theory]
    [InlineData(-90000)]
    [InlineData(100)]
    public void InvalidTimestampUsesPositionOnceInsteadOfReanchoringEveryPoll(double stamp)
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, stamp, true), Epoch, 100);
        Assert.Equal(10, p.RawPosition(100), 9);
        Assert.False(clock.Apply(Report(10, stamp, true), Epoch.AddSeconds(2), 102));
        Assert.Equal(12, p.RawPosition(102), 9);
    }

    [Fact]
    public void NoTimelineUsesSteadyTimeAcrossPauseAndWallClockChanges()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(0, 0, true, timeline: false), Epoch, 100);
        clock.Apply(Report(0, 0, false, timeline: false), Epoch.AddHours(-1), 102);
        Assert.Equal(2, p.RawPosition(150), 9);
        clock.Apply(Report(0, 0, true, timeline: false), Epoch.AddHours(1), 152);
        Assert.Equal(3, p.RawPosition(153), 9);
    }

    [Fact]
    public void InvalidFutureTimestampCannotBlockTheNextValidReport()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 100, true), Epoch, 100);
        Assert.True(clock.Apply(Report(50, 1, true), Epoch.AddSeconds(1), 101));
        Assert.Equal(50, p.RawPosition(101), 9);
    }

    [Fact]
    public void SmallFreshJitterDoesNotRestartAnimationButStateChangeAlwaysDoes()
    {
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(10, 0, true), Epoch, 100);
        int version = p.Version;
        Assert.False(clock.Apply(Report(11.1, 1, true), Epoch.AddSeconds(1), 101));
        Assert.Equal(version, p.Version);
        Assert.True(clock.Apply(Report(11.1, 1, false), Epoch.AddSeconds(1), 101));
        Assert.Equal(version + 1, p.Version);
    }

    [Theory]
    [InlineData(3.9)]
    [InlineData(4)]
    [InlineData(4.5)]
    public void LongLineResumesInTheSamePieceAfterAHiddenPause(double pausedAt)
    {
        string[] text = ["I ", "will ", "always ", "love ", "you, ", "wherever ", "you ", "go"];
        var line = new LyricLine { Start = 0, End = 8, Text = string.Concat(text),
            Words = text.Select((t, i) => new LyricWord(i, 1, t)).ToList() };
        var pieces = LinePieces.Split(line, 250, t => t.Length * 10);
        Assert.Equal(2, pieces.Count);
        var p = new Playhead();
        var clock = new MediaClock(p);
        clock.Apply(Report(0, 0, true), Epoch, 100);
        clock.Apply(Report(pausedAt, pausedAt, false), Epoch.AddSeconds(pausedAt), 100 + pausedAt);
        var piece = LinePieces.Current(pieces, p.Position(100 + pausedAt))!;
        var stops = Sweep.Stops(piece, 250, t => t.Length * 10);
        double before = Sweep.SungWidth(stops, piece, 250, p.Position(100 + pausedAt));
        // No renderer calls during the hidden interval. Showing re-plans from the model.
        clock.Apply(Report(pausedAt, pausedAt, true), Epoch.AddSeconds(pausedAt + 30), 130 + pausedAt);
        Assert.Same(piece, LinePieces.Current(pieces, p.Position(130 + pausedAt)));
        Assert.Equal(before, Sweep.Plan(stops, piece, 250, p.Position(130 + pausedAt)).Values[0], 9);
    }
}
