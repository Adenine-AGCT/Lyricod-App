namespace Lyricod.Core;

/// <summary>A media snapshot, including the source identity and when its position was measured.</summary>
public readonly record struct MediaClockReport(long SessionId, long Revision, string TrackId,
    double Position, DateTimeOffset PositionAt, bool IsPlaying, bool HasTimeline);

/// <summary>The clock before an asynchronous seek, used to distinguish a confirmation from a heartbeat.</summary>
public sealed record MediaSeekRequest(double Position, DateTimeOffset RequestedAt, double At,
    double PreviousPosition, bool WasPlaying)
{
    const double UndatedHeartbeatGraceSeconds = 2;

    internal bool Confirmed { get; set; }
    internal double CompletedAt { get; set; }

    internal double OldPositionAt(double at) => PreviousPosition + (WasPlaying ? at - At : 0);

    internal bool FavorsTarget(double position, double oldPosition) =>
        Math.Abs(position - Position) < Math.Abs(position - oldPosition);

    internal bool RejectsAfterCompletion(MediaClockReport report, bool dated, double now)
    {
        if (dated)
        {
            double elapsed = (report.PositionAt - RequestedAt).TotalSeconds;
            return elapsed < 0 || (elapsed <= CompletedAt - At &&
                !FavorsTarget(report.Position, OldPositionAt(At + elapsed)));
        }
        // Without a usable timestamp, only discard samples on the old trajectory
        // for one safety poll. Later external seeks can return to that same position.
        if (Confirmed || now - CompletedAt > UndatedHeartbeatGraceSeconds) return false;
        double nearestOld = Math.Clamp(report.Position, PreviousPosition, OldPositionAt(CompletedAt));
        return Math.Abs(report.Position - nearestOld) <= Playhead.Tolerance &&
            !FavorsTarget(report.Position, nearestOld);
    }
}

/// <summary>Converts media snapshots into the steady clock used by lyrics and progress controls.</summary>
public sealed class MediaClock(Playhead playhead)
{
    MediaClockReport? _previous;
    MediaClockReport? _timeline;
    bool _timelineStampValid;
    bool _awaitingRebindTimeline;
    MediaSeekRequest? _pendingSeek;
    DateTimeOffset? _resumeAt;
    DateTimeOffset? _pausedAt;
    MediaSeekRequest? _completedSeek;

    public void Reset()
    {
        _previous = _timeline = null;
        _timelineStampValid = _awaitingRebindTimeline = false;
        _pendingSeek = _completedSeek = null;
        _resumeAt = _pausedAt = null;
    }

    public MediaSeekRequest BeginSeek(double position, DateTimeOffset utcNow, double now) =>
        _pendingSeek = new(position, utcNow, now, playhead.RawPosition(now), playhead.IsPlaying);

    public void CancelSeek(MediaSeekRequest request)
    {
        if (ReferenceEquals(_pendingSeek, request)) _pendingSeek = null;
    }

    /// <summary>A successful local seek must not be undone by a repeated, pre-seek report.</summary>
    public void Seek(MediaSeekRequest request, double now)
    {
        if (!ReferenceEquals(_pendingSeek, request)) return;
        _pendingSeek = null;
        // The player can publish its confirming position before the async request completes.
        request.CompletedAt = now;
        _completedSeek = request;
        double position = request.Confirmed ? playhead.RawPosition(now) : request.Position;
        playhead.Anchor(position, now, playhead.IsPlaying);
    }

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now) =>
        Apply(report, utcNow, now, out _);

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now, out bool trackChanged)
    {
        trackChanged = false;
        // A completed asynchronous read from an older source/track must not move the clock.
        if (_previous is { } last && (report.SessionId < last.SessionId ||
                report.SessionId == last.SessionId && report.Revision < last.Revision)) return false;
        bool sessionChanged = _previous?.SessionId != report.SessionId;
        trackChanged = _previous is not { } previous || previous.TrackId != report.TrackId ||
            (sessionChanged && !RepeatsTimelineOnToggle(previous, report));
        if (trackChanged) Reset();
        else if (sessionChanged)
        {
            // NetEase can select another SMTC session on toggle while repeating the
            // same sample. Keep the effective position, but retire the old commands.
            _pendingSeek = _completedSeek = null;
            _resumeAt = _pausedAt = null;
            _awaitingRebindTimeline = true;
        }
        bool stateChanged = !trackChanged && playhead.IsPlaying != report.IsPlaying;
        // PlaybackInfo and TimelineProperties need not change together. Never extrapolate a
        // repeated position/timestamp using the *new* playing state: it includes the pause.
        double position = trackChanged ? 0 : playhead.RawPosition(now);
        bool validStamp = report.PositionAt <= utcNow && utcNow - report.PositionAt <= TimeSpan.FromDays(1);
        bool newTimeline = report.HasTimeline && double.IsFinite(report.Position) &&
            (_timeline is not { } old || old.Position != report.Position || old.PositionAt != report.PositionAt);
        bool sameStampNewPosition = _timeline is { } t && t.PositionAt == report.PositionAt && t.Position != report.Position;
        bool older = validStamp && _timelineStampValid && _timeline is { } latest && report.PositionAt < latest.PositionAt;
        // A delayed report from before a resume or local seek cannot undo that anchor.
        // Some players never update LastUpdatedTime; a changed position with that same
        // timestamp is still useful (including external seeking), but is treated as "now".
        bool dated = validStamp && !sameStampNewPosition;
        bool beforeAnchor = dated &&
            ((!stateChanged && report.IsPlaying && _resumeAt is { } resume && report.PositionAt < resume) ||
             (stateChanged && report.IsPlaying && _pausedAt is { } pause && report.PositionAt <= pause));
        // A second local seek can legitimately return to the first seek's old position.
        // For constant timestamps, match its confirmation before filtering the old window.
        double sampleAt = dated ? now - (utcNow - report.PositionAt).TotalSeconds : now;
        bool confirmsPending = _pendingSeek is { } pending && sampleAt >= pending.At &&
            pending.FavorsTarget(report.Position, pending.OldPositionAt(sampleAt));
        bool useTimeline = newTimeline && !older && !beforeAnchor &&
            (confirmsPending || _completedSeek?.RejectsAfterCompletion(report, dated, now) != true);
        if (stateChanged && report.IsPlaying)
            // A fresh resume sample already describes playing time, including read latency.
            // Only a repeated/undated sample needs the local receipt time as its resume anchor.
            _resumeAt = useTimeline && dated ? report.PositionAt : utcNow;
        if (!report.IsPlaying && (trackChanged || stateChanged)) _pausedAt = utcNow;
        if (useTimeline)
        {
            position = report.Position;
            if (report.IsPlaying && dated)
            {
                var start = report.PositionAt;
                if (_resumeAt is { } resumedAt && resumedAt > start) start = resumedAt;
                position += Math.Max((utcNow - start).TotalSeconds, 0);
            }
            _timeline = report;
            _timelineStampValid = validStamp;
            if (_completedSeek is { } completed) completed.Confirmed = true;
            if (confirmsPending) _pendingSeek!.Confirmed = true;
        }
        bool changed = (trackChanged || stateChanged || useTimeline) &&
            playhead.Report(position, now, report.IsPlaying, force: trackChanged || (_awaitingRebindTimeline && useTimeline));
        if (useTimeline) _awaitingRebindTimeline = false;
        _previous = report;
        return changed;
    }

    bool RepeatsTimelineOnToggle(MediaClockReport previous, MediaClockReport report) =>
        previous.IsPlaying != report.IsPlaying && previous.HasTimeline && report.HasTimeline &&
        previous.Position == report.Position && previous.PositionAt == report.PositionAt &&
        _timelineStampValid && _timeline is { } accepted &&
        accepted.Position == report.Position && accepted.PositionAt == report.PositionAt;
}
