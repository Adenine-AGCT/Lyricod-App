namespace Lyricod.Core;

/// <summary>A media snapshot, including the source identity and when its position was measured.</summary>
public readonly record struct MediaClockReport(long SessionId, long Revision, string TrackId,
    double Position, DateTimeOffset PositionAt, bool IsPlaying, bool HasTimeline);

/// <summary>The clock before an asynchronous seek, used to distinguish a confirmation from a heartbeat.</summary>
public sealed record MediaSeekRequest(double Position, DateTimeOffset RequestedAt, double At,
    double PreviousPosition, bool WasPlaying)
{
    internal bool Confirmed { get; set; }
}

/// <summary>Converts media snapshots into the steady clock used by lyrics and progress controls.</summary>
public sealed class MediaClock(Playhead playhead)
{
    MediaClockReport? _previous;
    MediaClockReport? _timeline;
    bool _timelineStampValid;
    MediaSeekRequest? _pendingSeek;
    DateTimeOffset? _resumeAt;
    DateTimeOffset? _pausedAt;
    DateTimeOffset? _seekAt;

    public void Reset()
    {
        _previous = _timeline = null;
        _timelineStampValid = false;
        _pendingSeek = null;
        _resumeAt = _pausedAt = _seekAt = null;
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
        _seekAt = request.RequestedAt;
        double position = request.Confirmed ? playhead.RawPosition(now) : request.Position;
        playhead.Anchor(position, now, playhead.IsPlaying);
    }

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now)
    {
        // A completed asynchronous read from an older source/track must not move the clock.
        if (_previous is { } last && (report.SessionId < last.SessionId ||
                report.SessionId == last.SessionId && report.Revision < last.Revision)) return false;
        bool trackChanged = _previous is not { } previous || previous.SessionId != report.SessionId || previous.TrackId != report.TrackId;
        if (trackChanged) Reset();
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
        bool beforeAnchor = validStamp && !sameStampNewPosition &&
            ((_seekAt is { } seek && report.PositionAt < seek) ||
             (!stateChanged && report.IsPlaying && _resumeAt is { } resume && report.PositionAt < resume) ||
             (stateChanged && report.IsPlaying && _pausedAt is { } pause && report.PositionAt <= pause));
        bool useTimeline = newTimeline && !older && !beforeAnchor;
        if (stateChanged && report.IsPlaying)
            // A fresh resume sample already describes playing time, including read latency.
            // Only a repeated/undated sample needs the local receipt time as its resume anchor.
            _resumeAt = useTimeline && validStamp && !sameStampNewPosition ? report.PositionAt : utcNow;
        if (!report.IsPlaying && (trackChanged || stateChanged)) _pausedAt = utcNow;
        if (useTimeline)
        {
            position = report.Position;
            if (report.IsPlaying && validStamp && !sameStampNewPosition)
            {
                var start = report.PositionAt;
                if (_resumeAt is { } resumedAt && resumedAt > start) start = resumedAt;
                position += Math.Max((utcNow - start).TotalSeconds, 0);
            }
            _timeline = report;
            _timelineStampValid = validStamp;
            if (_pendingSeek is { } request)
            {
                // Compare at the sample's instant, not command completion: playback may
                // have continued after the confirming event. Keep that evidence across
                // later heartbeats. A normal pre-seek heartbeat follows the old trajectory.
                bool dated = validStamp && !sameStampNewPosition;
                double sampleAt = dated ? now - (utcNow - report.PositionAt).TotalSeconds : now;
                if (sampleAt >= request.At)
                {
                    double previousPosition = request.PreviousPosition + (request.WasPlaying ? sampleAt - request.At : 0);
                    if (Math.Abs(report.Position - request.Position) < Math.Abs(report.Position - previousPosition))
                        request.Confirmed = true;
                }
            }
        }
        bool changed = (trackChanged || stateChanged || useTimeline) &&
            playhead.Report(position, now, report.IsPlaying, force: trackChanged);
        _previous = report;
        return changed;
    }
}
