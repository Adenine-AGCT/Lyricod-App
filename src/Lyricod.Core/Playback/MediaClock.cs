namespace Lyricod.Core;

/// <summary>A media snapshot, including the source identity and when its position was measured.</summary>
public readonly record struct MediaClockReport(long SessionId, long Revision, string TrackId,
    double Position, DateTimeOffset PositionAt, bool IsPlaying, bool HasTimeline);

/// <summary>A read-only snapshot before an asynchronous seek; creating it does not change the clock.</summary>
public readonly record struct MediaSeekRequest(double Position, DateTimeOffset RequestedAt, double At,
    double PreviousPosition, bool WasPlaying)
{
    internal double OldPositionAt(double at) => PreviousPosition + (WasPlaying ? Math.Max(at - At, 0) : 0);
    internal bool FavorsTarget(double position, double at) =>
        Math.Abs(position - (Position + (WasPlaying ? Math.Max(at - At, 0) : 0))) <
        Math.Abs(position - OldPositionAt(at));
}

/// <summary>Converts media snapshots into the steady clock used by lyrics and progress controls.</summary>
public sealed class MediaClock(Playhead playhead)
{
    MediaClockReport? _previous, _timeline;
    DateTimeOffset? _resumeAt, _pausedAt;
    (MediaSeekRequest Request, double At, bool Confirmed)? _completedSeek;
    double _timelineAt, _timelineSampleAt, _previousAt, _previousSampleAt;
    bool _timelineStampValid, _awaitingTimeline;

    public void Reset()
    {
        _previous = _timeline = null;
        _resumeAt = _pausedAt = null;
        _completedSeek = null;
        _timelineStampValid = _awaitingTimeline = false;
    }

    public MediaSeekRequest BeginSeek(double position, DateTimeOffset utcNow, double now) =>
        new(position, utcNow, now, playhead.RawPosition(now), playhead.IsPlaying);

    /// <summary>Preserve an early confirmation, otherwise give immediate feedback at the requested target.</summary>
    public void Seek(MediaSeekRequest request, double now)
    {
        bool Confirms(MediaClockReport? sample, double received, double measured) =>
            sample is { HasTimeline: true } report && received >= request.At && measured >= request.At &&
            double.IsFinite(report.Position) && request.FavorsTarget(report.Position, measured);
        bool confirmed = Confirms(_timeline, _timelineAt, _timelineSampleAt);
        double position = confirmed ? playhead.RawPosition(now) : request.Position;
        // A second seek can return to the old position while the first seek's grace window
        // still rejects it. Reconsider that latest sample only after the new command succeeds.
        if (_previousAt > _timelineAt && _previousSampleAt >= _timelineSampleAt &&
            Confirms(_previous, _previousAt, _previousSampleAt))
        {
            position = _previous!.Value.Position + (playhead.IsPlaying ? Math.Max(now - _previousSampleAt, 0) : 0);
            confirmed = true;
        }
        _completedSeek = (request, now, confirmed);
        playhead.Anchor(position, now, playhead.IsPlaying);
    }

    bool RejectsCompletedSeek(MediaClockReport report, bool dated, double now)
    {
        if (_completedSeek is not { } completed) return false;
        var request = completed.Request;
        if (dated)
        {
            double elapsed = (report.PositionAt - request.RequestedAt).TotalSeconds;
            return elapsed < 0 || (elapsed <= completed.At - request.At &&
                !request.FavorsTarget(report.Position, request.At + elapsed));
        }
        // Constant/invalid timestamps get two seconds of protection, then external seeks
        // back to the old position must work even if no confirmation ever arrived.
        if (completed.Confirmed || now - completed.At > 2) return false;
        double nearestOld = Math.Clamp(report.Position, request.PreviousPosition, request.OldPositionAt(completed.At));
        return Math.Abs(report.Position - nearestOld) <= Playhead.Tolerance &&
            Math.Abs(report.Position - request.Position) >= Math.Abs(report.Position - nearestOld);
    }

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now) =>
        Apply(report, utcNow, now, out _);

    public bool Apply(MediaClockReport report, DateTimeOffset utcNow, double now, out bool trackChanged)
    {
        trackChanged = false;
        if (_previous is { } last && (report.SessionId < last.SessionId ||
                report.SessionId == last.SessionId && report.Revision < last.Revision)) return false;
        trackChanged = _previous is not { } previous || previous.TrackId != report.TrackId;
        bool sessionChanged = _previous?.SessionId != report.SessionId;
        if (trackChanged) Reset();
        else if (sessionChanged)
        {
            // Session identity governs commands, not lyric/cover identity. NetEase can
            // rebind during a toggle and repeat the frozen sample from the old session.
            _completedSeek = null;
            _awaitingTimeline = true;
        }
        bool stateChanged = !trackChanged && playhead.IsPlaying != report.IsPlaying;
        double position = trackChanged ? 0 : playhead.RawPosition(now);
        bool validStamp = report.PositionAt <= utcNow && utcNow - report.PositionAt <= TimeSpan.FromDays(1);
        bool newTimeline = report.HasTimeline && double.IsFinite(report.Position) &&
            (_timeline is not { } old || old.Position != report.Position || old.PositionAt != report.PositionAt);
        // Players with constant/invalid timestamps can still report external seeks.
        bool dated = validStamp && !(_timeline is { } t && t.PositionAt == report.PositionAt && t.Position != report.Position);
        bool older = validStamp && _timelineStampValid && _timeline is { } latest && report.PositionAt < latest.PositionAt;
        bool beforeAnchor = dated &&
            ((!stateChanged && report.IsPlaying && _resumeAt is { } resume && report.PositionAt < resume) ||
             (stateChanged && report.IsPlaying && _pausedAt is { } pause && report.PositionAt <= pause));
        double sampleAt = dated ? now - (utcNow - report.PositionAt).TotalSeconds : now;
        bool useTimeline = newTimeline && !older && !beforeAnchor && !RejectsCompletedSeek(report, dated, now);
        if (stateChanged && report.IsPlaying)
            _resumeAt = useTimeline && dated ? report.PositionAt : utcNow;
        if (!report.IsPlaying && (trackChanged || stateChanged)) _pausedAt = utcNow;
        if (useTimeline)
        {
            position = report.Position;
            if (report.IsPlaying && dated) position += Math.Max((utcNow - report.PositionAt).TotalSeconds, 0);
            _timeline = report;
            _timelineStampValid = validStamp;
            _timelineAt = now;
            _timelineSampleAt = sampleAt;
            if (_completedSeek is { } completed) _completedSeek = (completed.Request, completed.At, true);
        }
        // With an unchanged timeline, toggling simply anchors the current position.
        // A new sample from a rebound session corrects even small drift once.
        bool changed = (trackChanged || stateChanged || useTimeline) &&
            playhead.Report(position, now, report.IsPlaying, force: trackChanged || (_awaitingTimeline && useTimeline));
        if (useTimeline) _awaitingTimeline = false;
        _previous = report;
        _previousAt = now;
        _previousSampleAt = sampleAt;
        return changed;
    }
}
